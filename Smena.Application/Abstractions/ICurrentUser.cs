namespace Smena.Application.Abstractions;

/// <summary>Prijavljeni korisnik, pročitan iz JWT claim-ova.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? EmployeeId { get; }
    Guid? RestaurantId { get; }
}
