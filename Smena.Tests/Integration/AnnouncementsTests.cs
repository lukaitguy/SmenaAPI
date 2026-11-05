using System.Net;
using System.Net.Http.Json;
using Smena.Application.Announcements;
using Smena.Application.Auth;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AnnouncementsTests(ApiFactory factory)
{
    private sealed record Setup(
        ApiClient Owner, ApiClient Waiter, ApiClient Waiter2, ApiClient Cook, ApiClient Bartender,
        Guid WaiterPosition, Guid CookPosition, Guid BartenderPosition, Guid CookId, Guid BartenderId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var waiter = await owner.InviteAsync(factory);
        var waiter2 = await owner.InviteAsync(factory);
        var cook = await owner.InviteAsync(factory);
        var bartender = await owner.InviteAsync(factory);
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        Guid Id(string name) => positions.Single(p => p.Name == name).Id;
        await owner.SetPositionsAsync((await waiter.MeAsync()).EmployeeId, Id("Konobar"));
        await owner.SetPositionsAsync((await waiter2.MeAsync()).EmployeeId, Id("Konobar"));
        var cookId = (await cook.MeAsync()).EmployeeId;
        var bartenderId = (await bartender.MeAsync()).EmployeeId;
        await owner.SetPositionsAsync(cookId, Id("Kuvar"));
        await owner.SetPositionsAsync(bartenderId, Id("Šanker"));
        return new Setup(owner, waiter, waiter2, cook, bartender, Id("Konobar"), Id("Kuvar"), Id("Šanker"), cookId, bartenderId);
    }

    private static Task<HttpResponseMessage> Create(
        ApiClient client, string title = "Sastanak", string? body = "Dolazite svi", DateTimeOffset? meetingAt = null,
        Guid[]? positionIds = null, Guid[]? employeeIds = null) =>
        client.Http.PostAsJsonAsync("/api/announcements",
            new { title, body, meetingAt, positionIds = positionIds ?? [], employeeIds = employeeIds ?? [] }, ApiClient.Json);

    private static async Task<List<AnnouncementDto>> MineAsync(ApiClient client) =>
        await ApiClient.ReadAsync<List<AnnouncementDto>>(await client.Http.GetAsync("/api/announcements/mine"));

    private int Mails(ApiClient client) =>
        factory.Emails.Sent.Count(m => m.To == client.Email && m.Subject.StartsWith("Najava:"));

    [Fact]
    public async Task Announcement_for_a_role_reaches_only_people_with_that_role()
    {
        var s = await ArrangeAsync();

        var response = await Create(s.Owner, "Sastanak sale", positionIds: [s.WaiterPosition]);
        var created = await ApiClient.ReadAsync<AnnouncementDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["Konobar"], created.Audience);
        Assert.Equal(2, created.Recipients!.Count);
        Assert.Equal(created.Id, Assert.Single(await MineAsync(s.Waiter)).Id);
        Assert.Equal(created.Id, Assert.Single(await MineAsync(s.Waiter2)).Id);
        Assert.Empty(await MineAsync(s.Cook));
        Assert.Empty(await MineAsync(s.Bartender));
    }

    [Fact]
    public async Task Roles_and_single_people_are_combined_and_nobody_is_counted_twice()
    {
        var s = await ArrangeAsync();

        // Šanker je izabran i pojedinačno i kroz svoju ulogu; kuvar samo pojedinačno.
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(
            s.Owner, positionIds: [s.WaiterPosition, s.BartenderPosition], employeeIds: [s.CookId, s.BartenderId]));

        Assert.Equal(4, created.Recipients!.Count);
        Assert.Equal(["Konobar", "Šanker"], created.Audience.Take(2));
        Assert.Single(await MineAsync(s.Cook));
        Assert.Single(await MineAsync(s.Bartender));
        Assert.Single(await MineAsync(s.Waiter));
    }

    [Fact]
    public async Task The_author_is_not_a_recipient_of_their_own_announcement()
    {
        var s = await ArrangeAsync();
        var ownerId = (await s.Owner.MeAsync()).EmployeeId;
        await s.Owner.SetPositionsAsync(ownerId, s.WaiterPosition);

        // Autor je obuhvaćen i ulogom i pojedinačno, a ipak sam sebi ne šalje.
        var created = await ApiClient.ReadAsync<AnnouncementDto>(
            await Create(s.Owner, positionIds: [s.WaiterPosition], employeeIds: [ownerId]));

        Assert.Equal(2, created.Recipients!.Count);
        Assert.DoesNotContain(created.Recipients, r => r.EmployeeId == ownerId);
        Assert.Empty(await MineAsync(s.Owner));
        Assert.Equal(0, Mails(s.Owner));
    }

    [Fact]
    public async Task Recipients_get_one_mail_with_title_meeting_time_and_link_and_others_none()
    {
        var s = await ArrangeAsync();
        var meeting = DateTimeOffset.UtcNow.AddDays(3);

        await Create(s.Owner, "Sastanak kuhinje", "Novi jelovnik", meeting, positionIds: [s.CookPosition]);

        Assert.Equal(1, Mails(s.Cook));
        Assert.Equal(0, Mails(s.Waiter));
        var mail = factory.Emails.Sent.Single(m => m.To == s.Cook.Email && m.Subject.StartsWith("Najava:"));
        Assert.Equal("Najava: Sastanak kuhinje", mail.Subject);
        Assert.Contains("Novi jelovnik", mail.TextBody);
        Assert.Contains("Termin:", mail.TextBody);
        Assert.Contains("/najave", mail.TextBody);
    }

    [Fact]
    public async Task A_failed_mail_does_not_stop_the_announcement()
    {
        var s = await ArrangeAsync();
        factory.Emails.FailFor(s.Cook.Email);

        var response = await Create(s.Owner, positionIds: [s.CookPosition, s.WaiterPosition]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(await MineAsync(s.Cook));
        Assert.Equal(1, Mails(s.Waiter));
    }

    [Fact]
    public async Task Inactive_employees_are_not_recipients_even_with_the_role()
    {
        var s = await ArrangeAsync();
        var waiter2Id = (await s.Waiter2.MeAsync()).EmployeeId;
        await s.Owner.Http.PutAsJsonAsync($"/api/employees/{waiter2Id}/status", new { status = "Inactive" }, ApiClient.Json);

        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(s.Owner, positionIds: [s.WaiterPosition]));

        Assert.Equal(1, created.Recipients!.Count);
        Assert.Equal(0, Mails(s.Waiter2));
    }

    [Fact]
    public async Task Employees_see_neither_the_recipient_list_nor_announcements_meant_for_others()
    {
        var s = await ArrangeAsync();
        await Create(s.Owner, positionIds: [s.WaiterPosition]);

        Assert.Null(Assert.Single(await MineAsync(s.Waiter)).Recipients);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Waiter.Http.GetAsync("/api/announcements")).StatusCode);
    }

    [Fact]
    public async Task Only_managers_create_and_delete()
    {
        var s = await ArrangeAsync();
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(s.Owner, positionIds: [s.WaiterPosition]));

        Assert.Equal(HttpStatusCode.Forbidden, (await Create(s.Waiter, positionIds: [s.CookPosition])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Waiter.Http.DeleteAsync($"/api/announcements/{created.Id}")).StatusCode);
        Assert.Single(await MineAsync(s.Waiter));
    }

    [Fact]
    public async Task Marking_as_read_is_visible_to_the_manager_and_repeating_changes_nothing()
    {
        var s = await ArrangeAsync();
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(s.Owner, positionIds: [s.WaiterPosition]));
        Assert.Null(Assert.Single(await MineAsync(s.Waiter)).ReadAt);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Waiter.Http.PostAsync($"/api/announcements/{created.Id}/read", null)).StatusCode);
        var firstRead = Assert.Single(await MineAsync(s.Waiter)).ReadAt;
        Assert.NotNull(firstRead);
        await s.Waiter.Http.PostAsync($"/api/announcements/{created.Id}/read", null);

        Assert.Equal(firstRead, Assert.Single(await MineAsync(s.Waiter)).ReadAt);
        var all = await ApiClient.ReadAsync<List<AnnouncementDto>>(await s.Owner.Http.GetAsync("/api/announcements"));
        var recipients = Assert.Single(all).Recipients!;
        Assert.Equal(1, recipients.Count(r => r.ReadAt is not null));
        Assert.Equal(1, recipients.Count(r => r.ReadAt is null));
    }

    [Fact]
    public async Task Someone_who_did_not_get_the_announcement_cannot_mark_it_read()
    {
        var s = await ArrangeAsync();
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(s.Owner, positionIds: [s.WaiterPosition]));

        Assert.Equal(HttpStatusCode.NotFound, (await s.Cook.Http.PostAsync($"/api/announcements/{created.Id}/read", null)).StatusCode);
    }

    [Fact]
    public async Task Deleting_removes_it_for_everyone()
    {
        var s = await ArrangeAsync();
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(s.Owner, positionIds: [s.WaiterPosition]));

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.Http.DeleteAsync($"/api/announcements/{created.Id}")).StatusCode);

        Assert.Empty(await MineAsync(s.Waiter));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.Http.DeleteAsync($"/api/announcements/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Bad_input_is_refused()
    {
        var s = await ArrangeAsync();
        var inThePast = DateTimeOffset.UtcNow.AddHours(-1);

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s.Owner, positionIds: [])).StatusCode);                          // nikome
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s.Owner, title: "  ", positionIds: [s.WaiterPosition])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s.Owner, title: new string('x', 101), positionIds: [s.WaiterPosition])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s.Owner, body: new string('x', 2001), positionIds: [s.WaiterPosition])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s.Owner, meetingAt: inThePast, positionIds: [s.WaiterPosition])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(s.Owner, positionIds: [Guid.NewGuid()])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(s.Owner, employeeIds: [Guid.NewGuid()])).StatusCode);
        Assert.Empty(await ApiClient.ReadAsync<List<AnnouncementDto>>(await s.Owner.Http.GetAsync("/api/announcements")));
    }

    [Fact]
    public async Task A_role_nobody_has_means_no_recipients_and_nothing_is_saved()
    {
        var s = await ArrangeAsync();
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await s.Owner.Http.GetAsync("/api/positions"));
        var hostess = positions.Single(p => p.Name == "Hostesa").Id;

        var response = await Create(s.Owner, positionIds: [hostess]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ApiClient.ReadAsync<List<AnnouncementDto>>(await s.Owner.Http.GetAsync("/api/announcements")));
    }

    [Fact]
    public async Task Other_restaurants_never_see_it_and_cannot_target_its_people()
    {
        var s = await ArrangeAsync();
        await Create(s.Owner, positionIds: [s.WaiterPosition]);
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Empty(await ApiClient.ReadAsync<List<AnnouncementDto>>(await stranger.Http.GetAsync("/api/announcements")));
        Assert.Equal(HttpStatusCode.NotFound, (await Create(stranger, employeeIds: [s.CookId])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(stranger, positionIds: [s.WaiterPosition])).StatusCode);
    }
}
