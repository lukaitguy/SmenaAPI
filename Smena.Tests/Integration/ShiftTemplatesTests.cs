using System.Net;
using System.Net.Http.Json;
using Smena.Application.Auth;
using Smena.Application.Shifts;
using Smena.Application.Templates;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ShiftTemplatesTests(ApiFactory factory)
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);

    private static DateTimeOffset Local(DateOnly date, int hour, int minute = 0)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Belgrade.GetUtcOffset(local)).ToUniversalTime();
    }

    private sealed record Setup(ApiClient Owner, ApiClient Staff, Guid StaffId, List<PositionDto> Positions, List<ShiftTemplateDto> Templates)
    {
        public PositionDto Position(string name) => Positions.Single(p => p.Name == name);
        public ShiftTemplateDto Template(string position, string name) =>
            Templates.Single(t => t.PositionName == position && t.Name == name);
    }

    private async Task<Setup> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var staff = await owner.InviteAsync(factory);
        var staffId = (await staff.MeAsync()).EmployeeId;
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        var templates = await ApiClient.ReadAsync<List<ShiftTemplateDto>>(await owner.Http.GetAsync("/api/shift-templates"));
        await owner.SetPositionsAsync(staffId, positions.Single(p => p.Name == "Konobar").Id, positions.Single(p => p.Name == "Kuvar").Id);
        return new Setup(owner, staff, staffId, positions, templates);
    }

    private static Task<HttpResponseMessage> Create(Setup s, Guid? employeeId, PositionDto position, DateTimeOffset from, DateTimeOffset to, Guid? templateId) =>
        s.Owner.Http.PostAsJsonAsync("/api/shifts", new { employeeId, positionId = position.Id, startsAt = from, endsAt = to, templateId }, ApiClient.Json);

    // ---------- šabloni ----------

    [Fact]
    public async Task New_restaurants_get_the_default_templates()
    {
        var s = await ArrangeAsync();

        var summary = s.Templates.Select(t => $"{t.PositionName} {t.Name} {t.StartTime:HH:mm}-{t.EndTime:HH:mm}").Order().ToList();

        Assert.Equal(
            new[]
            {
                "Konobar I 08:00-16:00", "Konobar II 16:00-00:00", "Šanker I 08:00-16:00", "Šanker II 16:00-00:00",
                "Kuvar I 07:00-15:00", "Kuvar II 15:00-23:00", "Hostesa II 16:00-00:00",
            }.Order(),
            summary);
        Assert.Equal(s.Template("Konobar", "I").StartTime, s.Template("Šanker", "I").StartTime);
        Assert.True(s.Template("Šanker", "II").EndsNextDay);
        Assert.True(s.Template("Konobar", "II").EndsNextDay);
        Assert.False(s.Template("Konobar", "I").EndsNextDay);
    }

    [Fact]
    public async Task Everyone_can_read_templates_but_only_managers_change_them()
    {
        var s = await ArrangeAsync();
        var bartender = s.Position("Šanker");

        Assert.Equal(HttpStatusCode.OK, (await s.Staff.Http.GetAsync("/api/shift-templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Staff.Http.PostAsJsonAsync("/api/shift-templates",
            new { positionId = bartender.Id, name = "I", startTime = "17:00:00", endTime = "01:00:00" }, ApiClient.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Staff.Http.PutAsJsonAsync($"/api/shift-templates/{s.Template("Konobar", "I").Id}",
            new { name = "I", startTime = "09:00:00", endTime = "17:00:00" }, ApiClient.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Staff.Http.DeleteAsync($"/api/shift-templates/{s.Template("Konobar", "I").Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/shift-templates")).StatusCode);
    }

    [Fact]
    public async Task Manager_can_add_change_and_remove_a_template()
    {
        var s = await ArrangeAsync();
        var hostess = s.Position("Hostesa");   // ima samo II, pa je "I" slobodno

        var created = await s.Owner.Http.PostAsJsonAsync("/api/shift-templates",
            new { positionId = hostess.Id, name = " I ", startTime = "17:00:00", endTime = "01:00:00" }, ApiClient.Json);
        var template = await ApiClient.ReadAsync<ShiftTemplateDto>(created);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("I", template.Name);
        Assert.True(template.EndsNextDay);

        var updated = await ApiClient.ReadAsync<ShiftTemplateDto>(await s.Owner.Http.PutAsJsonAsync($"/api/shift-templates/{template.Id}",
            new { name = "Večernja", startTime = "18:00:00", endTime = "02:00:00" }, ApiClient.Json));
        Assert.Equal("Večernja", updated.Name);
        Assert.Equal(new TimeOnly(18, 0), updated.StartTime);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.Http.DeleteAsync($"/api/shift-templates/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.Http.DeleteAsync($"/api/shift-templates/{template.Id}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_templates_are_rejected()
    {
        var s = await ArrangeAsync();
        var waiter = s.Position("Konobar");
        Task<HttpResponseMessage> Post(Guid positionId, string name, string from, string to) =>
            s.Owner.Http.PostAsJsonAsync("/api/shift-templates", new { positionId, name, startTime = from, endTime = to }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, (await Post(waiter.Id, "I", "09:00:00", "17:00:00")).StatusCode);      // naziv već postoji
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(waiter.Id, "III", "09:00:00", "09:00:00")).StatusCode);  // isto vreme
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(waiter.Id, "III", "06:00:00", "23:00:00")).StatusCode);  // 17 sati
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(waiter.Id, "", "09:00:00", "17:00:00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(waiter.Id, new string('x', 21), "09:00:00", "17:00:00")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(Guid.NewGuid(), "III", "09:00:00", "17:00:00")).StatusCode);

        var update = await s.Owner.Http.PutAsJsonAsync($"/api/shift-templates/{s.Template("Konobar", "II").Id}",
            new { name = "I", startTime = "16:00:00", endTime = "00:00:00" }, ApiClient.Json);   // preuzima naziv druge smene
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.Http.PutAsJsonAsync($"/api/shift-templates/{Guid.NewGuid()}",
            new { name = "I", startTime = "08:00:00", endTime = "16:00:00" }, ApiClient.Json)).StatusCode);
    }

    [Fact]
    public async Task Templates_of_other_restaurants_are_invisible_and_untouchable()
    {
        var s = await ArrangeAsync();
        var stranger = await ApiClient.RegisterOwnerAsync(factory);
        var strangersTemplates = await ApiClient.ReadAsync<List<ShiftTemplateDto>>(await stranger.Http.GetAsync("/api/shift-templates"));

        Assert.DoesNotContain(strangersTemplates, t => s.Templates.Any(m => m.Id == t.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PutAsJsonAsync($"/api/shift-templates/{s.Template("Konobar", "I").Id}",
            new { name = "X", startTime = "08:00:00", endTime = "16:00:00" }, ApiClient.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.DeleteAsync($"/api/shift-templates/{s.Template("Konobar", "I").Id}")).StatusCode);

        // Šablon drugog restorana ne može da se upotrebi ni sa sopstvenom pozicijom.
        var strangersWaiter = (await ApiClient.ReadAsync<List<PositionDto>>(await stranger.Http.GetAsync("/api/positions"))).Single(p => p.Name == "Konobar");
        var foreign = await stranger.Http.PostAsJsonAsync("/api/shifts", new
        {
            positionId = strangersWaiter.Id, startsAt = Local(Day, 8), endsAt = Local(Day, 16), templateId = s.Template("Konobar", "I").Id,
        }, ApiClient.Json);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_position_removes_its_templates()
    {
        var s = await ArrangeAsync();
        var hostess = s.Position("Hostesa");

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.Http.DeleteAsync($"/api/positions/{hostess.Id}")).StatusCode);

        var left = await ApiClient.ReadAsync<List<ShiftTemplateDto>>(await s.Owner.Http.GetAsync("/api/shift-templates"));
        Assert.DoesNotContain(left, t => t.PositionName == "Hostesa");
        Assert.Equal(6, left.Count);
    }

    // ---------- smene po šablonu ----------

    [Fact]
    public async Task A_shift_made_from_a_template_carries_its_label()
    {
        var s = await ArrangeAsync();
        var first = s.Template("Konobar", "I");

        var response = await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8), Local(Day, 16), first.Id);
        var shift = await ApiClient.ReadAsync<ShiftDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(first.Id, shift.TemplateId);
        Assert.Equal("I", shift.TemplateName);
    }

    [Fact]
    public async Task The_second_shift_ending_at_midnight_matches_its_template()
    {
        var s = await ArrangeAsync();

        var shift = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 16), Local(Day.AddDays(1), 0), s.Template("Konobar", "II").Id));

        Assert.Equal("II", shift.TemplateName);
        Assert.Equal(Local(Day.AddDays(1), 0), shift.EndsAt);
    }

    [Fact]
    public async Task A_shift_with_a_different_time_is_a_custom_one_without_a_label()
    {
        var s = await ArrangeAsync();

        var shift = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8, 30), Local(Day, 17), null));

        Assert.Null(shift.TemplateId);
        Assert.Null(shift.TemplateName);
    }

    [Fact]
    public async Task A_label_cannot_be_claimed_by_a_shift_that_does_not_fit_the_template()
    {
        var s = await ArrangeAsync();
        var waiter = s.Position("Konobar");
        var first = s.Template("Konobar", "I");

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, s.StaffId, waiter, Local(Day, 8, 30), Local(Day, 16), first.Id)).StatusCode);   // drugi početak
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, s.StaffId, waiter, Local(Day, 8), Local(Day, 17), first.Id)).StatusCode);       // drugi kraj
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, s.StaffId, s.Position("Kuvar"), Local(Day, 8), Local(Day, 16), first.Id)).StatusCode);   // šablon druge pozicije
        Assert.Equal(HttpStatusCode.NotFound, (await Create(s, s.StaffId, waiter, Local(Day, 8), Local(Day, 16), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Template_times_are_compared_in_the_restaurant_zone_in_winter_and_summer()
    {
        var s = await ArrangeAsync();
        var waiter = s.Position("Konobar");
        var first = s.Template("Konobar", "I");
        var summer = new DateOnly(DateTime.UtcNow.Year + 1, 7, 15);   // letnje vreme, pomak +02:00
        var winter = new DateOnly(DateTime.UtcNow.Year + 1, 12, 15);  // zimsko vreme, pomak +01:00

        Assert.Equal(HttpStatusCode.Created, (await Create(s, null, waiter, Local(summer, 8), Local(summer, 16), first.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(s, null, waiter, Local(winter, 8), Local(winter, 16), first.Id)).StatusCode);

        // 07:00-15:00 UTC je preko leta 09:00-17:00 po lokalnom, pa to nije smena "I".
        var winterUtc = new DateTimeOffset(summer.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, null, waiter, winterUtc, winterUtc.AddHours(8), first.Id)).StatusCode);
    }

    [Fact]
    public async Task Open_shifts_can_use_templates_too()
    {
        var s = await ArrangeAsync();

        var shift = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, null, s.Position("Kuvar"), Local(Day, 7), Local(Day, 15), s.Template("Kuvar", "I").Id));

        Assert.Null(shift.EmployeeId);
        Assert.Equal("I", shift.TemplateName);
    }

    [Fact]
    public async Task Editing_a_shift_can_switch_between_a_template_and_a_custom_time()
    {
        var s = await ArrangeAsync();
        var waiter = s.Position("Konobar");
        var shift = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, waiter, Local(Day, 8), Local(Day, 16), s.Template("Konobar", "I").Id));
        Task<HttpResponseMessage> Put(DateTimeOffset from, DateTimeOffset to, Guid? templateId) =>
            s.Owner.Http.PutAsJsonAsync($"/api/shifts/{shift.Id}",
                new { employeeId = s.StaffId, positionId = waiter.Id, startsAt = from, endsAt = to, templateId }, ApiClient.Json);

        var custom = await ApiClient.ReadAsync<ShiftDto>(await Put(Local(Day, 9), Local(Day, 17), null));
        Assert.Null(custom.TemplateName);

        var second = await ApiClient.ReadAsync<ShiftDto>(await Put(Local(Day, 16), Local(Day.AddDays(1), 0), s.Template("Konobar", "II").Id));
        Assert.Equal("II", second.TemplateName);

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(Local(Day, 9), Local(Day, 17), s.Template("Konobar", "II").Id)).StatusCode);
    }

    [Fact]
    public async Task Changing_a_template_later_does_not_move_existing_shifts()
    {
        var s = await ArrangeAsync();
        var first = s.Template("Konobar", "I");
        var shift = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8), Local(Day, 16), first.Id));

        await s.Owner.Http.PutAsJsonAsync($"/api/shift-templates/{first.Id}",
            new { name = "I", startTime = "09:00:00", endTime = "17:00:00" }, ApiClient.Json);

        var all = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync(
            $"/api/shifts?from={Uri.EscapeDataString(Local(Day, 0).ToString("O"))}&to={Uri.EscapeDataString(Local(Day.AddDays(1), 0).ToString("O"))}"));
        var same = all.Single(x => x.Id == shift.Id);
        Assert.Equal(Local(Day, 8), same.StartsAt);
        Assert.Equal("I", same.TemplateName);
    }

    [Fact]
    public async Task Deleting_a_template_keeps_the_shifts_but_drops_the_label()
    {
        var s = await ArrangeAsync();
        var first = s.Template("Konobar", "I");
        var shift = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8), Local(Day, 16), first.Id));

        await s.Owner.Http.DeleteAsync($"/api/shift-templates/{first.Id}");

        var all = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Owner.Http.GetAsync(
            $"/api/shifts?from={Uri.EscapeDataString(Local(Day, 0).ToString("O"))}&to={Uri.EscapeDataString(Local(Day.AddDays(1), 0).ToString("O"))}"));
        var kept = all.Single(x => x.Id == shift.Id);
        Assert.Null(kept.TemplateId);
        Assert.Null(kept.TemplateName);
        Assert.Equal(Local(Day, 8), kept.StartsAt);
    }

    [Fact]
    public async Task Recurring_series_can_use_a_template()
    {
        var s = await ArrangeAsync();
        var monday = Day;
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(1);
        var waiter = s.Position("Konobar");
        Task<HttpResponseMessage> Series(string from, string to, Guid templateId, Guid? positionId = null) =>
            s.Owner.Http.PostAsJsonAsync("/api/shifts/recurring", new
            {
                employeeId = s.StaffId, positionId = positionId ?? waiter.Id, fromDate = monday, untilDate = monday.AddDays(6),
                daysOfWeek = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, startTime = from, endTime = to, templateId,
            }, ApiClient.Json);

        var ok = await ApiClient.ReadAsync<RecurringShiftsResultDto>(await Series("16:00:00", "00:00:00", s.Template("Konobar", "II").Id));

        Assert.Equal(2, ok.Created);
        Assert.All(ok.Shifts, x => Assert.Equal("II", x.TemplateName));
        Assert.Equal(HttpStatusCode.BadRequest, (await Series("08:00:00", "17:00:00", s.Template("Konobar", "I").Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Series("07:00:00", "15:00:00", s.Template("Kuvar", "I").Id)).StatusCode);   // šablon druge pozicije
        Assert.Equal(HttpStatusCode.NotFound, (await Series("08:00:00", "16:00:00", Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Employees_see_the_label_in_their_own_shifts_and_in_the_roster()
    {
        var s = await ArrangeAsync();
        await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 16), Local(Day.AddDays(1), 0), s.Template("Konobar", "II").Id);
        await s.Owner.PublishAsync(Local(Day, 0), Local(Day.AddDays(2), 0));
        var query = $"from={Uri.EscapeDataString(Local(Day, 0).ToString("O"))}&to={Uri.EscapeDataString(Local(Day.AddDays(2), 0).ToString("O"))}";

        var mine = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Staff.Http.GetAsync($"/api/shifts/mine?{query}"));
        var roster = await ApiClient.ReadAsync<List<ShiftDto>>(await s.Staff.Http.GetAsync($"/api/schedule?{query}"));

        Assert.Equal("II", Assert.Single(mine).TemplateName);
        Assert.Equal("II", Assert.Single(roster).TemplateName);
    }

    [Fact]
    public async Task Someone_with_two_roles_works_each_day_as_one_of_them_with_that_roles_shifts()
    {
        var s = await ArrangeAsync();
        await s.Owner.SetPositionsAsync(s.StaffId, s.Position("Konobar").Id, s.Position("Šanker").Id);
        var next = Day.AddDays(1);

        var asWaiter = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8), Local(Day, 16), s.Template("Konobar", "I").Id));
        var asBartender = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, s.Position("Šanker"), Local(next, 16), Local(next.AddDays(1), 0), s.Template("Šanker", "II").Id));

        Assert.Equal(("Konobar", "I"), (asWaiter.PositionName, asWaiter.TemplateName));
        Assert.Equal(("Šanker", "II"), (asBartender.PositionName, asBartender.TemplateName));
        var employee = await ApiClient.ReadAsync<Smena.Application.Employees.EmployeeDto>(await s.Owner.Http.GetAsync($"/api/employees/{s.StaffId}"));
        Assert.Equal(["Konobar", "Šanker"], employee.Positions.Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Someone_with_two_roles_cannot_work_both_at_the_same_time_but_can_do_them_back_to_back()
    {
        var s = await ArrangeAsync();
        await s.Owner.SetPositionsAsync(s.StaffId, s.Position("Konobar").Id, s.Position("Šanker").Id);
        await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8), Local(Day, 16), s.Template("Konobar", "I").Id);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Create(s, s.StaffId, s.Position("Šanker"), Local(Day, 8), Local(Day, 16), s.Template("Šanker", "I").Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Create(s, s.StaffId, s.Position("Šanker"), Local(Day, 16), Local(Day.AddDays(1), 0), s.Template("Šanker", "II").Id)).StatusCode);
    }

    [Fact]
    public async Task A_role_cannot_use_the_other_roles_template_even_when_the_times_are_the_same()
    {
        var s = await ArrangeAsync();
        await s.Owner.SetPositionsAsync(s.StaffId, s.Position("Konobar").Id, s.Position("Šanker").Id);

        var response = await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8), Local(Day, 16), s.Template("Šanker", "I").Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("shifts.template_position", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Handing_a_shift_over_keeps_its_label()
    {
        var s = await ArrangeAsync();
        var colleague = await s.Owner.InviteAsync(factory);
        var colleagueId = (await colleague.MeAsync()).EmployeeId;
        await s.Owner.SetPositionsAsync(colleagueId, s.Position("Konobar").Id);
        var shift = await ApiClient.ReadAsync<ShiftDto>(
            await Create(s, s.StaffId, s.Position("Konobar"), Local(Day, 8), Local(Day, 16), s.Template("Konobar", "I").Id));
        await s.Owner.PublishAsync(Local(Day, 0), Local(Day.AddDays(1), 0));

        var swap = await ApiClient.ReadAsync<Smena.Application.Swaps.SwapDto>(await s.Staff.Http.PostAsJsonAsync("/api/swaps",
            new { shiftId = shift.Id, targetEmployeeId = colleagueId }, ApiClient.Json));
        await colleague.Http.PostAsync($"/api/swaps/{swap.Id}/accept", null);
        await s.Owner.Http.PostAsync($"/api/swaps/{swap.Id}/approve", null);

        var mine = await ApiClient.ReadAsync<List<ShiftDto>>(await colleague.Http.GetAsync(
            $"/api/shifts/mine?from={Uri.EscapeDataString(Local(Day, 0).ToString("O"))}&to={Uri.EscapeDataString(Local(Day.AddDays(1), 0).ToString("O"))}"));
        Assert.Equal("I", Assert.Single(mine).TemplateName);
    }
}
