using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;

namespace Smena.Application.Invitations;

public sealed record AcceptInvitationCommand(string Token, string FirstName, string LastName, string Password);

public sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

/// <summary>
/// Pravi nalog i zaposlenog iz poziva. Invitation ima xmin concurrency token, pa dva
/// istovremena prihvatanja istog linka ne mogu oba da uspeju: drugo dobija 409.
/// </summary>
public sealed class AcceptInvitationHandler(
    IAppDbContext db,
    IIdentityService identity,
    IAuthTokenService tokens,
    IValidator<AcceptInvitationCommand> validator,
    TimeProvider time)
{
    public async Task<Result<AuthTokens>> HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var now = time.GetUtcNow();
        var hash = SecureToken.Hash(command.Token);

        // Korisnik još nema nalog ni restoran u claim-u; token je jedini dokaz pripadnosti.
        var invitation = await db.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invitation is null || !invitation.IsUsable(now))
            return InvitationErrors.Invalid;

        if (await identity.EmailExistsAsync(invitation.Email, cancellationToken))
            return Error.Conflict("auth.email_taken", "Nalog sa ovim emailom već postoji.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = await identity.CreateUserAsync(invitation.Email, command.Password, cancellationToken);
        if (!user.IsSuccess)
            return user.Error!;

        var employee = new Employee(
            invitation.RestaurantId, user.Value, command.FirstName.Trim(), command.LastName.Trim(), invitation.Role);

        if (invitation.PositionId is { } positionId)
        {
            var position = await db.Positions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == positionId && p.RestaurantId == invitation.RestaurantId, cancellationToken);
            if (position is not null)
                employee.AssignPosition(position);
        }

        db.Employees.Add(employee);
        invitation.Accept(now);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await tokens.IssueAsync(employee, cancellationToken);
    }
}
