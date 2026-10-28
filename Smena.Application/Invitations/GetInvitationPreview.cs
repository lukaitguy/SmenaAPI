using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Invitations;

/// <summary>Javni prikaz poziva za ekran „Pridruži se restoranu“, pre nego što korisnik ima nalog.</summary>
public sealed class GetInvitationPreviewHandler(IAppDbContext db, TimeProvider time)
{
    public async Task<Result<InvitationPreviewDto>> HandleAsync(string token, CancellationToken cancellationToken)
    {
        var hash = SecureToken.Hash(token);
        var now = time.GetUtcNow();

        var preview = await (
                from i in db.Invitations.IgnoreQueryFilters()
                join r in db.Restaurants.IgnoreQueryFilters() on i.RestaurantId equals r.Id
                where i.TokenHash == hash && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now
                select new InvitationPreviewDto(r.Name, i.Email, i.Role, i.ExpiresAt))
            .FirstOrDefaultAsync(cancellationToken);

        return preview is null ? InvitationErrors.Invalid : preview;
    }
}

internal static class InvitationErrors
{
    public static readonly Error Invalid =
        Error.NotFound("invitations.invalid", "Poziv ne postoji, iskorišćen je ili je istekao.");
}
