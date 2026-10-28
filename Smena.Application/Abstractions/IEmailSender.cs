namespace Smena.Application.Abstractions;

public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>Slanje emaila; implementacija u Infrastructure sloju. Baca izuzetak ako slanje ne uspe.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
