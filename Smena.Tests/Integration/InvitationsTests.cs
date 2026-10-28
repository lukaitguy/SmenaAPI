using System.Net;
using System.Net.Http.Json;
using Smena.Domain.Employees;
using SmenaAPI.Controllers;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class InvitationsTests(ApiFactory factory)
{
    private static async Task<CreatedInvitationResponse> InviteAsync(ApiClient owner, string email, string role = "Staff") =>
        await ApiClient.ReadAsync<CreatedInvitationResponse>(
            await owner.Http.PostAsJsonAsync("/api/invitations", new { email, role }, ApiClient.Json));

    private Task<HttpResponseMessage> AcceptAsync(CreatedInvitationResponse invitation) =>
        factory.CreateClient().PostAsJsonAsync("/api/invitations/accept", new
        {
            token = Uri.UnescapeDataString(invitation.AcceptUrl.Split('#')[1]),
            firstName = "A",
            lastName = "B",
            password = ApiClient.Password,
        }, ApiClient.Json);

    [Fact]
    public async Task Invited_employee_joins_with_the_invited_role()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var manager = await owner.InviteAsync(factory, EmployeeRole.Manager);

        Assert.Equal(EmployeeRole.Manager, (await manager.MeAsync()).Role);
    }

    [Fact]
    public async Task Invitation_cannot_be_used_twice()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var invitation = await InviteAsync(owner, ApiClient.NewEmail());

        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(invitation)).StatusCode);
        Assert.False((await AcceptAsync(invitation)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Revoked_invitation_cannot_be_used()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var invitation = await InviteAsync(owner, ApiClient.NewEmail());

        var revoke = await owner.Http.DeleteAsync($"/api/invitations/{invitation.Invitation.Id}");
        var accept = await AcceptAsync(invitation);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.False(accept.IsSuccessStatusCode);
    }

    [Fact]
    public async Task New_invitation_to_same_email_replaces_the_old_one()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var email = ApiClient.NewEmail();
        var first = await InviteAsync(owner, email);
        var second = await InviteAsync(owner, email);

        Assert.False((await AcceptAsync(first)).IsSuccessStatusCode);
        Assert.True((await AcceptAsync(second)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Invitation_email_is_sent_with_the_accept_link()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var email = ApiClient.NewEmail();

        var invitation = await InviteAsync(owner, email);

        var message = Assert.Single(factory.Emails.Sent, m => m.To == email);
        Assert.True(invitation.EmailSent);
        Assert.Contains(invitation.AcceptUrl, message.TextBody);
        Assert.Contains("Test restoran", message.Subject);
        Assert.Contains("Vlasnik Test", message.TextBody);
    }

    [Fact]
    public async Task Failed_email_does_not_fail_the_invitation()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var email = ApiClient.NewEmail();
        factory.Emails.FailFor(email);

        var invitation = await InviteAsync(owner, email);

        Assert.False(invitation.EmailSent);
        Assert.Empty(factory.Emails.Sent.Where(m => m.To == email));
        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(invitation)).StatusCode);
    }

    [Fact]
    public async Task Existing_account_email_conflicts()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var response = await owner.Http.PostAsJsonAsync("/api/invitations", new { email = owner.Email, role = "Staff" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Owner_role_cannot_be_invited()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var response = await owner.Http.PostAsJsonAsync("/api/invitations", new { email = ApiClient.NewEmail(), role = "Owner" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_create_invitations()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);

        var response = await staff.Http.PostAsJsonAsync("/api/invitations", new { email = ApiClient.NewEmail(), role = "Staff" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
