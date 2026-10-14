using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.UnitTests;

public class AntiforgeryIntegrationTests
{
    private static readonly string[] Actions =
    {
        "/Auth/Login", "/Auth/Register", "/Auth/Logout",
        "/StudyItems/Create", "/StudyItems/Edit", "/StudyItems/Delete", "/StudyItems/ToggleComplete",
        "/Categories/Create", "/Categories/Edit", "/Categories/Delete",
        "/Sessions/Plan", "/Sessions/CompleteSession"
    };

    public static IEnumerable<object[]> InvalidRequests => Actions.SelectMany(action =>
        new[] { "missing", "invalid", "mismatched-cookie" }.Select(kind => new object[] { action, kind }));
    public static IEnumerable<object[]> ProtectedActions => Actions.Select(action => new object[] { action });

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task UnsafePost_RejectsMissingInvalidOrMismatchedTokenBeforeMutations(string action, string kind)
    {
        using var factory = new SecurityWebApplicationFactory();
        if (action.StartsWith("/Categories/")) factory.Store.Owner.SetRole(UserRole.Administrator);
        using var client = factory.NewClient();
        if (RequiresAuthentication(action)) await SignIn(client);
        var token = await GetToken(client, FormPage(action, factory.Store));
        if (kind == "mismatched-cookie")
        {
            using var otherClient = factory.NewClient();
            if (RequiresAuthentication(action)) await SignIn(otherClient);
            token = await GetToken(otherClient, FormPage(action, factory.Store));
        }
        if (kind == "invalid") token = "invalid-antiforgery-token";
        var writes = factory.Store.Writes;
        var lookups = factory.Store.CredentialLookups;

        using var response = await Post(client, action, Fields(action, factory.Store), kind == "missing" ? null : token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Store.Writes.Should().Be(writes);
        factory.Store.CredentialLookups.Should().Be(lookups);
        factory.Store.Progress.Should().BeEmpty();
        factory.Store.OwnSession.Status.Should().Be(SessionStatus.Planned);
        factory.Store.OwnItem.IsCompleted.Should().BeFalse();
        factory.Store.OwnItem.IsDeleted.Should().BeFalse();
        if (RequiresAuthentication(action))
        {
            using var stillAuthenticated = await client.GetAsync("/Sessions");
            stillAuthenticated.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Theory]
    [MemberData(nameof(ProtectedActions))]
    public async Task ValidRazorToken_AllowsIntendedActionAndPreservesWorkflow(string action)
    {
        using var factory = new SecurityWebApplicationFactory();
        if (action.StartsWith("/Categories/")) factory.Store.Owner.SetRole(UserRole.Administrator);
        using var client = factory.NewClient();
        if (RequiresAuthentication(action)) await SignIn(client);
        var token = await GetToken(client, FormPage(action, factory.Store));
        var writes = factory.Store.Writes;

        using var response = await Post(client, action, Fields(action, factory.Store), token);

        response.StatusCode.Should().Be(action == "/Sessions/Plan" ? HttpStatusCode.OK : HttpStatusCode.Redirect);
        if (action == "/Auth/Logout")
        {
            using var afterLogout = await client.GetAsync("/Sessions");
            afterLogout.StatusCode.Should().Be(HttpStatusCode.Redirect);
            afterLogout.Headers.Location!.OriginalString.Should().Contain("/Auth/Login");
        }
        else
        {
            factory.Store.Writes.Should().BeGreaterThan(writes);
            if (action == "/Auth/Login")
            {
                using var afterLogin = await client.GetAsync("/Sessions");
                afterLogin.StatusCode.Should().Be(HttpStatusCode.OK);
                factory.Store.Owner.PasswordHash.Should().StartWith("Identity:");
            }
            if (action == "/Sessions/CompleteSession")
            {
                factory.Store.OwnSession.Status.Should().Be(SessionStatus.Completed);
                factory.Store.Progress.Should().ContainSingle();
            }
            if (action == "/Sessions/Plan")
            {
                var html = await response.Content.ReadAsStringAsync();
                html.Should().Contain("/Sessions/CompleteSession");
                AssertPostFormsHaveOneToken(html);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProtectedActions))]
    public async Task RenderedPostForms_ContainExactlyOneFrameworkToken(string action)
    {
        using var factory = new SecurityWebApplicationFactory();
        if (action.StartsWith("/Categories/")) factory.Store.Owner.SetRole(UserRole.Administrator);
        using var client = factory.NewClient();
        if (RequiresAuthentication(action)) await SignIn(client);

        using var response = await client.GetAsync(FormPage(action, factory.Store));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertPostFormsHaveOneToken(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenIssuedBeforeLogin_IsRejectedAfterIdentityChanges()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        var anonymousToken = await GetToken(client, "/Auth/Login");
        using var login = await Post(client, "/Auth/Login", Fields("/Auth/Login", factory.Store), anonymousToken);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        using var authenticated = await client.GetAsync("/Sessions");
        authenticated.StatusCode.Should().Be(HttpStatusCode.OK);
        var writes = factory.Store.Writes;

        using var response = await Post(client, "/StudyItems/Create", Fields("/StudyItems/Create", factory.Store), anonymousToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Store.Writes.Should().Be(writes);
    }

    [Fact]
    public async Task LogoutGet_DoesNotSignOutAuthenticatedUser()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        await SignIn(client);

        using var logout = await client.GetAsync("/Auth/Logout");
        using var protectedPage = await client.GetAsync("/Sessions");

        logout.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NewlyRegisteredUser_CanLoginWithModernPasswordAndUseProtectedPage()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        var registration = Fields("/Auth/Register", factory.Store);
        var token = await GetToken(client, "/Auth/Register");
        using var registered = await Post(client, "/Auth/Register", registration, token);
        registered.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var newUser = factory.Store.Users.Values.Single(u => u.Email == registration["Email"]);
        newUser.PasswordHash.Should().StartWith("Identity:");
        var writes = factory.Store.Writes;
        var loginToken = await GetToken(client, "/Auth/Login");

        using var loggedIn = await Post(client, "/Auth/Login", new()
        {
            ["Email"] = registration["Email"], ["Password"] = registration["Password"]
        }, loginToken);
        using var protectedPage = await client.GetAsync("/Sessions");

        loggedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Store.Writes.Should().Be(writes);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("toggle")]
    [InlineData("foreign-session")]
    [InlineData("foreign-item")]
    [InlineData("duplicate")]
    public async Task ValidCsrfToken_DoesNotBypassPhase1Authorization(string attack)
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        await SignIn(client);
        var action = attack switch
        {
            "edit" => "/StudyItems/Edit", "delete" => "/StudyItems/Delete",
            "toggle" => "/StudyItems/ToggleComplete", _ => "/Sessions/CompleteSession"
        };
        var fields = Fields(action, factory.Store);
        if (attack is "edit" or "delete" or "toggle")
        {
            fields["Id"] = factory.Store.ForeignItem.Id.ToString();
            fields["UserId"] = factory.Store.ForeignItem.UserId.ToString();
        }
        if (attack == "foreign-session") fields["SessionId"] = factory.Store.ForeignSession.Id.ToString();
        if (attack == "foreign-item") fields["Results[0].StudyItemId"] = factory.Store.ForeignItem.Id.ToString();
        if (attack == "duplicate")
        {
            fields["Results[1].StudyItemId"] = fields["Results[0].StudyItemId"];
            fields["Results[1].MinutesSpent"] = "20";
        }
        var token = await GetToken(client, "/Sessions");
        var writes = factory.Store.Writes;

        using var response = await Post(client, action, fields, token);

        response.StatusCode.Should().Be(attack is "foreign-item" or "duplicate" ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound);
        factory.Store.Writes.Should().Be(writes);
        factory.Store.ForeignItem.IsCompleted.Should().BeFalse();
        factory.Store.ForeignItem.IsDeleted.Should().BeFalse();
        factory.Store.ForeignItem.Title.Should().Be("Test item");
        factory.Store.OwnSession.Status.Should().Be(SessionStatus.Planned);
        factory.Store.ForeignSession.Status.Should().Be(SessionStatus.Planned);
        factory.Store.Progress.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadOnlyCookieApi_RemainsAccessibleWithoutCsrfToken()
    {
        using var factory = new SecurityWebApplicationFactory();
        using var client = factory.NewClient();
        await SignIn(client);

        using var response = await client.GetAsync("/api/categories/list");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static bool RequiresAuthentication(string action) => action != "/Auth/Login" && action != "/Auth/Register";
    private static string FormPage(string action, SecurityTestStore store) => action switch
    {
        "/StudyItems/Edit" => "/StudyItems/Edit/" + store.OwnItem.Id,
        "/StudyItems/Delete" or "/StudyItems/ToggleComplete" => "/StudyItems",
        "/Categories/Edit" => "/Categories/Edit/" + store.Category.Id,
        "/Categories/Delete" => "/Categories",
        "/Auth/Logout" or "/Sessions/Plan" or "/Sessions/CompleteSession" => "/Sessions",
        _ => action
    };

    private static Dictionary<string, string> Fields(string action, SecurityTestStore store) => action switch
    {
        "/Auth/Login" => new() { ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword },
        "/Auth/Register" => new() { ["FullName"] = "New Test User", ["Email"] = "new@example.test", ["Password"] = "New-test-password!" },
        "/StudyItems/Create" or "/StudyItems/Edit" => new()
        {
            ["Id"] = store.OwnItem.Id.ToString(), ["Title"] = "Updated item", ["Description"] = "Description",
            ["Type"] = "Programming", ["Difficulty"] = "Beginner", ["Priority"] = "3",
            ["EstimatedDurationMinutes"] = "20", ["CategoryId"] = store.Category.Id.ToString(),
            ["UserId"] = store.ForeignItem.UserId.ToString()
        },
        "/StudyItems/Delete" or "/StudyItems/ToggleComplete" => new() { ["Id"] = store.OwnItem.Id.ToString() },
        "/Categories/Create" or "/Categories/Edit" => new()
        {
            ["Id"] = store.Category.Id.ToString(), ["Name"] = "Updated category", ["Description"] = "Description"
        },
        "/Categories/Delete" => new() { ["Id"] = store.Category.Id.ToString() },
        "/Sessions/Plan" => new() { ["AvailableMinutes"] = "45" },
        "/Sessions/CompleteSession" => new()
        {
            ["SessionId"] = store.OwnSession.Id.ToString(), ["Results[0].StudyItemId"] = store.OwnItem.Id.ToString(),
            ["Results[0].MinutesSpent"] = "20", ["Results[0].Notes"] = "Reviewed"
        },
        _ => new()
    };

    private static async Task SignIn(HttpClient client)
    {
        var token = await GetToken(client, "/Auth/Login");
        using var response = await Post(client, "/Auth/Login", new()
        {
            ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword
        }, token);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        using var authenticated = await client.GetAsync("/Sessions");
        authenticated.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<string> GetToken(HttpClient client, string page)
    {
        using var response = await client.GetAsync(page);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue("the real Razor form must emit a framework antiforgery token");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string action, Dictionary<string, string> fields, string? token)
    {
        if (token != null) fields["__RequestVerificationToken"] = token;
        return client.PostAsync(action, new FormUrlEncodedContent(fields));
    }

    private static void AssertPostFormsHaveOneToken(string html)
    {
        var forms = Regex.Matches(html, "<form\\b[^>]*method=\"post\"[^>]*>(.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        forms.Count.Should().BeGreaterThan(0);
        foreach (Match form in forms)
            Regex.Matches(form.Value, "name=\"__RequestVerificationToken\"").Count.Should().Be(1);
    }
}
