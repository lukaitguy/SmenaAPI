using System.Net;
using System.Net.Http.Json;
using Smena.Application.Employees;
using Smena.Domain.Employees;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class EmployeesTests(ApiFactory factory)
{
    private static object Update(EmployeeDto e, EmployeeRole? role = null, params Guid[] positionIds) =>
        new { e.FirstName, e.LastName, Role = role ?? e.Role, positionIds };

    private static async Task<EmployeeDto> GetAsync(ApiClient client, Guid id) =>
        await ApiClient.ReadAsync<EmployeeDto>(await client.Http.GetAsync($"/api/employees/{id}"));

    [Fact]
    public async Task Manager_lists_everyone_in_the_restaurant()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        await owner.InviteAsync(factory);

        var employees = await ApiClient.ReadAsync<List<EmployeeDto>>(await owner.Http.GetAsync("/api/employees"));

        Assert.Equal(2, employees.Count);
    }

    [Fact]
    public async Task Staff_cannot_list_employees()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Http.GetAsync("/api/employees")).StatusCode);
    }

    [Fact]
    public async Task Manager_can_promote_staff_and_assign_positions()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);
        var staffId = (await staff.MeAsync()).EmployeeId;
        var position = await owner.CreatePositionAsync();
        var current = await GetAsync(owner, staffId);

        var updated = await ApiClient.ReadAsync<EmployeeDto>(
            await owner.Http.PutAsJsonAsync($"/api/employees/{staffId}", Update(current, EmployeeRole.Manager, position.Id), ApiClient.Json));

        Assert.Equal(EmployeeRole.Manager, updated.Role);
        Assert.Equal([position.Id], updated.Positions.Select(p => p.Id));
    }

    [Fact]
    public async Task Unknown_position_is_not_found()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var me = await owner.MeAsync();
        var current = await GetAsync(owner, me.EmployeeId);

        var response = await owner.Http.PutAsJsonAsync($"/api/employees/{me.EmployeeId}",
            Update(current, null, Guid.NewGuid()), ApiClient.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Owner_role_cannot_be_removed_or_granted()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);
        var ownerId = (await owner.MeAsync()).EmployeeId;
        var staffId = (await staff.MeAsync()).EmployeeId;
        var ownerDto = await GetAsync(owner, ownerId);
        var staffDto = await GetAsync(owner, staffId);

        var demote = await owner.Http.PutAsJsonAsync($"/api/employees/{ownerId}", Update(ownerDto, EmployeeRole.Manager), ApiClient.Json);
        var grant = await owner.Http.PutAsJsonAsync($"/api/employees/{staffId}", Update(staffDto, EmployeeRole.Owner), ApiClient.Json);

        Assert.Equal(HttpStatusCode.Forbidden, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_change_their_own_role()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var manager = await owner.InviteAsync(factory, EmployeeRole.Manager);
        var managerId = (await manager.MeAsync()).EmployeeId;
        var dto = await GetAsync(manager, managerId);

        var response = await manager.Http.PutAsJsonAsync($"/api/employees/{managerId}", Update(dto, EmployeeRole.Staff), ApiClient.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deactivated_employee_cannot_log_in_until_reactivated()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);
        var staffId = (await staff.MeAsync()).EmployeeId;
        Task<HttpResponseMessage> Login() => factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = staff.Email, password = ApiClient.Password }, ApiClient.Json);

        await owner.Http.PutAsJsonAsync($"/api/employees/{staffId}/status", new { status = "Inactive" }, ApiClient.Json);
        Assert.Equal(HttpStatusCode.Forbidden, (await Login()).StatusCode);

        await owner.Http.PutAsJsonAsync($"/api/employees/{staffId}/status", new { status = "Active" }, ApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, (await Login()).StatusCode);
    }

    [Fact]
    public async Task Owner_and_self_cannot_be_deactivated()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var manager = await owner.InviteAsync(factory, EmployeeRole.Manager);
        var ownerId = (await owner.MeAsync()).EmployeeId;
        var managerId = (await manager.MeAsync()).EmployeeId;

        var deactivateOwner = await manager.Http.PutAsJsonAsync($"/api/employees/{ownerId}/status", new { status = "Inactive" }, ApiClient.Json);
        var deactivateSelf = await manager.Http.PutAsJsonAsync($"/api/employees/{managerId}/status", new { status = "Inactive" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Forbidden, deactivateOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivateSelf.StatusCode);
    }

    [Fact]
    public async Task Restaurants_do_not_see_each_others_employees()
    {
        var first = await ApiClient.RegisterOwnerAsync(factory);
        var second = await ApiClient.RegisterOwnerAsync(factory);
        var firstId = (await first.MeAsync()).EmployeeId;

        var get = await second.Http.GetAsync($"/api/employees/{firstId}");
        var status = await second.Http.PutAsJsonAsync($"/api/employees/{firstId}/status", new { status = "Inactive" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
    }
}
