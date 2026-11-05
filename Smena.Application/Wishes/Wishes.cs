using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.DayOffs;
using Smena.Application.Notifications;
using Smena.Domain.Wishes;

namespace Smena.Application.Wishes;

public sealed record WishDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
    string PreferredShift,
    DateTimeOffset CreatedAt);

internal static class WishQueries
{
    public const int MaxListed = 500;

    public static IQueryable<WishDto> ProjectToDto(this IAppDbContext db, IQueryable<ShiftWish> wishes) =>
        from w in wishes
        join e in db.Employees on w.EmployeeId equals e.Id
        orderby w.Date, e.FirstName
        select new WishDto(w.Id, e.Id, e.FirstName + " " + e.LastName, w.Date, w.PreferredShift, w.CreatedAt);
}

public sealed record SaveWishCommand(DateOnly Date, string PreferredShift);

public sealed class SaveWishValidator : AbstractValidator<SaveWishCommand>
{
    public SaveWishValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("Datum je obavezan.");
        RuleFor(x => x.PreferredShift).NotEmpty().MaximumLength(ShiftWish.MaxNameLength);
    }
}

public sealed class SaveWishHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<SaveWishCommand> validator,
    ManagerNotifier managerNotifier,
    TimeProvider time)
{
    /// <summary>
    /// Zapisuje želju za dan ("I ili slobodan"); ista osoba i dan imaju jednu želju, pa nova menja staru.
    /// Važi odmah i ništa ne blokira. Menadžeri dobijaju mejl kad se želja pojavi ili promeni.
    /// </summary>
    public async Task<Result<WishDto>> HandleAsync(SaveWishCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var me = currentUser.EmployeeId!.Value;
        var now = time.GetUtcNow();
        var zone = await DayOffRules.RestaurantZoneAsync(db, cancellationToken);
        var today = DayOffRules.LocalDate(zone, now);

        if (command.Date < today)
            return Error.Validation("wishes.date_in_past", "Želja ne može da bude za prošli dan.");
        if (command.Date > today.AddDays(DayOffRules.MaxDaysAhead))
            return Error.Validation("wishes.date_too_far", $"Želja se javlja najviše {DayOffRules.MaxDaysAhead} dana unapred.");

        var shift = command.PreferredShift.Trim();
        var knownShifts = await db.ShiftTemplates
            .Where(t => db.Employees.Any(e => e.Id == me && e.Positions.Any(p => p.Id == t.PositionId)))
            .Select(t => t.Name)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (!knownShifts.Contains(shift))
            return Error.Validation("wishes.unknown_shift", "Nijedna tvoja pozicija nema smenu sa tim nazivom.");

        var wish = await db.ShiftWishes.FirstOrDefaultAsync(w => w.EmployeeId == me && w.Date == command.Date, cancellationToken);
        var changed = true;
        if (wish is null)
        {
            wish = new ShiftWish(currentUser.RestaurantId!.Value, me, command.Date, shift, now);
            db.ShiftWishes.Add(wish);
        }
        else
        {
            changed = wish.Change(shift, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dva istovremena zahteva za isti dan: jedinstveni indeks pušta samo jedan, a ponovni poziv je izmena.
            return Error.Conflict("wishes.concurrent", "Želja za taj dan se upravo menja. Pokušaj ponovo.");
        }

        if (changed)
            await managerNotifier.WishSavedAsync(wish, cancellationToken);

        return await db.ProjectToDto(db.ShiftWishes.Where(w => w.Id == wish.Id)).FirstAsync(cancellationToken);
    }
}

public sealed class DeleteWishHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var wish = await db.ShiftWishes.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (wish is null)
            return Error.NotFound("wishes.not_found", "Želja nije pronađena.");
        if (wish.EmployeeId != currentUser.EmployeeId)
            return Error.Forbidden("wishes.not_yours", "Želju može da ukloni samo onaj ko ju je javio.");

        db.ShiftWishes.Remove(wish);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class ListWishesHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Sve želje u restoranu (menadžer), po datumu; period je opcion.</summary>
    public Task<IReadOnlyList<WishDto>> HandleAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        QueryAsync(db.ShiftWishes.AsQueryable(), from, to, cancellationToken);

    public Task<IReadOnlyList<WishDto>> HandleMineAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId;
        return QueryAsync(db.ShiftWishes.Where(w => w.EmployeeId == me), from, to, cancellationToken);
    }

    private async Task<IReadOnlyList<WishDto>> QueryAsync(
        IQueryable<ShiftWish> wishes, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (from is { } f)
            wishes = wishes.Where(w => w.Date >= f);
        if (to is { } t)
            wishes = wishes.Where(w => w.Date <= t);

        return await db.ProjectToDto(wishes).Take(WishQueries.MaxListed).ToListAsync(cancellationToken);
    }
}
