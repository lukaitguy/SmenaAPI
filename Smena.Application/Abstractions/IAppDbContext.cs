using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Smena.Domain.DayOffs;
using Smena.Domain.Employees;
using Smena.Domain.Invitations;
using Smena.Domain.Restaurants;
using Smena.Domain.Announcements;
using Smena.Domain.EightySix;
using Smena.Domain.Push;
using Smena.Domain.Shifts;
using Smena.Domain.Wishes;

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
    DbSet<Shift> Shifts { get; }
    DbSet<ShiftSwap> ShiftSwaps { get; }
    DbSet<ShiftClaim> ShiftClaims { get; }
    DbSet<DayOffRequest> DayOffRequests { get; }
    DbSet<ShiftTemplate> ShiftTemplates { get; }
    DbSet<ShiftWish> ShiftWishes { get; }
    DbSet<EightySixItem> EightySixItems { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<AnnouncementRecipient> AnnouncementRecipients { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
