using System.Collections.Concurrent;
using Smena.Application.Abstractions;

namespace Smena.Tests.Integration;

/// <summary>Pamti poslate mejlove; <see cref="FailFor"/> simulira pad SMTP servera za zadatu adresu.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    private readonly ConcurrentDictionary<string, bool> _failFor = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    public void FailFor(string address) => _failFor[address] = true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (_failFor.ContainsKey(message.To))
            throw new InvalidOperationException("SMTP nije dostupan.");

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
