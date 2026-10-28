using Smena.Application.Common;

namespace Smena.Application.Abstractions;

/// <summary>Omotač oko ASP.NET Identity, da Application ne zavisi od UserManager-a.</summary>
public interface IIdentityService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    /// <returns>Email adrese traženih korisnika, po id-u korisnika.</returns>
    Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<Result<Guid>> CreateUserAsync(string email, string password, CancellationToken cancellationToken);

    /// <returns>Id korisnika ili null ako email i lozinka ne odgovaraju (ili je nalog zaključan).</returns>
    Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    /// <returns>Token za reset lozinke ili null ako nalog sa tim emailom ne postoji.</returns>
    Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken);

    /// <summary>Postavlja novu lozinku i skida zaključavanje naloga; vraća id korisnika.</summary>
    Task<Result<Guid>> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken);
}
