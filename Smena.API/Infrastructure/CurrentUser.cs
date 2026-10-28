using System.Security.Claims;
using Smena.Application.Abstractions;
using Smena.Infrastructure.Identity;

namespace SmenaAPI.Infrastructure;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public Guid? UserId => ReadGuid(SmenaClaims.UserId);
    public Guid? EmployeeId => ReadGuid(SmenaClaims.EmployeeId);
    public Guid? RestaurantId => ReadGuid(SmenaClaims.RestaurantId);

    private Guid? ReadGuid(string claimType) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(claimType), out var value) ? value : null;
}
