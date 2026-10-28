using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;

namespace Smena.Application.Employees;

public sealed record UpdateEmployeeCommand(
    string FirstName,
    string LastName,
    EmployeeRole Role,
    IReadOnlyList<Guid> PositionIds);

public sealed class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.PositionIds).NotNull();
    }
}

public sealed class UpdateEmployeeHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<UpdateEmployeeCommand> validator)
{
    public async Task<Result<EmployeeDto>> HandleAsync(Guid employeeId, UpdateEmployeeCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var employee = await db.Employees
            .Include(e => e.Positions)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Error.NotFound("employees.not_found", "Zaposleni nije pronađen.");

        // Uloga vlasnika je zaključana (niko je ne dobija ni gubi), a ni ostali ne menjaju sopstvenu ulogu.
        if (command.Role != employee.Role)
        {
            if (employee.Role == EmployeeRole.Owner || command.Role == EmployeeRole.Owner)
                return Error.Forbidden("employees.owner_role_locked", "Uloga vlasnika se ne može menjati ni dodeliti.");
            if (employee.Id == currentUser.EmployeeId)
                return Error.Forbidden("employees.self_role_change", "Ne možete menjati sopstvenu ulogu.");
        }

        var positionIds = command.PositionIds.Distinct().ToList();
        var positions = await db.Positions.Where(p => positionIds.Contains(p.Id)).ToListAsync(cancellationToken);
        if (positions.Count != positionIds.Count)
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");

        employee.Rename(command.FirstName.Trim(), command.LastName.Trim());
        employee.ChangeRole(command.Role);
        employee.ReplacePositions(positions);
        await db.SaveChangesAsync(cancellationToken);

        return employee.ToDto();
    }
}
