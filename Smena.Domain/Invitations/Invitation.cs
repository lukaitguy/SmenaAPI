using Smena.Domain.Common;
using Smena.Domain.Employees;

namespace Smena.Domain.Invitations;

/// <summary>
/// Poziv zaposlenom da se pridruži restoranu. U bazi se čuva samo heš tokena,
/// pa ni curenje baze ne otkriva važeće linkove.
/// </summary>
public class Invitation : IRestaurantScoped
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Invitation() { }

    public Invitation(
        Guid restaurantId,
        string email,
        EmployeeRole role,
        Guid? positionId,
        string tokenHash,
        Guid invitedByEmployeeId,
        DateTimeOffset now)
    {
        if (role == EmployeeRole.Owner)
            throw new InvalidOperationException("Vlasnik se ne poziva, nastaje pri registraciji restorana.");

        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        Email = email;
        Role = role;
        PositionId = positionId;
        TokenHash = tokenHash;
        InvitedByEmployeeId = invitedByEmployeeId;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public string Email { get; private set; } = null!;
    public EmployeeRole Role { get; private set; }
    public Guid? PositionId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public Guid InvitedByEmployeeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && now < ExpiresAt;

    public void Accept(DateTimeOffset now)
    {
        if (!IsUsable(now))
            throw new InvalidOperationException("Poziv je iskorišćen, opozvan ili je istekao.");

        AcceptedAt = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (AcceptedAt is not null)
            throw new InvalidOperationException("Prihvaćen poziv se ne može opozvati.");

        RevokedAt ??= now;
    }
}
