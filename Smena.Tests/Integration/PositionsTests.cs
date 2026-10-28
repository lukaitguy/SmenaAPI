using System.Net;
using System.Net.Http.Json;
using Smena.Application.Auth;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class PositionsTests(ApiFactory factory)
{
    [Fact]
    public async Task Manager_can_create_rename_and_delete()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var created = await owner.CreatePositionAsync("Barista");

        var renamed = await ApiClient.ReadAsync<PositionDto>(
            await owner.Http.PutAsJsonAsync($"/api/positions/{created.Id}", new { name = "Glavni barista" }, ApiClient.Json));
        Assert.Equal("Glavni barista", renamed.Name);

        var deleted = await owner.Http.DeleteAsync($"/api/positions/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var again = await owner.Http.DeleteAsync($"/api/positions/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Duplicate_name_conflicts_on_create_and_rename()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var created = await owner.CreatePositionAsync("Barista");

        var duplicate = await owner.Http.PostAsJsonAsync("/api/positions", new { name = "Barista" }, ApiClient.Json);
        var rename = await owner.Http.PutAsJsonAsync($"/api/positions/{created.Id}", new { name = "Kuvar" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
    }

    [Fact]
    public async Task Empty_name_is_rejected()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var response = await owner.Http.PostAsJsonAsync("/api/positions", new { name = " " }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Staff_can_list_but_not_modify()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await staff.Http.GetAsync("/api/positions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.Http.PostAsJsonAsync("/api/positions", new { name = "X" }, ApiClient.Json)).StatusCode);
    }

    [Fact]
    public async Task Restaurants_do_not_see_each_others_positions()
    {
        var first = await ApiClient.RegisterOwnerAsync(factory);
        var second = await ApiClient.RegisterOwnerAsync(factory);
        var secret = await first.CreatePositionAsync("Tajna pozicija");

        var list = await ApiClient.ReadAsync<List<PositionDto>>(await second.Http.GetAsync("/api/positions"));
        var rename = await second.Http.PutAsJsonAsync($"/api/positions/{secret.Id}", new { name = "Hak" }, ApiClient.Json);
        var delete = await second.Http.DeleteAsync($"/api/positions/{secret.Id}");

        Assert.DoesNotContain(list, p => p.Id == secret.Id);
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }
}
