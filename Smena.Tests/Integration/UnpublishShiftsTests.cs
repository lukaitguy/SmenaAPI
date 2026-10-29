using System.Net;
using System.Net.Http.Json;
using Smena.Application.Claims;
using Smena.Application.Shifts;
using Smena.Application.Swaps;
using Smena.Domain.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class UnpublishShiftsTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Day = new(DateTimeOffset.UtcNow.Date.AddDays(14), TimeSpan.Zero);

    /// <summary>A i B su konobari (P), C je kuvar (Q). X (A, dan 0), Y (B, dan 1), Z (C, dan 2) i otvorena smena O (P, dan 3); sve objavljeno.</summary>
    private sealed record Setup(
        ApiClient Owner, ApiClient A, ApiClient B, ApiClient C,
        Guid AId, Guid BId, Guid CId, Guid WaiterId, Guid CookId,
        ShiftDto X, ShiftDto Y, ShiftDto Z, ShiftDto O);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var c = await owner.InviteAsync(factory);
        var (aId, bId, cId) = ((await a.MeAsync()).EmployeeId, (await b.MeAsync()).EmployeeId, (await c.MeAsync()).EmployeeId);
        var waiter = await owner.CreatePositionAsync();
        var cook = await owner.CreatePositionAsync();
        await owner.SetPositionsAsync(aId, waiter.Id);
        await owner.SetPositionsAsync(bId, waiter.Id);
        await owner.SetPositionsAsync(cId, cook.Id);
        var x = await CreateAsync(owner, aId, waiter.Id, 0);
        var y = await CreateAsync(owner, bId, waiter.Id, 1);
        var z = await CreateAsync(owner, cId, cook.Id, 2);
        var o = await CreateAsync(owner, null, waiter.Id, 3);
        await owner.PublishAsync(Day, Day.AddDays(10));
        return new Setup(owner, a, b, c, aId, bId, cId, waiter.Id, cook.Id, x, y, z, o);
    }

    private static async Task<ShiftDto> CreateAsync(ApiClient owner, Guid? employeeId, Guid positionId, int dayOffset) =>
        await ApiClient.ReadAsync<ShiftDto>(await owner.Http.PostAsJsonAsync("/api/shifts", new
        {
            employeeId, positionId,
            startsAt = Day.AddDays(dayOffset).AddHours(8), endsAt = Day.AddDays(dayOffset).AddHours(16),
        }, ApiClient.Json));

    private static Task<HttpResponseMessage> Unpublish(ApiClient owner, DateTimeOffset? from = null, DateTimeOffset? to = null, params Guid[] positionIds) =>
        owner.Http.PostAsJsonAsync("/api/shifts/unpublish",
            new { from = from ?? Day, to = to ?? Day.AddDays(10), positionIds }, ApiClient.Json);

    private static string Range() =>
        $"from={Uri.EscapeDataString(Day.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(20).ToString("O"))}";

    private static async Task<List<ShiftDto>> ScheduleAsync(ApiClient client) =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await client.Http.GetAsync($"/api/schedule?{Range()}"));

    private static async Task<List<ShiftDto>> MineAsync(ApiClient client) =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await client.Http.GetAsync($"/api/shifts/mine?{Range()}"));

    private static async Task<List<ShiftDto>> AllAsync(ApiClient owner) =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await owner.Http.GetAsync($"/api/shifts?{Range()}"));

    [Fact]
    public async Task Unpublishing_returns_shifts_to_drafts_that_employees_no_longer_see()
    {
        var s = await ArrangeAsync();
        Assert.Equal(3, (await ScheduleAsync(s.A)).Count);          // X, Y i otvorena smena O (sve konobarske)

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner));

        Assert.Equal(4, result.Unpublished);
        Assert.Empty(await MineAsync(s.A));
        Assert.Empty(await ScheduleAsync(s.A));
        Assert.Empty(await ScheduleAsync(s.C));
        Assert.All(await AllAsync(s.Owner), x => Assert.Null(x.PublishedAt));
    }

    [Fact]
    public async Task It_can_be_limited_to_a_period_and_to_roles()
    {
        var s = await ArrangeAsync();

        var waiters = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner, positionIds: s.WaiterId));
        Assert.Equal(3, waiters.Unpublished);                       // X, Y i otvorena O; kuvarska Z ostaje
        Assert.Empty(await ScheduleAsync(s.A));
        Assert.Single(await ScheduleAsync(s.C));

        var none = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner, Day.AddDays(2), Day.AddDays(3)));
        Assert.Equal(1, none.Unpublished);                          // samo Z (dan 2)
        Assert.Empty(await ScheduleAsync(s.C));
    }

    [Fact]
    public async Task Shifts_that_already_started_stay_published()
    {
        var s = await ArrangeAsync();
        var pastStart = DateTimeOffset.UtcNow.AddDays(-2);
        var past = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.AId, positionId = s.WaiterId, startsAt = pastStart, endsAt = pastStart.AddHours(8) }, ApiClient.Json));
        await s.Owner.PublishAsync(pastStart.AddHours(-1), pastStart.AddHours(1));

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(
            await Unpublish(s.Owner, pastStart.AddHours(-1), Day.AddDays(10)));

        Assert.Equal(4, result.Unpublished);                        // budućih četiri, prošla ne
        var all = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync(
            $"/api/shifts?from={Uri.EscapeDataString(pastStart.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(pastStart.AddDays(2).ToString("O"))}"));
        Assert.NotNull(all.Single(x => x.Id == past.Id).PublishedAt);
    }

    [Fact]
    public async Task Doing_it_twice_changes_nothing_and_sends_no_mail()
    {
        var s = await ArrangeAsync();
        var mailsBefore = factory.Emails.Sent.Count;

        await Unpublish(s.Owner);
        var again = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner));

        Assert.Equal(0, again.Unpublished);
        Assert.Equal(mailsBefore, factory.Emails.Sent.Count);       // povlačenje ne obaveštava nikoga
    }

    [Fact]
    public async Task Publishing_again_makes_the_shifts_visible_and_notifies_again()
    {
        var s = await ArrangeAsync();
        var before = factory.Emails.Sent.Count(m => m.To == s.A.Email && m.Subject == "Objavljen je novi raspored");
        await Unpublish(s.Owner);

        var result = await s.Owner.PublishAsync(Day, Day.AddDays(10));

        Assert.Equal(4, result.Published);
        Assert.Single(await MineAsync(s.A));
        Assert.Equal(before + 1, factory.Emails.Sent.Count(m => m.To == s.A.Email && m.Subject == "Objavljen je novi raspored"));
    }

    [Fact]
    public async Task Staff_cannot_unpublish_and_input_is_validated()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Unpublish(s.A)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Unpublish(s.Owner, Day.AddDays(5), Day)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Unpublish(s.Owner, Day, Day.AddDays(63))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Unpublish(s.Owner, positionIds: Guid.NewGuid())).StatusCode);
        Assert.Equal(3, (await ScheduleAsync(s.A)).Count);          // ništa nije povučeno
    }

    private static Task<HttpResponseMessage> Act(ApiClient client, Guid swapId, string action) =>
        client.Http.PostAsync($"/api/swaps/{swapId}/{action}", null);

    private static async Task<List<SwapDto>> SwapsOf(ApiClient client) =>
        await ApiClient.ReadAsync<List<SwapDto>>(await client.Http.GetAsync("/api/swaps/mine"));

    private static async Task<List<SwapDto>> AllSwapsAsync(ApiClient owner) =>
        await ApiClient.ReadAsync<List<SwapDto>>(await owner.Http.GetAsync("/api/swaps"));

    private static async Task<SwapDto> ExchangeAsync(Setup s) =>
        await ApiClient.ReadAsync<SwapDto>(await s.A.Http.PostAsJsonAsync("/api/swaps",
            new { shiftId = s.X.Id, targetShiftId = s.Y.Id }, ApiClient.Json));

    [Fact]
    public async Task A_request_waiting_for_the_colleague_is_deleted_together_with_the_roster()
    {
        var s = await ArrangeAsync();
        var swap = await ExchangeAsync(s);
        Assert.Single(await SwapsOf(s.B));

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner));

        Assert.Equal(1, result.SwapRequestsRemoved);
        Assert.Empty(await SwapsOf(s.A));
        Assert.Empty(await SwapsOf(s.B));                                              // kolega ga više ne vidi
        Assert.Empty(await AllSwapsAsync(s.Owner));
        Assert.Equal(HttpStatusCode.NotFound, (await Act(s.B, swap.Id, "accept")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(s.B, swap.Id, "decline")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(s.A, swap.Id, "cancel")).StatusCode);
    }

    [Fact]
    public async Task A_request_waiting_for_the_manager_is_deleted_too_and_the_shifts_stay_as_they_were()
    {
        var s = await ArrangeAsync();
        var swap = await ExchangeAsync(s);
        await Act(s.B, swap.Id, "accept");

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner));

        Assert.Equal(1, result.SwapRequestsRemoved);
        Assert.Empty(await SwapsOf(s.B));
        Assert.Equal(HttpStatusCode.NotFound, (await Act(s.Owner, swap.Id, "approve")).StatusCode);
        var all = await AllAsync(s.Owner);
        Assert.Equal(s.AId, all.Single(x => x.Id == s.X.Id).EmployeeId);
        Assert.Equal(s.BId, all.Single(x => x.Id == s.Y.Id).EmployeeId);
    }

    [Fact]
    public async Task It_is_enough_that_one_of_the_shifts_in_the_request_is_withdrawn()
    {
        var s = await ArrangeAsync();
        await ExchangeAsync(s);

        var onlyColleaguesShift = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner, Day.AddDays(1), Day.AddDays(2)));

        Assert.Equal(1, onlyColleaguesShift.Unpublished);                              // povučena je samo Y
        Assert.Equal(1, onlyColleaguesShift.SwapRequestsRemoved);
        Assert.Empty(await AllSwapsAsync(s.Owner));

        // Prepuštanje se briše kad se povuče smena koja se prepušta.
        await s.Owner.PublishAsync(Day, Day.AddDays(10));
        await s.A.Http.PostAsJsonAsync("/api/swaps", new { shiftId = s.X.Id, targetEmployeeId = s.BId }, ApiClient.Json);
        Assert.Single(await AllSwapsAsync(s.Owner));

        var handover = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner, Day, Day.AddDays(1)));

        Assert.Equal(1, handover.SwapRequestsRemoved);
        Assert.Empty(await AllSwapsAsync(s.Owner));
    }

    [Fact]
    public async Task Requests_about_shifts_that_stay_published_are_not_touched()
    {
        var s = await ArrangeAsync();
        await ExchangeAsync(s);

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner, Day.AddDays(2), Day.AddDays(4)));   // Z i O

        Assert.Equal(2, result.Unpublished);
        Assert.Equal(0, result.SwapRequestsRemoved);
        Assert.Single(await AllSwapsAsync(s.Owner));
    }

    [Fact]
    public async Task Finished_requests_are_deleted_too_and_the_shifts_keep_their_current_owners()
    {
        var s = await ArrangeAsync();
        var declined = await ExchangeAsync(s);
        await Act(s.B, declined.Id, "decline");
        var cancelled = await ExchangeAsync(s);
        await Act(s.A, cancelled.Id, "cancel");
        var approved = await ExchangeAsync(s);
        await Act(s.B, approved.Id, "accept");
        await Act(s.Owner, approved.Id, "approve");                                   // smene su već zamenile vlasnike

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner));

        Assert.Equal(3, result.SwapRequestsRemoved);
        Assert.Empty(await AllSwapsAsync(s.Owner));
        Assert.Empty(await SwapsOf(s.A));
        Assert.Empty(await SwapsOf(s.B));
        var all = await AllAsync(s.Owner);
        Assert.Equal(s.BId, all.Single(x => x.Id == s.X.Id).EmployeeId);              // brisanje zapisa ne vraća smene
        Assert.Equal(s.AId, all.Single(x => x.Id == s.Y.Id).EmployeeId);
    }

    [Fact]
    public async Task Publishing_again_does_not_bring_the_deleted_requests_back_and_a_new_one_is_possible()
    {
        var s = await ArrangeAsync();
        await ExchangeAsync(s);
        await Unpublish(s.Owner);

        await s.Owner.PublishAsync(Day, Day.AddDays(10));

        Assert.Empty(await AllSwapsAsync(s.Owner));
        Assert.Equal(HttpStatusCode.Created, (await s.A.Http.PostAsJsonAsync("/api/swaps",
            new { shiftId = s.X.Id, targetShiftId = s.Y.Id }, ApiClient.Json)).StatusCode);
    }

    [Fact]
    public async Task Claims_for_open_shifts_are_deleted_with_the_roster_whatever_their_status()
    {
        var s = await ArrangeAsync();
        var second = await CreateAsync(s.Owner, null, s.WaiterId, 4);
        await s.Owner.PublishAsync(Day.AddDays(4), Day.AddDays(5));
        var pending = await ApiClient.ReadAsync<ClaimDto>(await s.B.Http.PostAsJsonAsync(
            $"/api/shifts/{s.O.Id}/claims", new { note = (string?)null }, ApiClient.Json));
        var approved = await ApiClient.ReadAsync<ClaimDto>(await s.A.Http.PostAsJsonAsync(
            $"/api/shifts/{second.Id}/claims", new { note = (string?)null }, ApiClient.Json));
        await s.Owner.Http.PostAsync($"/api/claims/{approved.Id}/approve", null);

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner));

        Assert.Equal(2, result.ClaimRequestsRemoved);
        Assert.Empty(await ApiClient.ReadAsync<List<ClaimDto>>(await s.B.Http.GetAsync("/api/claims/mine")));
        Assert.Empty(await ApiClient.ReadAsync<List<ClaimDto>>(await s.Owner.Http.GetAsync("/api/claims")));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.Http.PostAsync($"/api/claims/{pending.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.B.Http.PostAsync($"/api/claims/{pending.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Requests_about_shifts_that_stay_published_are_left_alone()
    {
        var s = await ArrangeAsync();
        await ExchangeAsync(s);
        var claim = await ApiClient.ReadAsync<ClaimDto>(await s.B.Http.PostAsJsonAsync(
            $"/api/shifts/{s.O.Id}/claims", new { note = (string?)null }, ApiClient.Json));

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(s.Owner, positionIds: s.CookId));   // samo kuvarska Z

        Assert.Equal(1, result.Unpublished);
        Assert.Equal(0, result.SwapRequestsRemoved);
        Assert.Equal(0, result.ClaimRequestsRemoved);
        Assert.Single(await AllSwapsAsync(s.Owner));
        Assert.Equal(claim.Id, Assert.Single(await ApiClient.ReadAsync<List<ClaimDto>>(await s.B.Http.GetAsync("/api/claims/mine"))).Id);
    }

    [Fact]
    public async Task New_requests_for_withdrawn_shifts_are_not_possible()
    {
        var s = await ArrangeAsync();
        await Unpublish(s.Owner);

        var swap = await s.A.Http.PostAsJsonAsync("/api/swaps", new { shiftId = s.X.Id, targetEmployeeId = s.BId }, ApiClient.Json);
        var claim = await s.B.Http.PostAsJsonAsync($"/api/shifts/{s.O.Id}/claims", new { note = (string?)null }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.NotFound, swap.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, claim.StatusCode);
    }

    [Fact]
    public async Task Other_restaurants_cannot_withdraw_my_roster()
    {
        var s = await ArrangeAsync();
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        var result = await ApiClient.ReadAsync<UnpublishedShiftsDto>(await Unpublish(stranger));

        Assert.Equal(0, result.Unpublished);
        Assert.Equal(3, (await ScheduleAsync(s.A)).Count);
    }
}
