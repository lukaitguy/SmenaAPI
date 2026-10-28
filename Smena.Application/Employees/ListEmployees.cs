using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;

namespace Smena.Application.Employees;

public sealed class ListEmployeesHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<EmployeeDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var employees = await db.Employees
            .Include(e => e.Positions)
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);

        return employees.Select(e => e.ToDto()).ToList();
    }
}
