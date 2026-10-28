using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;

namespace Smena.Application.Auth;

public sealed record LoginCommand(string Email, string Password);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler(
    IAppDbContext db,
    IIdentityService identity,
    IAuthTokenService tokens,
    IValidator<LoginCommand> validator)
{
    public async Task<Result<AuthTokens>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var userId = await identity.ValidateCredentialsAsync(
            EmailAddress.Normalize(command.Email), command.Password, cancellationToken);
        if (userId is null)
            return Error.Unauthorized("auth.invalid_credentials", "Pogrešan email ili lozinka.");

        // Pre prijave nema restorana u claim-u, pa se tenant filter namerno zaobilazi.
        // Za sada jedan korisnik pripada jednom restoranu; više lokala je faza proizvoda.
        var employee = await db.Employees
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Status == EmployeeStatus.Active, cancellationToken);
        if (employee is null)
            return Error.Forbidden("auth.inactive", "Nalog nije aktivan ni u jednom restoranu.");

        return await tokens.IssueAsync(employee, cancellationToken);
    }
}
