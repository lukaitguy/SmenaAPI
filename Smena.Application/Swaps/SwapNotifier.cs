using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

/// <summary>
/// Šalje mejlove o zahtevima za prepuštanje smene. Zahtev je već sačuvan kad se ovo zove,
/// pa neuspelo slanje samo ide u log i nikad ne ruši zahtev.
/// </summary>
public sealed class SwapNotifier(
    IAppDbContext db,
    IIdentityService identity,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<SwapNotifier> logger)
{
    public Task RequestedAsync(ShiftSwap swap, CancellationToken cancellationToken) =>
        SendAsync(swap, decided: false, cancellationToken);

    public Task DecidedAsync(ShiftSwap swap, CancellationToken cancellationToken) =>
        SendAsync(swap, decided: true, cancellationToken);

    private async Task SendAsync(ShiftSwap swap, bool decided, CancellationToken cancellationToken)
    {
        try
        {
            var details = await (
                from sh in db.Shifts
                join p in db.Positions on sh.PositionId equals p.Id
                join r in db.Restaurants on sh.RestaurantId equals r.Id
                where sh.Id == swap.ShiftId
                select new { sh.StartsAt, sh.EndsAt, Position = p.Name, r.TimeZoneId })
                .FirstAsync(cancellationToken);

            var people = await db.Employees
                .Where(e => e.Id == swap.RequesterEmployeeId || e.Id == swap.TargetEmployeeId)
                .Select(e => new { e.Id, e.UserId, Name = e.FirstName + " " + e.LastName })
                .ToListAsync(cancellationToken);
            var requester = people.First(e => e.Id == swap.RequesterEmployeeId);
            var target = people.First(e => e.Id == swap.TargetEmployeeId);

            var emails = await identity.GetEmailsAsync([requester.UserId, target.UserId], cancellationToken);
            var when = $"{LocalTime.Format(details.StartsAt, details.TimeZoneId)} - {LocalTime.Format(details.EndsAt, details.TimeZoneId, timeOnly: true)}";
            var link = appOptions.Value.SwapsLink;

            // Kod razmene podnosilac dobija kolegin termin, pa mejlovi opisuju obe smene.
            Exchange? exchange = null;
            if (swap.TargetShiftId is { } receivedId)
            {
                var other = await (
                    from sh in db.Shifts
                    join p in db.Positions on sh.PositionId equals p.Id
                    where sh.Id == receivedId
                    select new { sh.StartsAt, sh.EndsAt, Position = p.Name })
                    .FirstAsync(cancellationToken);
                exchange = new Exchange(other.Position,
                    $"{LocalTime.Format(other.StartsAt, details.TimeZoneId)} - {LocalTime.Format(other.EndsAt, details.TimeZoneId, timeOnly: true)}");
            }

            if (!decided)
            {
                if (emails.TryGetValue(target.UserId, out var to))
                    await emailSender.SendAsync(SwapEmail.Requested(to, requester.Name, details.Position, when, swap.Note, link, exchange), cancellationToken);
                return;
            }

            var approved = swap.Status == SwapStatus.Approved;
            foreach (var person in new[] { requester, target })
            {
                if (emails.TryGetValue(person.UserId, out var to))
                    await emailSender.SendAsync(SwapEmail.Decided(to, approved, requester.Name, target.Name, details.Position, when, link, exchange), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Slanje obaveštenja za zahtev {SwapId} nije uspelo.", swap.Id);
        }
    }
}

internal static class LocalTime
{
    /// <summary>Vreme u zoni restorana; ako zona nije dostupna na serveru, prikazuje se UTC.</summary>
    public static string Format(DateTimeOffset value, string timeZoneId, bool timeOnly = false)
    {
        var suffix = "";
        try
        {
            value = TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            value = value.ToUniversalTime();
            suffix = " UTC";
        }

        return (timeOnly ? value.ToString("HH:mm") : value.ToString("dd.MM.yyyy. HH:mm")) + suffix;
    }
}

/// <summary>Smena koju podnosilac dobija zauzvrat.</summary>
internal sealed record Exchange(string Position, string When);

internal static class SwapEmail
{
    public static EmailMessage Requested(
        string to, string requester, string position, string when, string? note, string link, Exchange? exchange = null)
    {
        if (exchange is not null)
            return RequestedExchange(to, requester, position, when, note, link, exchange);

        var noteText = note is null ? "" : $"\nPoruka: {note}\n";
        var text = $"""
            Zdravo,

            {requester} vam nudi da preuzmete smenu ({position}): {when}
            {noteText}
            Odgovorite na zahtev: {link}

            Smena prelazi na vas tek kad vi prihvatite, a menadžer odobri.
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p><strong>{WebUtility.HtmlEncode(requester)}</strong> vam nudi da preuzmete smenu ({WebUtility.HtmlEncode(position)}): <strong>{WebUtility.HtmlEncode(when)}</strong></p>
            {(note is null ? "" : $"<p>Poruka: {WebUtility.HtmlEncode(note)}</p>")}
            <p><a href="{WebUtility.HtmlEncode(link)}">Odgovorite na zahtev</a></p>
            <p>Smena prelazi na vas tek kad vi prihvatite, a menadžer odobri.</p>
            """;
        return new EmailMessage(to, "Kolega vam nudi smenu", text, html);
    }

    public static EmailMessage Decided(
        string to, bool approved, string requester, string target, string position, string when, string link, Exchange? exchange = null)
    {
        if (exchange is not null)
            return DecidedExchange(to, approved, requester, target, position, when, link, exchange);

        var outcome = approved
            ? $"Menadžer je odobrio: smenu ({position}) {when} sada ima {target}."
            : $"Menadžer je odbio zahtev: smena ({position}) {when} ostaje kod {requester}.";
        var text = $"""
            Zdravo,

            {outcome}

            Pregled: {link}
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p>{WebUtility.HtmlEncode(outcome)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Pregled</a></p>
            """;
        return new EmailMessage(to, approved ? "Zamena smene odobrena" : "Zamena smene odbijena", text, html);
    }

    private static EmailMessage RequestedExchange(
        string to, string requester, string position, string when, string? note, string link, Exchange exchange)
    {
        var noteText = note is null ? "" : $"\nPoruka: {note}\n";
        var text = $"""
            Zdravo,

            {requester} vam predlaže zamenu smena: njegova smena ({position}) {when} za vašu ({exchange.Position}) {exchange.When}.
            {noteText}
            Odgovorite na zahtev: {link}

            Smene menjaju vlasnika tek kad vi prihvatite, a menadžer odobri.
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p><strong>{WebUtility.HtmlEncode(requester)}</strong> vam predlaže zamenu smena: njegova smena ({WebUtility.HtmlEncode(position)}) <strong>{WebUtility.HtmlEncode(when)}</strong> za vašu ({WebUtility.HtmlEncode(exchange.Position)}) <strong>{WebUtility.HtmlEncode(exchange.When)}</strong>.</p>
            {(note is null ? "" : $"<p>Poruka: {WebUtility.HtmlEncode(note)}</p>")}
            <p><a href="{WebUtility.HtmlEncode(link)}">Odgovorite na zahtev</a></p>
            <p>Smene menjaju vlasnika tek kad vi prihvatite, a menadžer odobri.</p>
            """;
        return new EmailMessage(to, "Kolega vam predlaže zamenu smena", text, html);
    }

    private static EmailMessage DecidedExchange(
        string to, bool approved, string requester, string target, string position, string when, string link, Exchange exchange)
    {
        var outcome = approved
            ? $"Menadžer je odobrio zamenu: {requester} sada ima smenu ({exchange.Position}) {exchange.When}, a {target} smenu ({position}) {when}."
            : $"Menadžer je odbio zamenu: smene ({position}) {when} i ({exchange.Position}) {exchange.When} ostaju kod {requester} i {target}.";
        var text = $"""
            Zdravo,

            {outcome}

            Pregled: {link}
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p>{WebUtility.HtmlEncode(outcome)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Pregled</a></p>
            """;
        return new EmailMessage(to, approved ? "Zamena smena odobrena" : "Zamena smena odbijena", text, html);
    }
}
