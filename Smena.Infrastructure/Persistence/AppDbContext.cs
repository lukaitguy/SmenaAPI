using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Domain.Common;
using Smena.Domain.Employees;
using Smena.Domain.Invitations;
using Smena.Domain.Restaurants;
using Smena.Domain.Shifts;
using Smena.Infrastructure.Identity;

namespace Smena.Infrastructure.Persistence;

/// <summary>
/// Jedna baza za sve restorane. Svaki <see cref="IRestaurantScoped"/> entitet dobija global
/// query filter po RestaurantId iz JWT claim-a, pa upit bez claim-a ne vraća ništa.
/// Kod koji svesno radi preko granice restorana (login, prihvatanje poziva) mora da pozove
/// IgnoreQueryFilters(), što se lako nađe pretragom.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityUserContext<ApplicationUser, Guid>(options), IAppDbContext
{
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // EF parametrizuje filter ovim svojstvom za svaku instancu konteksta (jednu po zahtevu).
    private Guid? CurrentRestaurantId => currentUser.RestaurantId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity sam postavlja imena tabela (AspNetUsers...), pa ih ovde svodimo na snake_case.
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<Restaurant>().HasQueryFilter(r => r.Id == CurrentRestaurantId);

        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(IRestaurantScoped).IsAssignableFrom(t.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var restaurantId = Expression.Property(parameter, nameof(IRestaurantScoped.RestaurantId));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentRestaurantId));
            var filter = Expression.Lambda(
                Expression.Equal(Expression.Convert(restaurantId, typeof(Guid?)), current),
                parameter);

            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardRestaurantBoundary();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Druga linija odbrane: prijavljeni korisnik ne može da upiše red drugog restorana,
    /// čak ni ako handler greškom koristi IgnoreQueryFilters().
    /// </summary>
    private void GuardRestaurantBoundary()
    {
        if (CurrentRestaurantId is not { } restaurantId)
            return;

        var foreign = ChangeTracker.Entries<IRestaurantScoped>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Any(e => e.Entity.RestaurantId != restaurantId);

        if (foreign)
            throw new InvalidOperationException("Pokušaj upisa podataka drugog restorana.");
    }
}
