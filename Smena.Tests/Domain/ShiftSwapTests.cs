using Smena.Domain.Shifts;

namespace Smena.Tests.Domain;

public class ShiftSwapTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Manager = Guid.NewGuid();

    private static ShiftSwap New() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  mogu li?  ", Now);

    [Fact]
    public void New_request_is_pending_and_open()
    {
        var swap = New();

        Assert.Equal(SwapStatus.Pending, swap.Status);
        Assert.True(swap.IsOpen);
        Assert.Equal("mogu li?", swap.Note);
    }

    [Fact]
    public void Cannot_hand_a_shift_to_yourself()
    {
        var id = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => new ShiftSwap(Guid.NewGuid(), Guid.NewGuid(), id, id, null, Now));
    }

    [Fact]
    public void Happy_path_pending_accepted_approved()
    {
        var swap = New();

        swap.Accept(Now.AddMinutes(1));
        swap.Approve(Manager, Now.AddMinutes(2));

        Assert.Equal(SwapStatus.Approved, swap.Status);
        Assert.Equal(Manager, swap.DecidedByEmployeeId);
        Assert.Equal(Now.AddMinutes(2), swap.UpdatedAt);
        Assert.False(swap.IsOpen);
    }

    [Fact]
    public void Manager_cannot_approve_before_the_colleague_accepts()
    {
        Assert.Throws<InvalidOperationException>(() => New().Approve(Manager, Now));
    }

    [Fact]
    public void Manager_can_reject_pending_and_accepted_requests()
    {
        var pending = New();
        pending.Reject(Manager, Now);

        var accepted = New();
        accepted.Accept(Now);
        accepted.Reject(Manager, Now);

        Assert.Equal(SwapStatus.Rejected, pending.Status);
        Assert.Equal(SwapStatus.Rejected, accepted.Status);
    }

    [Fact]
    public void Requester_can_cancel_while_open()
    {
        var pending = New();
        pending.Cancel(Now);

        var accepted = New();
        accepted.Accept(Now);
        accepted.Cancel(Now);

        Assert.Equal(SwapStatus.Cancelled, pending.Status);
        Assert.Equal(SwapStatus.Cancelled, accepted.Status);
    }

    [Fact]
    public void Colleague_can_only_answer_a_pending_request()
    {
        var accepted = New();
        accepted.Accept(Now);

        Assert.Throws<InvalidOperationException>(() => accepted.Accept(Now));
        Assert.Throws<InvalidOperationException>(() => accepted.Decline(Now));
    }

    [Theory]
    [InlineData(SwapStatus.Declined)]
    [InlineData(SwapStatus.Approved)]
    [InlineData(SwapStatus.Rejected)]
    [InlineData(SwapStatus.Cancelled)]
    public void Finished_requests_cannot_change_anymore(SwapStatus finalStatus)
    {
        var swap = New();
        switch (finalStatus)
        {
            case SwapStatus.Declined: swap.Decline(Now); break;
            case SwapStatus.Approved: swap.Accept(Now); swap.Approve(Manager, Now); break;
            case SwapStatus.Rejected: swap.Reject(Manager, Now); break;
            case SwapStatus.Cancelled: swap.Cancel(Now); break;
        }

        Assert.False(swap.IsOpen);
        Assert.Throws<InvalidOperationException>(() => swap.Accept(Now));
        Assert.Throws<InvalidOperationException>(() => swap.Cancel(Now));
        Assert.Throws<InvalidOperationException>(() => swap.Reject(Manager, Now));
        Assert.Equal(finalStatus, swap.Status);
    }
}
