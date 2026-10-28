using System.Net;
using System.Net.Http.Json;
using Smena.Application.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class RecurringShiftsTests(ApiFactory factory)
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");
    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];

    // Prvi ponedeljak najmanje dve nedelje unapred, da je sve u budućnosti.
    private static readonly DateOnly Monday = NextMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14));

    private static DateOnly NextMonday(DateOnly date)
    {
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);
        return date;
    }

    private sealed record Setup(ApiClient Owner, ApiClient Staff, Guid StaffId, Guid PositionId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);
        var staffId = (await staff.MeAsync()).EmployeeId;
        var position = await owner.CreatePositionAsync();
        await owner.SetPositionsAsync(staffId, position.Id);
        return new Setup(owner, staff, staffId, position.Id);
    }

    private static object Body(Setup s, DateOnly from, DateOnly until, DayOfWeek[]? days = null,
        string start = "08:00:00", string end = "16:00:00", string onConflict = "Fail") => new
        {
            employeeId = s.StaffId,
            positionId = s.PositionId,
            fromDate = from,
            untilDate = until,
            daysOfWeek = days ?? Weekdays,
            startTime = start,
            endTime = end,
            note = "stalni raspored",
            onConflict,
        };

    private static Task<HttpResponseMessage> Create(Setup s, object body) =>
        s.Owner.Http.PostAsJsonAsync("/api/shifts/recurring", body, ApiClient.Json);

    private static DateTimeOffset LocalToUtc(DateOnly date, int hour) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 0)), Belgrade.GetUtcOffset(date.ToDateTime(new TimeOnly(hour, 0)))).ToUniversalTime();

    private static async Task<List<ShiftDto>> ListAsync(Setup s, DateOnly from, int days = 60) =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync(
            $"/api/shifts?from={Uri.EscapeDataString(LocalToUtc(from, 0).ToString("O"))}&to={Uri.EscapeDataString(LocalToUtc(from.AddDays(days), 0).ToString("O"))}"));

    [Fact]
    public async Task Creates_one_shift_per_matching_day_with_restaurant_local_hours()
    {
        var s = await ArrangeAsync();

        var response = await Create(s, Body(s, Monday, Monday.AddDays(13)));
        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(6, result.Created);
        Assert.Empty(result.Skipped);
        Assert.All(result.Shifts, shift =>
        {
            Assert.Equal(result.SeriesId, shift.SeriesId);
            Assert.Equal(s.StaffId, shift.EmployeeId);
            Assert.Equal("stalni raspored", shift.Note);
        });
        var expected = new[] { 0, 2, 4, 7, 9, 11 }.Select(offset => LocalToUtc(Monday.AddDays(offset), 8));
        Assert.Equal(expected, result.Shifts.Select(x => x.StartsAt).OrderBy(x => x));
    }

    [Fact]
    public async Task Created_shifts_show_up_in_the_schedule_with_their_series()
    {
        var s = await ArrangeAsync();
        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await Create(s, Body(s, Monday, Monday.AddDays(6))));

        var listed = await ListAsync(s, Monday, 7);

        Assert.Equal(3, listed.Count);
        Assert.All(listed, x => Assert.Equal(result.SeriesId, x.SeriesId));
    }

    [Fact]
    public async Task Overnight_pattern_crosses_midnight()
    {
        var s = await ArrangeAsync();

        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(
            await Create(s, Body(s, Monday, Monday, [DayOfWeek.Monday], start: "22:00:00", end: "06:00:00")));

        var shift = Assert.Single(result.Shifts);
        Assert.Equal(LocalToUtc(Monday, 22), shift.StartsAt);
        Assert.Equal(LocalToUtc(Monday.AddDays(1), 6), shift.EndsAt);
    }

    [Fact]
    public async Task Overlap_rejects_the_whole_series_by_default()
    {
        var s = await ArrangeAsync();
        var wednesday = Monday.AddDays(2);
        await s.Owner.Http.PostAsJsonAsync("/api/shifts", new
        {
            employeeId = s.StaffId, positionId = s.PositionId,
            startsAt = LocalToUtc(wednesday, 10), endsAt = LocalToUtc(wednesday, 12),
        }, ApiClient.Json);

        var response = await Create(s, Body(s, Monday, Monday.AddDays(13)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("shifts.series_conflict", await response.Content.ReadAsStringAsync());
        Assert.Single(await ListAsync(s, Monday));   // samo ona ručno napravljena
    }

    [Fact]
    public async Task Skip_policy_creates_the_rest_and_reports_skipped_days()
    {
        var s = await ArrangeAsync();
        var wednesday = Monday.AddDays(2);
        await s.Owner.Http.PostAsJsonAsync("/api/shifts", new
        {
            employeeId = s.StaffId, positionId = s.PositionId,
            startsAt = LocalToUtc(wednesday, 10), endsAt = LocalToUtc(wednesday, 12),
        }, ApiClient.Json);

        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(
            await Create(s, Body(s, Monday, Monday.AddDays(13), onConflict: "Skip")));

        Assert.Equal(5, result.Created);
        Assert.Equal([wednesday], result.Skipped);
    }

    [Fact]
    public async Task Everything_overlapping_is_a_conflict_even_when_skipping()
    {
        var s = await ArrangeAsync();
        await Create(s, Body(s, Monday, Monday.AddDays(6)));

        var response = await Create(s, Body(s, Monday, Monday.AddDays(6), onConflict: "Skip"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_patterns_are_rejected()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, Body(s, Monday.AddDays(7), Monday))).StatusCode);                 // kraj pre početka
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, Body(s, Monday, Monday.AddDays(200)))).StatusCode);               // predug period
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, Body(s, Monday, Monday.AddDays(6), days: []))).StatusCode);       // bez dana
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, Body(s, Monday, Monday.AddDays(6), start: "08:00:00", end: "08:00:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, Body(s, Monday, Monday.AddDays(6), start: "06:00:00", end: "23:00:00"))).StatusCode);   // 17 sati
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, Body(s, Monday.AddDays(1), Monday.AddDays(1)))).StatusCode);      // utorak, a dani su pon/sre/pet
    }

    [Fact]
    public async Task Same_rules_as_single_shifts_apply_to_the_employee()
    {
        var s = await ArrangeAsync();
        var stranger = await s.Owner.InviteAsync(factory);   // bez pozicija
        var strangerId = (await stranger.MeAsync()).EmployeeId;
        object For(Guid employeeId) => new
        {
            employeeId, positionId = s.PositionId, fromDate = Monday, untilDate = Monday.AddDays(6),
            daysOfWeek = Weekdays, startTime = "08:00:00", endTime = "16:00:00",
        };

        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, For(strangerId))).StatusCode);          // nema poziciju
        Assert.Equal(HttpStatusCode.NotFound, (await Create(s, For(Guid.NewGuid()))).StatusCode);

        await s.Owner.Http.PutAsJsonAsync($"/api/employees/{s.StaffId}/status", new { status = "Inactive" }, ApiClient.Json);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, For(s.StaffId))).StatusCode);           // neaktivan
    }

    [Fact]
    public async Task Staff_cannot_create_or_delete_series()
    {
        var s = await ArrangeAsync();

        var create = await s.Staff.Http.PostAsJsonAsync("/api/shifts/recurring", Body(s, Monday, Monday.AddDays(6)), ApiClient.Json);
        var delete = await s.Staff.Http.DeleteAsync($"/api/shifts/series/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task One_occurrence_can_be_edited_without_leaving_the_series()
    {
        var s = await ArrangeAsync();
        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await Create(s, Body(s, Monday, Monday.AddDays(6))));
        var first = result.Shifts.OrderBy(x => x.StartsAt).First();

        var update = await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{first.Id}", new
        {
            employeeId = s.StaffId, positionId = s.PositionId,
            startsAt = LocalToUtc(Monday, 9), endsAt = LocalToUtc(Monday, 17),
        }, ApiClient.Json);
        var edited = await ApiClient.ReadAsync<ShiftDto>(update);

        Assert.Equal(LocalToUtc(Monday, 9), edited.StartsAt);
        Assert.Equal(result.SeriesId, edited.SeriesId);
    }

    [Fact]
    public async Task Deleting_a_series_removes_only_future_shifts()
    {
        var s = await ArrangeAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pastStart = today.AddDays(-5);
        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await Create(s,
            Body(s, pastStart, today.AddDays(5), days: Enum.GetValues<DayOfWeek>(), start: "01:00:00", end: "02:00:00")));
        var now = DateTimeOffset.UtcNow;
        var past = result.Shifts.Count(x => x.StartsAt < now);
        var future = result.Shifts.Count - past;
        Assert.True(past > 0 && future > 0);

        var response = await s.Owner.Http.DeleteAsync($"/api/shifts/series/{result.SeriesId}");

        Assert.Equal(future, (await ApiClient.ReadAsync<DeletedShiftsDto>(response)).Deleted);
        var remaining = await ListAsync(s, pastStart, 20);
        Assert.Equal(past, remaining.Count);
        Assert.All(remaining, x => Assert.True(x.StartsAt < now));
    }

    [Fact]
    public async Task Deleting_can_start_from_a_chosen_date()
    {
        var s = await ArrangeAsync();
        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await Create(s, Body(s, Monday, Monday.AddDays(13))));
        var from = Uri.EscapeDataString(LocalToUtc(Monday.AddDays(7), 0).ToString("O"));

        var deleted = await ApiClient.ReadAsync<DeletedShiftsDto>(
            await s.Owner.Http.DeleteAsync($"/api/shifts/series/{result.SeriesId}?from={from}"));

        Assert.Equal(3, deleted.Deleted);
        Assert.Equal(3, (await ListAsync(s, Monday, 20)).Count);
    }

    [Fact]
    public async Task Unknown_series_is_not_found_and_other_restaurants_cannot_delete_it()
    {
        var s = await ArrangeAsync();
        var result = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await Create(s, Body(s, Monday, Monday.AddDays(6))));
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.Http.DeleteAsync($"/api/shifts/series/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.DeleteAsync($"/api/shifts/series/{result.SeriesId}")).StatusCode);
        Assert.Equal(3, (await ListAsync(s, Monday, 7)).Count);
    }
}
