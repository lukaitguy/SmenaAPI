using System.Net;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Swaps;
using Smena.Domain.Announcements;
using Smena.Domain.Employees;

namespace Smena.Application.Announcements;

public sealed record AnnouncementRecipientDto(Guid EmployeeId, string Name, DateTimeOffset? ReadAt);

/// <param name="Audience">Kome je namenjena: nazivi uloga, pa imena pojedinačno izabranih osoba.</param>
/// <param name="ForMe">Prijavljeni zaposleni je primalac (menadžer vidi i najave koje nisu za njega).</param>
/// <param name="ReadAt">Kad je prijavljeni primalac potvrdio "Pročitao sam"; null dok nije.</param>
/// <param name="Recipients">Ko je primio i ko je pročitao; samo za menadžera, za ostale null.</param>
public sealed record AnnouncementDto(
    Guid Id,
    string Title,
    string? Body,
    DateTimeOffset? MeetingAt,
    Guid CreatedByEmployeeId,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Audience,
    bool ForMe,
    DateTimeOffset? ReadAt,
    IReadOnlyList<AnnouncementRecipientDto>? Recipients);

internal static class AnnouncementQueries
{
    public const int MaxListed = 100;

    /// <summary>Pravi DTO-e; nazivi uloga i imena se čitaju odjednom za sve najave.</summary>
    public static async Task<List<AnnouncementDto>> ToDtosAsync(
        IAppDbContext db, IReadOnlyCollection<Announcement> announcements, Guid? me, bool includeRecipients, CancellationToken cancellationToken)
    {
        var employeeIds = announcements
            .SelectMany(a => a.TargetEmployeeIds.Concat(a.Recipients.Select(r => r.EmployeeId)).Append(a.CreatedByEmployeeId))
            .Distinct().ToList();
        var names = await db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName })
            .ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var positions = await db.Positions.ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        string NameOf(Guid id) => names.GetValueOrDefault(id, "—");

        return announcements.Select(a =>
        {
            var audience = a.TargetPositionIds.Where(positions.ContainsKey).Select(p => positions[p]).Order()
                .Concat(a.TargetEmployeeIds.Select(NameOf).Order())
                .ToList();
            var mine = a.Recipients.FirstOrDefault(r => r.EmployeeId == me);
            return new AnnouncementDto(
                a.Id, a.Title, a.Body, a.MeetingAt, a.CreatedByEmployeeId, NameOf(a.CreatedByEmployeeId), a.CreatedAt, audience,
                mine is not null, mine?.ReadAt,
                includeRecipients
                    ? a.Recipients.Select(r => new AnnouncementRecipientDto(r.EmployeeId, NameOf(r.EmployeeId), r.ReadAt))
                        .OrderBy(r => r.Name).ToList()
                    : null);
        }).ToList();
    }
}

public sealed record CreateAnnouncementCommand(
    string Title, string? Body, DateTimeOffset? MeetingAt, IReadOnlyList<Guid>? PositionIds, IReadOnlyList<Guid>? EmployeeIds);

public sealed class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Announcement.MaxTitleLength);
        RuleFor(x => x.Body).MaximumLength(Announcement.MaxBodyLength);
        RuleForEach(x => x.PositionIds).NotEqual(Guid.Empty);
        RuleForEach(x => x.EmployeeIds).NotEqual(Guid.Empty);
    }
}

