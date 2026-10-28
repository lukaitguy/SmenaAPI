using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smena.Domain.Employees;
using Smena.Domain.Invitations;
using Smena.Domain.Restaurants;
using Smena.Infrastructure.Identity;

namespace Smena.Infrastructure.Persistence.Configurations;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.TimeZoneId).HasMaxLength(64);
    }
}

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.HasIndex(p => new { p.RestaurantId, p.Name }).IsUnique();
        builder.HasOne<Restaurant>().WithMany().HasForeignKey(p => p.RestaurantId);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.Property(e => e.FirstName).HasMaxLength(100);
        builder.Property(e => e.LastName).HasMaxLength(100);
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.RestaurantId, e.UserId }).IsUnique();
        builder.HasOne<Restaurant>().WithMany().HasForeignKey(e => e.RestaurantId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId);

        builder.HasMany(e => e.Positions)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "employee_positions",
                position => position.HasOne<Position>().WithMany().HasForeignKey("PositionId"),
                employee => employee.HasOne<Employee>().WithMany().HasForeignKey("EmployeeId"));
        builder.Navigation(e => e.Positions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.Property(i => i.Email).HasMaxLength(256);
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.TokenHash).HasMaxLength(64);

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.RestaurantId, i.Email });
        builder.HasOne<Restaurant>().WithMany().HasForeignKey(i => i.RestaurantId);
        builder.HasOne<Position>().WithMany().HasForeignKey(i => i.PositionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Employee>().WithMany().HasForeignKey(i => i.InvitedByEmployeeId).OnDelete(DeleteBehavior.Restrict);

        // PostgreSQL sistemska kolona xmin kao optimistic concurrency token.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId);

        builder.Property<uint>("Version").IsRowVersion();
    }
}
