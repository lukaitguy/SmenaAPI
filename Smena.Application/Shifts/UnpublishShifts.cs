using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Shifts;

/// <param name="PositionIds">Pozicije čiji se raspored povlači (npr. samo konobari); prazno znači sve pozicije.</param>
public sealed record UnpublishShiftsCommand(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<Guid>? PositionIds = null);

/// <param name="SwapRequestsRemoved">Zahtevi za prepuštanje i razmenu obrisani zajedno sa povučenim smenama.</param>
/// <param name="ClaimRequestsRemoved">Zahtevi za preuzimanje otvorenih smena obrisani zajedno sa povučenim smenama.</param>
public sealed record UnpublishedShiftsDto(int Unpublished, int SwapRequestsRemoved = 0, int ClaimRequestsRemoved = 0);

public sealed class UnpublishShiftsValidator : AbstractValidator<UnpublishShiftsCommand>
{
    public UnpublishShiftsValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From).WithMessage("Kraj perioda mora biti posle početka.");
        RuleFor(x => x).Must(x => x.To - x.From <= TimeSpan.FromDays(ShiftQueries.MaxRangeDays))
            .WithName(nameof(UnpublishShiftsCommand.To))
            .WithMessage($"Period ne može biti duži od {ShiftQueries.MaxRangeDays} dana.");
        RuleForEach(x => x.PositionIds).NotEqual(Guid.Empty);
    }
}

public sealed class UnpublishShiftsHandler(
    IAppDbContext db,
    IValidator<UnpublishShiftsCommand> validator,
    TimeProvider time)
{
    /// <summary>
    /// Vraća objavljene smene koje počinju u periodu [From, To) u nacrte, za zadate pozicije ili za sve.
    /// Smene koje su već počele ostaju objavljene, jer su istorija. Svi zahtevi vezani za povučene smene brišu se
    /// zajedno sa njima i više ne postoje, bez obzira na status: zahtevi za prepuštanje i razmenu (u kojima je bar
    /// jedna od smena povučena) i zahtevi za preuzimanje otvorenih smena. Zaposleni se ne obaveštavaju.
    /// </summary>
    public async Task<Result<UnpublishedShiftsDto>> HandleAsync(UnpublishShiftsCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var positionIds = command.PositionIds?.Distinct().ToList() ?? [];
        if (positionIds.Count > 0 && await db.Positions.CountAsync(p => positionIds.Contains(p.Id), cancellationToken) != positionIds.Count)
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");

        var now = time.GetUtcNow();
        var from = command.From.ToUniversalTime();
        var to = command.To.ToUniversalTime();
        var published = await db.Shifts
            .Where(s => s.PublishedAt != null && s.StartsAt >= from && s.StartsAt < to && s.StartsAt > now
                        && (positionIds.Count == 0 || positionIds.Contains(s.PositionId)))
            .ToListAsync(cancellationToken);

        foreach (var shift in published)
            shift.Unpublish();

        var ids = published.Select(s => s.Id).ToList();
        var swaps = await db.ShiftSwaps
            .Where(s => ids.Contains(s.ShiftId) || (s.TargetShiftId != null && ids.Contains(s.TargetShiftId.Value)))
            .ToListAsync(cancellationToken);
        var claims = await db.ShiftClaims
            .Where(c => ids.Contains(c.ShiftId))
            .ToListAsync(cancellationToken);
        db.ShiftSwaps.RemoveRange(swaps);
        db.ShiftClaims.RemoveRange(claims);

        await db.SaveChangesAsync(cancellationToken);
        return new UnpublishedShiftsDto(published.Count, swaps.Count, claims.Count);
    }
}
