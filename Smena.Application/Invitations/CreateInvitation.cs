using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;
using Smena.Domain.Invitations;

namespace Smena.Application.Invitations;

public sealed record CreateInvitationCommand(string Email, EmployeeRole Role, Guid? PositionId);

public sealed class CreateInvitationValidator : AbstractValidator<CreateInvitationCommand>
{
    public CreateInvitationValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).IsInEnum().NotEqual(EmployeeRole.Owner)
            .WithMessage("Može se pozvati samo zaposleni ili menadžer.");
    }
}

public sealed class CreateInvitationHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IIdentityService identity,
    IValidator<CreateInvitationCommand> validator,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<CreateInvitationHandler> logger,
    TimeProvider time)
{
    public async Task<Result<CreatedInvitationDto>> HandleAsync(CreateInvitationCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var email = EmailAddress.Normalize(command.Email);
        if (await identity.EmailExistsAsync(email, cancellationToken))
            return Error.Conflict("invitations.email_taken", "Nalog sa ovim emailom već postoji.");

        if (command.PositionId is { } positionId &&
            !await db.Positions.AnyAsync(p => p.Id == positionId, cancellationToken))
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");

        var now = time.GetUtcNow();

        // Novi poziv istom emailu poništava stare, da u opticaju bude samo jedan važeći link.
        var pending = await db.Invitations
            .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null)
            .ToListAsync(cancellationToken);
        pending.ForEach(i => i.Revoke(now));

        var token = SecureToken.Generate();
        var invitation = new Invitation(
            currentUser.RestaurantId!.Value,
            email,
            command.Role,
            command.PositionId,
            SecureToken.Hash(token),
            currentUser.EmployeeId!.Value,
            now);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        var acceptUrl = appOptions.Value.InvitationLink(token);
        var emailSent = await TrySendEmailAsync(invitation, email, acceptUrl, cancellationToken);

        return new CreatedInvitationDto(invitation.ToDto(), acceptUrl, emailSent);
    }

    /// <summary>
    /// Poziv je već sačuvan, pa neuspešno slanje ne ruši zahtev: menadžer dobija link
    /// i može da ga pošalje sam.
    /// </summary>
    private async Task<bool> TrySendEmailAsync(
        Invitation invitation, string email, string acceptUrl, CancellationToken cancellationToken)
    {
        try
        {
            var restaurantName = await db.Restaurants
                .Select(r => r.Name)
                .FirstAsync(cancellationToken);
            var invitedBy = await db.Employees
                .Where(e => e.Id == invitation.InvitedByEmployeeId)
                .Select(e => e.FirstName + " " + e.LastName)
                .FirstAsync(cancellationToken);

            await emailSender.SendAsync(
                InvitationEmail.Build(email, restaurantName, invitedBy, acceptUrl, invitation.ExpiresAt),
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Slanje pozivnice {InvitationId} nije uspelo.", invitation.Id);
            return false;
        }
    }
}
