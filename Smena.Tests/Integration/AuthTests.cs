using System.Net;
using System.Net.Http.Json;
using Smena.Application.Auth;
using Smena.Domain.Employees;
using SmenaAPI.Controllers;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Register_makes_caller_the_owner_with_default_positions()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var me = await owner.MeAsync();

        Assert.Equal(EmployeeRole.Owner, me.Role);
        Assert.Equal("Test restoran", me.RestaurantName);
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        Assert.Contains(positions, p => p.Name == "Konobar");
    }

    [Fact]
    public async Task Register_with_taken_email_conflicts()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            restaurantName = "Drugi",
            firstName = "A",
            lastName = "B",
            email = owner.Email,
            password = ApiClient.Password,
        }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_correct_password_returns_token()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = owner.Email, password = ApiClient.Password }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await ApiClient.ReadAsync<AccessTokenResponse>(response)).AccessToken));
    }

    [Fact]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = owner.Email, password = "pogresna-lozinka" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/positions")]
    [InlineData("/api/employees")]
    [InlineData("/api/invitations")]
    public async Task Protected_endpoints_require_a_token(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_check_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
