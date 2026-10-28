using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class RateLimitTests(ApiFactory factory)
{
    /// <summary>Svaki test dobija sopstveni server, jer se brojač zahteva čuva u njemu.</summary>
    private WebApplicationFactory<Program> WithLimits(int auth = 3, int forgotPassword = 2) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Auth:PermitLimit", auth.ToString());
            builder.UseSetting("RateLimiting:ForgotPassword:PermitLimit", forgotPassword.ToString());
        });

    private static Task<HttpResponseMessage> Login(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password = "pogresna-lozinka" }, ApiClient.Json);

    [Fact]
    public async Task Login_is_limited_per_client_after_the_permit_is_used()
    {
        var client = WithLimits(auth: 3).CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, ApiClient.NewEmail())).StatusCode);

        var limited = await Login(client, ApiClient.NewEmail());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Contains("rate_limited", await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Auth_endpoints_share_one_budget()
    {
        var client = WithLimits(auth: 2).CreateClient();

        await Login(client, ApiClient.NewEmail());
        await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email = ApiClient.NewEmail(), token = "x", newPassword = "lozinka123" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client, ApiClient.NewEmail())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/invitations/accept",
            new { token = "x", firstName = "A", lastName = "B", password = "lozinka123" }, ApiClient.Json)).StatusCode);
    }

    [Fact]
    public async Task Other_endpoints_are_not_affected_by_an_exhausted_auth_budget()
    {
        var web = WithLimits(auth: 1);
        var owner = await ApiClient.RegisterOwnerAsync(web);
        var client = web.CreateClient();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client, ApiClient.NewEmail())).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.Http.GetAsync("/api/positions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_has_its_own_stricter_limit()
    {
        var client = WithLimits(auth: 100, forgotPassword: 2).CreateClient();
        Task<HttpResponseMessage> Forgot() => client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = ApiClient.NewEmail() }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.NoContent, (await Forgot()).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Forgot()).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Forgot()).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, ApiClient.NewEmail())).StatusCode);
    }

    [Fact]
    public async Task One_address_gets_only_a_few_reset_emails_per_hour_even_from_many_clients()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        // Zahtevi sa "različitih" adresa: IP ograničenje ih ne zaustavlja, ograničenje po adresi mora.
        for (var i = 0; i < 6; i++)
        {
            var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password",
                new { email = owner.Email.ToUpperInvariant() }, ApiClient.Json);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(3, factory.Emails.Sent.Count(m => m.To == owner.Email));
    }
}
