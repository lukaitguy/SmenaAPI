using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Smena.Application.Abstractions;
using Smena.Infrastructure.Push;

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
    public FakePushSender Pushes { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Smena",
            Environment.GetEnvironmentVariable("SMENA_TEST_CONNECTION") ?? DefaultConnection);
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-0123456789abcdef0123456789");
        builder.UseSetting("Database:MigrateOnStartup", "true");

        // Samo za testove: javni par ključeva (prava slanja se ne dešavaju, šalje lažni sender).
        builder.UseSetting("Push:PublicKey", TestVapid.PublicKey);
        builder.UseSetting("Push:PrivateKey", TestVapid.PrivateKey);

        // Ostali testovi šalju mnogo zahteva sa iste adrese, pa je ograničenje praktično isključeno.
        // RateLimitTests ga spušta na male vrednosti.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:ForgotPassword:PermitLimit", "100000");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IEmailSender>(Emails);
            // Red za slanje u pozadini bi testove učinio nepouzdanim (mejl stiže "kasnije"), pa se u testovima šalje odmah.
            services.AddSingleton<IEmailQueue>(new InlineEmailQueue(Emails));

            // Isto za web push: red se zaobilazi, a umesto push servisa browsera piše lažni sender.
            services.AddSingleton<IPushSender>(Pushes);
            services.AddSingleton<IPushQueue, InlinePushQueue>();
        });
    }
}

/// <summary>Zamena za red mejlova u testovima: šalje odmah, pa test odmah vidi šta je poslato.</summary>
public sealed class InlineEmailQueue(FakeEmailSender sender) : IEmailQueue
{
    public void Enqueue(EmailMessage message)
    {
        try
        {
            sender.SendAsync(message, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            // Pravi red u pozadini loguje grešku slanja i ide dalje.
        }
    }
}

/// <summary>Javni VAPID par samo za testove.</summary>
public static class TestVapid
{
    public const string PublicKey = "BMOJ35d2cH3scayzfNDiUxeCZ2F_0VSDDOFWmOCKwD-RJmtt-sE0umPiF81Kf2zLYSBLMEcHtlCe475TJ9ucEJM";
    public const string PrivateKey = "Zz7vM5BY7TcXfSDpckbSdYLDHpwIEnIaYBBUQj3v_U8";
}

/// <summary>Pamti šta bi otišlo na koji uređaj; <see cref="Gone"/> i <see cref="Failing"/> simuliraju odgovor push servisa.</summary>
public sealed class FakePushSender : IPushSender
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(PushTarget Target, string Payload)> _sent = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PushResult> _results = new();

    public bool IsConfigured => true;

    public IReadOnlyCollection<(PushTarget Target, string Payload)> Sent => _sent;

    public void Gone(string endpoint) => _results[endpoint] = PushResult.Gone;

    public void Failing(string endpoint) => _results[endpoint] = PushResult.Failed;

    /// <summary>Push servis se oporavio: poruke ponovo stižu.</summary>
    public void Recover(string endpoint) => _results.TryRemove(endpoint, out _);

    public Task<PushResult> SendAsync(PushTarget target, string payload, CancellationToken cancellationToken)
    {
        var result = _results.GetValueOrDefault(target.Endpoint, PushResult.Sent);
        if (result == PushResult.Sent)
            _sent.Enqueue((target, payload));
        return Task.FromResult(result);
    }
}

/// <summary>Šalje odmah, u zasebnom opsegu kao pozadinski radnik, pa test odmah vidi šta je poslato.</summary>
public sealed class InlinePushQueue(IServiceScopeFactory scopes) : IPushQueue
{
    public void Enqueue(PushMessage message)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<PushDispatcher>().SendAsync(message, CancellationToken.None).GetAwaiter().GetResult();
    }
}
