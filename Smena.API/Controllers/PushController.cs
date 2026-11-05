using Microsoft.AspNetCore.Mvc;
using Smena.Application.Push;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>Web push obaveštenja: uređaj se pretplati, a server šalje poruke (nova najava, zamena, objava rasporeda…).</summary>
[ApiController]
[Route("api/push")]
public sealed class PushController : ControllerBase
{
    /// <summary>Da li je push uključen na serveru i javni VAPID ključ potreban browseru za pretplatu.</summary>
    [HttpGet("config")]
    public PushConfigDto Config(GetPushConfigHandler handler) => handler.Handle();

    /// <summary>Registruje uređaj prijavljenog zaposlenog (telo je <c>PushSubscription.toJSON()</c> iz browsera).</summary>
    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe(
        SubscribePushCommand command, SubscribePushHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, Request.Headers.UserAgent.ToString(), cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }

    /// <summary>Šalje probno obaveštenje na uređaje prijavljenog zaposlenog.</summary>
    [HttpPost("test")]
    public IActionResult Test(SendTestPushHandler handler)
    {
        handler.Handle();
        return NoContent();
    }

    /// <summary>Uklanja uređaj (npr. pri odjavi ili kad zaposleni isključi obaveštenja).</summary>
    [HttpDelete("subscriptions")]
    public async Task<IActionResult> Unsubscribe(
        [FromQuery] string endpoint, UnsubscribePushHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(endpoint, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
