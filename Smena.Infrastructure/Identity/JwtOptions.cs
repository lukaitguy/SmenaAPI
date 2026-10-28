using System.ComponentModel.DataAnnotations;

namespace Smena.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = null!;

    [Required]
    public string Audience { get; init; } = null!;

    /// <summary>HMAC-SHA256 ključ, najmanje 32 bajta. U produkciji samo kroz env var (Jwt__SigningKey).</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = null!;

    [Range(1, 120)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;
}
