namespace Smena.Domain.Common;

/// <summary>
/// Entitet koji pripada jednom restoranu (tenantu). Global query filter u Infrastructure
/// sloju vraća samo redove restorana iz JWT claim-a.
/// </summary>
public interface IRestaurantScoped
{
    Guid RestaurantId { get; }
}
