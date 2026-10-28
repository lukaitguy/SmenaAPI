using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Smena.Application;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Infrastructure;
using Smena.Infrastructure.Identity;
using Smena.Infrastructure.Persistence;
using Smena.Infrastructure.Security;
using SmenaAPI.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddRateLimiting(builder.Configuration);

// Iza reverse proxy-ja (nginx, Traefik, load balancer) prava adresa klijenta stiže u X-Forwarded-For.
// Uključuje se izričito, jer ga inače bilo ko može lažirati i zaobići ograničenje zahteva.
// Uključiti samo kad je API dostupan isključivo preko proxy-ja.
if (builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        // Claim-ovi ostaju pod kratkim imenima (sub, role, restaurant_id), bez Microsoft URI mapiranja.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
            NameClaimType = SmenaClaims.UserId,
            RoleClaimType = SmenaClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    // Sve je zaključano dok endpoint izričito ne kaže [AllowAnonymous].
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.Manager, policy => policy.RequireRole(Policies.ManagerRoles));

var app = builder.Build();

if (!app.Environment.IsDevelopment() && !KeyProtectionOptions.From(app.Configuration).IsEncryptionConfigured)
{
    app.Logger.LogWarning(
        "DataProtection ključevi se čuvaju nešifrovani u bazi. Postavi DataProtection:Certificate " +
        "(Path ili Base64, uz Password) da se šifruju sertifikatom.");
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    // Zgodno lokalno i u Compose-u. Za produkciju plan predviđa migration bundle u CI-u (7. nedelja).
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;
