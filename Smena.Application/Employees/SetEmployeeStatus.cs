using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;

namespace Smena.Application.Employees;

public sealed record SetEmployeeStatusCommand(EmployeeStatus Status);

public sealed class SetEmployeeStatusHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<EmployeeDto>> HandleAsync(Guid employeeId, SetEmployeeStatusCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Status))
            return Error.Validation("employees.invalid_status", "Nepoznat status.");

        var employee = await db.Employees
            .Include(e => e.Positions)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Error.NotFound("employees.not_found", "Zaposleni nije pronađen.");

        if (command.Status == EmployeeStatus.Inactive)
        {
            if (employee.Id == currentUser.EmployeeId)
                return Error.Forbidden("employees.self_deactivate", "Ne možete deaktivirati sopstveni nalog.");
            if (employee.Role == EmployeeRole.Owner)
                return Error.Forbidden("employees.owner_deactivate", "Vlasnik se ne može deaktivirati.");
            employee.Deactivate();
        }
        else
        {
            employee.Activate();
        }

        await db.SaveChangesAsync(cancellationToken);
        return employee.ToDto();
    }
}
