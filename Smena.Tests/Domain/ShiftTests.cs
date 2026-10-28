using Smena.Domain.Shifts;

namespace Smena.Tests.Domain;

public class ShiftTests
{
    [Fact]
    public void Times_are_stored_as_utc()
    {
        var start = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.FromHours(1));
        var shift = new Shift(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(8), null);

        Assert.Equal(TimeSpan.Zero, shift.StartsAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero), shift.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero), shift.EndsAt);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  zatvaranje  ", "zatvaranje")]
    public void Note_is_trimmed_and_blank_becomes_null(string? input, string? expected)
    {
        var start = DateTimeOffset.UtcNow;
        var shift = new Shift(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(4), input);

        Assert.Equal(expected, shift.Note);
    }

    [Fact]
    public void Reschedule_replaces_all_fields()
    {
        var start = DateTimeOffset.UtcNow;
        var shift = new Shift(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(4), "a");
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        shift.Reschedule(employeeId, positionId, start.AddDays(1), start.AddDays(1).AddHours(5), "b");

        Assert.Equal(employeeId, shift.EmployeeId);
        Assert.Equal(positionId, shift.PositionId);
        Assert.Equal(start.AddDays(1), shift.StartsAt);
        Assert.Equal("b", shift.Note);
    }
}
