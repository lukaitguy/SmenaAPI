namespace Smena.Infrastructure.Identity;

/// <summary>Nazivi claim-ova u access tokenu; API ih čita bez mapiranja na Microsoft URI-je.</summary>
public static class SmenaClaims
{
    public const string UserId = "sub";
    public const string EmployeeId = "employee_id";
    public const string RestaurantId = "restaurant_id";
    public const string Role = "role";
}
