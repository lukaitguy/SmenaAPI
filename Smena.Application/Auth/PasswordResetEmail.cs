using System.Net;
using Smena.Application.Abstractions;

namespace Smena.Application.Auth;

internal static class PasswordResetEmail
{
    public static EmailMessage Build(string to, string resetUrl)
    {
        const string subject = "Reset lozinke na Smeni";

        var text = $"""
            Zdravo,

            Primili smo zahtev za reset lozinke vašeg naloga na Smeni.

            Postavite novu lozinku: {resetUrl}

            Link važi sat vremena.
            Ako niste tražili reset, slobodno ignorišite ovu poruku, lozinka ostaje ista.
            """;

        var html = $"""
            <p>Zdravo,</p>
            <p>Primili smo zahtev za reset lozinke vašeg naloga na Smeni.</p>
            <p><a href="{WebUtility.HtmlEncode(resetUrl)}">Postavite novu lozinku</a></p>
            <p>Link važi sat vremena.<br>Ako niste tražili reset, slobodno ignorišite ovu poruku, lozinka ostaje ista.</p>
            """;

        return new EmailMessage(to, subject, text, html);
    }
}
