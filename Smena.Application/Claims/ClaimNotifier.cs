using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Swaps;
using Smena.Domain.Shifts;

namespace Smena.Application.Claims;

/// <summary>Obaveštava podnosioce o odluci za otvorenu smenu. Greške slanja idu samo u log.</summary>
public sealed class ClaimNotifier(
    IAppDbContext db,
    IIdentityService identity,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<ClaimNotifier> logger)
{
    public async Task DecidedAsync(IReadOnlyCollection<ShiftClaim> claims, CancellationToken cancellationToken)
    {
        if (claims.Count == 0)
            return;

        try
        {
            var shiftId = claims.First().ShiftId;
            var details = await (
                from sh in db.Shifts
                join p in db.Positions on sh.PositionId equals p.Id
                join r in db.Restaurants on sh.RestaurantId equals r.Id
                where sh.Id == shiftId
                select new { sh.StartsAt, sh.EndsAt, Position = p.Name, r.TimeZoneId })
                .FirstAsync(cancellationToken);
            var when = $"{LocalTime.Format(details.StartsAt, details.TimeZoneId)} - {LocalTime.Format(details.EndsAt, details.TimeZoneId, timeOnly: true)}";

            var employeeIds = claims.Select(c => c.EmployeeId).ToList();
            var userIds = await db.Employees
                .Where(e => employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.UserId, cancellationToken);
            var emails = await identity.GetEmailsAsync(userIds.Values.ToList(), cancellationToken);
            var link = appOptions.Value.ScheduleLink;

            foreach (var claim in claims)
            {
                if (!userIds.TryGetValue(claim.EmployeeId, out var userId) || !emails.TryGetValue(userId, out var to))
                    continue;

                try
                {
                    await emailSender.SendAsync(Build(to, claim.Status == ClaimStatus.Approved, details.Position, when, link), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Obaveštenje za zahtev {ClaimId} nije poslato.", claim.Id);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Slanje obaveštenja o otvorenoj smeni nije uspelo.");
        }
    }

    private static EmailMessage Build(string to, bool approved, string position, string when, string link)
    {
        var outcome = approved
            ? $"Dobili ste otvorenu smenu ({position}): {when}."
            : $"Otvorena smena ({position}) {when} nije dodeljena vama.";
        var text = $"""
            Zdravo,

            {outcome}

            Raspored: {link}
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p>{WebUtility.HtmlEncode(outcome)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Raspored</a></p>
            """;
        return new EmailMessage(to, approved ? "Dobili ste otvorenu smenu" : "Otvorena smena nije dodeljena", text, html);
    }
}
