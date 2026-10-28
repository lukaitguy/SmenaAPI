using Smena.Domain.Shifts;

namespace Smena.Tests.Domain;

public class ShiftClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Manager = Guid.NewGuid();

    private static ShiftClaim New(string? note = "  mogu  ") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), note, Now);

    [Fact]
    public void New_claim_is_pending_and_the_note_is_trimmed()
    {
        var claim = New();

        Assert.Equal(ClaimStatus.Pending, claim.Status);
        Assert.True(claim.IsPending);
        Assert.Equal("mogu", claim.Note);
    }

    [Fact]
    public void Blank_note_becomes_null() => Assert.Null(New("   ").Note);

    [Fact]
    public void Approve_records_who_decided_and_when()
    {
        var claim = New();

        claim.Approve(Manager, Now.AddMinutes(5));

        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(Manager, claim.DecidedByEmployeeId);
        Assert.Equal(Now.AddMinutes(5), claim.UpdatedAt);
    }

    [Fact]
    public void Reject_and_cancel_close_the_claim()
    {
        var rejected = New();
        rejected.Reject(Manager, Now);

        var cancelled = New();
        cancelled.Cancel(Now);

        Assert.Equal(ClaimStatus.Rejected, rejected.Status);
        Assert.Equal(ClaimStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.DecidedByEmployeeId);
    }

    [Theory]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Cancelled)]
    public void Finished_claims_cannot_change_anymore(ClaimStatus finalStatus)
    {
        var claim = New();
        switch (finalStatus)
        {
            case ClaimStatus.Approved: claim.Approve(Manager, Now); break;
            case ClaimStatus.Rejected: claim.Reject(Manager, Now); break;
            case ClaimStatus.Cancelled: claim.Cancel(Now); break;
        }

        Assert.Throws<InvalidOperationException>(() => claim.Approve(Manager, Now));
        Assert.Throws<InvalidOperationException>(() => claim.Reject(Manager, Now));
        Assert.Throws<InvalidOperationException>(() => claim.Cancel(Now));
        Assert.Equal(finalStatus, claim.Status);
    }

    [Fact]
    public void Shift_without_employee_is_open()
    {
        var start = Now.AddDays(1);
        var open = new Shift(Guid.NewGuid(), null, Guid.NewGuid(), start, start.AddHours(8), null);
        var assigned = new Shift(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(8), null);

        Assert.True(open.IsOpen);
        Assert.False(assigned.IsOpen);

        open.Reschedule(Guid.NewGuid(), open.PositionId, start, start.AddHours(8), null);
        Assert.False(open.IsOpen);
    }
}
