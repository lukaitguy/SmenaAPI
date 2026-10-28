using Smena.Application.DayOffs;
using Smena.Domain.DayOffs;

namespace Smena.Tests.Domain;

public class DayOffRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Manager = Guid.NewGuid();

    private static DayOffRequest New(string? reason = "  porodica  ") =>
        new(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 11, 20), reason, Now);

    [Fact]
    public void New_request_is_pending_and_active_and_the_reason_is_trimmed()
    {
        var request = New();

        Assert.Equal(DayOffStatus.Pending, request.Status);
        Assert.True(request.IsActive);
        Assert.Equal("porodica", request.Reason);
    }

    [Fact]
    public void Blank_reason_becomes_null() => Assert.Null(New("  ").Reason);

    [Fact]
    public void Approve_records_the_manager()
    {
        var request = New();

        request.Approve(Manager, Now.AddHours(1));

        Assert.Equal(DayOffStatus.Approved, request.Status);
        Assert.Equal(Manager, request.DecidedByEmployeeId);
        Assert.True(request.IsActive);
    }

    [Fact]
    public void Manager_can_reject_pending_and_revoke_approved()
    {
        var pending = New();
        pending.Reject(Manager, Now);

        var approved = New();
        approved.Approve(Manager, Now);
        approved.Reject(Manager, Now);

        Assert.Equal(DayOffStatus.Rejected, pending.Status);
        Assert.Equal(DayOffStatus.Rejected, approved.Status);
        Assert.False(approved.IsActive);
    }

    [Fact]
    public void Employee_can_cancel_pending_and_approved()
    {
        var pending = New();
        pending.Cancel(Now);

        var approved = New();
        approved.Approve(Manager, Now);
        approved.Cancel(Now);

        Assert.Equal(DayOffStatus.Cancelled, pending.Status);
        Assert.Equal(DayOffStatus.Cancelled, approved.Status);
    }

    [Fact]
    public void Only_a_pending_request_can_be_approved()
    {
        var approved = New();
        approved.Approve(Manager, Now);

        Assert.Throws<InvalidOperationException>(() => approved.Approve(Manager, Now));
    }

    [Theory]
    [InlineData(DayOffStatus.Rejected)]
    [InlineData(DayOffStatus.Cancelled)]
    public void Closed_requests_cannot_change_anymore(DayOffStatus finalStatus)
    {
        var request = New();
        if (finalStatus == DayOffStatus.Rejected) request.Reject(Manager, Now); else request.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => request.Approve(Manager, Now));
        Assert.Throws<InvalidOperationException>(() => request.Reject(Manager, Now));
        Assert.Throws<InvalidOperationException>(() => request.Cancel(Now));
    }
}

public class DayOffRulesTests
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Shift_inside_one_local_day_covers_that_day_only()
    {
        // 08:00-16:00 po lokalnom (zima, +1)
        var dates = DayOffRules.CoveredDates(Belgrade, Utc(11, 20, 7), Utc(11, 20, 15));

        Assert.Equal([new DateOnly(2026, 11, 20)], dates);
    }

    [Fact]
    public void Overnight_shift_covers_both_days()
    {
        // 22:00 -> 06:00 po lokalnom
        var dates = DayOffRules.CoveredDates(Belgrade, Utc(11, 20, 21), Utc(11, 21, 5));

        Assert.Equal([new DateOnly(2026, 11, 20), new(2026, 11, 21)], dates);
    }

    [Fact]
    public void Shift_ending_exactly_at_local_midnight_does_not_touch_the_next_day()
    {
        // 16:00 -> 24:00 po lokalnom (zima, ponoć je 23:00 UTC)
        var dates = DayOffRules.CoveredDates(Belgrade, Utc(11, 20, 15), Utc(11, 20, 23));

        Assert.Equal([new DateOnly(2026, 11, 20)], dates);
    }

    [Fact]
    public void Calendar_day_comes_from_the_restaurant_zone_not_from_utc()
    {
        // 23:30 UTC je već sledeći dan u Beogradu
        Assert.Equal(new DateOnly(2026, 11, 21), DayOffRules.LocalDate(Belgrade, Utc(11, 20, 23, 30)));
        Assert.Equal(new DateOnly(2026, 11, 20), DayOffRules.LocalDate(TimeZoneInfo.Utc, Utc(11, 20, 23, 30)));
    }

    [Fact]
    public void Day_bounds_follow_the_local_midnights_and_the_clock_change()
    {
        var (winterStart, winterEnd) = DayOffRules.DayBounds(Belgrade, new DateOnly(2026, 11, 20));
        Assert.Equal(Utc(11, 19, 23), winterStart);
        Assert.Equal(Utc(11, 20, 23), winterEnd);

        // 25.10.2026. se vraća na zimsko vreme: taj dan traje 25 sati
        var (start, end) = DayOffRules.DayBounds(Belgrade, new DateOnly(2026, 10, 25));
        Assert.Equal(TimeSpan.FromHours(25), end - start);
    }
}
