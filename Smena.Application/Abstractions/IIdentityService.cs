using Smena.Application.Common;

namespace Smena.Application.Abstractions;

/// <summary>Omotač oko ASP.NET Identity, da Application ne zavisi od UserManager-a.</summary>
public interface IIdentityService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<Result<Guid>> CreateUserAsync(string email, string password, CancellationToken cancellationToken);

    /// <returns>Id korisnika ili null ako email i lozinka ne odgovaraju (ili je nalog zaključan).</returns>
    Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);
}
