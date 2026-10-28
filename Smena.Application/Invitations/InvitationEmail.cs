using System.Net;
using Smena.Application.Abstractions;

namespace Smena.Application.Invitations;

internal static class InvitationEmail
{
    public static EmailMessage Build(string to, string restaurantName, string invitedBy, string acceptUrl, DateTimeOffset expiresAt)
    {
        var subject = $"Poziv za {restaurantName} na Smeni";
        var expires = expiresAt.UtcDateTime.ToString("dd.MM.yyyy.");

        var text = $"""
            Zdravo,

            {invitedBy} vas poziva da se pridružite restoranu {restaurantName} na Smeni.

            Prihvatite poziv: {acceptUrl}

            Link važi do {expires}
            Ako niste očekivali ovaj poziv, slobodno ignorišite ovu poruku.
            """;

        var html = $"""
            <p>Zdravo,</p>
            <p><strong>{WebUtility.HtmlEncode(invitedBy)}</strong> vas poziva da se pridružite restoranu
            <strong>{WebUtility.HtmlEncode(restaurantName)}</strong> na Smeni.</p>
            <p><a href="{WebUtility.HtmlEncode(acceptUrl)}">Prihvatite poziv</a></p>
            <p>Link važi do {expires}<br>Ako niste očekivali ovaj poziv, slobodno ignorišite ovu poruku.</p>
            """;

        return new EmailMessage(to, subject, text, html);
    }
}
