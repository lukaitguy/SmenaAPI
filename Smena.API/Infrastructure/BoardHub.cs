using Microsoft.AspNetCore.SignalR;
using Smena.Application.Abstractions;
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

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst(SmenaClaims.RestaurantId)?.Value, out var restaurantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(restaurantId));
        else
            Context.Abort();

        await base.OnConnectedAsync();
    }
}

/// <summary>Imena događaja koje klijent sluša.</summary>
public static class BoardEvents
{
    public const string EightySixAdded = "eightySixAdded";
    public const string EightySixRemoved = "eightySixRemoved";
}

internal sealed class SignalRBoardNotifier(IHubContext<BoardHub> hub, ILogger<SignalRBoardNotifier> logger) : IBoardNotifier
{
    public Task EightySixAddedAsync(Guid restaurantId, EightySixItemDto item, CancellationToken cancellationToken) =>
        SendAsync(restaurantId, BoardEvents.EightySixAdded, item, cancellationToken);

    public Task EightySixRemovedAsync(Guid restaurantId, Guid itemId, CancellationToken cancellationToken) =>
        SendAsync(restaurantId, BoardEvents.EightySixRemoved, itemId, cancellationToken);

    private async Task SendAsync(Guid restaurantId, string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(BoardHub.Group(restaurantId)).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Događaj {Event} nije poslat ekranima restorana {RestaurantId}.", eventName, restaurantId);
        }
    }
}
