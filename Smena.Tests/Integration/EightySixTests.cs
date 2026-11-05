using System.Net;
using System.Net.Http.Json;
using Smena.Application.Auth;
using Smena.Application.EightySix;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class EightySixTests(ApiFactory factory)
{
    private sealed record Setup(ApiClient Owner, ApiClient Cook, ApiClient Bartender, ApiClient Waiter, Guid KitchenId, Guid BarId, Guid WaiterPositionId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var cook = await owner.InviteAsync(factory);
        var bartender = await owner.InviteAsync(factory);
        var waiter = await owner.InviteAsync(factory);
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        Guid Id(string name) => positions.Single(p => p.Name == name).Id;
        await owner.SetPositionsAsync((await cook.MeAsync()).EmployeeId, Id("Kuvar"));
        await owner.SetPositionsAsync((await bartender.MeAsync()).EmployeeId, Id("Šanker"));
        await owner.SetPositionsAsync((await waiter.MeAsync()).EmployeeId, Id("Konobar"));
        return new Setup(owner, cook, bartender, waiter, Id("Kuvar"), Id("Šanker"), Id("Konobar"));
    }

    private static Task<HttpResponseMessage> Add(ApiClient client, Guid positionId, string name, string? note = null) =>
        client.Http.PostAsJsonAsync("/api/eighty-six", new { positionId, name, note }, ApiClient.Json);

    private static async Task<List<EightySixItemDto>> ListAsync(ApiClient client) =>
        await ApiClient.ReadAsync<List<EightySixItemDto>>(await client.Http.GetAsync("/api/eighty-six"));

    [Fact]
    public async Task Cook_adds_to_the_kitchen_section_and_everyone_sees_it_with_who_added_it()
    {
        var s = await ArrangeAsync();

        var response = await Add(s.Cook, s.KitchenId, "  Pljeskavica ", "nema mesa do sutra");
        var item = await ApiClient.ReadAsync<EightySixItemDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Pljeskavica", item.Name);
        Assert.Equal("Kuvar", item.PositionName);
        Assert.Equal((await s.Cook.MeAsync()).EmployeeId, item.CreatedByEmployeeId);

        foreach (var viewer in new[] { s.Waiter, s.Bartender, s.Owner })
            Assert.Equal(item.Id, Assert.Single(await ListAsync(viewer)).Id);
    }

    [Fact]
    public async Task Staff_can_only_change_the_section_of_their_own_position()
    {
        var s = await ArrangeAsync();
        var kitchen = await ApiClient.ReadAsync<EightySixItemDto>(await Add(s.Cook, s.KitchenId, "Pljeskavica"));

        Assert.Equal(HttpStatusCode.Forbidden, (await Add(s.Cook, s.BarId, "Heineken")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Bartender.Http.DeleteAsync($"/api/eighty-six/{kitchen.Id}")).StatusCode);
        Assert.Single(await ListAsync(s.Owner));
    }

    [Fact]
    public async Task Waiters_can_only_look()
    {
        var s = await ArrangeAsync();
        var item = await ApiClient.ReadAsync<EightySixItemDto>(await Add(s.Cook, s.KitchenId, "Pljeskavica"));

        Assert.Equal(HttpStatusCode.Forbidden, (await Add(s.Waiter, s.KitchenId, "Pomfrit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Waiter.Http.DeleteAsync($"/api/eighty-six/{item.Id}")).StatusCode);
        // Konobar nema odeljak na listi, pa ni pokušaj sa njegovom pozicijom ne prolazi.
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(s.Owner, s.WaiterPositionId, "Pomfrit")).StatusCode);
        Assert.Single(await ListAsync(s.Waiter));
    }

    [Fact]
    public async Task Manager_changes_every_section()
    {
        var s = await ArrangeAsync();

        var bar = await ApiClient.ReadAsync<EightySixItemDto>(await Add(s.Owner, s.BarId, "Heineken"));
        await ApiClient.ReadAsync<EightySixItemDto>(await Add(s.Owner, s.KitchenId, "Pljeskavica"));

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.Http.DeleteAsync($"/api/eighty-six/{bar.Id}")).StatusCode);
        Assert.Equal("Pljeskavica", Assert.Single(await ListAsync(s.Owner)).Name);
    }

    [Fact]
    public async Task Item_is_removed_when_it_is_back_in_stock()
    {
        var s = await ArrangeAsync();
        var item = await ApiClient.ReadAsync<EightySixItemDto>(await Add(s.Cook, s.KitchenId, "Pljeskavica"));

        Assert.Equal(HttpStatusCode.NoContent, (await s.Cook.Http.DeleteAsync($"/api/eighty-six/{item.Id}")).StatusCode);
        Assert.Empty(await ListAsync(s.Waiter));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Cook.Http.DeleteAsync($"/api/eighty-six/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_same_item_cannot_be_listed_twice_in_a_section_but_can_in_another()
    {
        var s = await ArrangeAsync();
        await Add(s.Cook, s.KitchenId, "Limun");

        Assert.Equal(HttpStatusCode.Conflict, (await Add(s.Cook, s.KitchenId, " LIMUN ")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Add(s.Bartender, s.BarId, "Limun")).StatusCode);
    }

    [Fact]
    public async Task Name_is_required_and_the_position_must_exist()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Add(s.Cook, s.KitchenId, "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(s.Cook, s.KitchenId, new string('x', 101))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Add(s.Owner, Guid.NewGuid(), "Limun")).StatusCode);
    }

    [Fact]
    public async Task Other_restaurants_never_see_the_list_and_a_token_is_required()
    {
        var s = await ArrangeAsync();
        await Add(s.Cook, s.KitchenId, "Pljeskavica");
        var other = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Empty(await ListAsync(other));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/eighty-six")).StatusCode);
    }
}
