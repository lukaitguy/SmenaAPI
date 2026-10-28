using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        await userManager.FindByEmailAsync(email) is not null;

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await userManager.Users
            .Where(u => userIds.Contains(u.Id) && u.Email != null)
            .ToDictionaryAsync(u => u.Id, u => u.Email!, cancellationToken);

    public async Task<Result<Guid>> CreateUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser { Id = Guid.CreateVersion7(), UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, password);

        if (result.Succeeded)
            return user.Id;

        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        return Error.Validation("auth.identity", message);
    }

    public async Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || await userManager.IsLockedOutAsync(user))
            return null;

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return user.Id;
    }

    public async Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? null : await userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<Result<Guid>> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken)
    {
        var invalidToken = Error.Validation("auth.invalid_reset_token", "Link za reset lozinke je neispravan ili je istekao.");

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return invalidToken;

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                return invalidToken;

            return Error.Validation("auth.identity", string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // Ko je zaključan zbog pogrešnih pokušaja ne sme da ostane zaključan posle uspešnog reseta.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        return user.Id;
    }
}
