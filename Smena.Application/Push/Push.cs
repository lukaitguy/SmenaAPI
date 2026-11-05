using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Push;

namespace Smena.Application.Push;

/// <param name="Enabled">Server ima VAPID ključeve, pa se uređaji mogu pretplatiti.</param>
public sealed record PushConfigDto(bool Enabled, string? PublicKey);

public sealed class GetPushConfigHandler(IOptions<PushOptions> options)
{
    public PushConfigDto Handle() =>
        options.Value.IsConfigured ? new PushConfigDto(true, options.Value.PublicKey) : new PushConfigDto(false, null);
}

/// <summary>Oblik koji browser vraća iz <c>PushSubscription.toJSON()</c>.</summary>
public sealed record PushKeysDto(string P256dh, string Auth);

public sealed record SubscribePushCommand(string Endpoint, PushKeysDto Keys);

public sealed class SubscribePushValidator : AbstractValidator<SubscribePushCommand>
{
    public SubscribePushValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(PushSubscription.MaxEndpointLength)
            .Must(e => Uri.TryCreate(e, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Adresa push servisa mora biti https adresa.");
        RuleFor(x => x.Keys).NotNull();
        RuleFor(x => x.Keys.P256dh).NotEmpty().MaximumLength(PushSubscription.MaxKeyLength).When(x => x.Keys is not null);
        RuleFor(x => x.Keys.Auth).NotEmpty().MaximumLength(PushSubscription.MaxKeyLength).When(x => x.Keys is not null);
    }
}

public sealed class SubscribePushHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IOptions<PushOptions> options,
    IValidator<SubscribePushCommand> validator,
    TimeProvider time)
{
    /// <summary>
    /// Registruje uređaj prijavljenog zaposlenog. Ponovljeno slanje iste adrese ništa ne dupla, a adresa koja je
    /// pripadala kolegi iz istog restorana prelazi na prijavljenog (isti browser, druga osoba).
    /// </summary>
    public async Task<Result> HandleAsync(SubscribePushCommand command, string? userAgent, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (!options.Value.IsConfigured)
            return Error.Conflict("push.not_configured", "Obaveštenja nisu uključena na serveru.");

        var me = currentUser.EmployeeId!.Value;
        var now = time.GetUtcNow();

        var existing = await db.PushSubscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Endpoint == command.Endpoint, cancellationToken);
        if (existing is not null && existing.RestaurantId != currentUser.RestaurantId)
            return Error.Conflict("push.endpoint_in_use", "Ovaj browser je već vezan za nalog drugog restorana. Odjavi ga i pretplati se ponovo.");

        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription(
                currentUser.RestaurantId!.Value, me, command.Endpoint, command.Keys.P256dh, command.Keys.Auth, userAgent, now));
        }
        else
        {
            existing.Reassign(me, command.Keys.P256dh, command.Keys.Auth, userAgent);
        }

        // Najviše nekoliko uređaja po osobi: najstariji se ukidaju.
        var mine = await db.PushSubscriptions.Where(s => s.EmployeeId == me && s.Endpoint != command.Endpoint)
            .OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);
        db.PushSubscriptions.RemoveRange(mine.Skip(PushSubscription.MaxPerEmployee - 1));

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class UnsubscribePushHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Uklanja uređaj prijavljenog zaposlenog; nepoznata adresa nije greška, cilj je već ostvaren.</summary>
    public async Task<Result> HandleAsync(string endpoint, CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId;
        var subscription = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.EmployeeId == me, cancellationToken);
        if (subscription is not null)
        {
            db.PushSubscriptions.Remove(subscription);
            await db.SaveChangesAsync(cancellationToken);
        }
        return Result.Success();
    }
}

public sealed class SendTestPushHandler(ICurrentUser currentUser, IPushQueue queue)
{
    /// <summary>Probno obaveštenje samo na uređaje prijavljenog zaposlenog, da proveri da li sve radi.</summary>
    public Result Handle()
    {
        queue.Enqueue(new PushMessage([currentUser.EmployeeId!.Value], "Probno obaveštenje", "Obaveštenja iz Smene rade na ovom uređaju.", "/obavestenja"));
        return Result.Success();
    }
}
