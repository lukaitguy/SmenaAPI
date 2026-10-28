using Smena.Application.Shifts;

namespace Smena.Tests.Domain;

public class RecurrencePlannerTests
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");
    private static readonly DayOfWeek[] Everyday = Enum.GetValues<DayOfWeek>();

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Only_the_selected_weekdays_inside_the_period_are_planned()
    {
        // 02.11.2026. je ponedeljak.
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 11, 2), new(2026, 11, 15),
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], new(8, 0), new(16, 0));

        Assert.Equal(6, plan.Count);
        Assert.Equal(
            [new DateOnly(2026, 11, 2), new(2026, 11, 4), new(2026, 11, 6), new(2026, 11, 9), new(2026, 11, 11), new(2026, 11, 13)],
            plan.Select(p => p.Date));
    }

    [Fact]
    public void Period_without_a_matching_weekday_plans_nothing()
    {
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 11, 3), new(2026, 11, 3), [DayOfWeek.Monday], new(8, 0), new(16, 0));

        Assert.Empty(plan);
    }

    [Fact]
    public void Local_hour_stays_the_same_across_the_autumn_clock_change()
    {
        // 25.10.2026. u 03:00 se vraća na 02:00: UTC pomak sa +2 na +1.
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 10, 24), new(2026, 10, 26), Everyday, new(8, 0), new(16, 0));

        Assert.Equal(Utc(2026, 10, 24, 6), plan[0].StartsAt);
        Assert.Equal(Utc(2026, 10, 25, 7), plan[1].StartsAt);
        Assert.Equal(Utc(2026, 10, 26, 7), plan[2].StartsAt);
        Assert.All(plan, p => Assert.Equal(TimeSpan.FromHours(8), p.EndsAt - p.StartsAt));
    }

    [Fact]
    public void Local_hour_stays_the_same_across_the_spring_clock_change()
    {
        // 29.03.2026. u 02:00 se pomera na 03:00: UTC pomak sa +1 na +2.
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 3, 28), new(2026, 3, 30), Everyday, new(8, 0), new(16, 0));

        Assert.Equal(Utc(2026, 3, 28, 7), plan[0].StartsAt);
        Assert.Equal(Utc(2026, 3, 29, 6), plan[1].StartsAt);
        Assert.Equal(Utc(2026, 3, 30, 6), plan[2].StartsAt);
    }

    [Fact]
    public void Overnight_shift_ends_on_the_next_day()
    {
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 11, 2), new(2026, 11, 2), Everyday, new(22, 0), new(6, 0));

        var shift = Assert.Single(plan);
        Assert.Equal(Utc(2026, 11, 2, 21), shift.StartsAt);
        Assert.Equal(Utc(2026, 11, 3, 5), shift.EndsAt);
    }

    [Fact]
    public void Overnight_shift_over_the_autumn_change_is_an_hour_longer()
    {
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 10, 24), new(2026, 10, 24), Everyday, new(22, 0), new(6, 0));

        Assert.Equal(TimeSpan.FromHours(9), plan.Single().EndsAt - plan.Single().StartsAt);
    }

    [Fact]
    public void Time_that_does_not_exist_is_moved_forward()
    {
        // 29.03.2026. 02:30 ne postoji (sat preskače sa 02:00 na 03:00).
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 3, 29), new(2026, 3, 29), Everyday, new(2, 30), new(10, 0));

        Assert.Equal(Utc(2026, 3, 29, 1), plan.Single().StartsAt);   // 03:00 po lokalnom, letnje vreme
        Assert.Equal(Utc(2026, 3, 29, 8), plan.Single().EndsAt);
    }

    [Fact]
    public void Time_that_happens_twice_uses_the_first_occurrence()
    {
        // 25.10.2026. 02:30 se dešava dvaput; prvi put je još letnje vreme (+2).
        var plan = RecurrencePlanner.Plan(Belgrade, new(2026, 10, 25), new(2026, 10, 25), Everyday, new(2, 30), new(10, 0));

        Assert.Equal(Utc(2026, 10, 25, 0, 30), plan.Single().StartsAt);
    }

    [Fact]
    public void Utc_zone_has_no_shift_in_offset()
    {
        var plan = RecurrencePlanner.Plan(TimeZoneInfo.Utc, new(2026, 10, 25), new(2026, 10, 25), Everyday, new(8, 0), new(16, 0));

        Assert.Equal(Utc(2026, 10, 25, 8), plan.Single().StartsAt);
    }

    [Theory]
    [InlineData(8, 16, 8)]
    [InlineData(22, 6, 8)]
    [InlineData(0, 23, 23)]
    [InlineData(16, 8, 16)]
    [InlineData(10, 10, 24)]
    public void Nominal_duration_wraps_past_midnight(int startHour, int endHour, int expectedHours)
    {
        var duration = RecurrencePlanner.NominalDuration(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

        Assert.Equal(TimeSpan.FromHours(expectedHours), duration);
    }
}
