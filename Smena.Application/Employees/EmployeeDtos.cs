using Smena.Application.Auth;
using Smena.Domain.Employees;

namespace Smena.Application.Employees;

public sealed record EmployeeDto(
    Guid Id,
    string FirstName,
    string LastName,
    EmployeeRole Role,
    EmployeeStatus Status,
    IReadOnlyList<PositionDto> Positions);

internal static class EmployeeMapping
{
    public static EmployeeDto ToDto(this Employee e) => new(
        e.Id,
        e.FirstName,
        e.LastName,
        e.Role,
        e.Status,
        e.Positions.OrderBy(p => p.Name).Select(p => new PositionDto(p.Id, p.Name)).ToList());
}
