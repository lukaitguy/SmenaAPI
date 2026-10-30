using Smena.Domain.Shifts;

namespace Smena.Tests.Domain;

public class ShiftTemplateTests
{
    private static ShiftTemplate New(string name = "I", int fromHour = 8, int toHour = 16) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0));

    [Fact]
    public void Defaults_follow_the_restaurants_standard_shifts_and_leave_out_the_bartender()
    {
        var defaults = ShiftTemplate.Defaults.Select(d => $"{d.Position} {d.Name} {d.Start:HH:mm}-{d.End:HH:mm}").ToList();

        Assert.Equal(
            ["Konobar I 08:00-16:00", "Konobar II 16:00-00:00", "Kuvar I 07:00-15:00", "Kuvar II 15:00-23:00", "Hostesa II 16:00-00:00"],
            defaults);
        Assert.DoesNotContain(ShiftTemplate.Defaults, d => d.Position == "Šanker");
    }

    [Fact]
    public void Every_default_template_is_a_valid_shift()
    {
        foreach (var (position, name, start, end) in ShiftTemplate.Defaults)
        {
            var template = new ShiftTemplate(Guid.NewGuid(), Guid.NewGuid(), name, start, end);

            Assert.True(template.Duration <= Shift.MaxDuration, $"{position} {name}");
            Assert.Equal(TimeSpan.FromHours(8), template.Duration);
        }
    }

    [Theory]
    [InlineData(8, 16, false, 8)]
    [InlineData(16, 0, true, 8)]
    [InlineData(22, 6, true, 8)]
    [InlineData(15, 23, false, 8)]
    public void Shift_ending_at_or_before_the_start_ends_the_next_day(int from, int to, bool nextDay, int hours)
    {
        var template = New(fromHour: from, toHour: to);

        Assert.Equal(nextDay, template.EndsNextDay);
        Assert.Equal(TimeSpan.FromHours(hours), template.Duration);
    }

    [Fact]
    public void Name_is_trimmed()
    {
        Assert.Equal("II", New("  II ").Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tačno dvadeset jedan zn")]
    public void Name_is_required_and_limited(string name)
    {
        Assert.Throws<ArgumentException>(() => New(name));
    }

    [Fact]
    public void Start_and_end_cannot_be_the_same()
    {
        Assert.Throws<ArgumentException>(() => New(fromHour: 8, toHour: 8));
    }

    [Fact]
    public void Update_replaces_name_and_times_but_keeps_identity()
    {
        var template = New();
        var id = template.Id;

        template.Update("A", new TimeOnly(9, 30), new TimeOnly(17, 30));

        Assert.Equal(id, template.Id);
        Assert.Equal("A", template.Name);
        Assert.Equal(new TimeOnly(9, 30), template.StartTime);
    }

    [Fact]
    public void A_shift_remembers_its_template_without_changing_its_time()
    {
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var shift = new Shift(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(8), null, templateId: Guid.NewGuid());
        var template = shift.TemplateId;

        shift.Reschedule(Guid.NewGuid(), shift.PositionId, start, start.AddHours(8), null);
        Assert.Equal(template, shift.TemplateId);   // promena zaposlenog ne dira oznaku

        shift.UseTemplate(null);
        Assert.Null(shift.TemplateId);
    }
}
