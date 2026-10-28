using Smena.Domain.Employees;

namespace SmenaAPI.Infrastructure;

internal static class Policies
{
    public const string Manager = "Manager";

    public static readonly string[] ManagerRoles = [nameof(EmployeeRole.Manager), nameof(EmployeeRole.Owner)];
}
