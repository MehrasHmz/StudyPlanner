using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Administration;
using StudyPlanner.Web.Security;
using Microsoft.AspNetCore.Identity;
using StudyPlanner.Infrastructure.Authentication;
using StudyPlanner.Infrastructure.Repositories;
using StudyPlanner.Application.Commands.Users;

namespace StudyPlanner.UnitTests;

public class PostgresReleaseAcceptanceTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresReleaseAcceptanceTests(PostgresTestDatabase database) => _database = database;

    [PostgresFact]
    public async Task SeedLogin_UpgradesLegacyHashButDisablingPreventsFurtherLoginWithoutRemovingHistory()
    {
        await using var context = _database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"IsActive\" = true WHERE \"Id\" = {DemoAccountRelease.HistoricalId}");
        var user = (await new DemoAccountRelease(context).InspectAsync(DemoAccountRelease.HistoricalId))!;
        var login = new LoginHandler(new UserRepository(context), new PasswordHashService(new PasswordHasher<User>()));
        (await login.Handle(new(user.Email, SecurityTestStore.OwnerPassword), default)).User.Id.Should().Be(user.Id);
        var upgraded = (await new UserRepository(context).GetByIdAsync(user.Id))!.PasswordHash; upgraded.Should().StartWith("Identity:");
        await new DemoAccountRelease(context).DisableAsync(user.Id, "test", "synthetic verification", true);
        (await Record.ExceptionAsync(() => login.Handle(new(user.Email, SecurityTestStore.OwnerPassword), default))).Should().BeOfType<UnauthorizedAccessException>();
        (await new UserRepository(context).GetByIdAsync(user.Id))!.PasswordHash.Should().Be(upgraded);
    }

    [PostgresFact]
    public async Task ActiveHistoricalDemo_BlocksReleaseAndExplicitDisablePreservesHashReferencesAndOtherUsers()
    {
        await using var context = _database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"IsActive\" = true WHERE \"Id\" = {DemoAccountRelease.HistoricalId}");
        var tool = new DemoAccountRelease(context);
        var demo = (await tool.InspectAsync(DemoAccountRelease.HistoricalId))!;
        var hash = demo.PasswordHash;
        var items = await context.StudyItems.IgnoreQueryFilters().AsNoTracking().Select(i => new { i.Id, i.UserId, i.CategoryId }).ToListAsync();
        var other = new User("Untouched", Guid.NewGuid() + "@example.test", "private-synthetic-hash"); context.Users.Add(other); await context.SaveChangesAsync();
        (await Record.ExceptionAsync(() => tool.VerifyAsync())).Should().BeOfType<InvalidOperationException>();
        (await Record.ExceptionAsync(() => tool.DisableAsync(demo.Id, "test", "reason", false))).Should().BeOfType<ArgumentException>();
        (await tool.DisableAsync(demo.Id, "test-operator", "approved synthetic release", true)).Should().BeTrue();
        (await tool.DisableAsync(demo.Id, "test-operator", "repeat", true)).Should().BeFalse();
        await tool.VerifyAsync();
        var after = (await tool.InspectAsync(demo.Id))!;
        after.IsActive.Should().BeFalse(); after.Role.Should().Be(UserRole.Regular); after.PasswordHash.Should().Be(hash);
        (await context.StudyItems.IgnoreQueryFilters().AsNoTracking().Select(i => new { i.Id, i.UserId, i.CategoryId }).ToListAsync()).Should().BeEquivalentTo(items);
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == other.Id)).IsActive.Should().BeTrue();
    }

    [PostgresTheory]
    [InlineData("target")]
    [InlineData("operator")]
    [InlineData("reason")]
    public async Task InvalidReleaseOperation_DoesNotChangeTargetOrArbitraryAccount(string kind)
    {
        await using var context = _database.CreateContext(); var tool = new DemoAccountRelease(context);
        var before = await tool.InspectAsync(DemoAccountRelease.HistoricalId);
        var error = await Record.ExceptionAsync(() => tool.DisableAsync(kind == "target" ? Guid.NewGuid() : DemoAccountRelease.HistoricalId,
            kind == "operator" ? "" : "test", kind == "reason" ? "bad\nreason" : "approved", true));
        error.Should().BeOfType<ArgumentException>();
        (await tool.InspectAsync(DemoAccountRelease.HistoricalId))!.IsActive.Should().Be(before!.IsActive);
    }

    [PostgresFact]
    public async Task ReleaseCli_RequiresExplicitTargetAndConfirmationAndNeverPrintsCredentials()
    {
        var services = new ServiceCollection(); services.AddScoped(_ => _database.CreateContext()); using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        var args = new[] { "--disable-demo-account", "--user-id", DemoAccountRelease.HistoricalId.ToString(), "--operator", "test", "--reason", "approved synthetic test" };
        (await DemoAccountReleaseCommand.RunAsync(args, provider, output)).Should().Be(0);
        output.ToString().Should().Contain("Inspection only").And.NotContain("Password=").And.NotContain("PasswordHash").And.NotContain("Host=");
        (await DemoAccountReleaseCommand.RunAsync(args.Concat(new[] { "--user-id", Guid.NewGuid().ToString(), "--confirm" }).ToArray(), provider, output)).Should().Be(2);
        (await DemoAccountReleaseCommand.RunAsync(args.Append("--confirm").ToArray(), provider, output)).Should().Be(0);
        await using var verify = _database.CreateContext(); await new DemoAccountRelease(verify).VerifyAsync();
    }
}
