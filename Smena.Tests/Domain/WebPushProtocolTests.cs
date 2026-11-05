using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Smena.Infrastructure.Push;
using Smena.Tests.Integration;

namespace Smena.Tests.Domain;

/// <summary>
/// Šifrovanje i potpis web pusha proveravaju se uzorkom koji je napravila nezavisna implementacija (Node biblioteka
/// <c>http_ece</c>, koju koristi pravi <c>web-push</c>) za iste ulazne vrednosti: izlaz mora biti isti bajt po bajt.
/// Ključevi i so su iz RFC 8291 (dodatak A). Time je sigurno da poruku razume i Safari na iPhone-u, koji prima
/// samo standardni oblik (aes128gcm).
/// </summary>
public class WebPushProtocolTests
{
    // RFC 8291, dodatak A
    private const string Plaintext = "Nova najava: Sastanak sale — 15:00";
    private const string SenderPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string SenderPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string BrowserPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string Expected =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_rkM1UQrz7fEDKsrRZiURM6WTqtLAVGciJ9jieYU6wGqGFDmhB-PNpKZohk-OAHVETw7Ispg";

    private static ECDiffieHellman Key(string privateKey, string publicKey)
    {
        var point = WebPushProtocol.Decode(publicKey);
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = WebPushProtocol.Decode(privateKey),
            Q = new ECPoint { X = point[1..33], Y = point[33..65] },
        });
    }

    [Fact]
    public void Encryption_matches_an_independent_implementation_byte_for_byte()
    {
        using var sender = Key(SenderPrivate, SenderPublic);

        var body = WebPushProtocol.Encrypt(
            Encoding.UTF8.GetBytes(Plaintext), WebPushProtocol.Decode(BrowserPublic), WebPushProtocol.Decode(AuthSecret), sender, WebPushProtocol.Decode(Salt));

        Assert.Equal(Expected, WebPushProtocol.Encode(body));
    }

    [Fact]
    public void Every_message_gets_a_new_key_and_salt_so_two_identical_messages_differ()
    {
        var payload = Encoding.UTF8.GetBytes(Plaintext);
        var browser = WebPushProtocol.Decode(BrowserPublic);
        var auth = WebPushProtocol.Decode(AuthSecret);

        Assert.NotEqual(WebPushProtocol.Encrypt(payload, browser, auth), WebPushProtocol.Encrypt(payload, browser, auth));
    }

    [Fact]
    public void The_body_carries_salt_record_size_and_the_sender_key_in_its_header()
    {
        var body = WebPushProtocol.Encrypt(Encoding.UTF8.GetBytes("x"), WebPushProtocol.Decode(BrowserPublic), WebPushProtocol.Decode(AuthSecret));

        Assert.Equal(new byte[] { 0, 0, 16, 0 }, body[16..20]);   // 4096 bajtova
        Assert.Equal(65, body[20]);                                // dužina ključa pošiljaoca
        Assert.Equal(4, body[21]);                                 // nekompresovana tačka
        Assert.Equal(16 + 4 + 1 + 65 + 1 + 1 + 16, body.Length);   // zaglavlje + poruka + graničnik + oznaka
    }

    [Fact]
    public void Refuses_broken_keys_and_messages_that_do_not_fit_in_one_record()
    {
        var browser = WebPushProtocol.Decode(BrowserPublic);
        var auth = WebPushProtocol.Decode(AuthSecret);

        Assert.Throws<ArgumentException>(() => WebPushProtocol.Encrypt([1], browser[..64], auth));
        Assert.Throws<ArgumentException>(() => WebPushProtocol.Encrypt([1], browser, auth[..15]));
        Assert.Throws<ArgumentException>(() => WebPushProtocol.Encrypt(new byte[WebPushProtocol.MaxPayloadBytes + 1], browser, auth));
        WebPushProtocol.Encrypt(new byte[WebPushProtocol.MaxPayloadBytes], browser, auth);   // tačno staje
    }

    [Fact]
    public void Vapid_header_is_a_signed_token_for_the_push_service_and_the_public_key()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

        var header = WebPushProtocol.VapidAuthorization(
            "https://web.push.apple.com/QWxs/abc?x=1", "mailto:admin@smena.rs", TestVapid.PublicKey, TestVapid.PrivateKey, now);

        Assert.StartsWith("vapid t=", header);
        Assert.EndsWith($", k={TestVapid.PublicKey}", header);
        var jwt = header["vapid t=".Length..header.IndexOf(',')].Split('.');
        Assert.Equal(3, jwt.Length);
        Assert.Equal("ES256", JsonDocument.Parse(WebPushProtocol.Decode(jwt[0])).RootElement.GetProperty("alg").GetString());
        var claims = JsonDocument.Parse(WebPushProtocol.Decode(jwt[1])).RootElement;
        Assert.Equal("https://web.push.apple.com", claims.GetProperty("aud").GetString());   // samo servis, bez putanje
        Assert.Equal("mailto:admin@smena.rs", claims.GetProperty("sub").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());   // manje od 24 h, kako traži RFC

        // Potpis se proverava javnim ključem, kao što radi push servis.
        var point = WebPushProtocol.Decode(TestVapid.PublicKey);
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[1..33], Y = point[33..65] } });
        Assert.True(verifier.VerifyData(
            Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), WebPushProtocol.Decode(jwt[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void Vapid_refuses_malformed_keys()
    {
        Assert.ThrowsAny<Exception>(() => WebPushProtocol.VapidAuthorization("https://x.example/a", "mailto:a@b.rs", "kratko", "kratko", DateTimeOffset.UtcNow));
    }
}
