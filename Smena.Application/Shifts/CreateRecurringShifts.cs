using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.DayOffs;
using Smena.Domain.DayOffs;
using Smena.Domain.Employees;
using Smena.Domain.Shifts;

namespace Smena.Application.Shifts;

public enum ConflictPolicy
{
    /// <summary>Ako se bilo koji termin preklapa, ne pravi se nijedna smena.</summary>
    Fail = 0,

    /// <summary>Termini koji se preklapaju se preskaču, a ostali se prave.</summary>
    Skip = 1,
}

/// <param name="EmployeeId">Izostavljen znači da se prave otvorene smene.</param>
/// <param name="TemplateId">Šablon smene (npr. "I"); StartTime i EndTime moraju da mu odgovaraju.</param>
public sealed record CreateRecurringShiftsCommand(
    Guid? EmployeeId,
    Guid PositionId,
    DateOnly FromDate,
    DateOnly UntilDate,
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Note,
    ConflictPolicy OnConflict = ConflictPolicy.Fail,
    Guid? TemplateId = null);

public sealed record RecurringShiftsResultDto(
    Guid SeriesId,
    int Created,
    IReadOnlyList<DateOnly> Skipped,
    IReadOnlyList<ShiftDto> Shifts);

public sealed class CreateRecurringShiftsValidator : AbstractValidator<CreateRecurringShiftsCommand>
{
    public const int MaxRangeDays = 180;

    public CreateRecurringShiftsValidator()
    {
        RuleFor(x => x.EmployeeId).NotEqual(Guid.Empty);
        RuleFor(x => x.PositionId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEqual(Guid.Empty);
        RuleFor(x => x.DaysOfWeek).NotEmpty().WithMessage("Izaberite bar jedan dan u nedelji.");
        RuleForEach(x => x.DaysOfWeek).IsInEnum();
        RuleFor(x => x.UntilDate).GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("Kraj perioda ne može biti pre početka.");
        RuleFor(x => x).Must(x => x.UntilDate.DayNumber - x.FromDate.DayNumber < MaxRangeDays)
            .WithName(nameof(CreateRecurringShiftsCommand.UntilDate))
            .WithMessage($"Period ne može biti duži od {MaxRangeDays} dana.");
        RuleFor(x => x.EndTime).NotEqual(x => x.StartTime)
            .WithMessage("Početak i kraj smene ne mogu biti isti.");
        RuleFor(x => x).Must(x => RecurrencePlanner.NominalDuration(x.StartTime, x.EndTime) <= Shift.MaxDuration)
            .WithName(nameof(CreateRecurringShiftsCommand.EndTime))
            .WithMessage($"Smena ne može trajati duže od {Shift.MaxDuration.TotalHours} sati.");
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.OnConflict).IsInEnum();
    }
}

public sealed class CreateRecurringShiftsHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateRecurringShiftsCommand> validator)
{
    public async Task<Result<RecurringShiftsResultDto>> HandleAsync(
        CreateRecurringShiftsCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (command.EmployeeId is { } employeeId)
        {
            var employee = await db.Employees
                .Include(e => e.Positions)
                .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
            if (employee is null)
                return Error.NotFound("employees.not_found", "Zaposleni nije pronađen.");
            if (employee.Status != EmployeeStatus.Active)
                return Error.Conflict("shifts.employee_inactive", "Zaposleni nije aktivan.");
            if (employee.Positions.All(p => p.Id != command.PositionId))
                return Error.Conflict("shifts.position_not_assigned", "Zaposleni nema ovu poziciju.");
        }
        else if (!await db.Positions.AnyAsync(p => p.Id == command.PositionId, cancellationToken))
        {
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");
        }

        if (command.TemplateId is { } templateId)
        {
            var template = await db.ShiftTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
            var (_, templateError) = TemplateRules.ForSeries(template, command.PositionId, command.StartTime, command.EndTime);
            if (templateError is not null)
                return templateError;
        }

        var timeZoneId = await db.Restaurants.Select(r => r.TimeZoneId).FirstAsync(cancellationToken);
        if (!TryFindZone(timeZoneId, out var zone))
            return Error.Conflict("shifts.timezone_unavailable", "Vremenska zona restorana nije dostupna na serveru.");

        var planned = RecurrencePlanner.Plan(
            zone, command.FromDate, command.UntilDate, command.DaysOfWeek.Distinct().ToList(), command.StartTime, command.EndTime);
        if (planned.Count == 0)
            return Error.Validation("shifts.no_occurrences", "Nijedan izabrani dan u nedelji ne pada u zadati period.");

        var windowStart = planned.Min(p => p.StartsAt);
        var windowEnd = planned.Max(p => p.EndsAt);
        // Otvorene smene nemaju zaposlenog, pa nema ni preklapanja.
        var existing = command.EmployeeId is null
            ? []
            : (await db.Shifts
                .Where(s => s.EmployeeId == command.EmployeeId && s.StartsAt < windowEnd && s.EndsAt > windowStart)
                .Select(s => new { s.StartsAt, s.EndsAt })
                .ToListAsync(cancellationToken))
                .Select(s => (s.StartsAt, s.EndsAt)).ToList();

        // Odobreni slobodni dani su isto što i zauzet termin.
        var offDays = new HashSet<DateOnly>();
        if (command.EmployeeId is { } offEmployeeId)
        {
            var firstDay = planned.Min(p => p.Date);
            var lastDay = planned.Max(p => p.Date).AddDays(1);
            offDays = (await db.DayOffRequests
                .Where(d => d.EmployeeId == offEmployeeId && d.Status == DayOffStatus.Approved && d.Date >= firstDay && d.Date <= lastDay)
                .Select(d => d.Date)
                .ToListAsync(cancellationToken)).ToHashSet();
        }

        var conflicts = planned
            .Where(p => existing.Any(s => s.StartsAt < p.EndsAt && s.EndsAt > p.StartsAt)
                        || DayOffRules.CoveredDates(zone, p.StartsAt, p.EndsAt).Any(offDays.Contains))
            .ToList();
        if (conflicts.Count > 0 && command.OnConflict == ConflictPolicy.Fail)
        {
            var dates = string.Join(", ", conflicts.Take(5).Select(c => c.Date.ToString("dd.MM.")));
            var more = conflicts.Count > 5 ? $" i još {conflicts.Count - 5}" : "";
            return Error.Conflict("shifts.series_conflict",
                $"Zaposleni je zauzet (smena ili slobodan dan) u terminima: {dates}{more}. Nijedna smena nije napravljena.");
        }

        var toCreate = planned.Except(conflicts).ToList();
        if (toCreate.Count == 0)
            return Error.Conflict("shifts.series_conflict", "Svi termini su zauzeti (preklapanje sa smenama ili slobodni dani).");

        var seriesId = Guid.CreateVersion7();
        var restaurantId = currentUser.RestaurantId!.Value;
        db.Shifts.AddRange(toCreate.Select(p =>
            new Shift(restaurantId, command.EmployeeId, command.PositionId, p.StartsAt, p.EndsAt, command.Note, seriesId, command.TemplateId)));
        await db.SaveChangesAsync(cancellationToken);

        var shifts = await db.ProjectToDto(db.Shifts.Where(s => s.SeriesId == seriesId)).ToListAsync(cancellationToken);
        return new RecurringShiftsResultDto(seriesId, shifts.Count, conflicts.Select(c => c.Date).ToList(), shifts);
    }

    private static bool TryFindZone(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
