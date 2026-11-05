using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Smena.Application.Auth;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class PushTests(ApiFactory factory)
{
    private sealed record Setup(
        ApiClient Owner, ApiClient A, ApiClient B, ApiClient Cook, Guid AId, Guid BId, Guid WaiterPosition, Guid CookPosition,
        string OwnerDevice, string ADevice, string BDevice, string CookDevice);

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var a = await owner.InviteAsync(factory);
        var b = await owner.InviteAsync(factory);
        var cook = await owner.InviteAsync(factory);
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        Guid Id(string name) => positions.Single(p => p.Name == name).Id;
        var (aId, bId) = ((await a.MeAsync()).EmployeeId, (await b.MeAsync()).EmployeeId);
        await owner.SetPositionsAsync(aId, Id("Konobar"));
        await owner.SetPositionsAsync(bId, Id("Konobar"));
        await owner.SetPositionsAsync((await cook.MeAsync()).EmployeeId, Id("Kuvar"));

        var devices = new[] { owner, a, b, cook }.Select(_ => NewEndpoint()).ToArray();
        for (var i = 0; i < devices.Length; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await Subscribe(new[] { owner, a, b, cook }[i], devices[i])).StatusCode);

        return new Setup(owner, a, b, cook, aId, bId, Id("Konobar"), Id("Kuvar"), devices[0], devices[1], devices[2], devices[3]);
    }

    private static string NewEndpoint() => $"https://push.example.test/{Guid.NewGuid():N}";

    private static Task<HttpResponseMessage> Subscribe(ApiClient client, string endpoint, string p256dh = "BKey-key_123", string auth = "Auth_123") =>
        client.Http.PostAsJsonAsync("/api/push/subscriptions", new { endpoint, keys = new { p256dh, auth } }, ApiClient.Json);

    /// <summary>Naslovi svih obaveštenja koja su stigla na jedan uređaj.</summary>
    private List<string> Titles(string device) =>
        factory.Pushes.Sent.Where(p => p.Target.Endpoint == device).Select(p => Notification(p.Payload).GetProperty("title").GetString()!).ToList();

    private static JsonElement Notification(string payload) => JsonDocument.Parse(payload).RootElement.GetProperty("notification");

    private (string Title, string Body, string Url) Last(string device)
    {
        var n = Notification(factory.Pushes.Sent.Last(p => p.Target.Endpoint == device).Payload);
        return (n.GetProperty("title").GetString()!, n.GetProperty("body").GetString()!,
            n.GetProperty("data").GetProperty("onActionClick").GetProperty("default").GetProperty("url").GetString()!);
    }

    private static DateTimeOffset InDays(int days, int hour = 10) => DateTimeOffset.UtcNow.Date.AddDays(days).AddHours(hour);

    private static async Task<Guid> PublishedShiftAsync(Setup s, Guid? employeeId, Guid positionId, int days = 10)
    {
        var response = await s.Owner.Http.PostAsJsonAsync("/api/shifts",
            new { employeeId, positionId, startsAt = InDays(days), endsAt = InDays(days, 18) }, ApiClient.Json);
        var shift = await response.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json);
        await s.Owner.PublishAsync(InDays(days - 1), InDays(days + 1));
        return shift.GetProperty("id").GetGuid();
    }

    // ---- pretplate ----

    [Fact]
    public async Task Config_tells_the_browser_the_public_key()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);

        var config = await owner.Http.GetFromJsonAsync<JsonElement>("/api/push/config", ApiClient.Json);

        Assert.True(config.GetProperty("enabled").GetBoolean());
        Assert.Equal(TestVapid.PublicKey, config.GetProperty("publicKey").GetString());
    }

    [Fact]
    public async Task Subscribing_twice_with_the_same_address_keeps_one_device()
    {
        var s = await ArrangeAsync();

        await Subscribe(s.A, s.ADevice, "BNewKey", "NewAuth");
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "T", positionIds = new[] { s.WaiterPosition } }, ApiClient.Json);

        Assert.Single(factory.Pushes.Sent, p => p.Target.Endpoint == s.ADevice);
        Assert.Equal("BNewKey", factory.Pushes.Sent.Single(p => p.Target.Endpoint == s.ADevice).Target.P256dh);   // ključevi su osveženi
    }

    [Fact]
    public async Task Subscription_input_is_validated_and_a_sign_in_is_required()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Subscribe(s.A, "http://not-secure.example/x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Subscribe(s.A, "nije-adresa")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Subscribe(s.A, NewEndpoint(), p256dh: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Subscribe(s.A, NewEndpoint(), auth: "")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = NewEndpoint(), keys = new { p256dh = "k", auth = "a" } }, ApiClient.Json)).StatusCode);
    }

    [Fact]
    public async Task The_same_browser_used_by_a_colleague_moves_to_the_colleague()
    {
        var s = await ArrangeAsync();

        await Subscribe(s.B, s.ADevice);
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "T", employeeIds = new[] { s.AId } }, ApiClient.Json);

        Assert.Empty(Titles(s.ADevice));                 // Ana više nema taj uređaj
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "T2", employeeIds = new[] { s.BId } }, ApiClient.Json);
        Assert.Contains("T2", Titles(s.ADevice));         // a Bojan ga ima
    }

    [Fact]
    public async Task A_browser_tied_to_another_restaurant_is_refused_with_a_clear_message()
    {
        var s = await ArrangeAsync();
        var stranger = await ApiClient.RegisterOwnerAsync(factory);

        var response = await Subscribe(stranger, s.ADevice);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("drugog restorana", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_the_newest_devices_of_a_person_are_kept()
    {
        var s = await ArrangeAsync();   // Ana već ima jedan uređaj
        var devices = Enumerable.Range(0, 11).Select(_ => NewEndpoint()).ToList();
        foreach (var device in devices)
            await Subscribe(s.A, device);

        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "T", employeeIds = new[] { s.AId } }, ApiClient.Json);

        var all = devices.Prepend(s.ADevice).ToList();
        Assert.Equal(10, all.Count(d => Titles(d).Count == 1));
        Assert.Empty(Titles(s.ADevice));      // najstariji je istisnut
        Assert.Single(Titles(devices[^1]));   // najnoviji ostaje
    }

    [Fact]
    public async Task Unsubscribing_stops_the_notifications_and_an_unknown_address_is_not_an_error()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await s.A.Http.DeleteAsync($"/api/push/subscriptions?endpoint={Uri.EscapeDataString(s.ADevice)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.A.Http.DeleteAsync($"/api/push/subscriptions?endpoint={Uri.EscapeDataString(NewEndpoint())}")).StatusCode);
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "T", positionIds = new[] { s.WaiterPosition } }, ApiClient.Json);

        Assert.Empty(Titles(s.ADevice));
        Assert.Single(Titles(s.BDevice));
    }

    [Fact]
    public async Task Nobody_can_remove_a_device_of_someone_else()
    {
        var s = await ArrangeAsync();

        await s.B.Http.DeleteAsync($"/api/push/subscriptions?endpoint={Uri.EscapeDataString(s.ADevice)}");
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "T", employeeIds = new[] { s.AId } }, ApiClient.Json);

        Assert.Single(Titles(s.ADevice));
    }

    [Fact]
    public async Task The_test_notification_goes_only_to_the_devices_of_the_person_who_asked()
    {
        var s = await ArrangeAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await s.A.Http.PostAsync("/api/push/test", null)).StatusCode);

        Assert.Equal(("Probno obaveštenje", "/obavestenja"), (Last(s.ADevice).Title, Last(s.ADevice).Url));
        Assert.Empty(Titles(s.BDevice));
        Assert.Empty(Titles(s.OwnerDevice));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/push/test", null)).StatusCode);
    }

    // ---- isporuka ----

    [Fact]
    public async Task An_announcement_reaches_only_its_recipients_with_the_page_to_open()
    {
        var s = await ArrangeAsync();

        await s.Owner.Http.PostAsJsonAsync("/api/announcements",
            new { title = "Sastanak sale", body = "Dolazite svi", positionIds = new[] { s.WaiterPosition } }, ApiClient.Json);

        Assert.Equal(("Sastanak sale", "Dolazite svi", "/najave"), Last(s.ADevice));
        Assert.Single(Titles(s.BDevice));
        Assert.Empty(Titles(s.CookDevice));
        Assert.Empty(Titles(s.OwnerDevice));        // autor sam sebi ne šalje
    }

    [Fact]
    public async Task A_person_with_two_devices_gets_the_notification_on_both()
    {
        var s = await ArrangeAsync();
        var second = NewEndpoint();
        await Subscribe(s.A, second);

        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "T", employeeIds = new[] { s.AId } }, ApiClient.Json);

        Assert.Single(Titles(s.ADevice));
        Assert.Single(Titles(second));
    }

    [Fact]
    public async Task Publishing_a_schedule_notifies_the_people_of_that_role_only()
    {
        var s = await ArrangeAsync();

        await PublishedShiftAsync(s, s.AId, s.WaiterPosition);

        Assert.Equal("Objavljen je novi raspored", Last(s.ADevice).Title);
        Assert.Equal("/raspored", Last(s.ADevice).Url);
        Assert.Contains("Konobar", Last(s.ADevice).Body);
        Assert.Single(Titles(s.BDevice));
        Assert.Empty(Titles(s.CookDevice));          // raspored kuvara nije objavljen
        Assert.Empty(Titles(s.OwnerDevice));         // ko objavljuje ne dobija
    }

    [Fact]
    public async Task A_swap_tells_the_colleague_then_the_manager_then_both_colleagues()
    {
        var s = await ArrangeAsync();
        var shiftId = await PublishedShiftAsync(s, s.AId, s.WaiterPosition);
        var before = Titles(s.BDevice).Count;

        var swap = await s.A.Http.PostAsJsonAsync("/api/swaps", new { shiftId, targetEmployeeId = s.BId }, ApiClient.Json);
        var swapId = (await swap.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetGuid();
        Assert.Equal(("Kolega ti prepušta smenu", "/zamene"), (Last(s.BDevice).Title, Last(s.BDevice).Url));
        Assert.Equal(before + 1, Titles(s.BDevice).Count);

        await s.B.Http.PostAsync($"/api/swaps/{swapId}/accept", null);
        Assert.Equal(("Zamena smene čeka odluku", "/zahtevi"), (Last(s.OwnerDevice).Title, Last(s.OwnerDevice).Url));

        await s.Owner.Http.PostAsync($"/api/swaps/{swapId}/approve", null);
        Assert.Equal("Zamena smene je odobrena", Last(s.ADevice).Title);
        Assert.Equal("Zamena smene je odobrena", Last(s.BDevice).Title);
        Assert.Equal("/moje-smene", Last(s.ADevice).Url);
    }

    [Fact]
    public async Task An_open_shift_request_tells_the_manager_and_then_the_applicant()
    {
        var s = await ArrangeAsync();
        var shiftId = await PublishedShiftAsync(s, null, s.WaiterPosition);

        var claim = await s.B.Http.PostAsJsonAsync($"/api/shifts/{shiftId}/claims", new { note = (string?)null }, ApiClient.Json);
        var claimId = (await claim.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetGuid();
        Assert.Equal("Novi zahtev za otvorenu smenu", Last(s.OwnerDevice).Title);

        await s.Owner.Http.PostAsync($"/api/claims/{claimId}/approve", null);
        Assert.Equal(("Otvorena smena je tvoja", "/moje-smene"), (Last(s.BDevice).Title, Last(s.BDevice).Url));
    }

    [Fact]
    public async Task A_day_off_request_tells_the_manager_and_the_decision_tells_the_employee()
    {
        var s = await ArrangeAsync();
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        var request = await s.A.Http.PostAsJsonAsync("/api/dayoffs", new { date = day, reason = "slava" }, ApiClient.Json);
        var id = (await request.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetGuid();
        Assert.Equal("Novi zahtev za slobodan dan", Last(s.OwnerDevice).Title);

        await s.Owner.Http.PostAsync($"/api/dayoffs/{id}/approve", null);
        Assert.Equal(("Slobodan dan odobren", "/slobodni-dani"), (Last(s.ADevice).Title, Last(s.ADevice).Url));
        Assert.Contains($"{day:dd.MM.yyyy.}", Last(s.ADevice).Body);
    }

    // ---- nevažeći uređaji ----

    [Fact]
    public async Task A_device_the_push_service_reports_as_gone_is_removed_and_the_others_still_get_it()
    {
        var s = await ArrangeAsync();
        factory.Pushes.Gone(s.ADevice);

        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "Prvi", positionIds = new[] { s.WaiterPosition } }, ApiClient.Json);
        factory.Pushes.Gone(NewEndpoint());   // ostali zapisi ne smetaju
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "Drugi", positionIds = new[] { s.WaiterPosition } }, ApiClient.Json);

        Assert.Empty(Titles(s.ADevice));                              // nikad nije stiglo
        Assert.Equal(["Prvi", "Drugi"], Titles(s.BDevice));
        // Posle uklanjanja Ana ponovo sme da se pretplati istom adresom.
        Assert.Equal(HttpStatusCode.NoContent, (await Subscribe(s.A, s.ADevice)).StatusCode);
    }

    [Fact]
    public async Task A_temporary_failure_keeps_the_device_so_later_messages_still_arrive()
    {
        var s = await ArrangeAsync();
        factory.Pushes.Failing(s.ADevice);
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "Prvi", positionIds = new[] { s.WaiterPosition } }, ApiClient.Json);
        Assert.Empty(Titles(s.ADevice));

        factory.Pushes.Recover(s.ADevice);
        await s.Owner.Http.PostAsJsonAsync("/api/announcements", new { title = "Drugi", positionIds = new[] { s.WaiterPosition } }, ApiClient.Json);

        Assert.Equal(["Drugi"], Titles(s.ADevice));     // uređaj nije uklonjen zbog privremene greške
        Assert.Equal(["Prvi", "Drugi"], Titles(s.BDevice));
    }
}
