using Smena.Application.Abstractions;

namespace SmenaAPI.Infrastructure;

/// <summary>
/// Refresh token ide u HttpOnly cookie, pa ga JavaScript (i XSS) ne može pročitati.
/// Access token klijent drži samo u memoriji. SameSite=Strict i putanja /api/auth
/// znače da browser cookie šalje jedino na auth endpointe sa našeg sajta.
/// </summary>
internal static class RefreshTokenCookie
{
    public const string Name = "smena_refresh";
    private const string Path = "/api/auth";

    public static void Set(HttpResponse response, AuthTokens tokens) =>
        response.Cookies.Append(Name, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = tokens.RefreshTokenExpiresAt,
        });

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
        });
}
