using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Swaps;
using Smena.Domain.DayOffs;
using Smena.Domain.Employees;
using Smena.Domain.Shifts;

namespace Smena.Application.Notifications;

/// <summary>
/// Obaveštava menadžere i vlasnika kad im neki zahtev čeka odluku. Osoba koja je sama podnela
/// zahtev se preskače. Zahtev je već sačuvan kad se ovo zove, pa greške slanja idu samo u log.
/// </summary>
public sealed class ManagerNotifier(
    IAppDbContext db,
    IIdentityService identity,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<ManagerNotifier> logger)
{
    public Task DayOffRequestedAsync(DayOffRequest request, CancellationToken cancellationToken) =>
        SafeAsync("zahtev za slobodan dan", async () =>
        {
            var name = await NameAsync(request.EmployeeId, cancellationToken);
            var reason = request.Reason is null ? "" : $" Razlog: {request.Reason}";
            await SendToManagersAsync(
                request.EmployeeId,
                "Novi zahtev za slobodan dan",
                $"{name} traži slobodan dan {request.Date:dd.MM.yyyy.}.{reason}",
                cancellationToken);
        });

    public Task ClaimRequestedAsync(ShiftClaim claim, CancellationToken cancellationToken) =>
        SafeAsync("zahtev za otvorenu smenu", async () =>
        {
            var name = await NameAsync(claim.EmployeeId, cancellationToken);
            var (position, when) = await ShiftInfoAsync(claim.ShiftId, cancellationToken);
            await SendToManagersAsync(
                claim.EmployeeId,
                "Novi zahtev za otvorenu smenu",
                $"{name} traži otvorenu smenu ({position}): {when}.",
                cancellationToken);
        });

    /// <summary>Zamena je spremna za odluku tek kad je kolega prihvatio.</summary>
    public Task SwapAwaitingDecisionAsync(ShiftSwap swap, CancellationToken cancellationToken) =>
        SafeAsync("zamena smene", async () =>
        {
            var requester = await NameAsync(swap.RequesterEmployeeId, cancellationToken);
            var target = await NameAsync(swap.TargetEmployeeId, cancellationToken);
            var (position, when) = await ShiftInfoAsync(swap.ShiftId, cancellationToken);
            await SendToManagersAsync(
                swap.TargetEmployeeId,
                "Zamena smene čeka odluku",
                $"{requester} prepušta smenu ({position}) {when} kolegi {target}, koji je prihvatio. Čeka vašu odluku.",
                cancellationToken);
        });

    private async Task SendToManagersAsync(Guid exceptEmployeeId, string subject, string line, CancellationToken cancellationToken)
    {
        var userIds = await db.Employees
            .Where(e => (e.Role == EmployeeRole.Manager || e.Role == EmployeeRole.Owner)
                        && e.Status == EmployeeStatus.Active && e.Id != exceptEmployeeId)
            .Select(e => e.UserId)
            .ToListAsync(cancellationToken);
        if (userIds.Count == 0)
            return;

        var emails = await identity.GetEmailsAsync(userIds, cancellationToken);
        var link = appOptions.Value.RequestsLink;
        var text = $"""
            Zdravo,

            {line}

            Pregled zahteva: {link}
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p>{WebUtility.HtmlEncode(line)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Pregled zahteva</a></p>
            """;

        foreach (var email in emails.Values)
        {
            try
            {
                await emailSender.SendAsync(new EmailMessage(email, subject, text, html), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Obaveštenje menadžeru ({Subject}) nije poslato.", subject);
            }
        }
    }

    private Task<string> NameAsync(Guid employeeId, CancellationToken cancellationToken) =>
        db.Employees.Where(e => e.Id == employeeId).Select(e => e.FirstName + " " + e.LastName).FirstAsync(cancellationToken);

    private async Task<(string Position, string When)> ShiftInfoAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        var details = await (
            from sh in db.Shifts
            join p in db.Positions on sh.PositionId equals p.Id
            join r in db.Restaurants on sh.RestaurantId equals r.Id
            where sh.Id == shiftId
            select new { sh.StartsAt, sh.EndsAt, Position = p.Name, r.TimeZoneId })
            .FirstAsync(cancellationToken);

        return (details.Position,
            $"{LocalTime.Format(details.StartsAt, details.TimeZoneId)} - {LocalTime.Format(details.EndsAt, details.TimeZoneId, timeOnly: true)}");
    }

    private async Task SafeAsync(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Obaveštenje menadžerima ({What}) nije poslato.", what);
        }
    }
}
