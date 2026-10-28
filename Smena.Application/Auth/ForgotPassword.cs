using FluentValidation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Auth;

public sealed record ForgotPasswordCommand(string Email);

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
    }
}

internal sealed class Counter
{
    public int Value;
}

public sealed class ForgotPasswordHandler(
    IIdentityService identity,
    IMemoryCache cache,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    IValidator<ForgotPasswordCommand> validator,
    ILogger<ForgotPasswordHandler> logger)
{
    /// <summary>Najviše toliko mejlova za reset po adresi u jednom satu, ma sa koliko IP adresa stigli zahtevi.</summary>
    public const int MaxEmailsPerAddressPerHour = 3;

    /// <summary>
    /// Uvek uspeva, bez obzira da li nalog postoji ili je slanje uspelo, da odgovor
    /// ne otkriva koji su emailovi registrovani.
    /// </summary>
    public async Task HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var email = EmailAddress.Normalize(command.Email);

        // Broji se i za nepostojeće adrese, da odgovor ne otkriva koje nalozi postoje.
        // Brojač je u memoriji instance; sa više instanci kvota je po instanci.
        var counter = cache.GetOrCreate($"forgot-password:{email}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return new Counter();
        })!;
        if (Interlocked.Increment(ref counter.Value) > MaxEmailsPerAddressPerHour)
            return;

        try
        {
            var token = await identity.GeneratePasswordResetTokenAsync(email, cancellationToken);
            if (token is null)
                return;

            var link = appOptions.Value.PasswordResetLink(email, token);
            await emailSender.SendAsync(PasswordResetEmail.Build(email, link), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Slanje emaila za reset lozinke nije uspelo.");
        }
    }
}
