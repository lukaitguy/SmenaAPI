using System.Net;
using System.Net.Http.Json;
using Smena.Application.Shifts;
using Smena.Application.Swaps;
using Smena.Domain.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ExchangeTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Day = new(DateTimeOffset.UtcNow.Date.AddDays(14), TimeSpan.Zero);

    /// <summary>
    /// A, B i D su konobari (P), C je kuvar (Q). X je A-ova smena (dan 0), Y je B-ova (dan 1),
    /// W je D-ova (dan 3), Z je C-ova (dan 2).
    /// </summary>
    private sealed record Setup(
        ApiClient Owner, ApiClient Manager, ApiClient A, ApiClient B, ApiClient C, ApiClient D,
        Guid AId, Guid BId, Guid CId, Guid DId, Guid WaiterId, Guid CookId,
        ShiftDto X, ShiftDto Y, ShiftDto Z, ShiftDto W);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var manager = await owner.InviteAsync(factory, Smena.Domain.Employees.EmployeeRole.Manager);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var c = await owner.InviteAsync(factory);
        var d = await owner.InviteAsync(factory);
        var aId = (await a.MeAsync()).EmployeeId;
        var bId = (await b.MeAsync()).EmployeeId;
        var cId = (await c.MeAsync()).EmployeeId;
        var dId = (await d.MeAsync()).EmployeeId;
        var waiter = await owner.CreatePositionAsync();
        var cook = await owner.CreatePositionAsync();
        foreach (var id in new[] { aId, bId, dId })
            await owner.SetPositionsAsync(id, waiter.Id);
        await owner.SetPositionsAsync(cId, cook.Id);

        var x = await CreateAsync(owner, aId, waiter.Id, 0);
        var y = await CreateAsync(owner, bId, waiter.Id, 1);
        var z = await CreateAsync(owner, cId, cook.Id, 2);
        var w = await CreateAsync(owner, dId, waiter.Id, 3);
        await owner.PublishAsync(Day, Day.AddDays(10));
        return new Setup(owner, manager, a, b, c, d, aId, bId, cId, dId, waiter.Id, cook.Id, x, y, z, w);
    }

    private static async Task<ShiftDto> CreateAsync(ApiClient owner, Guid? employeeId, Guid positionId, int dayOffset, int fromHour = 8, int toHour = 16) =>
        await ApiClient.ReadAsync<ShiftDto>(await owner.Http.PostAsJsonAsync("/api/shifts", new
        {
            employeeId, positionId,
            startsAt = Day.AddDays(dayOffset).AddHours(fromHour), endsAt = Day.AddDays(dayOffset).AddHours(toHour),
        }, ApiClient.Json));

    private static Task<HttpResponseMessage> Propose(ApiClient client, Guid shiftId, Guid? targetShiftId, Guid? targetEmployeeId = null, string? note = null) =>
        client.Http.PostAsJsonAsync("/api/swaps", new { shiftId, targetShiftId, targetEmployeeId, note }, ApiClient.Json);

    private static async Task<SwapDto> ProposeOkAsync(ApiClient client, Guid shiftId, Guid targetShiftId) =>
        await ApiClient.ReadAsync<SwapDto>(await Propose(client, shiftId, targetShiftId));

    private static Task<HttpResponseMessage> Act(ApiClient client, Guid swapId, string action) =>
        client.Http.PostAsync($"/api/swaps/{swapId}/{action}", null);

    private static async Task<Guid?> OwnerOfAsync(Setup s, Guid shiftId)
    {
        var list = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync(
            $"/api/shifts?from={Uri.EscapeDataString(Day.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(20).ToString("O"))}"));
        return list.Single(x => x.Id == shiftId).EmployeeId;
    }

    private int Mails(ApiClient client, string subject) =>
        factory.Emails.Sent.Count(m => m.To == client.Email && m.Subject == subject);

    [Fact]
    public async Task Full_flow_swaps_the_two_shifts_at_once()
    {
        var s = await ArrangeAsync();

        var created = await Propose(s.A, s.X.Id, s.Y.Id, note: "imam ispit");
        var swap = await ApiClient.ReadAsync<SwapDto>(created);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(SwapStatus.Pending, swap.Status);
        Assert.Equal(s.AId, swap.RequesterId);
        Assert.Equal(s.BId, swap.TargetId);                       // kolega se izvodi iz njegove smene
        Assert.Equal(s.Y.Id, swap.TargetShiftId);
        Assert.Equal(s.Y.StartsAt, swap.TargetShiftStartsAt);
        Assert.NotNull(swap.TargetPositionName);
        Assert.Equal(1, Mails(s.B, "Kolega vam predlaže zamenu smena"));
        Assert.Equal(0, Mails(s.Owner, "Zamena smena čeka odluku"));

        Assert.Equal(SwapStatus.Accepted, (await ApiClient.ReadAsync<SwapDto>(await Act(s.B, swap.Id, "accept"))).Status);
        Assert.Equal(s.AId, await OwnerOfAsync(s, s.X.Id));        // još ništa nije promenjeno
        Assert.Equal(s.BId, await OwnerOfAsync(s, s.Y.Id));
        Assert.Equal(1, Mails(s.Owner, "Zamena smena čeka odluku"));
        Assert.Equal(1, Mails(s.Manager, "Zamena smena čeka odluku"));

        Assert.Equal(SwapStatus.Approved, (await ApiClient.ReadAsync<SwapDto>(await Act(s.Owner, swap.Id, "approve"))).Status);
        Assert.Equal(s.BId, await OwnerOfAsync(s, s.X.Id));
        Assert.Equal(s.AId, await OwnerOfAsync(s, s.Y.Id));
        Assert.Equal(1, Mails(s.A, "Zamena smena odobrena"));
        Assert.Equal(1, Mails(s.B, "Zamena smena odobrena"));
    }

    [Fact]
    public async Task Handover_without_a_target_shift_still_works_the_same()
    {
        var s = await ArrangeAsync();

        var swap = await ApiClient.ReadAsync<SwapDto>(await Propose(s.A, s.X.Id, null, targetEmployeeId: s.BId));

        Assert.Null(swap.TargetShiftId);
        Assert.Null(swap.TargetPositionName);
        await Act(s.B, swap.Id, "accept");
        await Act(s.Owner, swap.Id, "approve");
        Assert.Equal(s.BId, await OwnerOfAsync(s, s.X.Id));
        Assert.Equal(s.BId, await OwnerOfAsync(s, s.Y.Id));         // B sada ima obe, ništa nije stiglo A-u
    }

    [Fact]
    public async Task Request_must_name_a_colleague_or_a_shift_and_they_must_agree()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Propose(s.A, s.X.Id, null)).StatusCode);                         // ni kolega ni smena
        Assert.Equal(HttpStatusCode.BadRequest, (await Propose(s.A, s.X.Id, s.Y.Id, targetEmployeeId: s.DId)).StatusCode);   // smena nije njegova
        Assert.Equal(HttpStatusCode.Created, (await Propose(s.A, s.X.Id, s.Y.Id, targetEmployeeId: s.BId)).StatusCode);       // slaže se
    }

    [Fact]
    public async Task Only_valid_shifts_can_be_exchanged()
    {
        var s = await ArrangeAsync();
        var open = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { positionId = s.WaiterId, startsAt = Day.AddDays(5).AddHours(8), endsAt = Day.AddDays(5).AddHours(16) }, ApiClient.Json));
        var draft = await CreateAsync(s.Owner, s.BId, s.WaiterId, 6);                                                    // nije objavljena
        var pastStart = DateTimeOffset.UtcNow.AddDays(-2);
        var past = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.BId, positionId = s.WaiterId, startsAt = pastStart, endsAt = pastStart.AddHours(8) }, ApiClient.Json));
        await s.Owner.PublishAsync(pastStart.AddHours(-1), pastStart.AddHours(1));
        await s.Owner.PublishAsync(Day, Day.AddDays(5.5));

        Assert.Equal(HttpStatusCode.BadRequest, (await Propose(s.A, s.X.Id, s.X.Id)).StatusCode);                        // sama sa sobom
        Assert.Equal(HttpStatusCode.BadRequest, (await Propose(s.A, s.X.Id, s.W.Id, targetEmployeeId: s.AId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.A, s.X.Id, open.Id)).StatusCode);                          // otvorena se traži, ne menja
        Assert.Equal(HttpStatusCode.NotFound, (await Propose(s.A, s.X.Id, draft.Id)).StatusCode);                          // nacrt
        Assert.Equal(HttpStatusCode.NotFound, (await Propose(s.A, s.X.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.A, s.X.Id, past.Id)).StatusCode);                          // već prošla
        Assert.Equal(HttpStatusCode.Forbidden, (await Propose(s.A, s.Y.Id, s.W.Id)).StatusCode);                          // Y nije A-ova
    }

    [Fact]
    public async Task Both_sides_need_the_position_of_the_shift_they_receive()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.A, s.X.Id, s.Z.Id)).StatusCode);                           // konobar ne može kuvarsku smenu

        // Kad obojica imaju obe uloge, razmena između uloga je u redu.
        await s.Owner.SetPositionsAsync(s.AId, s.WaiterId, s.CookId);
        await s.Owner.SetPositionsAsync(s.CId, s.WaiterId, s.CookId);
        Assert.Equal(HttpStatusCode.Created, (await Propose(s.A, s.X.Id, s.Z.Id)).StatusCode);
    }

    [Fact]
    public async Task The_shift_you_give_away_does_not_count_as_an_overlap_but_other_shifts_do()
    {
        var s = await ArrangeAsync();
        // B ima smenu istog dana kad i A (X je 08-16, ova je 10-18): A je zauzet tada samo dok ne preda X.
        var sameDay = await CreateAsync(s.Owner, s.BId, s.WaiterId, 0, 10, 18);
        await s.Owner.PublishAsync(Day, Day.AddDays(1));
        Assert.Equal(HttpStatusCode.Created, (await Propose(s.A, s.X.Id, sameDay.Id)).StatusCode);

        // Druga A-ova smena koja se preklapa sa Y blokira razmenu.
        var other = await CreateAsync(s.Owner, s.AId, s.WaiterId, 1, 12, 20);
        await s.Owner.PublishAsync(Day.AddDays(1), Day.AddDays(2));
        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.A, s.X.Id, s.Y.Id)).StatusCode);
        Assert.NotNull(other);
    }

    [Fact]
    public async Task A_shift_in_an_open_request_cannot_be_part_of_another_one()
    {
        var s = await ArrangeAsync();
        var first = await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.D, s.W.Id, s.Y.Id)).StatusCode);                                   // Y je zaključana
        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.B, s.Y.Id, null, targetEmployeeId: s.DId)).StatusCode);            // i B ne može da je prepusti
        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.A, s.X.Id, null, targetEmployeeId: s.DId)).StatusCode);            // ni A svoju X
        Assert.Equal(HttpStatusCode.Conflict, (await Propose(s.D, s.W.Id, s.X.Id)).StatusCode);

        await Act(s.B, first.Id, "decline");
        Assert.Equal(HttpStatusCode.Created, (await Propose(s.D, s.W.Id, s.Y.Id)).StatusCode);                                    // posle odbijanja je slobodna
    }

    [Fact]
    public async Task Approval_is_rechecked_if_either_shift_changed_hands()
    {
        var s = await ArrangeAsync();
        var swap = await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);
        await Act(s.B, swap.Id, "accept");

        // Menadžer u međuvremenu daje A-ovu smenu D-u.
        await s.Owner.Http.PutAsJsonAsync($"/api/shifts/{s.X.Id}", new
        {
            employeeId = s.DId, positionId = s.WaiterId, startsAt = s.X.StartsAt, endsAt = s.X.EndsAt,
        }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, (await Act(s.Owner, swap.Id, "approve")).StatusCode);
        Assert.Equal(s.DId, await OwnerOfAsync(s, s.X.Id));
        Assert.Equal(s.BId, await OwnerOfAsync(s, s.Y.Id));         // Y nije prešla bez X
    }

    [Fact]
    public async Task Approval_is_blocked_by_a_day_off_of_the_one_who_would_receive_the_shift()
    {
        var s = await ArrangeAsync();
        var swap = await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);
        await Act(s.B, swap.Id, "accept");
        var xDate = DateOnly.FromDateTime(s.X.StartsAt.UtcDateTime);
        var dayOff = await ApiClient.ReadAsync<Smena.Application.DayOffs.DayOffDto>(
            await s.B.Http.PostAsJsonAsync("/api/dayoffs", new { date = xDate }, ApiClient.Json));
        await s.Owner.Http.PostAsync($"/api/dayoffs/{dayOff.Id}/approve", null);

        var response = await Act(s.Owner, swap.Id, "approve");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("shifts.day_off", await response.Content.ReadAsStringAsync());
        Assert.Equal(s.AId, await OwnerOfAsync(s, s.X.Id));
    }

    [Fact]
    public async Task Declined_cancelled_and_rejected_requests_leave_both_shifts_alone()
    {
        var s = await ArrangeAsync();
        var declined = await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);
        await Act(s.B, declined.Id, "decline");
        var cancelled = await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);
        await Act(s.A, cancelled.Id, "cancel");
        var rejected = await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);
        await Act(s.B, rejected.Id, "accept");

        Assert.Equal(SwapStatus.Rejected, (await ApiClient.ReadAsync<SwapDto>(await Act(s.Owner, rejected.Id, "reject"))).Status);

        Assert.Equal(s.AId, await OwnerOfAsync(s, s.X.Id));
        Assert.Equal(s.BId, await OwnerOfAsync(s, s.Y.Id));
        Assert.Equal(1, Mails(s.A, "Zamena smena odbijena"));
        Assert.Equal(1, Mails(s.B, "Zamena smena odbijena"));
    }

    [Fact]
    public async Task Only_the_right_people_can_act()
    {
        var s = await ArrangeAsync();
        var swap = await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.A, swap.Id, "accept")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.D, swap.Id, "accept")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, swap.Id, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(s.B, swap.Id, "approve")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_colleagues_shift_removes_the_request()
    {
        var s = await ArrangeAsync();
        await ProposeOkAsync(s.A, s.X.Id, s.Y.Id);

        await s.Owner.Http.DeleteAsync($"/api/shifts/{s.Y.Id}");

        Assert.Empty(await ApiClient.ReadAsync<List<SwapDto>>(await s.A.Http.GetAsync("/api/swaps/mine")));
        Assert.Equal(HttpStatusCode.Created, (await Propose(s.A, s.X.Id, s.W.Id)).StatusCode);    // X je opet slobodna
    }

    [Fact]
    public async Task A_failing_mailbox_does_not_block_the_request()
    {
        var s = await ArrangeAsync();
        factory.Emails.FailFor(s.B.Email);

        Assert.Equal(HttpStatusCode.Created, (await Propose(s.A, s.X.Id, s.Y.Id)).StatusCode);
    }

    [Fact]
    public async Task Other_restaurants_cannot_use_my_shifts()
    {
        var s = await ArrangeAsync();
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await Propose(stranger, s.X.Id, s.Y.Id)).StatusCode);
    }
}
