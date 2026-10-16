using System.Net;
using FluentAssertions;

namespace StudyPlanner.UnitTests;

public class BrandingTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Auth/Login")]
    [InlineData("/Focus")]
    public async Task HeaderAndFocus_UseThemeColoredLogoAndVersionedGreenFavicon(string path)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        if (path == "/Focus") await ProgressHistoryTests.SignIn(client);
        var html = await client.GetStringAsync(path);
        html.Should().MatchRegex(@"<span\b[^>]*class=""brand-logo""[^>]*aria-hidden=""true""[^>]*></span>");
        html.Should().Contain("type=\"image/svg+xml\"").And.Contain("/logo.svg?v=");
        html.Should().NotContain("src=\"/logo.png\"");
    }

    [Fact]
    public async Task Favicon_IsSelfContainedAndIncludesBothThemeColors()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        using var response = await client.GetAsync("/logo.svg");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        var svg = await response.Content.ReadAsStringAsync();
        svg.Should().Contain("#306c36").And.Contain("#b4ed85").And.Contain("data:image/png;base64,")
            .And.Contain("mask-type:alpha").And.NotContain("https://");
    }
}
