using Microsoft.AspNetCore.Identity;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        await userManager.FindByEmailAsync(email) is not null;

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
}
