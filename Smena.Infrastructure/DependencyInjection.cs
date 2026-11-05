using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Infrastructure.Email;
using Smena.Infrastructure.Identity;
using Smena.Infrastructure.Persistence;
using Smena.Infrastructure.Push;
using Smena.Infrastructure.Security;

namespace Smena.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Smena")
            ?? throw new InvalidOperationException("Nedostaje ConnectionStrings:Smena.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddDataProtection()
            .SetApplicationName("Smena")
            .PersistKeysToDbContext<AppDbContext>()
            .ProtectKeys(KeyProtectionOptions.From(configuration));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IAuthTokenService, AuthTokenService>();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        if (string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Host"]))
            services.AddScoped<IEmailSender, LoggingEmailSender>();
        else
            services.AddScoped<IEmailSender, SmtpEmailSender>();

        services.AddSingleton<ChannelEmailQueue>();
        services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<ChannelEmailQueue>());
        services.AddHostedService<EmailQueueWorker>();

        services.Configure<PushOptions>(configuration.GetSection(PushOptions.SectionName));
        services.AddSingleton<IPushSender, WebPushSender>();
        services.AddScoped<PushDispatcher>();
        services.AddSingleton<ChannelPushQueue>();
        services.AddSingleton<IPushQueue>(sp => sp.GetRequiredService<ChannelPushQueue>());
        services.AddHostedService<PushQueueWorker>();

        return services;
    }
}
