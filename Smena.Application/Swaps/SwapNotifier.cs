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

            if (!decided)
            {
                if (emails.TryGetValue(target.UserId, out var to))
                    await emailSender.SendAsync(SwapEmail.Requested(to, requester.Name, details.Position, when, swap.Note, link), cancellationToken);
                return;
            }

            var approved = swap.Status == SwapStatus.Approved;
            foreach (var person in new[] { requester, target })
            {
                if (emails.TryGetValue(person.UserId, out var to))
                    await emailSender.SendAsync(SwapEmail.Decided(to, approved, requester.Name, target.Name, details.Position, when, link), cancellationToken);
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

internal static class SwapEmail
{
    public static EmailMessage Requested(string to, string requester, string position, string when, string? note, string link)
    {
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

    public static EmailMessage Decided(string to, bool approved, string requester, string target, string position, string when, string link)
    {
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
}
