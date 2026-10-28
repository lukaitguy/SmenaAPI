using System.Net;
using System.Net.Http.Json;
using Smena.Application.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ShiftsTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Day = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

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

    private static object Body(Setup s, int fromHour, int toHour, string? note = null) =>
        new { employeeId = s.StaffId, positionId = s.PositionId, startsAt = Day.AddHours(fromHour), endsAt = Day.AddHours(toHour), note };

    private static Task<HttpResponseMessage> Create(Setup s, int fromHour, int toHour) =>
        s.Owner.Http.PostAsJsonAsync("/api/shifts", Body(s, fromHour, toHour), ApiClient.Json);

    private static string Range(int days = 7) =>
        $"from={Uri.EscapeDataString(Day.ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(days).ToString("O"))}";

    [Fact]
    public async Task Manager_creates_a_shift_with_names_and_utc_times()
    {
        var s = await ArrangeAsync();

        var response = await s.Owner.Http.PostAsJsonAsync("/api/shifts", Body(s, 8, 16, "  otvaranje "), ApiClient.Json);
        var shift = await ApiClient.ReadAsync<ShiftDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(s.StaffId, shift.EmployeeId);
        Assert.Equal("Zaposleni Test", shift.EmployeeName);
        Assert.Equal(Day.AddHours(8), shift.StartsAt);
        Assert.Equal("otvaranje", shift.Note);
    }

    [Fact]
    public async Task Employee_without_the_position_cannot_be_scheduled()
    {
        var s = await ArrangeAsync();
        var other = await s.Owner.CreatePositionAsync();

        var response = await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.StaffId, positionId = other.Id, startsAt = Day.AddHours(8), endsAt = Day.AddHours(16) }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Overlapping_shifts_conflict_but_adjacent_ones_do_not()
    {
        var s = await ArrangeAsync();
        Assert.Equal(HttpStatusCode.Created, (await Create(s, 8, 16)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, 15, 20)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, 6, 9)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, 10, 12)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(s, 16, 20)).StatusCode);
    }

    [Fact]
    public async Task Updating_a_shift_does_not_conflict_with_itself()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await Create(s, 8, 16));

        var response = await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{shift.Id}", Body(s, 9, 17), ApiClient.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Day.AddHours(9), (await ApiClient.ReadAsync<ShiftDto>(response)).StartsAt);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 8)]
    [InlineData(0, 17)]
    public async Task Invalid_duration_is_rejected(int fromHour, int toHour)
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, fromHour, toHour)).StatusCode);
    }

    [Fact]
    public async Task Inactive_employee_cannot_be_scheduled()
    {
        var s = await ArrangeAsync();
        await s.Owner.Http.PutAsJsonAsync($"/api/employees/{s.StaffId}/status", new { status = "Inactive" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, 8, 16)).StatusCode);
    }

    [Fact]
    public async Task List_returns_only_shifts_touching_the_range()
    {
        var s = await ArrangeAsync();
        await Create(s, 8, 16);
        await s.Owner.Http.PostAsJsonAsync("/api/shifts", new
        {
            employeeId = s.StaffId,
            positionId = s.PositionId,
            startsAt = Day.AddDays(30),
            endsAt = Day.AddDays(30).AddHours(8),
        }, ApiClient.Json);

        var inRange = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync($"/api/shifts?{Range()}"));

        Assert.Single(inRange);
    }

    [Fact]
    public async Task List_rejects_inverted_or_too_long_ranges()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.Http.GetAsync($"/api/shifts?{Range(-1)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.Http.GetAsync($"/api/shifts?{Range(63)}")).StatusCode);
    }

    [Fact]
    public async Task Staff_sees_only_their_own_shifts_and_cannot_manage()
    {
        var s = await ArrangeAsync();
        await Create(s, 8, 16);
        await s.Owner.PublishAsync(Day, Day.AddDays(1));

        var mine = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Staff.Http.GetAsync($"/api/shifts/mine?{Range()}"));
        var ownersMine = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync($"/api/shifts/mine?{Range()}"));

        Assert.Single(mine);
        Assert.Empty(ownersMine);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Staff.Http.GetAsync($"/api/shifts?{Range()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Staff.Http.PostAsJsonAsync("/api/shifts", Body(s, 18, 20), ApiClient.Json)).StatusCode);
    }

    [Fact]
    public async Task Position_in_use_cannot_be_deleted_until_shifts_are_gone()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await Create(s, 8, 16));

        Assert.Equal(HttpStatusCode.Conflict, (await s.Owner.Http.DeleteAsync($"/api/positions/{s.PositionId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.Http.DeleteAsync($"/api/shifts/{shift.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.Http.DeleteAsync($"/api/positions/{s.PositionId}")).StatusCode);
    }

    [Fact]
    public async Task Restaurants_do_not_see_or_touch_each_others_shifts()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await Create(s, 8, 16));
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        var list = await ApiClient.ReadAsync<List<ShiftDto>>(await stranger.Http.GetAsync($"/api/shifts?{Range()}"));
        var update = await stranger.Http.PutAsJsonAsync($"/api/shifts/{shift.Id}", Body(s, 9, 17), ApiClient.Json);
        var delete = await stranger.Http.DeleteAsync($"/api/shifts/{shift.Id}");

        Assert.Empty(list);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }
}
