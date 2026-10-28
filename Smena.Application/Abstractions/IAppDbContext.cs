using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Smena.Domain.Employees;
using Smena.Domain.Invitations;
using Smena.Domain.Restaurants;

namespace Smena.Application.Abstractions;

/// <summary>
/// Application sloj radi direktno sa EF Core DbSet-ovima umesto kroz repozitorijume:
/// manje koda, a upiti ostaju čitljivi. Cena je zavisnost od EF Core paketa (ne od Npgsql-a).
/// </summary>
public interface IAppDbContext
{
    DbSet<Restaurant> Restaurants { get; }
    DbSet<Position> Positions { get; }
    DbSet<Employee> Employees { get; }
    DbSet<Invitation> Invitations { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
