using System.Net;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Swaps;
using Smena.Domain.Employees;
using Smena.Domain.Shifts;

namespace Smena.Application.Shifts;

/// <param name="PositionIds">Pozicije čiji se raspored objavljuje (npr. samo konobari); prazno znači sve pozicije.</param>
public sealed record PublishShiftsCommand(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<Guid>? PositionIds = null);

public sealed record PublishedShiftsDto(int Published, int EmployeesNotified);

public sealed class PublishShiftsValidator : AbstractValidator<PublishShiftsCommand>
{
    public PublishShiftsValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From).WithMessage("Kraj perioda mora biti posle početka.");
        RuleFor(x => x).Must(x => x.To - x.From <= TimeSpan.FromDays(ShiftQueries.MaxRangeDays))
            .WithName(nameof(PublishShiftsCommand.To))
            .WithMessage($"Period ne može biti duži od {ShiftQueries.MaxRangeDays} dana.");
        RuleForEach(x => x.PositionIds).NotEqual(Guid.Empty);
    }
}

public sealed class PublishShiftsHandler(
    IAppDbContext db,
    IValidator<PublishShiftsCommand> validator,
    SchedulePublishedNotifier notifier,
    TimeProvider time)
{
    /// <summary>
    /// Objavljuje nacrte koji počinju u periodu [From, To), za zadate pozicije ili za sve. Već objavljene
    /// smene se ne diraju, pa ponovno objavljivanje istog perioda ne šalje nove mejlove.
    /// </summary>
    public async Task<Result<PublishedShiftsDto>> HandleAsync(PublishShiftsCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var positionIds = command.PositionIds?.Distinct().ToList() ?? [];
        if (positionIds.Count > 0 && await db.Positions.CountAsync(p => positionIds.Contains(p.Id), cancellationToken) != positionIds.Count)
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");

        var from = command.From.ToUniversalTime();
        var to = command.To.ToUniversalTime();
        var drafts = await db.Shifts
            .Where(s => s.PublishedAt == null && s.StartsAt >= from && s.StartsAt < to
                        && (positionIds.Count == 0 || positionIds.Contains(s.PositionId)))
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

/// <summary>
/// Jedan mejl svakom aktivnom zaposlenom koji ima neku od objavljenih pozicija: raspored za njegovu ulogu
/// je objavljen, pa neka ga pogleda. Osoba koja objavljuje se preskače. Greške slanja idu samo u log.
/// </summary>
public sealed class SchedulePublishedNotifier(
    IAppDbContext db,
    ICurrentUser currentUser,
    IIdentityService identity,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<SchedulePublishedNotifier> logger)
{
    public async Task<int> NotifyAsync(IReadOnlyCollection<Shift> published, CancellationToken cancellationToken)
    {
        var sent = 0;
        try
        {
            var timeZoneId = await db.Restaurants.Select(r => r.TimeZoneId).FirstAsync(cancellationToken);
            var byPosition = published.GroupBy(s => s.PositionId).ToDictionary(g => g.Key, g => g.ToList());
            var positionIds = byPosition.Keys.ToList();
            var positionNames = await db.Positions
                .Where(p => positionIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

            var recipients = await db.Employees
                .Where(e => e.Status == EmployeeStatus.Active && e.Id != currentUser.EmployeeId
                            && e.Positions.Any(p => positionIds.Contains(p.Id)))
                .Select(e => new { e.Id, e.UserId, Positions = e.Positions.Select(p => p.Id).ToList() })
                .ToListAsync(cancellationToken);
            var emails = await identity.GetEmailsAsync(recipients.Select(r => r.UserId).ToList(), cancellationToken);
            var link = appOptions.Value.ScheduleLink;

            foreach (var recipient in recipients)
            {
                if (!emails.TryGetValue(recipient.UserId, out var to))
                    continue;

                // Samo uloge ovog zaposlenog: konobar ne dobija obaveštenje o rasporedu kuvara.
                var relevant = recipient.Positions.Where(byPosition.ContainsKey).ToList();
                var shifts = relevant.SelectMany(p => byPosition[p]).ToList();
                var names = string.Join(", ", relevant.Select(p => positionNames[p]).Order());
                var range = $"{LocalTime.Format(shifts.Min(s => s.StartsAt), timeZoneId)} - {LocalTime.Format(shifts.Max(s => s.StartsAt), timeZoneId)}";

                try
                {
                    await emailSender.SendAsync(Build(to, names, range, link), cancellationToken);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Obaveštenje o rasporedu zaposlenom {EmployeeId} nije poslato.", recipient.Id);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Slanje obaveštenja o objavljenom rasporedu nije uspelo.");
        }

        return sent;
    }

    private static EmailMessage Build(string to, string positions, string range, string link)
    {
        var text = $"""
            Zdravo,

            Objavljen je novi raspored za: {positions} ({range}).

            Pogledajte raspored: {link}
            """;
        var html = $"""
            <p>Zdravo,</p>
            <p>Objavljen je novi raspored za: <strong>{WebUtility.HtmlEncode(positions)}</strong> ({WebUtility.HtmlEncode(range)}).</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Pogledajte raspored</a></p>
            """;
        return new EmailMessage(to, "Objavljen je novi raspored", text, html);
    }
}
