using Smena.Domain.Employees;
using Smena.Domain.Restaurants;

namespace Smena.Tests.Domain;

public class EmployeeTests
{
    private static Employee NewEmployee(Guid restaurantId, EmployeeRole role = EmployeeRole.Staff) =>
        new(restaurantId, Guid.NewGuid(), "Pera", "Perić", role);

    [Fact]
    public void New_employee_is_active()
    {
        Assert.Equal(EmployeeStatus.Active, NewEmployee(Guid.NewGuid()).Status);
    }

    [Theory]
    [InlineData(EmployeeRole.Staff, false)]
    [InlineData(EmployeeRole.Manager, true)]
    [InlineData(EmployeeRole.Owner, true)]
    public void Only_manager_and_owner_can_manage(EmployeeRole role, bool expected)
    {
        Assert.Equal(expected, NewEmployee(Guid.NewGuid(), role).CanManage);
    }

    [Fact]
    public void Assigning_the_same_position_twice_keeps_one()
    {
        var restaurantId = Guid.NewGuid();
        var employee = NewEmployee(restaurantId);
        var position = new Position(restaurantId, "Konobar");

        employee.AssignPosition(position);
        employee.AssignPosition(position);

        Assert.Single(employee.Positions);
    }

    [Fact]
    public void Position_from_another_restaurant_is_rejected()
    {
        var employee = NewEmployee(Guid.NewGuid());
        var foreign = new Position(Guid.NewGuid(), "Konobar");

        Assert.Throws<InvalidOperationException>(() => employee.AssignPosition(foreign));
    }

    [Fact]
    public void ReplacePositions_swaps_the_whole_set()
    {
        var restaurantId = Guid.NewGuid();
        var employee = NewEmployee(restaurantId);
        employee.AssignPosition(new Position(restaurantId, "Konobar"));
        var cook = new Position(restaurantId, "Kuvar");

        employee.ReplacePositions([cook]);

        Assert.Equal([cook.Id], employee.Positions.Select(p => p.Id));
    }

    [Fact]
    public void Deactivate_and_activate_toggle_status()
    {
        var employee = NewEmployee(Guid.NewGuid());

        employee.Deactivate();
        Assert.Equal(EmployeeStatus.Inactive, employee.Status);

        employee.Activate();
        Assert.Equal(EmployeeStatus.Active, employee.Status);
    }
}
