using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Smena.Application.Abstractions;

namespace Smena.Infrastructure.Email;

/// <summary>Red u memoriji. Mejlovi koji nisu stigli da se pošalju pri gašenju servera se gube; za obaveštenja je to prihvatljivo.</summary>
internal sealed class ChannelEmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateUnbounded<EmailMessage>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}

/// <summary>Šalje mejlove iz reda jedan po jedan. Pad jednog slanja ne zaustavlja ostale.</summary>
internal sealed class EmailQueueWorker(
    ChannelEmailQueue queue, IServiceScopeFactory scopes, ILogger<EmailQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Mejl ({Subject}) nije poslat.", message.Subject);
            }
        }
    }
}
