using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace StudyPlanner.UnitTests;

public class FocusWorkflowTests
{
    [Fact]
    public async Task AnonymousFocus_RequiresExistingCookieAuthentication()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient();
        using var response = await client.GetAsync("/Focus");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Auth/Login");
    }

    [Fact]
    public async Task OwnedFocus_RendersOnlyOwnedItemsAndNeverWritesProgress()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var writes = factory.Store.Writes;
        using var response = await client.GetAsync("/Focus?itemId=" + factory.Store.OwnItem.Id);
        var html = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain(factory.Store.OwnItem.Id.ToString()).And.NotContain(factory.Store.ForeignItem.Id.ToString());
        html.Should().Contain("Let's Focus Together").And.Contain("Study progress unchanged");
        factory.Store.Writes.Should().Be(writes); factory.Store.Progress.Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("deleted")]
    public async Task InaccessibleSelectedStudyItem_Returns404(string kind)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var id = kind == "foreign" ? factory.Store.ForeignItem.Id : kind == "missing" ? Guid.NewGuid() : factory.Store.OwnItem.Id;
        if (kind == "deleted") factory.Store.OwnItem.SoftDelete();
        using var response = await client.GetAsync("/Focus?itemId=" + id);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound); factory.Store.Progress.Should().BeEmpty();
    }

    [Fact]
    public async Task OwnedTaskTitle_IsHtmlEncodedInFocusMarkup()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        factory.Store.OwnItem.SetTitle("<img src=x onerror=alert(1)>");
        using var response = await client.GetAsync("/Focus?itemId=" + factory.Store.OwnItem.Id);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("&lt;img").And.NotContain("<img src=x onerror");
    }

    [Fact]
    public async Task SessionCompletion_AcceptsOptionalBlankNotesAndRealMinutes()
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await SignIn(client);
        var token = await Token(client, "/Sessions");
        using var response = await client.PostAsync("/Sessions/CompleteSession", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SessionId"] = factory.Store.OwnSession.Id.ToString(), ["Results[0].StudyItemId"] = factory.Store.OwnItem.Id.ToString(),
            ["Results[0].MinutesSpent"] = "17", ["Results[0].Notes"] = "", ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Store.Progress.Should().ContainSingle().Which.MinutesSpent.Should().Be(17);
        factory.Store.Progress.Single().Notes.Should().BeEmpty();
    }

    private static async Task SignIn(HttpClient client)
    {
        var token = await Token(client, "/Auth/Login");
        using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword,
            ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
    private static async Task<string> Token(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); response.StatusCode.Should().Be(HttpStatusCode.OK);
        var match = Regex.Match(await response.Content.ReadAsStringAsync(), "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue(); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
