using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;

namespace Smena.Application.Invitations;

/// <summary>Pozivi koji još čekaju odgovor.</summary>
public sealed class ListInvitationsHandler(IAppDbContext db, TimeProvider time)
{
    public async Task<IReadOnlyList<InvitationDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        return await db.Invitations
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationDto(i.Id, i.Email, i.Role, i.PositionId, i.CreatedAt, i.ExpiresAt))
            .ToListAsync(cancellationToken);
    }
}
