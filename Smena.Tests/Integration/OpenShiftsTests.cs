using System.Net;
using System.Net.Http.Json;
using Smena.Application.Claims;
using Smena.Application.Shifts;
using Smena.Domain.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class OpenShiftsTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Day = new(DateTimeOffset.UtcNow.Date.AddDays(14), TimeSpan.Zero);

    /// <summary>A i B imaju poziciju smene, C ima samo drugu poziciju.</summary>
    private sealed record Setup(
        ApiClient Owner, ApiClient A, ApiClient B, ApiClient C,
        Guid AId, Guid BId, Guid CId, Guid PositionId, Guid OtherPositionId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var c = await owner.InviteAsync(factory);
        var (aId, bId, cId) = ((await a.MeAsync()).EmployeeId, (await b.MeAsync()).EmployeeId, (await c.MeAsync()).EmployeeId);
        var position = await owner.CreatePositionAsync();
        var other = await owner.CreatePositionAsync();
        await owner.SetPositionsAsync(aId, position.Id);
        await owner.SetPositionsAsync(bId, position.Id);
        await owner.SetPositionsAsync(cId, other.Id);
        return new Setup(owner, a, b, c, aId, bId, cId, position.Id, other.Id);
    }

    private static Task<HttpResponseMessage> CreateOpen(Setup s, DateTimeOffset from, int hours = 8, Guid? positionId = null) =>
        s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { positionId = positionId ?? s.PositionId, startsAt = from, endsAt = from.AddHours(hours) }, ApiClient.Json);

    private static async Task<ShiftDto> CreateOpenOkAsync(Setup s, DateTimeOffset from, bool publish = true, int hours = 8)
    {
        var shift = await ApiClient.ReadAsync<ShiftDto>(await CreateOpen(s, from, hours));
        if (publish)
            await s.Owner.PublishAsync(from, from.AddMinutes(1));
        return shift;
    }

    private static Task<HttpResponseMessage> Claim(ApiClient client, Guid shiftId, string? note = null) =>
        client.Http.PostAsJsonAsync($"/api/shifts/{shiftId}/claims", new { note }, ApiClient.Json);

    private static async Task<ClaimDto> ClaimOkAsync(ApiClient client, Guid shiftId) =>
        await ApiClient.ReadAsync<ClaimDto>(await Claim(client, shiftId));

    private static Task<HttpResponseMessage> Act(ApiClient client, Guid claimId, string action) =>
        client.Http.PostAsync($"/api/claims/{claimId}/{action}", null);

    private static string Range() =>
        $"from={Uri.EscapeDataString(Day.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(20).ToString("O"))}";

    private static async Task<List<ShiftDto>> OpenFor(ApiClient client) =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await client.Http.GetAsync($"/api/shifts/open?{Range()}"));

    private static async Task<ShiftDto> ShiftNowAsync(Setup s, Guid shiftId) =>
        (await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync($"/api/shifts?{Range()}"))).Single(x => x.Id == shiftId);

    private int Mails(ApiClient client, string subject) =>
        factory.Emails.Sent.Count(m => m.To == client.Email && m.Subject == subject);

    [Fact]
    public async Task Manager_creates_an_open_shift_without_an_employee()
    {
        var s = await ArrangeAsync();

        var response = await CreateOpen(s, Day.AddHours(8));
        var shift = await ApiClient.ReadAsync<ShiftDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(shift.EmployeeId);
        Assert.Null(shift.EmployeeName);
        Assert.Equal(s.PositionId, shift.PositionId);
        Assert.Null(shift.PublishedAt);
    }

    [Fact]
    public async Task Open_shift_needs_an_existing_position()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await CreateOpen(s, Day.AddHours(8), positionId: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateOpen(s, Day.AddHours(8), hours: 20)).StatusCode);
    }

    [Fact]
    public async Task Employees_see_published_open_shifts_only_for_their_own_positions()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8), publish: false);

        Assert.Empty(await OpenFor(s.A));                       // nacrt

        await s.Owner.PublishAsync(Day, Day.AddDays(1));

        Assert.Equal(shift.Id, Assert.Single(await OpenFor(s.A)).Id);
        Assert.Equal(shift.Id, Assert.Single(await OpenFor(s.B)).Id);
        Assert.Empty(await OpenFor(s.C));                       // druga pozicija
    }

    [Fact]
    public async Task Open_shifts_are_not_in_anyones_own_schedule_but_the_manager_sees_them()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));

        var mine = await ApiClient.ReadAsync<List<ShiftDto>>(await s.A.Http.GetAsync($"/api/shifts/mine?{Range()}"));

        Assert.Empty(mine);
        Assert.Null((await ShiftNowAsync(s, shift.Id)).EmployeeId);
    }

    [Fact]
    public async Task Started_open_shifts_are_not_offered()
    {
        var s = await ArrangeAsync();
        var past = DateTimeOffset.UtcNow.AddHours(-3);
        await CreateOpenOkAsync(s, past, hours: 2);

        var open = await ApiClient.ReadAsync<List<ShiftDto>>(await s.A.Http.GetAsync(
            $"/api/shifts/open?from={Uri.EscapeDataString(past.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(past.AddDays(2).ToString("O"))}"));

        Assert.Empty(open);
    }

    [Fact]
    public async Task Approving_one_claim_assigns_the_shift_and_closes_the_others()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));

        var claimA = await ClaimOkAsync(s.A, shift.Id);
        var claimB = await ClaimOkAsync(s.B, shift.Id);
        Assert.Equal(ClaimStatus.Pending, claimA.Status);
        var pending = await ApiClient.ReadAsync<List<ClaimDto>>(await s.Owner.Http.GetAsync("/api/claims?status=Pending"));
        Assert.Equal(2, pending.Count);
        Assert.Null((await ShiftNowAsync(s, shift.Id)).EmployeeId);

        var approved = await ApiClient.ReadAsync<ClaimDto>(await Act(s.Owner, claimA.Id, "approve"));

        Assert.Equal(ClaimStatus.Approved, approved.Status);
        Assert.Equal(s.AId, (await ShiftNowAsync(s, shift.Id)).EmployeeId);
        var mineB = await ApiClient.ReadAsync<List<ClaimDto>>(await s.B.Http.GetAsync("/api/claims/mine"));
        Assert.Equal(ClaimStatus.Rejected, Assert.Single(mineB).Status);
        Assert.Equal(claimB.Id, mineB[0].Id);
        Assert.Empty(await OpenFor(s.B));
        Assert.Equal(1, Mails(s.A, "Dobili ste otvorenu smenu"));
        Assert.Equal(1, Mails(s.B, "Otvorena smena nije dodeljena"));
    }

    [Fact]
    public async Task Two_simultaneous_approvals_for_the_same_shift_cannot_both_win()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claimA = await ClaimOkAsync(s.A, shift.Id);
        var claimB = await ClaimOkAsync(s.B, shift.Id);

        var results = await Task.WhenAll(Act(s.Owner, claimA.Id, "approve"), Act(s.Owner, claimB.Id, "approve"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var winner = (await ShiftNowAsync(s, shift.Id)).EmployeeId;
        var claims = await ApiClient.ReadAsync<List<ClaimDto>>(await s.Owner.Http.GetAsync("/api/claims"));
        Assert.Equal(1, claims.Count(c => c.Status == ClaimStatus.Approved));
        Assert.Equal(winner, claims.Single(c => c.Status == ClaimStatus.Approved).EmployeeId);
    }

    [Fact]
    public async Task Rejecting_a_claim_leaves_the_shift_open()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claim = await ClaimOkAsync(s.A, shift.Id);

        var rejected = await ApiClient.ReadAsync<ClaimDto>(await Act(s.Owner, claim.Id, "reject"));

        Assert.Equal(ClaimStatus.Rejected, rejected.Status);
        Assert.Null((await ShiftNowAsync(s, shift.Id)).EmployeeId);
        Assert.Single(await OpenFor(s.A));
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, claim.Id, "approve")).StatusCode);
        Assert.Equal(1, Mails(s.A, "Otvorena smena nije dodeljena"));
    }

    [Fact]
    public async Task Claim_rules_are_enforced()
    {
        var s = await ArrangeAsync();
        var draft = await CreateOpenOkAsync(s, Day.AddHours(8), publish: false);
        var open = await CreateOpenOkAsync(s, Day.AddDays(1).AddHours(8));
        var past = await CreateOpenOkAsync(s, DateTimeOffset.UtcNow.AddHours(-3), hours: 2);

        Assert.Equal(HttpStatusCode.NotFound, (await Claim(s.A, draft.Id)).StatusCode);            // nacrt
        Assert.Equal(HttpStatusCode.NotFound, (await Claim(s.A, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Claim(s.A, past.Id)).StatusCode);              // već počela
        Assert.Equal(HttpStatusCode.Conflict, (await Claim(s.C, open.Id)).StatusCode);              // nema poziciju

        Assert.Equal(HttpStatusCode.Created, (await Claim(s.A, open.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Claim(s.A, open.Id)).StatusCode);              // već traženo
    }

    [Fact]
    public async Task Assigned_shift_cannot_be_claimed_and_busy_employee_cannot_claim()
    {
        var s = await ArrangeAsync();
        var open = await CreateOpenOkAsync(s, Day.AddHours(8));
        await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.AId, positionId = s.PositionId, startsAt = Day.AddHours(10), endsAt = Day.AddHours(14) }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, (await Claim(s.A, open.Id)).StatusCode);              // zauzet u tom terminu

        var assigned = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.BId, positionId = s.PositionId, startsAt = Day.AddDays(3).AddHours(8), endsAt = Day.AddDays(3).AddHours(16) }, ApiClient.Json));
        await s.Owner.PublishAsync(Day.AddDays(3), Day.AddDays(4));
        Assert.Equal(HttpStatusCode.Conflict, (await Claim(s.A, assigned.Id)).StatusCode);          // nije otvorena
    }

    [Fact]
    public async Task Claim_can_be_cancelled_and_requested_again()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claim = await ClaimOkAsync(s.A, shift.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, claim.Id, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Act(s.A, claim.Id, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.A, claim.Id, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, claim.Id, "approve")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await Claim(s.A, shift.Id)).StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_decide_or_list_all_claims()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claim = await ClaimOkAsync(s.A, shift.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.A, claim.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, claim.Id, "reject")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.A.Http.GetAsync("/api/claims")).StatusCode);
    }

    [Fact]
    public async Task Approval_is_rechecked_when_the_employee_gets_busy_in_the_meantime()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claim = await ClaimOkAsync(s.A, shift.Id);
        await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.AId, positionId = s.PositionId, startsAt = Day.AddHours(12), endsAt = Day.AddHours(18) }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, claim.Id, "approve")).StatusCode);
        Assert.Null((await ShiftNowAsync(s, shift.Id)).EmployeeId);
        Assert.Equal(HttpStatusCode.OK, (await Act(s.Owner, claim.Id, "reject")).StatusCode);
    }

    [Fact]
    public async Task Assigning_directly_closes_the_claims_for_that_shift()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claimA = await ClaimOkAsync(s.A, shift.Id);
        var claimB = await ClaimOkAsync(s.B, shift.Id);

        var update = await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{shift.Id}",
            new { employeeId = s.BId, positionId = s.PositionId, startsAt = Day.AddHours(8), endsAt = Day.AddHours(16) }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var all = await ApiClient.ReadAsync<List<ClaimDto>>(await s.Owner.Http.GetAsync("/api/claims"));
        Assert.Equal(ClaimStatus.Rejected, all.Single(c => c.Id == claimA.Id).Status);
        Assert.Equal(ClaimStatus.Approved, all.Single(c => c.Id == claimB.Id).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, claimA.Id, "approve")).StatusCode);
        Assert.Equal(1, Mails(s.B, "Dobili ste otvorenu smenu"));
        Assert.Equal(1, Mails(s.A, "Otvorena smena nije dodeljena"));
    }

    [Fact]
    public async Task Unassigning_a_shift_makes_it_open_again()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.AId, positionId = s.PositionId, startsAt = Day.AddHours(8), endsAt = Day.AddHours(16) }, ApiClient.Json));
        await s.Owner.PublishAsync(Day, Day.AddDays(1));

        await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{shift.Id}",
            new { employeeId = (Guid?)null, positionId = s.PositionId, startsAt = Day.AddHours(8), endsAt = Day.AddHours(16) }, ApiClient.Json);

        Assert.Null((await ShiftNowAsync(s, shift.Id)).EmployeeId);
        Assert.Equal(shift.Id, Assert.Single(await OpenFor(s.B)).Id);
    }

    [Fact]
    public async Task Failing_mail_does_not_block_the_decision()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claim = await ClaimOkAsync(s.A, shift.Id);
        factory.Emails.FailFor(s.A.Email);

        Assert.Equal(HttpStatusCode.OK, (await Act(s.Owner, claim.Id, "approve")).StatusCode);
        Assert.Equal(s.AId, (await ShiftNowAsync(s, shift.Id)).EmployeeId);
    }

    [Fact]
    public async Task Open_shift_cannot_be_handed_over_as_if_it_were_mine()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));

        var response = await s.A.Http.PostAsJsonAsync("/api/swaps",
            new { shiftId = shift.Id, targetEmployeeId = s.BId }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Recurring_series_can_be_open()
    {
        var s = await ArrangeAsync();
        var monday = DateOnly.FromDateTime(Day.UtcDateTime);
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(1);
        var body = new
        {
            positionId = s.PositionId, fromDate = monday, untilDate = monday.AddDays(13),
            daysOfWeek = new[] { DayOfWeek.Monday, DayOfWeek.Friday },
            startTime = "18:00:00", endTime = "23:00:00",
        };

        var first = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts/recurring", body, ApiClient.Json));
        var second = await s.Owner.Http.PostAsJsonAsync("/api/shifts/recurring", body, ApiClient.Json);   // isti termini: otvorene smene se ne preklapaju

        Assert.Equal(4, first.Created);
        Assert.All(first.Shifts, x => Assert.Null(x.EmployeeId));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Restaurants_do_not_touch_each_others_open_shifts_or_claims()
    {
        var s = await ArrangeAsync();
        var shift = await CreateOpenOkAsync(s, Day.AddHours(8));
        var claim = await ClaimOkAsync(s.A, shift.Id);
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await Claim(stranger, shift.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(stranger, claim.Id, "approve")).StatusCode);
        Assert.Empty(await ApiClient.ReadAsync<List<ClaimDto>>(await stranger.Http.GetAsync("/api/claims")));
    }
}
