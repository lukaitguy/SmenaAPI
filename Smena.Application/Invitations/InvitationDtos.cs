using Smena.Domain.Employees;
using Smena.Domain.Invitations;

namespace Smena.Application.Invitations;

public sealed record InvitationDto(
    Guid Id,
    string Email,
    EmployeeRole Role,
    Guid? PositionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Token se vraća samo jednom, pri kreiranju; u bazi postoji samo njegov heš.</summary>
public sealed record CreatedInvitationDto(InvitationDto Invitation, string AcceptUrl, bool EmailSent);

public sealed record InvitationPreviewDto(string RestaurantName, string Email, EmployeeRole Role, DateTimeOffset ExpiresAt);

internal static class InvitationMapping
{
    public static InvitationDto ToDto(this Invitation i) =>
        new(i.Id, i.Email, i.Role, i.PositionId, i.CreatedAt, i.ExpiresAt);
}
