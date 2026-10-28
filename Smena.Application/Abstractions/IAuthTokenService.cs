using Smena.Application.Common;
using Smena.Domain.Employees;

namespace Smena.Application.Abstractions;

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public interface IAuthTokenService
{
    Task<AuthTokens> IssueAsync(Employee employee, CancellationToken cancellationToken);

    /// <summary>
    /// Rotacija: stari refresh token se opoziva i izdaje se novi par. Ponovna upotreba
    /// već opozvanog tokena opoziva sve tokene tog korisnika (znak krađe).
    /// </summary>
    Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}
