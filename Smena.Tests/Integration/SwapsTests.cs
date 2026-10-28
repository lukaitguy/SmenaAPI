using System.Net;
using System.Net.Http.Json;
using Smena.Application.Shifts;
using Smena.Application.Swaps;
using Smena.Domain.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class SwapsTests(ApiFactory factory)
{
    // Uvek u budućnosti u odnosu na stvarni sat, da testovi ne zastare.
    private static readonly DateTimeOffset Day = new(DateTimeOffset.UtcNow.Date.AddDays(14), TimeSpan.Zero);

    private sealed record Setup(
        ApiClient Owner, ApiClient A, ApiClient B, ApiClient C,
        Guid AId, Guid BId, Guid CId, Guid PositionId, ShiftDto Shift);

    /// <summary>Restoran sa tri zaposlena na istoj poziciji i smenom zaposlenog A.</summary>
    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var c = await owner.InviteAsync(factory);
        var (aId, bId, cId) = ((await a.MeAsync()).EmployeeId, (await b.MeAsync()).EmployeeId, (await c.MeAsync()).EmployeeId);
        var position = await owner.CreatePositionAsync();
        foreach (var id in new[] { aId, bId, cId })
            await owner.SetPositionsAsync(id, position.Id);

        var shift = await CreateShiftAsync(owner, aId, position.Id, Day.AddHours(8), Day.AddHours(16));
        return new Setup(owner, a, b, c, aId, bId, cId, position.Id, shift);
    }

    private static async Task<ShiftDto> CreateShiftAsync(ApiClient owner, Guid employeeId, Guid positionId, DateTimeOffset from, DateTimeOffset to) =>
        await ApiClient.ReadAsync<ShiftDto>(await owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId, positionId, startsAt = from, endsAt = to }, ApiClient.Json));

    private static Task<HttpResponseMessage> Request(ApiClient client, Guid shiftId, Guid targetId, string? note = null) =>
        client.Http.PostAsJsonAsync("/api/swaps", new { shiftId, targetEmployeeId = targetId, note }, ApiClient.Json);

    private static Task<HttpResponseMessage> Act(ApiClient client, Guid swapId, string action) =>
        client.Http.PostAsync($"/api/swaps/{swapId}/{action}", null);

    private static async Task<SwapDto> RequestOkAsync(Setup s) =>
        await ApiClient.ReadAsync<SwapDto>(await Request(s.A, s.Shift.Id, s.BId));

    private static async Task<ShiftDto> ShiftNowAsync(Setup s)
    {
        var list = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync(
            $"/api/shifts?from={Uri.EscapeDataString(Day.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(2).ToString("O"))}"));
        return list.Single(x => x.Id == s.Shift.Id);
    }

    [Fact]
    public async Task Full_flow_moves_the_shift_to_the_colleague()
    {
        var s = await ArrangeAsync();

        var created = await Request(s.A, s.Shift.Id, s.BId, "imam ispit");
        var swap = await ApiClient.ReadAsync<SwapDto>(created);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(SwapStatus.Pending, swap.Status);
        Assert.Equal(s.AId, swap.RequesterId);
        Assert.Equal(s.BId, swap.TargetId);
        Assert.Equal("imam ispit", swap.Note);

        Assert.Equal(SwapStatus.Accepted, (await ApiClient.ReadAsync<SwapDto>(await Act(s.B, swap.Id, "accept"))).Status);
        Assert.Equal(s.AId, (await ShiftNowAsync(s)).EmployeeId);   // još nije odobreno

        Assert.Equal(SwapStatus.Approved, (await ApiClient.ReadAsync<SwapDto>(await Act(s.Owner, swap.Id, "approve"))).Status);
        Assert.Equal(s.BId, (await ShiftNowAsync(s)).EmployeeId);
    }

    [Fact]
    public async Task Emails_go_to_the_colleague_on_request_and_to_both_on_decision()
    {
        var s = await ArrangeAsync();

        var swap = await RequestOkAsync(s);
        Assert.Single(factory.Emails.Sent, m => m.To == s.B.Email && m.Subject == "Kolega vam nudi smenu");
        Assert.DoesNotContain(factory.Emails.Sent, m => m.To == s.A.Email && m.Subject == "Kolega vam nudi smenu");

        await Act(s.B, swap.Id, "accept");
        await Act(s.Owner, swap.Id, "approve");

        Assert.Contains(factory.Emails.Sent, m => m.To == s.A.Email && m.Subject == "Zamena smene odobrena");
        Assert.Contains(factory.Emails.Sent, m => m.To == s.B.Email && m.Subject == "Zamena smene odobrena");
    }

    [Fact]
    public async Task Failing_mail_server_does_not_fail_the_request()
    {
        var s = await ArrangeAsync();
        factory.Emails.FailFor(s.B.Email);

        Assert.Equal(HttpStatusCode.Created, (await Request(s.A, s.Shift.Id, s.BId)).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_of_a_shift_can_hand_it_over()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Request(s.B, s.Shift.Id, s.CId)).StatusCode);
    }

    [Fact]
    public async Task Started_or_past_shifts_cannot_be_handed_over()
    {
        var s = await ArrangeAsync();
        var past = await CreateShiftAsync(s.Owner, s.AId, s.PositionId, DateTimeOffset.UtcNow.AddHours(-5), DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Equal(HttpStatusCode.Conflict, (await Request(s.A, past.Id, s.BId)).StatusCode);
    }

    [Fact]
    public async Task Colleague_must_be_able_to_take_the_shift()
    {
        var s = await ArrangeAsync();
        var otherPosition = await s.Owner.CreatePositionAsync();
        await s.Owner.SetPositionsAsync(s.BId, otherPosition.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Request(s.A, s.Shift.Id, s.BId)).StatusCode);   // nema poziciju

        await CreateShiftAsync(s.Owner, s.CId, s.PositionId, Day.AddHours(10), Day.AddHours(14));
        Assert.Equal(HttpStatusCode.Conflict, (await Request(s.A, s.Shift.Id, s.CId)).StatusCode);   // zauzet

        await s.Owner.Http.PutAsJsonAsync($"/api/employees/{s.CId}/status", new { status = "Inactive" }, ApiClient.Json);
        Assert.Equal(HttpStatusCode.Conflict, (await Request(s.A, s.Shift.Id, s.CId)).StatusCode);   // neaktivan

        Assert.Equal(HttpStatusCode.BadRequest, (await Request(s.A, s.Shift.Id, s.AId)).StatusCode);  // sebi
        Assert.Equal(HttpStatusCode.NotFound, (await Request(s.A, s.Shift.Id, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Only_one_open_request_per_shift_but_a_new_one_after_it_closes()
    {
        var s = await ArrangeAsync();
        var first = await RequestOkAsync(s);

        Assert.Equal(HttpStatusCode.Conflict, (await Request(s.A, s.Shift.Id, s.CId)).StatusCode);

        await Act(s.B, first.Id, "decline");
        Assert.Equal(HttpStatusCode.Created, (await Request(s.A, s.Shift.Id, s.CId)).StatusCode);
    }

    [Fact]
    public async Task Only_the_right_person_can_answer_or_cancel()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);

        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.A, swap.Id, "accept")).StatusCode);    // podnosilac
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.C, swap.Id, "accept")).StatusCode);    // treći
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.C, swap.Id, "decline")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, swap.Id, "cancel")).StatusCode);    // kolega ne otkazuje
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, swap.Id, "approve")).StatusCode);   // nije menadžer
    }

    [Fact]
    public async Task Manager_cannot_approve_until_the_colleague_accepts()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, swap.Id, "approve")).StatusCode);
        Assert.Equal(s.AId, (await ShiftNowAsync(s)).EmployeeId);
    }

    [Fact]
    public async Task Rejected_request_leaves_the_shift_where_it_was()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);
        await Act(s.B, swap.Id, "accept");

        var rejected = await ApiClient.ReadAsync<SwapDto>(await Act(s.Owner, swap.Id, "reject"));

        Assert.Equal(SwapStatus.Rejected, rejected.Status);
        Assert.Equal(s.AId, (await ShiftNowAsync(s)).EmployeeId);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, swap.Id, "approve")).StatusCode);
    }

    [Fact]
    public async Task Cancelled_request_cannot_be_approved()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);
        await Act(s.B, swap.Id, "accept");

        Assert.Equal(HttpStatusCode.OK, (await Act(s.A, swap.Id, "cancel")).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, swap.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.A, swap.Id, "cancel")).StatusCode);
    }

    [Fact]
    public async Task Approval_is_rechecked_when_the_colleague_gets_busy_in_the_meantime()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);
        await Act(s.B, swap.Id, "accept");
        await CreateShiftAsync(s.Owner, s.BId, s.PositionId, Day.AddHours(12), Day.AddHours(18));

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, swap.Id, "approve")).StatusCode);
        Assert.Equal(s.AId, (await ShiftNowAsync(s)).EmployeeId);
        Assert.Equal(HttpStatusCode.OK, (await Act(s.Owner, swap.Id, "reject")).StatusCode);
    }

    [Fact]
    public async Task Approval_fails_if_the_shift_was_reassigned_in_the_meantime()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);
        await Act(s.B, swap.Id, "accept");
        await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{s.Shift.Id}",
            new { employeeId = s.CId, positionId = s.PositionId, startsAt = Day.AddHours(8), endsAt = Day.AddHours(16) }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, swap.Id, "approve")).StatusCode);
        Assert.Equal(s.CId, (await ShiftNowAsync(s)).EmployeeId);
    }

    [Fact]
    public async Task Listing_is_scoped_to_the_caller_and_filterable_by_status()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);

        var mineA = await ApiClient.ReadAsync<List<SwapDto>>(await s.A.Http.GetAsync("/api/swaps/mine"));
        var mineB = await ApiClient.ReadAsync<List<SwapDto>>(await s.B.Http.GetAsync("/api/swaps/mine"));
        var mineC = await ApiClient.ReadAsync<List<SwapDto>>(await s.C.Http.GetAsync("/api/swaps/mine"));
        var all = await ApiClient.ReadAsync<List<SwapDto>>(await s.Owner.Http.GetAsync("/api/swaps"));
        var pending = await ApiClient.ReadAsync<List<SwapDto>>(await s.Owner.Http.GetAsync("/api/swaps?status=Pending"));
        var accepted = await ApiClient.ReadAsync<List<SwapDto>>(await s.Owner.Http.GetAsync("/api/swaps?status=Accepted"));

        Assert.Equal(swap.Id, Assert.Single(mineA).Id);
        Assert.Equal(swap.Id, Assert.Single(mineB).Id);
        Assert.Empty(mineC);
        Assert.Single(all);
        Assert.Single(pending);
        Assert.Empty(accepted);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.A.Http.GetAsync("/api/swaps")).StatusCode);
    }

    [Fact]
    public async Task Restaurants_do_not_see_or_touch_each_others_requests()
    {
        var s = await ArrangeAsync();
        var swap = await RequestOkAsync(s);
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        var list = await ApiClient.ReadAsync<List<SwapDto>>(await stranger.Http.GetAsync("/api/swaps"));

        Assert.Empty(list);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(stranger, swap.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(stranger, swap.Id, "reject")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Request(stranger, s.Shift.Id, s.BId)).StatusCode);
    }

    [Fact]
    public async Task Unknown_request_is_not_found()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Act(s.B, Guid.NewGuid(), "accept")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(s.Owner, Guid.NewGuid(), "approve")).StatusCode);
    }
}
