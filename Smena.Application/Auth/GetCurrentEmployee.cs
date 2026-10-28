using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;

namespace Smena.Application.Auth;

public sealed record PositionDto(Guid Id, string Name);

public sealed record CurrentEmployeeDto(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    EmployeeRole Role,
    Guid RestaurantId,
    string RestaurantName,
    string RestaurantTimeZoneId,
    IReadOnlyList<PositionDto> Positions);

public sealed class GetCurrentEmployeeHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<CurrentEmployeeDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var dto = await (
                from e in db.Employees
                join r in db.Restaurants on e.RestaurantId equals r.Id
                where e.Id == currentUser.EmployeeId
                select new CurrentEmployeeDto(
                    e.Id,
                    e.FirstName,
                    e.LastName,
                    e.Role,
                    r.Id,
                    r.Name,
                    r.TimeZoneId,
                    e.Positions.Select(p => new PositionDto(p.Id, p.Name)).ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return dto is null
            ? Error.NotFound("employees.not_found", "Zaposleni nije pronađen.")
            : dto;
    }
}
