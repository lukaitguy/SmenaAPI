using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.DayOffs;

namespace Smena.Application.DayOffs;

/// <summary>Obaveštava zaposlenog o odluci za slobodan dan. Greška slanja ide samo u log.</summary>
public sealed class DayOffNotifier(
    IAppDbContext db,
    IIdentityService identity,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<DayOffNotifier> logger)
{
    public async Task DecidedAsync(DayOffRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = await db.Employees
                .Where(e => e.Id == request.EmployeeId)
                .Select(e => e.UserId)
                .FirstAsync(cancellationToken);
            var emails = await identity.GetEmailsAsync([userId], cancellationToken);
            if (!emails.TryGetValue(userId, out var to))
                return;

            await emailSender.SendAsync(Build(to, request, appOptions.Value.ScheduleLink), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Obaveštenje o slobodnom danu {RequestId} nije poslato.", request.Id);
        }
    }

    private static EmailMessage Build(string to, DayOffRequest request, string link)
    {
        var date = request.Date.ToString("dd.MM.yyyy.");
        var approved = request.Status == DayOffStatus.Approved;
        var outcome = approved
            ? $"Vaš slobodan dan {date} je odobren."
            : $"Vaš zahtev za slobodan dan {date} nije odobren.";
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
        return new EmailMessage(to, approved ? "Slobodan dan odobren" : "Slobodan dan nije odobren", text, html);
    }
}