public sealed class CreateAnnouncementHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateAnnouncementCommand> validator,
    IBoardNotifier board,
    IEmailQueue emailQueue,
    IPushQueue pushQueue,
    IIdentityService identity,
    IOptions<AppOptions> appOptions,
    TimeProvider time)
{
    public async Task<Result<AnnouncementDto>> HandleAsync(CreateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var now = time.GetUtcNow();
        var me = currentUser.EmployeeId!.Value;
        var positionIds = command.PositionIds?.Distinct().ToList() ?? [];
        var employeeIds = command.EmployeeIds?.Distinct().ToList() ?? [];

        if (command.MeetingAt is { } meetingAt && meetingAt <= now)
            return Error.Validation("announcements.meeting_in_past", "Termin sastanka mora biti u budućnosti.");
        if (positionIds.Count == 0 && employeeIds.Count == 0)
            return Error.Validation("announcements.no_audience", "Izaberite uloge ili osobe kojima je najava namenjena.");

        if (await db.Positions.CountAsync(p => positionIds.Contains(p.Id), cancellationToken) != positionIds.Count)
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");
        var chosen = await db.Employees.Where(e => employeeIds.Contains(e.Id)).Select(e => e.Status).ToListAsync(cancellationToken);
        if (chosen.Count != employeeIds.Count)
            return Error.NotFound("employees.not_found", "Zaposleni nije pronađen.");

        // Primaoci: svi aktivni sa izabranom ulogom plus izabrane osobe; autor sam sebi ne šalje.
        var recipients = await db.Employees
            .Where(e => e.Status == EmployeeStatus.Active && e.Id != me
                        && (employeeIds.Contains(e.Id) || e.Positions.Any(p => positionIds.Contains(p.Id))))
            .Select(e => new { e.Id, e.UserId })
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0)
            return Error.Validation("announcements.no_recipients", "Nema nikoga ko bi dobio ovu najavu (aktivni zaposleni sa izabranim ulogama).");

        var announcement = new Announcement(
            currentUser.RestaurantId!.Value, me, command.Title, command.Body, command.MeetingAt, positionIds, employeeIds, now);
        announcement.AddRecipients(recipients.Select(r => r.Id));
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(cancellationToken);

        var recipientIds = recipients.Select(r => r.Id).ToList();
        var forEmployees = (await AnnouncementQueries.ToDtosAsync(db, [announcement], me: null, includeRecipients: false, cancellationToken))[0];
        await board.AnnouncementCreatedAsync(announcement.RestaurantId, recipientIds, forEmployees, cancellationToken);
        await QueuePushAsync(announcement, recipientIds, cancellationToken);
        await QueueEmailsAsync(announcement, recipients.Select(r => r.UserId).ToList(), cancellationToken);

        return (await AnnouncementQueries.ToDtosAsync(db, [announcement], me, includeRecipients: true, cancellationToken))[0];
    }

    /// <summary>Push svim primaocima: termin sastanka ako postoji, inače početak opisa.</summary>
    private async Task QueuePushAsync(Announcement announcement, IReadOnlyCollection<Guid> recipientIds, CancellationToken cancellationToken)
    {
        string body;
        if (announcement.MeetingAt is { } at)
        {
            var timeZoneId = await db.Restaurants.Select(r => r.TimeZoneId).FirstAsync(cancellationToken);
            body = $"Termin: {LocalTime.Format(at, timeZoneId)}";
        }
        else
        {
            var text = announcement.Body ?? "";
            body = text.Length <= 100 ? text : text[..100] + "…";
        }
        pushQueue.Enqueue(new PushMessage(recipientIds, announcement.Title, body, "/najave"));
    }

    /// <summary>Mejl svakom primaocu, u pozadini; greška pripreme ne sme da obori već sačuvanu najavu.</summary>
    private async Task QueueEmailsAsync(Announcement announcement, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        try
        {
            var emails = await identity.GetEmailsAsync(userIds, cancellationToken);
            var timeZoneId = await db.Restaurants.Select(r => r.TimeZoneId).FirstAsync(cancellationToken);
            var link = appOptions.Value.AnnouncementsLink;
            var when = announcement.MeetingAt is { } at ? $"Termin: {LocalTime.Format(at, timeZoneId)}" : null;

            var text = string.Join("\n\n", new[] { "Zdravo,", announcement.Title, when, announcement.Body, $"Sve najave: {link}" }
                .Where(s => !string.IsNullOrEmpty(s)));
            var html = string.Concat(
                "<p>Zdravo,</p>",
                $"<h3>{WebUtility.HtmlEncode(announcement.Title)}</h3>",
                when is null ? "" : $"<p><strong>{WebUtility.HtmlEncode(when)}</strong></p>",
                announcement.Body is null ? "" : $"<p>{WebUtility.HtmlEncode(announcement.Body).Replace("\n", "<br>")}</p>",
                $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Sve najave</a></p>");

            foreach (var email in emails.Values)
                emailQueue.Enqueue(new EmailMessage(email, $"Najava: {announcement.Title}", text, html));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Najava je sačuvana i stiže uživo i na stranici; samo mejl izostaje.
        }
    }
}

public sealed class ListAnnouncementsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Sve najave restorana sa spiskom ko je pročitao (menadžer).</summary>
    public async Task<IReadOnlyList<AnnouncementDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var items = await db.Announcements.Include(a => a.Recipients)
            .OrderByDescending(a => a.CreatedAt).Take(AnnouncementQueries.MaxListed)
            .ToListAsync(cancellationToken);
        return await AnnouncementQueries.ToDtosAsync(db, items, currentUser.EmployeeId, includeRecipients: true, cancellationToken);
    }

    /// <summary>Najave koje je prijavljeni zaposleni primio; ne vidi ko su ostali primaoci.</summary>
    public async Task<IReadOnlyList<AnnouncementDto>> HandleMineAsync(CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId;
        var items = await db.Announcements.Include(a => a.Recipients)
            .Where(a => a.Recipients.Any(r => r.EmployeeId == me))
            .OrderByDescending(a => a.CreatedAt).Take(AnnouncementQueries.MaxListed)
            .ToListAsync(cancellationToken);
        return await AnnouncementQueries.ToDtosAsync(db, items, me, includeRecipients: false, cancellationToken);
    }
}

public sealed class MarkAnnouncementReadHandler(IAppDbContext db, ICurrentUser currentUser, IBoardNotifier board, TimeProvider time)
{
    /// <summary>"Pročitao sam": ponovljeno potvrđivanje ništa ne menja.</summary>
    public async Task<Result> HandleAsync(Guid announcementId, CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId!.Value;
        var recipient = await db.AnnouncementRecipients
            .FirstOrDefaultAsync(r => r.AnnouncementId == announcementId && r.EmployeeId == me, cancellationToken);
        if (recipient is null)
            return Error.NotFound("announcements.not_found", "Najava nije pronađena.");

        var now = time.GetUtcNow();
        if (recipient.MarkRead(now))
        {
            await db.SaveChangesAsync(cancellationToken);
            await board.AnnouncementReadAsync(recipient.RestaurantId, announcementId, me, now, cancellationToken);
        }
        return Result.Success();
    }
}

public sealed class DeleteAnnouncementHandler(IAppDbContext db, IBoardNotifier board)
{
    public async Task<Result> HandleAsync(Guid announcementId, CancellationToken cancellationToken)
    {
        var announcement = await db.Announcements.Include(a => a.Recipients)
            .FirstOrDefaultAsync(a => a.Id == announcementId, cancellationToken);
        if (announcement is null)
            return Error.NotFound("announcements.not_found", "Najava nije pronađena.");

        var recipientIds = announcement.Recipients.Select(r => r.EmployeeId).ToList();
        db.Announcements.Remove(announcement);
        await db.SaveChangesAsync(cancellationToken);
        await board.AnnouncementDeletedAsync(announcement.RestaurantId, recipientIds, announcement.Id, cancellationToken);
        return Result.Success();
    }
}
