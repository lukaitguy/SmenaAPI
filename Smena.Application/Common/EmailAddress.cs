namespace Smena.Application.Common;

public static class EmailAddress
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
