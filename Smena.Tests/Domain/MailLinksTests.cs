using Smena.Application.Common;

namespace Smena.Tests.Domain;

/// <summary>
/// Linkovi u mejlovima moraju da vode na stranice koje aplikacija zaista ima. Spisak ruta je prepisan iz
/// <c>Smena.Front/src/app/app.routes.ts</c>; kad se ruta tamo promeni, menja se i ovde.
/// </summary>
public class MailLinksTests
{
    private static readonly string[] FrontendRoutes =
        ["/raspored", "/zahtevi", "/zamene", "/slobodni-dani", "/najave", "/moje-smene", "/86-lista"];

    private static readonly AppOptions Options = new() { FrontendUrl = "https://smena.example/" };

    public static TheoryData<string> Links => new()
    {
        Options.RequestsLink, Options.ScheduleLink, Options.SwapsLink, Options.DayOffsLink, Options.AnnouncementsLink,
    };

    [Theory]
    [MemberData(nameof(Links))]
    public void Every_link_points_to_a_page_of_the_app(string link)
    {
        var uri = new Uri(link);

        Assert.Equal("https://smena.example", uri.GetLeftPart(UriPartial.Authority));
        Assert.Contains(uri.AbsolutePath, FrontendRoutes);
    }

    [Fact]
    public void The_trailing_slash_of_the_frontend_url_does_not_double_up()
    {
        Assert.DoesNotContain("//zahtevi", Options.RequestsLink);
    }
}
