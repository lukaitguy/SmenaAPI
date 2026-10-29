using System.Net;
using System.Net.Http.Json;
using Smena.Application.Claims;
using Smena.Application.Shifts;
using Smena.Application.Swaps;
using Smena.Domain.Employees;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ManagerNotificationTests(ApiFactory factory)
{
    private const string DayOffSubject = "Novi zahtev za slobodan dan";
    private const string ClaimSubject = "Novi zahtev za otvorenu smenu";
    private const string SwapSubject = "Zamena smene čeka odluku";

    private static readonly DateTimeOffset Day = new(DateTimeOffset.UtcNow.Date.AddDays(14), TimeSpan.Zero);
    private static readonly DateOnly DayOffDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    /// <summary>Vlasnik, još jedan menadžer i dva zaposlena na istoj poziciji.</summary>
    private sealed record Setup(
        ApiClient Owner, ApiClient Manager, ApiClient A, ApiClient B,
        Guid OwnerId, Guid ManagerId, Guid AId, Guid BId, Guid PositionId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var manager = await owner.InviteAsync(factory, EmployeeRole.Manager);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var ids = (Owner: (await owner.MeAsync()).EmployeeId, Manager: (await manager.MeAsync()).EmployeeId,
            A: (await a.MeAsync()).EmployeeId, B: (await b.MeAsync()).EmployeeId);
        var position = await owner.CreatePositionAsync();
        await owner.SetPositionsAsync(ids.A, position.Id);
        await owner.SetPositionsAsync(ids.B, position.Id);
        return new Setup(owner, manager, a, b, ids.Owner, ids.Manager, ids.A, ids.B, position.Id);
    }

    private int Mails(ApiClient client, string subject) =>
        factory.Emails.Sent.Count(m => m.To == client.Email && m.Subject == subject);

    private static Task<HttpResponseMessage> RequestDayOff(ApiClient client, DateOnly date) =>
        client.Http.PostAsJsonAsync("/api/dayoffs", new { date, reason = "slava" }, ApiClient.Json);

    private static async Task<ShiftDto> OpenShiftAsync(Setup s, DateTimeOffset from)
    {
        var shift = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { positionId = s.PositionId, startsAt = from, endsAt = from.AddHours(8) }, ApiClient.Json));
        await s.Owner.PublishAsync(from, from.AddMinutes(1));
        return shift;
    }

    [Fact]
    public async Task Day_off_request_notifies_every_manager_but_not_the_requester_or_colleagues()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.Created, (await RequestDayOff(s.A, DayOffDate)).StatusCode);

        Assert.Equal(1, Mails(s.Owner, DayOffSubject));
        Assert.Equal(1, Mails(s.Manager, DayOffSubject));
        Assert.Equal(0, Mails(s.A, DayOffSubject));
        Assert.Equal(0, Mails(s.B, DayOffSubject));
        var mail = factory.Emails.Sent.Single(m => m.To == s.Owner.Email && m.Subject == DayOffSubject);
        Assert.Contains("Zaposleni Test", mail.TextBody);
        Assert.Contains($"{DayOffDate:dd.MM.yyyy.}", mail.TextBody);
        Assert.Contains("slava", mail.TextBody);
    }

    [Fact]
    public async Task A_manager_who_asks_for_a_day_off_does_not_notify_themselves()
    {
        var s = await ArrangeAsync();

        await RequestDayOff(s.Manager, DayOffDate);

        Assert.Equal(1, Mails(s.Owner, DayOffSubject));
        Assert.Equal(0, Mails(s.Manager, DayOffSubject));
    }

    [Fact]
    public async Task Inactive_managers_are_not_notified()
    {
        var s = await ArrangeAsync();
        await s.Owner.Http.PutAsJsonAsync($"/api/employees/{s.ManagerId}/status", new { status = "Inactive" }, ApiClient.Json);

        await RequestDayOff(s.A, DayOffDate);

        Assert.Equal(1, Mails(s.Owner, DayOffSubject));
        Assert.Equal(0, Mails(s.Manager, DayOffSubject));
    }

    [Fact]
    public async Task Rejected_input_sends_nothing()
    {
        var s = await ArrangeAsync();

        var past = await RequestDayOff(s.A, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3));

        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal(0, Mails(s.Owner, DayOffSubject));
    }

    [Fact]
    public async Task Claim_for_an_open_shift_notifies_managers()
    {
        var s = await ArrangeAsync();
        var shift = await OpenShiftAsync(s, Day.AddHours(8));

        var response = await s.A.Http.PostAsJsonAsync($"/api/shifts/{shift.Id}/claims", new { note = (string?)null }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, Mails(s.Owner, ClaimSubject));
        Assert.Equal(1, Mails(s.Manager, ClaimSubject));
        Assert.Equal(0, Mails(s.A, ClaimSubject));
        Assert.Contains("Zaposleni Test", factory.Emails.Sent.Single(m => m.To == s.Owner.Email && m.Subject == ClaimSubject).TextBody);
    }

    [Fact]
    public async Task Invalid_claim_sends_nothing()
    {
        var s = await ArrangeAsync();
        var shift = await OpenShiftAsync(s, Day.AddHours(8));
        await s.A.Http.PostAsJsonAsync($"/api/shifts/{shift.Id}/claims", new { note = (string?)null }, ApiClient.Json);

        var duplicate = await s.A.Http.PostAsJsonAsync($"/api/shifts/{shift.Id}/claims", new { note = (string?)null }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, Mails(s.Owner, ClaimSubject));
    }

    [Fact]
    public async Task Handover_notifies_managers_only_once_the_colleague_accepts()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.AId, positionId = s.PositionId, startsAt = Day.AddHours(8), endsAt = Day.AddHours(16) }, ApiClient.Json));
        await s.Owner.PublishAsync(Day, Day.AddDays(1));

        var swap = await ApiClient.ReadAsync<SwapDto>(await s.A.Http.PostAsJsonAsync("/api/swaps",
            new { shiftId = shift.Id, targetEmployeeId = s.BId }, ApiClient.Json));
        Assert.Equal(0, Mails(s.Owner, SwapSubject));        // kolega još nije odgovorio

        await s.B.Http.PostAsync($"/api/swaps/{swap.Id}/accept", null);

        Assert.Equal(1, Mails(s.Owner, SwapSubject));
        Assert.Equal(1, Mails(s.Manager, SwapSubject));
        var text = factory.Emails.Sent.Single(m => m.To == s.Owner.Email && m.Subject == SwapSubject).TextBody;
        Assert.Contains("Zaposleni Test", text);
    }

    [Fact]
    public async Task Declined_handover_does_not_bother_managers()
    {
        var s = await ArrangeAsync();
        var shift = await ApiClient.ReadAsync<ShiftDto>(await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId = s.AId, positionId = s.PositionId, startsAt = Day.AddHours(8), endsAt = Day.AddHours(16) }, ApiClient.Json));
        await s.Owner.PublishAsync(Day, Day.AddDays(1));
        var swap = await ApiClient.ReadAsync<SwapDto>(await s.A.Http.PostAsJsonAsync("/api/swaps",
            new { shiftId = shift.Id, targetEmployeeId = s.BId }, ApiClient.Json));

        await s.B.Http.PostAsync($"/api/swaps/{swap.Id}/decline", null);

        Assert.Equal(0, Mails(s.Owner, SwapSubject));
        Assert.Equal(0, Mails(s.Manager, SwapSubject));
    }

    [Fact]
    public async Task A_failing_mailbox_does_not_block_the_request_or_the_other_managers()
    {
        var s = await ArrangeAsync();
        factory.Emails.FailFor(s.Owner.Email);

        var response = await RequestDayOff(s.A, DayOffDate);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, Mails(s.Manager, DayOffSubject));
    }

    [Fact]
    public async Task Managers_of_other_restaurants_are_not_notified()
    {
        var s = await ArrangeAsync();
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        await RequestDayOff(s.A, DayOffDate);

        Assert.Equal(0, Mails(stranger, DayOffSubject));
    }
}
