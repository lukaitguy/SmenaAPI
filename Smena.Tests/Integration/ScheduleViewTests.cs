using System.Net;
using System.Net.Http.Json;
using Smena.Application.Shifts;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ScheduleViewTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Day = new(DateTimeOffset.UtcNow.Date.AddDays(14), TimeSpan.Zero);

    /// <summary>A i B su konobari, C je kuvar, D je i konobar i kuvar.</summary>
    private sealed record Setup(
        ApiClient Owner, ApiClient A, ApiClient B, ApiClient C, ApiClient D,
        Guid AId, Guid BId, Guid CId, Guid DId, Guid WaiterId, Guid CookId);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var c = await owner.InviteAsync(factory);
        var d = await owner.InviteAsync(factory);
        var ids = new[] { a, b, c, d }.Select(x => x.MeAsync().GetAwaiter().GetResult().EmployeeId).ToArray();
        var waiter = await owner.CreatePositionAsync();
        var cook = await owner.CreatePositionAsync();
        await owner.SetPositionsAsync(ids[0], waiter.Id);
        await owner.SetPositionsAsync(ids[1], waiter.Id);
        await owner.SetPositionsAsync(ids[2], cook.Id);
        await owner.SetPositionsAsync(ids[3], waiter.Id, cook.Id);
        return new Setup(owner, a, b, c, d, ids[0], ids[1], ids[2], ids[3], waiter.Id, cook.Id);
    }

    private static async Task<ShiftDto> CreateAsync(ApiClient owner, Guid? employeeId, Guid positionId, int dayOffset) =>
        await ApiClient.ReadAsync<ShiftDto>(await owner.Http.PostAsJsonAsync("/api/shifts", new
        {
            employeeId, positionId,
            startsAt = Day.AddDays(dayOffset).AddHours(8), endsAt = Day.AddDays(dayOffset).AddHours(16),
        }, ApiClient.Json));

    private static string Range(string extra = "") =>
        $"from={Uri.EscapeDataString(Day.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(30).ToString("O"))}{extra}";

    private static async Task<List<ShiftDto>> ScheduleAsync(ApiClient client, string extra = "") =>
        await ApiClient.ReadAsync<List<ShiftDto>>(await client.Http.GetAsync($"/api/schedule?{Range(extra)}"));

    [Fact]
    public async Task Employees_see_the_whole_published_roster_of_their_own_role_and_nothing_else()
    {
        var s = await ArrangeAsync();
        var shiftA = await CreateAsync(s.Owner, s.AId, s.WaiterId, 0);
        var shiftB = await CreateAsync(s.Owner, s.BId, s.WaiterId, 1);
        var shiftC = await CreateAsync(s.Owner, s.CId, s.CookId, 2);
        await s.Owner.PublishAsync(Day, Day.AddDays(7));

        var forA = await ScheduleAsync(s.A);
        var forC = await ScheduleAsync(s.C);

        Assert.Equal(new[] { shiftA.Id, shiftB.Id }.Order(), forA.Select(x => x.Id).Order());   // kolega vidi, kuvar ne
        Assert.Equal([shiftC.Id], forC.Select(x => x.Id));
        Assert.Contains(forA, x => x.EmployeeName == "Zaposleni Test");
    }

    [Fact]
    public async Task Drafts_are_not_part_of_the_roster()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s.Owner, s.AId, s.WaiterId, 0);
        await CreateAsync(s.Owner, s.BId, s.WaiterId, 20);
        await s.Owner.PublishAsync(Day, Day.AddDays(7));

        var forA = await ScheduleAsync(s.A);

        Assert.Single(forA);
        Assert.All(forA, x => Assert.NotNull(x.PublishedAt));
    }

    [Fact]
    public async Task Roster_is_published_by_role_so_each_role_sees_its_own_when_it_is_published()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s.Owner, s.AId, s.WaiterId, 0);
        await CreateAsync(s.Owner, s.CId, s.CookId, 0);
        await s.Owner.Http.PostAsJsonAsync("/api/shifts/publish",
            new { from = Day, to = Day.AddDays(7), positionIds = new[] { s.WaiterId } }, ApiClient.Json);

        Assert.Single(await ScheduleAsync(s.A));
        Assert.Empty(await ScheduleAsync(s.C));
    }

    [Fact]
    public async Task Open_shifts_of_the_role_are_part_of_its_roster()
    {
        var s = await ArrangeAsync();
        var open = await CreateAsync(s.Owner, null, s.WaiterId, 0);
        await s.Owner.PublishAsync(Day, Day.AddDays(7));

        var forA = await ScheduleAsync(s.A);

        Assert.Equal(open.Id, Assert.Single(forA).Id);
        Assert.Null(forA[0].EmployeeId);
        Assert.Empty(await ScheduleAsync(s.C));
    }

    [Fact]
    public async Task Someone_with_two_roles_sees_both_and_can_narrow_to_one()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s.Owner, s.AId, s.WaiterId, 0);
        await CreateAsync(s.Owner, s.CId, s.CookId, 1);
        await s.Owner.PublishAsync(Day, Day.AddDays(7));

        Assert.Equal(2, (await ScheduleAsync(s.D)).Count);
        Assert.Equal(s.WaiterId, Assert.Single(await ScheduleAsync(s.D, $"&positionId={s.WaiterId}")).PositionId);
        Assert.Equal(s.CookId, Assert.Single(await ScheduleAsync(s.D, $"&positionId={s.CookId}")).PositionId);
    }

    [Fact]
    public async Task Narrowing_to_a_role_you_do_not_have_shows_nothing()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s.Owner, s.CId, s.CookId, 0);
        await s.Owner.PublishAsync(Day, Day.AddDays(7));

        Assert.Empty(await ScheduleAsync(s.A, $"&positionId={s.CookId}"));
    }

    [Fact]
    public async Task Manager_can_narrow_the_full_schedule_to_one_role()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s.Owner, s.AId, s.WaiterId, 0);
        await CreateAsync(s.Owner, s.CId, s.CookId, 1);

        var cooks = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync($"/api/shifts?{Range($"&positionId={s.CookId}")}"));
        var all = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync($"/api/shifts?{Range()}"));

        Assert.Equal(s.CookId, Assert.Single(cooks).PositionId);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Period_is_validated_and_a_token_is_required()
    {
        var s = await ArrangeAsync();

        var inverted = await s.A.Http.GetAsync($"/api/schedule?from={Uri.EscapeDataString(Day.AddDays(2).ToString("O"))}&to={Uri.EscapeDataString(Day.ToString("O"))}");
        var tooLong = await s.A.Http.GetAsync($"/api/schedule?from={Uri.EscapeDataString(Day.ToString("O"))}&to={Uri.EscapeDataString(Day.AddDays(70).ToString("O"))}");
        var anonymous = await factory.CreateClient().GetAsync($"/api/schedule?{Range()}");

        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Other_restaurants_never_show_up()
    {
        var s = await ArrangeAsync();
        await CreateAsync(s.Owner, s.AId, s.WaiterId, 0);
        await s.Owner.PublishAsync(Day, Day.AddDays(7));
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        Assert.Empty(await ScheduleAsync(stranger));
    }
}
