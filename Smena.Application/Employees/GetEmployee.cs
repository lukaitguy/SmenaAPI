using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Employees;

public sealed class GetEmployeeHandler(IAppDbContext db)
{
    public async Task<Result<EmployeeDto>> HandleAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .Include(e => e.Positions)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        return employee is null
            ? Error.NotFound("employees.not_found", "Zaposleni nije pronađen.")
            : employee.ToDto();
    }
}
