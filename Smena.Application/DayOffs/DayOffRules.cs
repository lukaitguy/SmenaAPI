using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Shifts;
using Smena.Domain.DayOffs;

namespace Smena.Application.DayOffs;

/// <summary>Kalendarski dani se uvek računaju u zoni restorana, a ne u UTC-u.</summary>
public static class DayOffRules
{
    public const int MaxDaysAhead = 365;

    public static async Task<TimeZoneInfo> RestaurantZoneAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        var id = await db.Restaurants.Select(r => r.TimeZoneId).FirstAsync(cancellationToken);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static DateOnly LocalDate(TimeZoneInfo zone, DateTimeOffset moment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone).DateTime);

    /// <summary>Kalendarski dani (po zoni restorana) koje smena dodiruje; smena koja se završava tačno u ponoć ne ulazi u sledeći dan.</summary>
    public static IReadOnlyList<DateOnly> CoveredDates(TimeZoneInfo zone, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var first = LocalDate(zone, startsAt);
        var last = LocalDate(zone, endsAt.AddTicks(-1));
        var dates = new List<DateOnly>();
        for (var date = first; date <= last; date = date.AddDays(1))
            dates.Add(date);

        return dates.Count == 0 ? [first] : dates;
    }

    /// <summary>Početak i kraj (isključivo) kalendarskog dana kao trenuci u UTC-u.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) DayBounds(TimeZoneInfo zone, DateOnly date) =>
        (RecurrencePlanner.ToUtc(zone, date.ToDateTime(TimeOnly.MinValue)),
         RecurrencePlanner.ToUtc(zone, date.AddDays(1).ToDateTime(TimeOnly.MinValue)));

    /// <summary>Prvi odobren slobodan dan zaposlenog koji smena dodiruje, ili null.</summary>
    public static async Task<DateOnly?> FindApprovedDayOffAsync(
        IAppDbContext db, TimeZoneInfo zone, Guid employeeId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        var dates = CoveredDates(zone, startsAt, endsAt).ToList();
        return await db.DayOffRequests
            .Where(d => d.EmployeeId == employeeId && d.Status == DayOffStatus.Approved && dates.Contains(d.Date))
            .OrderBy(d => d.Date)
            .Select(d => (DateOnly?)d.Date)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static Error NotFound() => Error.NotFound("dayoffs.not_found", "Zahtev nije pronađen.");

    public static Error InvalidState(DayOffStatus status) =>
        Error.Conflict("dayoffs.invalid_state", $"Zahtev je u stanju {status} i ta radnja nije moguća.");
}
