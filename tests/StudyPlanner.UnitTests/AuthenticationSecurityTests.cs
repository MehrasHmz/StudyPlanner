using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using StudyPlanner.Application.Authentication;
using StudyPlanner.Application.Commands.Users;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Infrastructure;
using StudyPlanner.Infrastructure.Authentication;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.UnitTests;

public class AuthenticationSecurityTests : IDisposable
{
    private const string Password = "Test-password-42!";
    private readonly ServiceProvider _services;
    private readonly IPasswordHashService _passwords;
    private readonly Mock<IUserRepository> _users = new();

    public AuthenticationSecurityTests()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        _services = services.BuildServiceProvider();
        _passwords = _services.GetRequiredService<IPasswordHashService>();
    }

    [Fact]
    public async Task Registration_StoresModernVersionedSaltedHash()
    {
        User? stored = null;
        _users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => stored = user)
            .ReturnsAsync((User user, CancellationToken _) => user);

        var result = await new RegisterHandler(_users.Object, _passwords)
            .Handle(new RegisterCommand("Test Owner", "owner@example.test", Password), default);

        stored.Should().NotBeNull();
        result.User.Id.Should().Be(stored!.Id);
        stored.PasswordHash.Should().StartWith("Identity:").And.NotContain(Password);
        stored.PasswordHash.Length.Should().BeLessThanOrEqualTo(500);
        var payload = Convert.FromBase64String(stored.PasswordHash[9..]);
        payload[0].Should().Be(1);
        BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(1, 4)).Should().Be(2);
        BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(5, 4)).Should().Be(220_000);
        BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(9, 4)).Should().Be(16);
        _passwords.VerifyPassword(stored, Password).Should().Be(PasswordVerificationOutcome.Success);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SamePasswords_ReceiveDifferentSaltsAndHashes()
    {
        var first = ModernUser();
        var second = ModernUser();

        first.PasswordHash.Should().NotBe(second.PasswordHash);
        Convert.FromBase64String(first.PasswordHash[9..]).Skip(13).Take(16)
            .Should().NotEqual(Convert.FromBase64String(second.PasswordHash[9..]).Skip(13).Take(16));
        _passwords.VerifyPassword(first, Password).Should().Be(PasswordVerificationOutcome.Success);
        _passwords.VerifyPassword(second, Password).Should().Be(PasswordVerificationOutcome.Success);
    }

    [Fact]
    public async Task CorrectModernPassword_AuthenticatesWithoutUnnecessaryWrites()
    {
        var user = ModernUser();
        SetupUser(user);
        var original = user.PasswordHash;

        var result = await Login(Password);

        result.User.Id.Should().Be(user.Id);
        user.PasswordHash.Should().Be(original);
        AssertNoWrites();
    }

    [Fact]
    public async Task IncorrectModernPassword_DoesNotChangeCredentials()
    {
        var user = ModernUser();
        SetupUser(user);
        var original = user.PasswordHash;

        var act = () => Login("Wrong-password");

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Invalid credentials.");
        user.PasswordHash.Should().Be(original);
        user.UpdatedAt.Should().BeNull();
        AssertNoWrites();
    }

    [Fact]
    public async Task SuccessfulLegacyLogin_UpgradesStorageAndSubsequentLoginNeedsNoWrite()
    {
        var user = UserWithHash(LegacyHash(Password));
        SetupUser(user);

        var first = await Login(Password);

        first.User.Id.Should().Be(user.Id);
        user.PasswordHash.Should().StartWith("Identity:");
        user.UpdatedAt.Should().NotBeNull();
        _passwords.VerifyPassword(user, Password).Should().Be(PasswordVerificationOutcome.Success);
        _users.Verify(r => r.Update(user), Times.Once);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var upgraded = user.PasswordHash;
        var second = await Login(Password);
        second.User.Id.Should().Be(user.Id);
        user.PasswordHash.Should().Be(upgraded);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedLegacyLogin_PreservesExistingHashWithoutWrites()
    {
        var original = LegacyHash(Password);
        var user = UserWithHash(original);
        SetupUser(user);

        var act = () => Login("Wrong-password");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        user.PasswordHash.Should().Be(original);
        user.UpdatedAt.Should().BeNull();
        AssertNoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModernParametersRequiringUpgrade_RehashOnlyOnSuccessfulLogin(bool incorrectPassword)
    {
        var user = UserWithHash("");
        var oldHasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 10_000 }));
        user.SetPasswordHash("Identity:" + oldHasher.HashPassword(user, Password));
        var original = user.PasswordHash;
        SetupUser(user);

        if (incorrectPassword)
        {
            var act = () => Login("Wrong-password");
            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            user.PasswordHash.Should().Be(original);
            AssertNoWrites();
        }
        else
        {
            await Login(Password);
            user.PasswordHash.Should().NotBe(original);
            _passwords.VerifyPassword(user, Password).Should().Be(PasswordVerificationOutcome.Success);
            _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task SeededDemoCredentials_UseActualModelSeedAndUpgradeThroughLogin()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=StudyPlannerSecurityTests").Options);
        var seed = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(User))!.GetSeedData().Single();
        var user = new User((Guid)seed["Id"]!, (string)seed["FullName"]!, (string)seed["Email"]!, (string)seed["PasswordHash"]!);
        SetupUser(user);

        var result = await new LoginHandler(_users.Object, _passwords)
            .Handle(new LoginCommand(user.Email, "password123"), default);

        result.User.Id.Should().Be(user.Id);
        user.PasswordHash.Should().StartWith("Identity:");
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("bcrypt:unsupported")]
    [InlineData("Identity:")]
    [InlineData("Identity:not-base64")]
    [InlineData("Identity:Ag==")]
    [InlineData("Identity:AQ==")]
    [InlineData("IdentityV9:unsupported")]
    public async Task MalformedOrUnsupportedHash_FailsWithoutStorageChanges(string hash)
    {
        var user = UserWithHash(hash);
        SetupUser(user);

        var act = () => Login(Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        user.PasswordHash.Should().Be(hash);
        AssertNoWrites();
    }

    [Fact]
    public async Task ModernPrefixWithValidLegacyPayload_NeverFallsBackToLegacyVerification()
    {
        var user = UserWithHash("Identity:" + LegacyHash(Password));
        SetupUser(user);

        var act = () => Login(Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        AssertNoWrites();
    }

    [Theory]
    [InlineData(1, uint.MaxValue)]
    [InlineData(5, uint.MaxValue)]
    [InlineData(9, uint.MaxValue)]
    [InlineData(5, 0u)]
    public async Task MalformedIdentityMetadata_IsRejectedSafely(int offset, uint value)
    {
        var user = ModernUser();
        var payload = Convert.FromBase64String(user.PasswordHash[9..]);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset, 4), value);
        user.SetPasswordHash("Identity:" + Convert.ToBase64String(payload));
        SetupUser(user);

        var act = () => Login(Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        AssertNoWrites();
    }

    [Fact]
    public async Task MissingAccount_FailsWithSameGenericCredentialError()
    {
        var act = () => Login(Password);
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Invalid credentials.");
        AssertNoWrites();
    }

    [Fact]
    public async Task SupportedIdentityV2Hash_IsUpgradedOnSuccessfulLogin()
    {
        var user = UserWithHash("");
        var oldHasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
        }));
        user.SetPasswordHash("Identity:" + oldHasher.HashPassword(user, Password));
        SetupUser(user);

        await Login(Password);

        Convert.FromBase64String(user.PasswordHash[9..])[0].Should().Be(1);
        _passwords.VerifyPassword(user, Password).Should().Be(PasswordVerificationOutcome.Success);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LegacyDigestStartingWithIdentityMarker_IsStillMigrated(int marker)
    {
        var candidate = Enumerable.Range(0, 10_000).Select(i => "Test-password-" + i)
            .First(password => Convert.FromBase64String(LegacyHash(password))[0] == marker);
        var user = UserWithHash(LegacyHash(candidate));
        SetupUser(user);

        await Login(candidate);

        user.PasswordHash.Should().StartWith("Identity:");
        _passwords.VerifyPassword(user, candidate).Should().Be(PasswordVerificationOutcome.Success);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnmarkedIdentityPayload_IsNotMistakenForLegacy()
    {
        var user = ModernUser();
        user.SetPasswordHash(user.PasswordHash[9..]);
        SetupUser(user);

        var act = () => Login(Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        AssertNoWrites();
    }

    private User ModernUser()
    {
        var user = UserWithHash("");
        user.SetPasswordHash(_passwords.HashPassword(user, Password));
        return user;
    }

    private static User UserWithHash(string hash) => new("Test Owner", "owner@example.test", hash);
    private void SetupUser(User user) => _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
    private Task<StudyPlanner.Application.DTOs.AuthDto> Login(string password) =>
        new LoginHandler(_users.Object, _passwords).Handle(new LoginCommand("owner@example.test", password), default);
    private void AssertNoWrites()
    {
        _users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Fixture creation only; production compatibility code has no legacy creation API.
    private static string LegacyHash(string password) => Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes(password + "StudyPlannerSalt2024")));

    public void Dispose() => _services.Dispose();
}
