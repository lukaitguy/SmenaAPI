using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SmenaAPI.Infrastructure;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Prijava, registracija, reset lozinke i pozivnice: zajednički budžet po IP adresi.</summary>
    public WindowOptions Auth { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Zahtevi za reset lozinke (svaki može da pošalje mejl), po IP adresi.</summary>
    public WindowOptions ForgotPassword { get; init; } = new() { PermitLimit = 3, WindowSeconds = 900 };

    public sealed class WindowOptions
    {
        [Range(1, int.MaxValue)]
        public int PermitLimit { get; init; }

        [Range(1, int.MaxValue)]
        public int WindowSeconds { get; init; }
    }
}

internal static class RateLimiting
{
    public const string Auth = "auth";
    public const string ForgotPassword = "forgot-password";

    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(Auth, context => PerClient(context, options.Auth));
            limiter.AddPolicy(ForgotPassword, context => PerClient(context, options.ForgotPassword));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                await response.WriteAsJsonAsync(new
                {
                    status = StatusCodes.Status429TooManyRequests,
                    title = "Previše zahteva.",
                    detail = "Sačekajte malo pa pokušajte ponovo.",
                    code = "rate_limited",
                }, cancellationToken);
            };
        });

        return services;
    }

    // Particija je IP adresa klijenta. Iza proxy-ja je to prava adresa samo ako je uključen
    // ForwardedHeaders:Enabled, inače svi klijenti dele adresu proxy-ja i istu kvotu.
    private static RateLimitPartition<string> PerClient(HttpContext context, RateLimitOptions.WindowOptions window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = window.PermitLimit,
                Window = TimeSpan.FromSeconds(window.WindowSeconds),
                QueueLimit = 0,
            });
}
