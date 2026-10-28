using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Smena.Application.Abstractions;

namespace Smena.Tests.Integration;

/// <summary>
/// Diže ceo API nad pravim PostgreSQL-om (iz docker-compose.yml: <c>docker compose up -d db</c>).
/// Koristi zasebnu bazu smena_tests, a svaki test pravi sopstveni restoran pa se testovi ne mešaju.
/// Drugi server se zadaje promenljivom okruženja SMENA_TEST_CONNECTION.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=smena_tests;Username=smena;Password=smena_dev";

    public FakeEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Smena",
            Environment.GetEnvironmentVariable("SMENA_TEST_CONNECTION") ?? DefaultConnection);
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-0123456789abcdef0123456789");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.ConfigureServices(services => services.AddSingleton<IEmailSender>(Emails));
    }
}
