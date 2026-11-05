using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Infrastructure.Push;

namespace Smena.Tests.Integration;

/// <summary>
/// Pravo slanje: lokalni HTTP server glumi push servis browsera, pa se proverava šta zaista ide preko mreže
/// (VAPID potpis, šifrovana poruka, TTL) i kako se odgovori servisa pretvaraju u ishod.
/// </summary>
public class WebPushSenderTests
{
    private sealed record Received(string? Authorization, string? ContentEncoding, string? Ttl, byte[] Body);

    /// <summary>Pretplata kakvu pravi browser: javni ključ (65 bajtova) i tajna od 16 bajtova.</summary>
    private static (string P256dh, string Auth) NewBrowserKeys()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var point = new byte[65];
        point[0] = 4;
        p.Q.X!.CopyTo(point, 1);
        p.Q.Y!.CopyTo(point, 33);
        return (WebEncoders.Base64UrlEncode(point), WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)));
    }

    private static WebPushSender NewSender(PushOptions? options = null) =>
        new(Options.Create(options ?? new PushOptions { PublicKey = TestVapid.PublicKey, PrivateKey = TestVapid.PrivateKey, Subject = "mailto:test@smena.local" }),
            NullLogger<WebPushSender>.Instance);

    /// <summary>Podiže "push servis" koji odgovara zadatim statusom i pamti poslednji zahtev.</summary>
    private static async Task<(PushTarget Target, Func<Task<Received?>> Request, HttpListener Listener)> StartServiceAsync(HttpStatusCode answer)
    {
        var port = new Random().Next(20_000, 40_000);
        HttpListener listener;
        while (true)
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                break;
            }
            catch (HttpListenerException)
            {
                port++;
            }
        }

        var received = new TaskCompletionSource<Received?>();
        _ = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                using var body = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(body);
                received.TrySetResult(new Received(
                    context.Request.Headers["Authorization"], context.Request.Headers["Content-Encoding"], context.Request.Headers["TTL"], body.ToArray()));
                context.Response.StatusCode = (int)answer;
                context.Response.Close();
            }
            catch (Exception)
            {
                received.TrySetResult(null);
            }
        });

        var (p256dh, auth) = NewBrowserKeys();
        await Task.CompletedTask;
        return (new PushTarget($"http://127.0.0.1:{port}/push/abc", p256dh, auth),
            async () => await received.Task.WaitAsync(TimeSpan.FromSeconds(10)), listener);
    }

    [Fact]
    public async Task Sends_an_encrypted_message_with_a_vapid_signature_and_a_one_day_lifetime()
    {
        var (target, request, listener) = await StartServiceAsync(HttpStatusCode.Created);
        using var _ = listener;
        const string payload = "{\"notification\":{\"title\":\"Tajni naslov sastanka\"}}";

        var result = await NewSender().SendAsync(target, payload, CancellationToken.None);

        Assert.Equal(PushResult.Sent, result);
        var sent = await request();
        Assert.NotNull(sent);
        Assert.StartsWith("vapid t=", sent.Authorization);
        Assert.Contains($"k={TestVapid.PublicKey}", sent.Authorization);
        Assert.Equal("aes128gcm", sent.ContentEncoding);
        Assert.Equal("86400", sent.Ttl);
        Assert.True(sent.Body.Length > payload.Length);
        Assert.DoesNotContain("Tajni naslov", Encoding.UTF8.GetString(sent.Body));   // šifrovano, ne čist tekst
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, PushResult.Gone)]
    [InlineData(HttpStatusCode.NotFound, PushResult.Gone)]
    [InlineData(HttpStatusCode.InternalServerError, PushResult.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, PushResult.Failed)]
    [InlineData(HttpStatusCode.Forbidden, PushResult.Failed)]
    public async Task Turns_the_answer_of_the_push_service_into_an_outcome(HttpStatusCode answer, PushResult expected)
    {
        var (target, _, listener) = await StartServiceAsync(answer);
        using var _l = listener;

        var result = await NewSender().SendAsync(target, "{}", CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task An_unreachable_service_is_a_failure_and_never_an_exception()
    {
        var (p256dh, auth) = NewBrowserKeys();

        var result = await NewSender().SendAsync(new PushTarget("http://127.0.0.1:1/nema", p256dh, auth), "{}", CancellationToken.None);

        Assert.Equal(PushResult.Failed, result);
    }

    [Fact]
    public void It_is_off_until_both_keys_are_set()
    {
        Assert.False(NewSender(new PushOptions()).IsConfigured);
        Assert.False(NewSender(new PushOptions { PublicKey = TestVapid.PublicKey }).IsConfigured);
        Assert.True(NewSender().IsConfigured);
    }

    [Fact]
    public void The_payload_has_the_shape_the_angular_service_worker_shows_and_opens()
    {
        var json = JsonDocument.Parse(PushDispatcher.BuildPayload(new PushMessage([Guid.NewGuid()], "Naslov", "Tekst \"sa\" navodnicima", "/najave")));

        var n = json.RootElement.GetProperty("notification");
        Assert.Equal("Naslov", n.GetProperty("title").GetString());
        Assert.Equal("Tekst \"sa\" navodnicima", n.GetProperty("body").GetString());
        Assert.Equal("/icons/icon-192.png", n.GetProperty("icon").GetString());
        var action = n.GetProperty("data").GetProperty("onActionClick").GetProperty("default");
        Assert.Equal("navigateLastFocusedOrOpen", action.GetProperty("operation").GetString());
        Assert.Equal("/najave", action.GetProperty("url").GetString());
    }
}

/// <summary>Kad server nema VAPID ključeve, push je isključen a aplikacija radi kao i pre.</summary>
[Collection(ApiCollection.Name)]
public class PushDisabledTests(ApiFactory factory)
{
    [Fact]
    public async Task Without_keys_the_config_says_disabled_and_subscribing_is_refused_clearly()
    {
        await using var off = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Push:PublicKey", "");
            b.UseSetting("Push:PrivateKey", "");
        });
        var owner = await ApiClient.RegisterOwnerAsync(off);

        var config = await owner.Http.GetFromJsonAsync<JsonElement>("/api/push/config", ApiClient.Json);
        var subscribe = await owner.Http.PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = "https://push.example.test/x", keys = new { p256dh = "k", auth = "a" } }, ApiClient.Json);

        Assert.False(config.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("publicKey").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, subscribe.StatusCode);
        Assert.Contains("nisu uključena", await subscribe.Content.ReadAsStringAsync());
    }
}
