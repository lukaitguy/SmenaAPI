using Smena.Application.Shifts;

namespace Smena.Tests.Domain;

public class SaveShiftValidatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private readonly SaveShiftValidator _validator = new();

    private static SaveShiftCommand Command(DateTimeOffset start, DateTimeOffset end, string? note = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), start, end, note);

    [Fact]
    public void Valid_shift_passes() =>
        Assert.True(_validator.Validate(Command(Start, Start.AddHours(8))).IsValid);

    [Fact]
    public void End_before_start_fails() =>
        Assert.False(_validator.Validate(Command(Start, Start.AddHours(-1))).IsValid);

    [Fact]
    public void Zero_length_fails() =>
        Assert.False(_validator.Validate(Command(Start, Start)).IsValid);

    [Fact]
    public void Sixteen_hours_is_allowed_but_more_is_not()
    {
        Assert.True(_validator.Validate(Command(Start, Start.AddHours(16))).IsValid);
        Assert.False(_validator.Validate(Command(Start, Start.AddHours(16).AddMinutes(1))).IsValid);
    }

    [Fact]
    public void Too_long_note_fails() =>
        Assert.False(_validator.Validate(Command(Start, Start.AddHours(4), new string('x', 501))).IsValid);

    [Fact]
    public void Empty_ids_fail()
    {
        var command = new SaveShiftCommand(Guid.Empty, Guid.Empty, Start, Start.AddHours(4), null);
        Assert.False(_validator.Validate(command).IsValid);
    }
}
