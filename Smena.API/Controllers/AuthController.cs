using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Smena.Application.Abstractions;
using Smena.Application.Auth;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    /// <summary>Registracija novog restorana; prijavljeni korisnik postaje vlasnik.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [HttpPost("register")]
    public async Task<ActionResult<AccessTokenResponse>> Register(
        RegisterRestaurantCommand command, RegisterRestaurantHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? SignedIn(result.Value) : this.ToProblem(result.Error!);
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [HttpPost("login")]
    public async Task<ActionResult<AccessTokenResponse>> Login(
        LoginCommand command, LoginHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? SignedIn(result.Value) : this.ToProblem(result.Error!);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AccessTokenResponse>> Refresh(
        IAuthTokenService tokens, CancellationToken cancellationToken)
    {
        if (RefreshTokenCookie.Read(Request) is not { } refreshToken)
            return Unauthorized();

        var result = await tokens.RefreshAsync(refreshToken, cancellationToken);
        if (result.IsSuccess)
            return SignedIn(result.Value);

        RefreshTokenCookie.Delete(Response);
        return this.ToProblem(result.Error!);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(IAuthTokenService tokens, CancellationToken cancellationToken)
    {
        if (RefreshTokenCookie.Read(Request) is { } refreshToken)
            await tokens.RevokeAsync(refreshToken, cancellationToken);

        RefreshTokenCookie.Delete(Response);
        return NoContent();
    }

    /// <summary>Šalje link za reset lozinke. Uvek vraća 204, da se ne otkrije koji nalozi postoje.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.ForgotPassword)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordCommand command, ForgotPasswordHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(command, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordCommand command, ResetPasswordHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentEmployeeDto>> Me(
        GetCurrentEmployeeHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    private AccessTokenResponse SignedIn(AuthTokens tokens)
    {
        RefreshTokenCookie.Set(Response, tokens);
        return new AccessTokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt);
    }
}
