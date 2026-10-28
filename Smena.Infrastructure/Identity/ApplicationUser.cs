using Microsoft.AspNetCore.Identity;

namespace Smena.Infrastructure.Identity;

/// <summary>
/// Nalog za prijavu. Podaci o radu (restoran, uloga, pozicije) su u Domain entitetu Employee.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
}
