namespace Smena.Application.Common;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Osnovni URL frontenda, iz kojeg se prave linkovi u emailovima.</summary>
    public string FrontendUrl { get; init; } = "http://localhost:4200";

    public string InvitationLink(string token) =>
        $"{FrontendUrl.TrimEnd('/')}/join#{Uri.EscapeDataString(token)}";

    public string ScheduleLink => $"{FrontendUrl.TrimEnd('/')}/schedule";

    public string SwapsLink => $"{FrontendUrl.TrimEnd('/')}/swaps";

    public string PasswordResetLink(string email, string token) =>
        $"{FrontendUrl.TrimEnd('/')}/reset-password#email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}
