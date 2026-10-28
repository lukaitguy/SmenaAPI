using System.ComponentModel.DataAnnotations;

namespace Smena.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP server. Ako je prazan, mejlovi se samo upisuju u log (korisno za lokalni razvoj).</summary>
    public string? Host { get; init; }

    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    public string? Username { get; init; }

    /// <summary>U produkciji samo kroz env var (Email__Password).</summary>
    public string? Password { get; init; }

    /// <summary>Šifrovanje veze: STARTTLS kad server podržava, inače bez. Mailpit lokalno nema TLS.</summary>
    public bool UseTls { get; init; } = true;

    [Required, EmailAddress]
    public string FromAddress { get; init; } = "no-reply@smena.local";

    [Required]
    public string FromName { get; init; } = "Smena";
}
