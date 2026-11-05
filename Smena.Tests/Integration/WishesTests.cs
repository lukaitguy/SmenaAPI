using System.Net;
using System.Net.Http.Json;
using Smena.Application.Auth;
using Smena.Application.Wishes;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class WishesTests(ApiFactory factory)
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Belgrade).DateTime);
    private static readonly DateOnly Day = Today.AddDays(14);

    private const string Subject = "Nova želja radnika";

    private sealed record Setup(ApiClient Owner, ApiClient Waiter, ApiClient Cook, Guid WaiterId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var waiter = await owner.InviteAsync(factory);
        var cook = await owner.InviteAsync(factory);
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        var waiterId = (await waiter.MeAsync()).EmployeeId;
        await owner.SetPositionsAsync(waiterId, positions.Single(p => p.Name == "Konobar").Id);
        await owner.SetPositionsAsync((await cook.MeAsync()).EmployeeId, positions.Single(p => p.Name == "Kuvar").Id);
        return new Setup(owner, waiter, cook, waiterId);
    }

    private static Task<HttpResponseMessage> Save(ApiClient client, DateOnly date, string shift) =>
        client.Http.PutAsJsonAsync("/api/wishes", new { date, preferredShift = shift }, ApiClient.Json);

    private int Mails(ApiClient client) =>
        factory.Emails.Sent.Count(m => m.To == client.Email && m.Subject == Subject);

    [Fact]
    public async Task Employee_saves_a_wish_and_the_manager_sees_it_and_is_notified()
    {
        var s = await ArrangeAsync();

        var response = await Save(s.Waiter, Day, "I");
        var wish = await ApiClient.ReadAsync<WishDto>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("I", wish.PreferredShift);
        Assert.Equal(Day, wish.Date);
        Assert.Equal(s.WaiterId, wish.EmployeeId);

        var all = await ApiClient.ReadAsync<List<WishDto>>(await s.Owner.Http.GetAsync($"/api/wishes?from={Day:O}&to={Day:O}"));
        Assert.Equal(wish.Id, Assert.Single(all).Id);
        Assert.Equal(1, Mails(s.Owner));
    }

    [Fact]
    public async Task A_second_wish_for_the_same_day_replaces_the_first()
    {
        var s = await ArrangeAsync();
        var first = await ApiClient.ReadAsync<WishDto>(await Save(s.Waiter, Day, "I"));

        var second = await ApiClient.ReadAsync<WishDto>(await Save(s.Waiter, Day, "II"));

        Assert.Equal(first.Id, second.Id);
        var mine = await ApiClient.ReadAsync<List<WishDto>>(await s.Waiter.Http.GetAsync("/api/wishes/mine"));
        Assert.Equal("II", Assert.Single(mine).PreferredShift);
        Assert.Equal(2, Mails(s.Owner));
    }

    [Fact]
    public async Task Saving_the_same_wish_again_sends_no_second_mail()
    {
        var s = await ArrangeAsync();
        await Save(s.Waiter, Day, "I");
        await Save(s.Waiter, Day, "I");

        Assert.Equal(1, Mails(s.Owner));
    }

    [Fact]
    public async Task A_wish_blocks_nothing_the_manager_can_still_assign_the_shift()
    {
        var s = await ArrangeAsync();
        await Save(s.Waiter, Day, "I");
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await s.Owner.Http.GetAsync("/api/positions"));
        var local = Day.ToDateTime(new TimeOnly(16, 0));
        var startsAt = new DateTimeOffset(local, Belgrade.GetUtcOffset(local)).ToUniversalTime();

        var response = await s.Owner.Http.PostAsJsonAsync("/api/shifts", new
        {
            employeeId = s.WaiterId,
            positionId = positions.Single(p => p.Name == "Konobar").Id,
            startsAt,
            endsAt = startsAt.AddHours(8),
        }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Shift_must_exist_for_one_of_the_employees_positions()
    {
        var s = await ArrangeAsync();

        // Nijedna pozicija konobara nema smenu "III".
        var response = await Save(s.Waiter, Day, "III");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Past_and_far_days_are_rejected()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Save(s.Waiter, Today.AddDays(-1), "I")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(s.Waiter, Today.AddDays(400), "I")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Save(s.Waiter, Today, "I")).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_of_a_wish_can_delete_it()
    {
        var s = await ArrangeAsync();
        var wish = await ApiClient.ReadAsync<WishDto>(await Save(s.Waiter, Day, "I"));

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Cook.Http.DeleteAsync($"/api/wishes/{wish.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Waiter.Http.DeleteAsync($"/api/wishes/{wish.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Waiter.Http.DeleteAsync($"/api/wishes/{wish.Id}")).StatusCode);
    }

    [Fact]
    public async Task Employees_cannot_list_everyones_wishes_and_do_not_see_other_restaurants()
    {
        var s = await ArrangeAsync();
        await Save(s.Waiter, Day, "I");
        var other = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Cook.Http.GetAsync("/api/wishes")).StatusCode);
        var foreign = await ApiClient.ReadAsync<List<WishDto>>(await other.Http.GetAsync("/api/wishes"));
        Assert.Empty(foreign);
        Assert.Empty(await ApiClient.ReadAsync<List<WishDto>>(await s.Cook.Http.GetAsync("/api/wishes/mine")));
    }
}
