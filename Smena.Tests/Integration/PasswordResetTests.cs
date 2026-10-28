using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public partial class PasswordResetTests(ApiFactory factory)
{
    private const string NewPassword = "novaLozinka456";

    [GeneratedRegex(@"https?://\S+#\S+")]
    private static partial Regex LinkPattern();

    private Task<HttpResponseMessage> Forgot(string email) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email }, ApiClient.Json);

    private Task<HttpResponseMessage> Reset(string email, string token, string password = NewPassword) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password",
            new { email, token, newPassword = password }, ApiClient.Json);

    private Task<HttpResponseMessage> Login(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password }, ApiClient.Json);

    /// <summary>Čita token iz linka u poslatom mejlu, kao što bi ga pročitao frontend.</summary>
    private string TokenFromEmail(string email)
    {
        var message = Assert.Single(factory.Emails.Sent, m => m.To == email);
        var link = LinkPattern().Match(message.TextBody).Value;
        var fragment = HttpUtility.ParseQueryString(link[(link.IndexOf('#') + 1)..]);
        Assert.Equal(email, fragment["email"]);
        return fragment["token"]!;
    }

    [Fact]
    public async Task Reset_flow_changes_the_password()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await Forgot(owner.Email)).StatusCode);
        var token = TokenFromEmail(owner.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await Reset(owner.Email, token)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login(owner.Email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(owner.Email, ApiClient.Password)).StatusCode);
    }

    [Fact]
    public async Task Unknown_email_gets_the_same_response_and_no_mail()
    {
        var email = ApiClient.NewEmail();

        var response = await Forgot(email);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(factory.Emails.Sent, m => m.To == email);
    }

    [Fact]
    public async Task Failing_mail_server_still_returns_no_content()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        factory.Emails.FailFor(owner.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await Forgot(owner.Email)).StatusCode);
    }

    [Fact]
    public async Task Token_works_only_once()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        await Forgot(owner.Email);
        var token = TokenFromEmail(owner.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await Reset(owner.Email, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(owner.Email, token, "drugaLozinka789")).StatusCode);
    }

    [Fact]
    public async Task Garbage_token_and_unknown_email_are_rejected_alike()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(owner.Email, "nije-token")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(ApiClient.NewEmail(), "nije-token")).StatusCode);
    }

    [Fact]
    public async Task Token_cannot_be_used_for_another_account()
    {
        var first = await ApiClient.RegisterOwnerAsync(factory);
        var second = await ApiClient.RegisterOwnerAsync(factory);
        await Forgot(first.Email);
        var token = TokenFromEmail(first.Email);

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(second.Email, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(second.Email, ApiClient.Password)).StatusCode);
    }

    [Fact]
    public async Task Weak_password_is_rejected_and_the_token_stays_valid()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        await Forgot(owner.Email);
        var token = TokenFromEmail(owner.Email);

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(owner.Email, token, "kratka")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Reset(owner.Email, token)).StatusCode);
    }

    [Fact]
    public async Task Reset_unlocks_a_locked_out_account()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        for (var i = 0; i < 5; i++)
            await Login(owner.Email, "pogresna-lozinka");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(owner.Email, ApiClient.Password)).StatusCode);

        await Forgot(owner.Email);
        await Reset(owner.Email, TokenFromEmail(owner.Email));

        Assert.Equal(HttpStatusCode.OK, (await Login(owner.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_signs_the_user_out_everywhere()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var device = factory.CreateClient();
        var login = await device.PostAsJsonAsync("/api/auth/login",
            new { email = owner.Email, password = ApiClient.Password }, ApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        async Task<HttpStatusCode> Refresh()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
            request.Headers.Add("Cookie", cookie);
            return (await device.SendAsync(request)).StatusCode;
        }

        await Forgot(owner.Email);
        await Reset(owner.Email, TokenFromEmail(owner.Email));

        Assert.Equal(HttpStatusCode.Unauthorized, await Refresh());
    }
}
