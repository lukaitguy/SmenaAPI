using System.Net;
using System.Net.Http.Json;
using Smena.Application.Claims;
using Smena.Application.DayOffs;
using Smena.Application.Shifts;
using Smena.Application.Swaps;
using Smena.Domain.DayOffs;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class DayOffsTests(ApiFactory factory)
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Belgrade).DateTime);
    private static readonly DateOnly Day = Today.AddDays(21);   // restoran je u Europe/Belgrade (podrazumevana zona)

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

    private static DateTimeOffset Local(DateOnly date, int hour, int minute = 0)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Belgrade.GetUtcOffset(local)).ToUniversalTime();
    }

    private static Task<HttpResponseMessage> Request(ApiClient client, DateOnly date, string? reason = null) =>
        client.Http.PostAsJsonAsync("/api/dayoffs", new { date, reason }, ApiClient.Json);

    private static async Task<DayOffDto> RequestOkAsync(ApiClient client, DateOnly date) =>
        await ApiClient.ReadAsync<DayOffDto>(await Request(client, date));

    private static Task<HttpResponseMessage> Act(ApiClient client, Guid id, string action) =>
        client.Http.PostAsync($"/api/dayoffs/{id}/{action}", null);

    private static async Task<DayOffDto> ApprovedAsync(Setup s, ApiClient employee, DateOnly date)
    {
        var request = await RequestOkAsync(employee, date);
        return await ApiClient.ReadAsync<DayOffDto>(await Act(s.Owner, request.Id, "approve"));
    }

    private static Task<HttpResponseMessage> Assign(Setup s, Guid employeeId, DateTimeOffset from, DateTimeOffset to) =>
        s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId, positionId = s.PositionId, startsAt = from, endsAt = to }, ApiClient.Json);

    private int Mails(ApiClient client, string subject) =>
        factory.Emails.Sent.Count(m => m.To == client.Email && m.Subject == subject);

    [Fact]
    public async Task Employee_requests_and_manager_approves()
    {
        var s = await ArrangeAsync();

        var response = await Request(s.A, Day, "  porodična slava ");
        var request = await ApiClient.ReadAsync<DayOffDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(DayOffStatus.Pending, request.Status);
        Assert.Equal(Day, request.Date);
        Assert.Equal(s.AId, request.EmployeeId);
        Assert.Equal("porodična slava", request.Reason);

        var approved = await ApiClient.ReadAsync<DayOffDto>(await Act(s.Owner, request.Id, "approve"));

        Assert.Equal(DayOffStatus.Approved, approved.Status);
        Assert.Equal(1, Mails(s.A, "Slobodan dan odobren"));
    }

    [Fact]
    public async Task There_is_no_weekly_limit()
    {
        var s = await ArrangeAsync();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Created, (await Request(s.A, Day.AddDays(i))).StatusCode);

        var mine = await ApiClient.ReadAsync<List<DayOffDto>>(await s.A.Http.GetAsync("/api/dayoffs/mine"));
        Assert.Equal(5, mine.Count);
    }

    [Fact]
    public async Task Date_must_be_reasonable()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Request(s.A, Today.AddDays(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Request(s.A, Today.AddDays(DayOffRules.MaxDaysAhead + 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Request(s.A, default)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Request(s.A, Day, new string('x', 501))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Request(s.A, Today)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Request(s.A, Today.AddDays(DayOffRules.MaxDaysAhead))).StatusCode);
    }

    [Fact]
    public async Task Same_day_cannot_be_requested_twice_until_the_first_one_is_closed()
    {
        var s = await ArrangeAsync();
        var first = await RequestOkAsync(s.A, Day);

        Assert.Equal(HttpStatusCode.Conflict, (await Request(s.A, Day)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Request(s.B, Day)).StatusCode);   // kolega može isti dan

        await Act(s.A, first.Id, "cancel");
        Assert.Equal(HttpStatusCode.Created, (await Request(s.A, Day)).StatusCode);
    }

    [Fact]
    public async Task Only_the_manager_decides_and_only_the_owner_cancels()
    {
        var s = await ArrangeAsync();
        var request = await RequestOkAsync(s.A, Day);

        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.A, request.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, request.Id, "reject")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, request.Id, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.A.Http.GetAsync("/api/dayoffs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(s.Owner, Guid.NewGuid(), "approve")).StatusCode);
    }

    [Fact]
    public async Task Cannot_approve_while_the_employee_has_a_shift_that_day()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await Assign(s, s.AId, Local(Day, 10), Local(Day, 18)));   // nacrt, ali se računa
        var request = await RequestOkAsync(s.A, Day);

        var blocked = await Act(s.Owner, request.Id, "approve");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("dayoffs.has_shifts", await blocked.Content.ReadAsStringAsync());

        await s.Owner.Http.DeleteAsync($"/api/shifts/{shift.Id}");
        Assert.Equal(HttpStatusCode.OK, (await Act(s.Owner, request.Id, "approve")).StatusCode);
    }

    [Fact]
    public async Task Overnight_shift_from_the_evening_before_also_blocks_approval()
    {
        var s = await ArrangeAsync();
        await Assign(s, s.AId, Local(Day.AddDays(-1), 22), Local(Day, 6));
        var request = await RequestOkAsync(s.A, Day);

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, request.Id, "approve")).StatusCode);
    }

    [Fact]
    public async Task Approved_day_off_blocks_assigning_a_shift_that_day()
    {
        var s = await ArrangeAsync();
        await ApprovedAsync(s, s.A, Day);

        var blocked = await Assign(s, s.AId, Local(Day, 10), Local(Day, 18));

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("shifts.day_off", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, (await Assign(s, s.AId, Local(Day.AddDays(1), 10), Local(Day.AddDays(1), 18))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Assign(s, s.BId, Local(Day, 10), Local(Day, 18))).StatusCode);   // kolega može
    }

    [Fact]
    public async Task Day_boundaries_follow_the_restaurant_zone()
    {
        var s = await ArrangeAsync();
        await ApprovedAsync(s, s.A, Day);

        // Završava se tačno u lokalnu ponoć: ne dodiruje slobodan dan.
        Assert.Equal(HttpStatusCode.Created, (await Assign(s, s.AId, Local(Day.AddDays(-1), 16), Local(Day, 0))).StatusCode);
        // Počinje 00:30 lokalno (u UTC-u je to još prethodni dan): to je već slobodan dan.
        Assert.Equal(HttpStatusCode.Conflict, (await Assign(s, s.AId, Local(Day, 0, 30), Local(Day, 8))).StatusCode);
        // Počinje uveče pre slobodnog dana i zalazi u njega.
        Assert.Equal(HttpStatusCode.Conflict, (await Assign(s, s.AId, Local(Day.AddDays(-1), 22), Local(Day, 6))).StatusCode);
        // Počinje u lokalnu ponoć sledećeg dana: slobodan dan je gotov.
        Assert.Equal(HttpStatusCode.Created, (await Assign(s, s.AId, Local(Day.AddDays(1), 0), Local(Day.AddDays(1), 8))).StatusCode);
    }

    [Fact]
    public async Task Pending_request_does_not_block_scheduling_yet()
    {
        var s = await ArrangeAsync();
        await RequestOkAsync(s.A, Day);

        Assert.Equal(HttpStatusCode.Created, (await Assign(s, s.AId, Local(Day, 10), Local(Day, 18))).StatusCode);
    }

    [Fact]
    public async Task Reassigning_a_shift_to_someone_on_a_day_off_is_blocked()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await Assign(s, s.BId, Local(Day, 10), Local(Day, 18)));
        await ApprovedAsync(s, s.A, Day);

        var response = await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{shift.Id}",
            new { employeeId = s.AId, positionId = s.PositionId, startsAt = Local(Day, 10), endsAt = Local(Day, 18) }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Recurring_series_treats_days_off_as_conflicts()
    {
        var s = await ArrangeAsync();
        var monday = Day;
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(1);
        var wednesday = monday.AddDays(2);
        await ApprovedAsync(s, s.A, wednesday);
        object Body(string onConflict) => new
        {
            employeeId = s.AId, positionId = s.PositionId, fromDate = monday, untilDate = monday.AddDays(6),
            daysOfWeek = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            startTime = "08:00:00", endTime = "16:00:00", onConflict,
        };

        var failed = await s.Owner.Http.PostAsJsonAsync("/api/shifts/recurring", Body("Fail"), ApiClient.Json);
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);

        var skipped = await ApiClient.ReadAsync<RecurringShiftsResultDto>(
            await s.Owner.Http.PostAsJsonAsync("/api/shifts/recurring", Body("Skip"), ApiClient.Json));
        Assert.Equal(2, skipped.Created);
        Assert.Equal([wednesday], skipped.Skipped);
    }

    [Fact]
    public async Task Employee_on_a_day_off_cannot_claim_an_open_shift_or_receive_a_handover()
    {
        var s = await ArrangeAsync();
        await ApprovedAsync(s, s.B, Day);
        var open = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { positionId = s.PositionId, startsAt = Local(Day, 10), endsAt = Local(Day, 18) }, ApiClient.Json));
        var mine = await ApiClient.ReadAsync<ShiftDto>(await Assign(s, s.AId, Local(Day.AddDays(-2), 10), Local(Day.AddDays(-2), 18)));
        await s.Owner.PublishAsync(Local(Day.AddDays(-3), 0), Local(Day.AddDays(1), 0));
        await ApprovedAsync(s, s.B, Day.AddDays(-2));

        var claim = await s.B.Http.PostAsJsonAsync($"/api/shifts/{open.Id}/claims", new { note = (string?)null }, ApiClient.Json);
        var handover = await s.A.Http.PostAsJsonAsync("/api/swaps", new { shiftId = mine.Id, targetEmployeeId = s.BId }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, claim.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, handover.StatusCode);
    }

    [Fact]
    public async Task Approval_of_a_claim_is_blocked_if_a_day_off_was_approved_in_the_meantime()
    {
        var s = await ArrangeAsync();
        var open = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { positionId = s.PositionId, startsAt = Local(Day, 10), endsAt = Local(Day, 18) }, ApiClient.Json));
        await s.Owner.PublishAsync(Local(Day, 0), Local(Day.AddDays(1), 0));
        var claim = await ApiClient.ReadAsync<ClaimDto>(await s.A.Http.PostAsJsonAsync(
            $"/api/shifts/{open.Id}/claims", new { note = (string?)null }, ApiClient.Json));
        await ApprovedAsync(s, s.A, Day);

        var approve = await s.Owner.Http.PostAsync($"/api/claims/{claim.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
    }

    [Fact]
    public async Task Rejecting_or_cancelling_frees_the_day_again()
    {
        var s = await ArrangeAsync();
        var first = await ApprovedAsync(s, s.A, Day);
        var second = await ApprovedAsync(s, s.A, Day.AddDays(1));
        Assert.Equal(HttpStatusCode.Conflict, (await Assign(s, s.AId, Local(Day, 10), Local(Day, 18))).StatusCode);

        // Menadžer povlači odobren slobodan dan.
        Assert.Equal(DayOffStatus.Rejected, (await ApiClient.ReadAsync<DayOffDto>(await Act(s.Owner, first.Id, "reject"))).Status);
        Assert.Equal(HttpStatusCode.Created, (await Assign(s, s.AId, Local(Day, 10), Local(Day, 18))).StatusCode);
        Assert.Equal(1, Mails(s.A, "Slobodan dan nije odobren"));

        // Zaposleni otkazuje odobren slobodan dan.
        Assert.Equal(DayOffStatus.Cancelled, (await ApiClient.ReadAsync<DayOffDto>(await Act(s.A, second.Id, "cancel"))).Status);
        Assert.Equal(HttpStatusCode.Created, (await Assign(s, s.AId, Local(Day.AddDays(1), 10), Local(Day.AddDays(1), 18))).StatusCode);
    }

    [Fact]
    public async Task Closed_requests_cannot_be_decided_again()
    {
        var s = await ArrangeAsync();
        var rejected = await RequestOkAsync(s.A, Day);
        await Act(s.Owner, rejected.Id, "reject");
        var approved = await ApprovedAsync(s, s.A, Day.AddDays(1));

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, rejected.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, rejected.Id, "reject")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.A, rejected.Id, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, approved.Id, "approve")).StatusCode);
    }

    [Fact]
    public async Task Failing_mail_does_not_block_the_decision()
    {
        var s = await ArrangeAsync();
        var request = await RequestOkAsync(s.A, Day);
        factory.Emails.FailFor(s.A.Email);

        Assert.Equal(HttpStatusCode.OK, (await Act(s.Owner, request.Id, "approve")).StatusCode);
    }

    [Fact]
    public async Task Lists_can_be_filtered_by_status_and_date()
    {
        var s = await ArrangeAsync();
        var approved = await ApprovedAsync(s, s.A, Day);
        await RequestOkAsync(s.B, Day.AddDays(10));
        await RequestOkAsync(s.A, Day.AddDays(20));

        async Task<List<DayOffDto>> Get(string query) =>
            await ApiClient.ReadAsync<List<DayOffDto>>(await s.Owner.Http.GetAsync($"/api/dayoffs{query}"));

        Assert.Equal(3, (await Get("")).Count);
        Assert.Equal(approved.Id, Assert.Single(await Get("?status=Approved")).Id);
        Assert.Equal(2, (await Get("?status=Pending")).Count);
        Assert.Single(await Get($"?from={Day.AddDays(5):yyyy-MM-dd}&to={Day.AddDays(15):yyyy-MM-dd}"));
        Assert.Equal(2, (await ApiClient.ReadAsync<List<DayOffDto>>(await s.A.Http.GetAsync("/api/dayoffs/mine"))).Count);
        Assert.Single(await ApiClient.ReadAsync<List<DayOffDto>>(await s.B.Http.GetAsync("/api/dayoffs/mine")));
    }

    [Fact]
    public async Task Restaurants_do_not_see_or_touch_each_others_requests()
    {
        var s = await ArrangeAsync();
        var request = await RequestOkAsync(s.A, Day);
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Empty(await ApiClient.ReadAsync<List<DayOffDto>>(await stranger.Http.GetAsync("/api/dayoffs")));
        Assert.Equal(HttpStatusCode.NotFound, (await Act(stranger, request.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(stranger, request.Id, "reject")).StatusCode);
    }
}
