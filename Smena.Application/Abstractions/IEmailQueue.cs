namespace Smena.Application.Abstractions;

/// <summary>
/// Stavlja mejl u red za slanje u pozadini, pa zahtev ne čeka SMTP (jedan mejl zna da traje sekunde, a najava
/// može imati desetine primalaca). Greške slanja se loguju i ne vraćaju pozivaocu.
/// </summary>
public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
}
