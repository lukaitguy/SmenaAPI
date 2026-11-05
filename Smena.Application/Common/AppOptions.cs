namespace Smena.Application.Common;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Osnovni URL frontenda, iz kojeg se prave linkovi u emailovima.</summary>
    public string FrontendUrl { get; init; } = "http://localhost:4200";

    public string InvitationLink(string token) =>
        $"{FrontendUrl.TrimEnd('/')}/pozivnica#{Uri.EscapeDataString(token)}";

    public string RequestsLink => $"{FrontendUrl.TrimEnd('/')}/zahtevi";

    public string ScheduleLink => $"{FrontendUrl.TrimEnd('/')}/raspored";

    public string AnnouncementsLink => $"{FrontendUrl.TrimEnd('/')}/najave";

    public string DayOffsLink => $"{FrontendUrl.TrimEnd('/')}/slobodni-dani";

    public string SwapsLink => $"{FrontendUrl.TrimEnd('/')}/zamene";

    public string PasswordResetLink(string email, string token) =>
        $"{FrontendUrl.TrimEnd('/')}/nova-lozinka#email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}
