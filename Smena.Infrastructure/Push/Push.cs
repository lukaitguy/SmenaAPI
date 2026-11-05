using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Infrastructure.Persistence;

namespace Smena.Infrastructure.Push;

/// <summary>
/// Šalje poruku push servisu browsera (FCM, Mozilla, Apple). Šifrovanje i VAPID potpis su u
/// <see cref="WebPushProtocol"/> (standardi koje razume i Safari na iPhone-u).
/// </summary>
public sealed class WebPushSender(IOptions<PushOptions> options, ILogger<WebPushSender> logger, TimeProvider? time = null) : IPushSender, IDisposable
{
    /// <summary>Obaveštenja o smenama zastarevaju brzo: push servis ih odbacuje posle jednog dana.</summary>
    private const int TimeToLiveSeconds = 24 * 60 * 60;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<PushResult> SendAsync(PushTarget target, string payload, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        try
        {
            var body = WebPushProtocol.Encrypt(Encoding.UTF8.GetBytes(payload), WebPushProtocol.Decode(target.P256dh), WebPushProtocol.Decode(target.Auth));

            using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("TTL", TimeToLiveSeconds.ToString());
            request.Headers.TryAddWithoutValidation("Urgency", "normal");
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                WebPushProtocol.VapidAuthorization(target.Endpoint, settings.Subject, settings.PublicKey!, settings.PrivateKey!, _time.GetUtcNow()));

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return PushResult.Sent;

            // Browser je odjavio ili obrisao pretplatu: više je nema smisla slati.
            if (response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                return PushResult.Gone;

            logger.LogWarning("Push servis {Host} je odgovorio {Status}.", new Uri(target.Endpoint).Host, (int)response.StatusCode);
            return PushResult.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Push nije poslat na {Host}.", Uri.TryCreate(target.Endpoint, UriKind.Absolute, out var uri) ? uri.Host : "?");
            return PushResult.Failed;
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Priprema poruku i šalje je na sve uređaje navedenih zaposlenih. Poziva se iz pozadine, bez prijavljenog korisnika,
/// pa pretplate traži po id-ju zaposlenog (jedinstven je u celom sistemu) mimo filtera restorana.
/// </summary>
public sealed class PushDispatcher(AppDbContext db, IPushSender sender, ILogger<PushDispatcher> logger)
{
    public async Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        if (!sender.IsConfigured || message.EmployeeIds.Count == 0)
            return;

        var ids = message.EmployeeIds.ToList();
        var subscriptions = await db.PushSubscriptions.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.EmployeeId))
            .ToListAsync(cancellationToken);
        if (subscriptions.Count == 0)
            return;

        var payload = BuildPayload(message);
        var gone = new List<Domain.Push.PushSubscription>();
        foreach (var subscription in subscriptions)
        {
            var result = await sender.SendAsync(new PushTarget(subscription.Endpoint, subscription.P256dh, subscription.Auth), payload, cancellationToken);
            if (result == PushResult.Gone)
                gone.Add(subscription);
        }

        if (gone.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(gone);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Uklonjeno {Count} nevažećih push pretplata.", gone.Count);
        }
    }

    /// <summary>
    /// Oblik koji razume Angular service worker (<c>ngsw</c>): sam prikazuje obaveštenje, a klik otvara stranicu
    /// u već otvorenom prozoru aplikacije ili u novom.
    /// </summary>
    public static string BuildPayload(PushMessage message) => JsonSerializer.Serialize(new
    {
        notification = new
        {
            title = message.Title,
            body = message.Body,
            icon = "/icons/icon-192.png",
            badge = "/icons/badge-72.png",
            data = new { onActionClick = new { @default = new { operation = "navigateLastFocusedOrOpen", url = message.Url } } },
        },
    });
}

internal sealed class ChannelPushQueue : IPushQueue
{
    private readonly Channel<PushMessage> _channel = Channel.CreateUnbounded<PushMessage>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<PushMessage> Reader => _channel.Reader;

    public void Enqueue(PushMessage message) => _channel.Writer.TryWrite(message);
}

internal sealed class PushQueueWorker(ChannelPushQueue queue, IServiceScopeFactory scopes, ILogger<PushQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PushDispatcher>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Push ({Title}) nije poslat.", message.Title);
            }
        }
    }
}
