namespace Smena.Application.Shifts;

public sealed record PlannedShift(DateOnly Date, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Razvija ponavljajući obrazac u konkretne termine. Sat u zoni restorana ostaje isti tokom cele
/// godine (smena od 08:00 je uvek u 08:00), pa se u UTC pomera kad se menja letnje/zimsko vreme.
/// </summary>
public static class RecurrencePlanner
{
    /// <summary>Kraj pre ili jednak početku znači da smena traje preko ponoći.</summary>
    public static TimeSpan NominalDuration(TimeOnly start, TimeOnly end)
    {
        var duration = end.ToTimeSpan() - start.ToTimeSpan();
        return duration > TimeSpan.Zero ? duration : duration + TimeSpan.FromDays(1);
    }

    public static IReadOnlyList<PlannedShift> Plan(
        TimeZoneInfo zone,
        DateOnly from,
        DateOnly until,
        IReadOnlyCollection<DayOfWeek> days,
        TimeOnly start,
        TimeOnly end)
    {
        var overnight = end <= start;
        var planned = new List<PlannedShift>();

        for (var date = from; date <= until; date = date.AddDays(1))
        {
            if (!days.Contains(date.DayOfWeek))
                continue;

            var startLocal = date.ToDateTime(start);
            var endLocal = (overnight ? date.AddDays(1) : date).ToDateTime(end);
            planned.Add(new PlannedShift(date, ToUtc(zone, startLocal), ToUtc(zone, endLocal)));
        }

        return planned;
    }

    private static DateTimeOffset ToUtc(TimeZoneInfo zone, DateTime local)
    {
        // Sat koji ne postoji (preskočen pri prelasku na letnje vreme) pomera se unapred.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(30);

        // Sat koji se dešava dvaput (pri povratku na zimsko vreme) uzima se prvi put.
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
