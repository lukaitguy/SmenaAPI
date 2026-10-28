using System.Security.Cryptography;
using System.Text;

namespace Smena.Application.Common;

/// <summary>Nasumični token za link (poziv, refresh) i njegov SHA-256 heš za bazu.</summary>
public static class SecureToken
{
    public static string Generate(int byteLength = 32) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(byteLength));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
