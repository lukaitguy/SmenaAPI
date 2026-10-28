using System.Net;
using System.Net.Http.Json;
using Smena.Application.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class PublishShiftsTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Day = new(DateTimeOffset.UtcNow.Date.AddDays(14), TimeSpan.Zero);

    private sealed record Setup(ApiClient Owner, ApiClient A, ApiClient B, Guid AId, Guid BId, Guid PositionId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var (aId, bId) = ((await a.MeAsync()).EmployeeId, (await b.MeAsync()).EmployeeId);
        var position = await owner.CreatePositionAsync();
        await owner.SetPositionsAsync(aId, position.Id);
        await owner.SetPositionsAsync(bId, position.Id);
        return new Setup(owner, a, b, aId, bId, position.Id);
    }

    private static async Task<ShiftDto> CreateAsync(Setup s, Guid employeeId, DateTimeOffset from, int hours = 8) =>
        await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId, positionId = s.PositionId, startsAt = from, endsAt = from.AddHours(hours) }, ApiClient.Json));

    private static string Range(int fromDay = 0, int days = 30) =>
        $"from={Uri.EscapeDataString(Day.AddDays(fromDay).ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(fromDay + days).ToString("O"))}";

    private static async Task<List<ShiftDto>> MineAsync(ApiClient client) =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await client.Http.GetAsync($"/api/shifts/mine?{Range()}"));

    private static async Task<List<ShiftDto>> AllAsync(ApiClient owner) =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await owner.Http.GetAsync($"/api/shifts?{Range()}"));

    private int MailsTo(ApiClient client) =>
        factory.Emails.Sent.Count(m => m.To == client.Email && m.Subject == "Objavljen je novi raspored");

    [Fact]
    public async Task New_shifts_are_drafts_until_published()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s, s.AId, Day.AddHours(8));

        var draft = Assert.Single(await AllAsync(s.Owner));
        Assert.Null(draft.PublishedAt);
        Assert.Empty(await MineAsync(s.A));

        var result = await s.Owner.PublishAsync(Day, Day.AddDays(1));

        Assert.Equal(1, result.Published);
        Assert.NotNull(Assert.Single(await AllAsync(s.Owner)).PublishedAt);
        Assert.Single(await MineAsync(s.A));
    }

    [Fact]
    public async Task Only_shifts_starting_inside_the_period_are_published()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s, s.AId, Day.AddHours(8));
        await CreateAsync(s, s.AId, Day.AddDays(10).AddHours(8));

        var result = await s.Owner.PublishAsync(Day, Day.AddDays(7));

        Assert.Equal(1, result.Published);
        var mine = await MineAsync(s.A);
        Assert.Equal(Day.AddHours(8), Assert.Single(mine).StartsAt);
        Assert.Contains(await AllAsync(s.Owner), x => x.StartsAt == Day.AddDays(10).AddHours(8) && x.PublishedAt is null);
    }

    [Fact]
    public async Task Publishing_again_changes_nothing_and_sends_no_new_mail()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s, s.AId, Day.AddHours(8));
        await s.Owner.PublishAsync(Day, Day.AddDays(7));

        var again = await s.Owner.PublishAsync(Day, Day.AddDays(7));

        Assert.Equal(0, again.Published);
        Assert.Equal(0, again.EmployeesNotified);
        Assert.Equal(1, MailsTo(s.A));
    }

    [Fact]
    public async Task Each_employee_gets_one_mail_with_only_their_own_shifts()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s, s.AId, Day.AddHours(8));
        await CreateAsync(s, s.AId, Day.AddDays(1).AddHours(8));
        await CreateAsync(s, s.BId, Day.AddDays(2).AddHours(8));

        var result = await s.Owner.PublishAsync(Day, Day.AddDays(7));

        Assert.Equal(3, result.Published);
        Assert.Equal(2, result.EmployeesNotified);
        Assert.Equal(1, MailsTo(s.A));
        Assert.Equal(1, MailsTo(s.B));
        Assert.Contains("2 smena", factory.Emails.Sent.Single(m => m.To == s.A.Email && m.Subject == "Objavljen je novi raspored").TextBody);
        Assert.Contains("1 smena", factory.Emails.Sent.Single(m => m.To == s.B.Email && m.Subject == "Objavljen je novi raspored").TextBody);
    }

    [Fact]
    public async Task Employees_without_shifts_get_no_mail_and_failed_mail_does_not_block_publishing()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s, s.AId, Day.AddHours(8));
        factory.Emails.FailFor(s.A.Email);

        var result = await s.Owner.PublishAsync(Day, Day.AddDays(7));

        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.EmployeesNotified);
        Assert.Equal(0, MailsTo(s.B));
        Assert.Single(await MineAsync(s.A));
    }

    [Fact]
    public async Task Staff_cannot_publish_and_the_period_is_validated()
    {
        var s = await ArrangeAsync();

        var staff = await s.A.Http.PostAsJsonAsync("/api/shifts/publish", new { from = Day, to = Day.AddDays(7) }, ApiClient.Json);
        var inverted = await s.Owner.Http.PostAsJsonAsync("/api/shifts/publish", new { from = Day.AddDays(7), to = Day }, ApiClient.Json);
        var tooLong = await s.Owner.Http.PostAsJsonAsync("/api/shifts/publish", new { from = Day, to = Day.AddDays(63) }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Editing_a_published_shift_keeps_it_published()
    {
        var s = await ArrangeAsync();
        var shift = await CreateAsync(s, s.AId, Day.AddHours(8));
        await s.Owner.PublishAsync(Day, Day.AddDays(1));

        var edited = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{shift.Id}",
            new { employeeId = s.AId, positionId = s.PositionId, startsAt = Day.AddHours(9), endsAt = Day.AddHours(17) }, ApiClient.Json));

        Assert.NotNull(edited.PublishedAt);
        Assert.Equal(Day.AddHours(9), Assert.Single(await MineAsync(s.A)).StartsAt);
    }

    [Fact]
    public async Task Recurring_series_also_start_as_drafts_and_can_be_published_week_by_week()
    {
        var s = await ArrangeAsync();
        var monday = DateOnly.FromDateTime(Day.UtcDateTime);
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(1);

        var series = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts/recurring", new
        {
            employeeId = s.AId, positionId = s.PositionId, fromDate = monday, untilDate = monday.AddDays(13),
            daysOfWeek = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            startTime = "08:00:00", endTime = "16:00:00",
        }, ApiClient.Json));
        Assert.All(series.Shifts, x => Assert.Null(x.PublishedAt));
        Assert.Empty(await MineAsync(s.A));

        var weekStart = new DateTimeOffset(monday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var result = await s.Owner.PublishAsync(weekStart, weekStart.AddDays(7));

        Assert.Equal(3, result.Published);
        Assert.Equal(3, (await MineAsync(s.A)).Count);
    }

    [Fact]
    public async Task A_draft_cannot_be_handed_over_until_it_is_published()
    {
        var s = await ArrangeAsync();
        var shift = await CreateAsync(s, s.AId, Day.AddHours(8));
        Task<HttpResponseMessage> Request() => s.A.Http.PostAsJsonAsync("/api/swaps",
            new { shiftId = shift.Id, targetEmployeeId = s.BId }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.NotFound, (await Request()).StatusCode);

        await s.Owner.PublishAsync(Day, Day.AddDays(1));
        Assert.Equal(HttpStatusCode.Created, (await Request()).StatusCode);
    }

    [Fact]
    public async Task Another_restaurant_cannot_publish_my_drafts()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s, s.AId, Day.AddHours(8));
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        var result = await stranger.PublishAsync(Day, Day.AddDays(7));

        Assert.Equal(0, result.Published);
        Assert.Null(Assert.Single(await AllAsync(s.Owner)).PublishedAt);
    }
}
