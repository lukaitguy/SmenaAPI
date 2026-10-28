namespace Smena.Application.Common;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Osnovni URL frontenda, iz kojeg se prave linkovi u emailovima.</summary>
    public string FrontendUrl { get; init; } = "http://localhost:4200";

    public string InvitationLink(string token) =>
        $"{FrontendUrl.TrimEnd('/')}/join#{Uri.EscapeDataString(token)}";
}
