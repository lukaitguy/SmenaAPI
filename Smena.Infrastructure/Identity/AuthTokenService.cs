using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;
using Smena.Infrastructure.Persistence;

namespace Smena.Infrastructure.Identity;

internal sealed class AuthTokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider time)
    : IAuthTokenService
{
    private static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "Sesija je istekla, prijavi se ponovo.");

    private readonly JwtOptions _options = options.Value;

    public async Task<AuthTokens> IssueAsync(Employee employee, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (refreshToken, entity) = CreateRefreshToken(employee, now);

        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return CreateTokens(employee, now, refreshToken, entity.ExpiresAt);
    }

    public async Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var tokenHash = SecureToken.Hash(refreshToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (stored is null)
            return InvalidRefreshToken;

        if (stored.RevokedAt is not null)
        {
            // Opozvan token je ponovo upotrebljen: neko drugi verovatno ima kopiju,
            // pa se gase sve sesije tog korisnika. Klijent zato ne sme da šalje dva
            // osvežavanja paralelno (Angular interceptor drži jedno u letu).
            await RevokeAllForUserAsync(stored.UserId, now, cancellationToken);
            return InvalidRefreshToken;
        }

        if (!stored.IsActive(now))
            return InvalidRefreshToken;

        // Zaposleni se čita ponovo, pa promena uloge ili deaktivacija važi od sledećeg osvežavanja.
        var employee = await db.Employees
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == stored.EmployeeId && e.Status == EmployeeStatus.Active, cancellationToken);
        if (employee is null)
            return InvalidRefreshToken;

        var (newToken, newEntity) = CreateRefreshToken(employee, now);
        stored.RevokedAt = now;
        stored.ReplacedByTokenId = newEntity.Id;
        db.RefreshTokens.Add(newEntity);

        // xmin na RefreshToken: od dva istovremena osvežavanja istim tokenom uspeva samo jedno.
        await db.SaveChangesAsync(cancellationToken);

        return CreateTokens(employee, now, newToken, newEntity.ExpiresAt);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = SecureToken.Hash(refreshToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        RevokeAllForUserAsync(userId, time.GetUtcNow(), cancellationToken);

    private async Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    private (string Token, RefreshToken Entity) CreateRefreshToken(Employee employee, DateTimeOffset now)
    {
        var token = SecureToken.Generate(64);
        var entity = new RefreshToken
        {
            UserId = employee.UserId,
            EmployeeId = employee.Id,
            TokenHash = SecureToken.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        };
        return (token, entity);
    }

    private AuthTokens CreateTokens(Employee employee, DateTimeOffset now, string refreshToken, DateTimeOffset refreshExpiresAt)
    {
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(SmenaClaims.UserId, employee.UserId.ToString()),
                new Claim(SmenaClaims.EmployeeId, employee.Id.ToString()),
                new Claim(SmenaClaims.RestaurantId, employee.RestaurantId.ToString()),
                new Claim(SmenaClaims.Role, employee.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
        };

        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AuthTokens(accessToken, expiresAt, refreshToken, refreshExpiresAt);
    }
}
