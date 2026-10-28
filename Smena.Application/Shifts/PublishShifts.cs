using System.Net;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Swaps;

namespace Smena.Application.Shifts;

public sealed record PublishShiftsCommand(DateTimeOffset From, DateTimeOffset To);

public sealed record PublishedShiftsDto(int Published, int EmployeesNotified);

public sealed class PublishShiftsValidator : AbstractValidator<PublishShiftsCommand>
{
    public PublishShiftsValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From).WithMessage("Kraj perioda mora biti posle početka.");
        RuleFor(x => x).Must(x => x.To - x.From <= TimeSpan.FromDays(ShiftQueries.MaxRangeDays))
            .WithName(nameof(PublishShiftsCommand.To))
            .WithMessage($"Period ne može biti duži od {ShiftQueries.MaxRangeDays} dana.");
    }
}

public sealed class PublishShiftsHandler(
    IAppDbContext db,
    IValidator<PublishShiftsCommand> validator,
    SchedulePublishedNotifier notifier,
    TimeProvider time)
{
    /// <summary>
    /// Objavljuje sve nacrte koji počinju u periodu [From, To). Već objavljene smene se ne diraju,
    /// pa ponovno objavljivanje istog perioda ne šalje nove mejlove.
    /// </summary>
    public async Task<PublishedShiftsDto> HandleAsync(PublishShiftsCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var from = command.From.ToUniversalTime();
        var to = command.To.ToUniversalTime();
        var drafts = await db.Shifts
            .Where(s => s.PublishedAt == null && s.StartsAt >= from && s.StartsAt < to)
            .ToListAsync(cancellationToken);
        if (drafts.Count == 0)
            return new PublishedShiftsDto(0, 0);

        var now = time.GetUtcNow();
        foreach (var shift in drafts)
            shift.Publish(now);
        await db.SaveChangesAsync(cancellationToken);

        var notified = await notifier.NotifyAsync(drafts, cancellationToken);
        return new PublishedShiftsDto(drafts.Count, notified);
    }
}

/// <summary>Jedan mejl po zaposlenom o njegovim novoobjavljenim smenama. Greške slanja idu samo u log.</summary>
public sealed class SchedulePublishedNotifier(
    IAppDbContext db,
    IIdentityService identity,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<SchedulePublishedNotifier> logger)
{
    public async Task<int> NotifyAsync(IReadOnlyCollection<Domain.Shifts.Shift> published, CancellationToken cancellationToken)
    {
        var sent = 0;
        try
        {
            var timeZoneId = await db.Restaurants.Select(r => r.TimeZoneId).FirstAsync(cancellationToken);
            var byEmployee = published.GroupBy(s => s.EmployeeId).ToList();
            var employeeIds = byEmployee.Select(g => g.Key).ToList();
            var userIds = await db.Employees
                .Where(e => employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.UserId, cancellationToken);
            var emails = await identity.GetEmailsAsync(userIds.Values.ToList(), cancellationToken);
            var link = appOptions.Value.ScheduleLink;

            foreach (var group in byEmployee)
            {
                if (!userIds.TryGetValue(group.Key, out var userId) || !emails.TryGetValue(userId, out var to))
                    continue;

                try
                {
                    var first = group.Min(s => s.StartsAt);
                    var last = group.Max(s => s.StartsAt);
                    var range = $"{LocalTime.Format(first, timeZoneId)} - {LocalTime.Format(last, timeZoneId)}";
                    await emailSender.SendAsync(Build(to, group.Count(), range, link), cancellationToken);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Obaveštenje o rasporedu zaposlenom {EmployeeId} nije poslato.", group.Key);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Slanje obaveštenja o objavljenom rasporedu nije uspelo.");
        }

        return sent;
    }

    private static EmailMessage Build(string to, int count, string range, string link)
    {
        var shifts = count == 1 ? "1 smena" : $"{count} smena";
        var text = $"""
            Zdravo,

            Objavljen je novi raspored. Imate {shifts}, od {range}.

            Pogledajte raspored: {link}
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p>Objavljen je novi raspored. Imate <strong>{WebUtility.HtmlEncode(shifts)}</strong>, od {WebUtility.HtmlEncode(range)}.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Pogledajte raspored</a></p>
            """;
        return new EmailMessage(to, "Objavljen je novi raspored", text, html);
    }
}
