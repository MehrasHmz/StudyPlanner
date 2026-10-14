using System.Net;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StudyPlanner.Application.Commands.Users;
using StudyPlanner.Domain.Entities;

namespace StudyPlanner.UnitTests;

public class ReleaseSecurityTests
{
    [Fact]
    public async Task InactiveAccount_CannotLoginOrUpgradePasswordAndUsesSameGenericFailure()
    {
        using var factory = new SecurityWebApplicationFactory();
        typeof(User).GetProperty(nameof(User.IsActive))!.SetValue(factory.Store.Owner, false);
        var original = factory.Store.Owner.PasswordHash;
        using var scope = factory.Services.CreateScope();
        (await Record.ExceptionAsync(() => scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand(SecurityTestStore.OwnerEmail, SecurityTestStore.OwnerPassword))))
            .Should().BeOfType<UnauthorizedAccessException>().Which.Message.Should().Be("Invalid credentials.");
        factory.Store.Owner.PasswordHash.Should().Be(original); factory.Store.Writes.Should().Be(0);
        using var client = factory.NewClient(); var token = await ProgressHistoryTests.Token(client, "/Auth/Login");
        using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword, ["__RequestVerificationToken"] = token }));
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid credentials.").And.NotContain("inactive");
        (await client.GetAsync("/Sessions/History")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingCookie_IsRevokedForDisabledOrMissingAccountWithoutWrites(bool removed)
    {
        using var factory = new SecurityWebApplicationFactory(); using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        var writes = factory.Store.Writes;
        if (removed) factory.Store.Users.Remove(factory.Store.Owner.Id);
        else typeof(User).GetProperty(nameof(User.IsActive))!.SetValue(factory.Store.Owner, false);
        using var response = await client.GetAsync("/Sessions/History"); response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Auth/Login"); factory.Store.Writes.Should().Be(writes);
    }

    [Fact]
    public async Task LoginLimiter_RejectsBeforeCredentialWorkAndDoesNotTrustSpoofedForwardedIp()
    {
        using var factory = new SecurityWebApplicationFactory { Settings = new Dictionary<string, string?> { ["Security:LoginAttemptsPerMinute"] = "2" } };
        using var client = factory.NewClient(); var token = await ProgressHistoryTests.Token(client, "/Auth/Login");
        for (var i = 0; i < 3; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For"); client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2." + i);
            using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = "wrong-password", ["__RequestVerificationToken"] = token }));
            response.StatusCode.Should().Be(i < 2 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests);
            if (i == 2) response.Headers.RetryAfter.Should().NotBeNull();
        }
        factory.Store.CredentialLookups.Should().Be(2); factory.Store.Writes.Should().Be(0);
        (await client.GetAsync("/Auth/Login")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RegistrationLimiter_BlocksAdditionalWritesWithoutLockingAnAccount()
    {
        using var factory = new SecurityWebApplicationFactory { Settings = new Dictionary<string, string?> { ["Security:RegistrationAttemptsPerMinute"] = "1" } };
        using var client = factory.NewClient(); var token = await ProgressHistoryTests.Token(client, "/Auth/Register");
        var fields = new Dictionary<string, string> { ["FullName"] = "Synthetic user", ["Email"] = "rate@example.test", ["Password"] = "password123", ["__RequestVerificationToken"] = token };
        using var first = await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(fields)); first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var writes = factory.Store.Writes;
        fields["Email"] = "another@example.test";
        using var second = await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(fields)); second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        factory.Store.Writes.Should().Be(writes); factory.Store.Users.Values.Should().NotContain(u => u.Email == "another@example.test");
    }

    [Fact]
    public async Task CookieOptions_SetHttpOnlySameSiteAndConfiguredSecureFlag()
    {
        using var factory = new SecurityWebApplicationFactory { Settings = new Dictionary<string, string?> { ["Security:RequireSecureCookies"] = "true" } };
        using var client = factory.NewClient(); var token = await ProgressHistoryTests.Token(client, "/Auth/Login");
        using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = SecurityTestStore.OwnerEmail, ["Password"] = SecurityTestStore.OwnerPassword, ["__RequestVerificationToken"] = token }));
        response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(".AspNetCore.Cookies=")).ToLowerInvariant()
            .Should().Contain("secure").And.Contain("httponly").And.Contain("samesite=lax");
    }

    [Fact]
    public async Task ProductionExceptionResponse_HidesInternalDetailsAndPublicHealthIgnoresStaleCookie()
    {
        using var factory = new SecurityWebApplicationFactory { EnvironmentName = "Production" }; using var client = factory.NewClient();
        await ProgressHistoryTests.SignIn(client);
        factory.Store.ItemRepository.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("PRIVATE-ERROR-PASSWORD-TOKEN"));
        using var response = await client.GetAsync("/"); response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("An unexpected error occurred.").And.NotContain("PRIVATE-ERROR").And.NotContain("Exception");
        factory.Store.UserRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Database offline"));
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ReferencedCategoryDeletion_IsBlockedWithoutChangingSharedOwnershipPolicy()
    {
        using var factory = new SecurityWebApplicationFactory();
        factory.Store.Owner.SetRole(StudyPlanner.Domain.Enums.UserRole.Administrator);
        factory.Store.CategoryRepository.Setup(r => r.HasStudyItemsAsync(factory.Store.Category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var client = factory.NewClient(); await ProgressHistoryTests.SignIn(client);
        var token = await ProgressHistoryTests.Token(client, "/Categories"); var writes = factory.Store.Writes;
        using var response = await client.PostAsync("/Categories/Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["id"] = factory.Store.Category.Id.ToString(), ["__RequestVerificationToken"] = token }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Store.Category.IsDeleted.Should().BeFalse(); factory.Store.Writes.Should().Be(writes);
        (await client.GetStringAsync("/Categories")).Should().Contain("referenced by study items");
    }
}
