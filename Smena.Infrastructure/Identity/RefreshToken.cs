namespace Smena.Infrastructure.Identity;

/// <summary>
/// Refresh token u bazi; čuva se samo SHA-256 heš. Pri svakom osvežavanju stari se opoziva
/// i pokazuje na novi (ReplacedByTokenId), pa se ponovna upotreba starog tokena prepoznaje.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid EmployeeId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
