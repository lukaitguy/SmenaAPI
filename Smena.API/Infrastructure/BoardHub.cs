using Microsoft.AspNetCore.SignalR;
using Smena.Application.Abstractions;
using Smena.Application.Announcements;
using Smena.Application.EightySix;
using Smena.Infrastructure.Identity;

namespace SmenaAPI.Infrastructure;

/// <summary>
/// Zajednički ekran smene. Komande idu običnim REST pozivima, a hub samo gura promene: svako ko se poveže
/// ulazi u grupu svog restorana (iz JWT claim-a), pa nikad ne dobija događaje drugog restorana.
/// </summary>
public sealed class BoardHub : Hub
{
    public const string Path = "/hubs/board";

    public static string Group(Guid restaurantId) => $"restaurant:{restaurantId}";

    /// <summary>Samo menadžeri i vlasnik restorana (npr. ko je pročitao najavu).</summary>
    public static string ManagersGroup(Guid restaurantId) => $"restaurant:{restaurantId}:managers";

    /// <summary>Jedna osoba (na svim njenim uređajima), za poruke namenjene samo njoj.</summary>
    public static string EmployeeGroup(Guid employeeId) => $"employee:{employeeId}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst(SmenaClaims.RestaurantId)?.Value, out var restaurantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(restaurantId));
            if (Policies.ManagerRoles.Contains(Context.User?.FindFirst(SmenaClaims.Role)?.Value))
                await Groups.AddToGroupAsync(Context.ConnectionId, ManagersGroup(restaurantId));
            if (Guid.TryParse(Context.User?.FindFirst(SmenaClaims.EmployeeId)?.Value, out var employeeId))
                await Groups.AddToGroupAsync(Context.ConnectionId, EmployeeGroup(employeeId));
        }
        else
        {
            Context.Abort();
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Imena događaja koje klijent sluša.</summary>
public static class BoardEvents
{
    public const string EightySixAdded = "eightySixAdded";
    public const string EightySixRemoved = "eightySixRemoved";

    /// <summary>Primaocu: cela nova najava.</summary>
    public const string AnnouncementCreated = "announcementCreated";

    /// <summary>Primaocu: id obrisane najave.</summary>
    public const string AnnouncementDeleted = "announcementDeleted";

    /// <summary>Menadžerima: spisak najava se promenio (nova ili obrisana), povuci ga ponovo.</summary>
    public const string AnnouncementsChanged = "announcementsChanged";

    /// <summary>Menadžerima: neko je potvrdio čitanje ({ announcementId, employeeId, readAt }).</summary>
    public const string AnnouncementRead = "announcementRead";
}

internal sealed class SignalRBoardNotifier(IHubContext<BoardHub> hub, ILogger<SignalRBoardNotifier> logger) : IBoardNotifier
{
    public Task EightySixAddedAsync(Guid restaurantId, EightySixItemDto item, CancellationToken cancellationToken) =>
        SendAsync(restaurantId, BoardEvents.EightySixAdded, item, cancellationToken);

    public Task EightySixRemovedAsync(Guid restaurantId, Guid itemId, CancellationToken cancellationToken) =>
        SendAsync(restaurantId, BoardEvents.EightySixRemoved, itemId, cancellationToken);

    public async Task AnnouncementCreatedAsync(
        Guid restaurantId, IReadOnlyCollection<Guid> recipientEmployeeIds, AnnouncementDto item, CancellationToken cancellationToken)
    {
        await SendToAsync(recipientEmployeeIds.Select(BoardHub.EmployeeGroup).ToList(), BoardEvents.AnnouncementCreated, item, cancellationToken);
        await SendToAsync([BoardHub.ManagersGroup(restaurantId)], BoardEvents.AnnouncementsChanged, null, cancellationToken);
    }

    public async Task AnnouncementDeletedAsync(
        Guid restaurantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid announcementId, CancellationToken cancellationToken)
    {
        await SendToAsync(recipientEmployeeIds.Select(BoardHub.EmployeeGroup).ToList(), BoardEvents.AnnouncementDeleted, announcementId, cancellationToken);
        await SendToAsync([BoardHub.ManagersGroup(restaurantId)], BoardEvents.AnnouncementsChanged, null, cancellationToken);
    }

    public Task AnnouncementReadAsync(Guid restaurantId, Guid announcementId, Guid employeeId, DateTimeOffset readAt, CancellationToken cancellationToken) =>
        SendToAsync([BoardHub.ManagersGroup(restaurantId)], BoardEvents.AnnouncementRead, new { announcementId, employeeId, readAt }, cancellationToken);

    private Task SendAsync(Guid restaurantId, string eventName, object payload, CancellationToken cancellationToken) =>
        SendToAsync([BoardHub.Group(restaurantId)], eventName, payload, cancellationToken);

    private async Task SendToAsync(IReadOnlyList<string> groups, string eventName, object? payload, CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
            return;
        try
        {
            // Signal bez podataka se šalje bez argumenata, da ga klijent sa praznim handlerom primi.
            if (payload is null)
                await hub.Clients.Groups(groups).SendAsync(eventName, cancellationToken);
            else
                await hub.Clients.Groups(groups).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Događaj {Event} nije poslat.", eventName);
        }
    }
}
