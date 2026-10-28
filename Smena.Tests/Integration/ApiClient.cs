using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Smena.Application.Auth;
using Smena.Application.Employees;
using Smena.Application.Positions;
using Smena.Domain.Employees;
using SmenaAPI.Controllers;

namespace Smena.Tests.Integration;

/// <summary>Tanak omotač oko HttpClient-a sa JWT-om i pomoćnim metodama za pripremu podataka.</summary>
public sealed class ApiClient(HttpClient http, string email)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public const string Password = "lozinka123";

    public HttpClient Http => http;
    public string Email => email;

    public static string NewEmail() => $"{Guid.NewGuid():N}@test.rs";

    /// <summary>Registruje novi restoran; vraćeni klijent je prijavljen kao njegov vlasnik.</summary>
    public static async Task<ApiClient> RegisterOwnerAsync(ApiFactory factory)
    {
        var email = NewEmail();
        var client = new ApiClient(factory.CreateClient(), email);
        var response = await client.Http.PostAsJsonAsync("/api/auth/register", new
        {
            restaurantName = "Test restoran",
            firstName = "Vlasnik",
            lastName = "Test",
            email,
            password = Password,
        }, Json);
        response.EnsureSuccessStatusCode();
        client.SetToken(await ReadAsync<AccessTokenResponse>(response));
        return client;
    }

    /// <summary>Poziva novog zaposlenog u isti restoran i vraća klijenta prijavljenog kao on.</summary>
    public async Task<ApiClient> InviteAsync(ApiFactory factory, EmployeeRole role = EmployeeRole.Staff)
    {
        var email = NewEmail();
        var created = await ReadAsync<CreatedInvitationResponse>(
            await Http.PostAsJsonAsync("/api/invitations", new { email, role }, Json));
        var token = Uri.UnescapeDataString(created.AcceptUrl.Split('#')[1]);

        var invited = new ApiClient(factory.CreateClient(), email);
        var response = await invited.Http.PostAsJsonAsync("/api/invitations/accept", new
        {
            token,
            firstName = "Zaposleni",
            lastName = "Test",
            password = Password,
        }, Json);
        response.EnsureSuccessStatusCode();
        invited.SetToken(await ReadAsync<AccessTokenResponse>(response));
        return invited;
    }

    public void SetToken(AccessTokenResponse token) =>
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

    public async Task<CurrentEmployeeDto> MeAsync() =>
        await ReadAsync<CurrentEmployeeDto>(await http.GetAsync("/api/auth/me"));

    public async Task<PositionDto> CreatePositionAsync(string? name = null) =>
        await ReadAsync<PositionDto>(await http.PostAsJsonAsync(
            "/api/positions", new { name = name ?? $"Pozicija {Guid.NewGuid():N}" }, Json));

    public async Task<EmployeeDto> SetPositionsAsync(Guid employeeId, params Guid[] positionIds)
    {
        var current = await ReadAsync<EmployeeDto>(await http.GetAsync($"/api/employees/{employeeId}"));
        return await ReadAsync<EmployeeDto>(await http.PutAsJsonAsync($"/api/employees/{employeeId}", new
        {
            current.FirstName,
            current.LastName,
            current.Role,
            positionIds,
        }, Json));
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}
