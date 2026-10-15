using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StudyPlanner.Application.Authorization;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Infrastructure.Administration;
using StudyPlanner.Infrastructure.Data;
using StudyPlanner.Infrastructure.Repositories;
using StudyPlanner.Web.Security;

namespace StudyPlanner.UnitTests;

public class PostgresAdministratorTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database;
    public PostgresAdministratorTests(PostgresTestDatabase database) => _database = database;
    private async Task<User> Seed()
    {
        var user = new User("Operator target", Guid.NewGuid() + "@example.test", "ijB+qBaQa257yaJeoHCrzR/fBHVJ4IHm+mpkiCk7GY0=");
        await using var context = _database.CreateContext(); context.Users.Add(user); await context.SaveChangesAsync(); return user;
    }

    [PostgresFact]
    public async Task FailedAuditWrite_RollsBackRoleChangeAndConcurrentRepeatedGrantsCreateOneAudit()
    {
        var user = await Seed();
        await using (var failing = _database.CreateContext(new RejectAuditInterceptor()))
            (await Record.ExceptionAsync(() => new RoleProvisioner(failing).ApplyAsync(user.Id, UserRole.Administrator, "test", "failure", true)))
                .Should().BeOfType<InvalidOperationException>();
        await using (var verify = _database.CreateContext())
        {
            (await verify.Users.SingleAsync(u => u.Id == user.Id)).Role.Should().Be(UserRole.Regular);
            (await verify.Set<RoleChangeAudit>().CountAsync(a => a.UserId == user.Id)).Should().Be(0);
        }
        async Task<bool> Grant() { await using var context = _database.CreateContext(); return await new RoleProvisioner(context).ApplyAsync(user.Id, UserRole.Administrator, "test", "concurrent", true); }
        (await Task.WhenAll(Grant(), Grant())).Should().BeEquivalentTo(new[] { true, false });
        await using var final = _database.CreateContext();
        (await final.Set<RoleChangeAudit>().CountAsync(a => a.UserId == user.Id)).Should().Be(1);
    }

    private sealed class RejectAuditInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<RoleChangeAudit>().Any()) throw new InvalidOperationException("Synthetic audit failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [PostgresFact]
    public async Task Provisioning_RequiresConfirmationTargetsOnlyExactUserAndRecordsAtomicIdempotentGrantAndRevoke()
    {
        var user = await Seed(); var other = await Seed();
        await using var context = _database.CreateContext(); var tool = new RoleProvisioner(context);
        (await tool.InspectAsync(user.Id, UserRole.Administrator)).CurrentRole.Should().Be(UserRole.Regular);
        (await Record.ExceptionAsync(() => tool.ApplyAsync(user.Id, UserRole.Administrator, "test-operator", "test grant", false))).Should().BeOfType<InvalidOperationException>();
        (await context.Set<RoleChangeAudit>().CountAsync(a => a.UserId == user.Id)).Should().Be(0);
        (await tool.ApplyAsync(user.Id, UserRole.Administrator, "test-operator", "test grant", true)).Should().BeTrue();
        (await tool.ApplyAsync(user.Id, UserRole.Administrator, "test-operator", "repeat", true)).Should().BeFalse();
        (await tool.ApplyAsync(user.Id, UserRole.Regular, "test-operator", "test revoke", true)).Should().BeTrue();
        (await tool.ApplyAsync(user.Id, UserRole.Regular, "test-operator", "repeat", true)).Should().BeFalse();
        var records = await context.Set<RoleChangeAudit>().Where(a => a.UserId == user.Id).OrderBy(a => a.ChangedAt).ToListAsync();
        records.Should().HaveCount(2); records[0].PreviousRole.Should().Be(UserRole.Regular); records[0].NewRole.Should().Be(UserRole.Administrator);
        records[1].NewRole.Should().Be(UserRole.Regular); records.Should().OnlyContain(a => a.Operator == "test-operator");
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == other.Id)).Role.Should().Be(UserRole.Regular);
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).PasswordHash.Should().Be(user.PasswordHash);
    }

    [PostgresTheory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("operator")]
    public async Task Provisioning_InvalidTargetOrAuditMetadataIsRejectedWithoutRoleOrAuditWrites(string invalid)
    {
        var user = await Seed(); await using var context = _database.CreateContext();
        if (invalid == "inactive") await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"IsActive\" = false WHERE \"Id\" = {user.Id}");
        var id = invalid == "missing" ? Guid.NewGuid() : invalid == "empty" ? Guid.Empty : user.Id;
        var role = invalid == "unknown" ? (UserRole)999 : UserRole.Administrator;
        var error = await Record.ExceptionAsync(() => new RoleProvisioner(context).ApplyAsync(id, role, invalid == "operator" ? "" : "test-operator", "reason", true));
        error.Should().NotBeNull();
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Role.Should().Be(UserRole.Regular);
        (await context.Set<RoleChangeAudit>().CountAsync(a => a.UserId == user.Id)).Should().Be(0);
    }

    [PostgresFact]
    public async Task Database_RejectsUnknownRoleValueAndOrdinaryAccountUpdateCannotUndoRevocationOrDeactivation()
    {
        var user = await Seed(); await using var stale = _database.CreateContext(); var repo = new UserRepository(stale);
        await using (var op = _database.CreateContext()) await new RoleProvisioner(op).ApplyAsync(user.Id, UserRole.Administrator, "test", "grant", true);
        var loaded = (await repo.GetByIdAsync(user.Id))!;
        await using (var op = _database.CreateContext())
        {
            await new RoleProvisioner(op).ApplyAsync(user.Id, UserRole.Regular, "test", "revoke", true);
            await op.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"IsActive\" = false WHERE \"Id\" = {user.Id}");
            (await Record.ExceptionAsync(() => op.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"Role\" = 999 WHERE \"Id\" = {user.Id}"))).Should().BeOfType<Npgsql.PostgresException>();
        }
        loaded.SetPasswordHash("updated-test-hash"); repo.Update(loaded); await repo.SaveChangesAsync();
        await using var verify = _database.CreateContext(); var actual = await verify.Users.SingleAsync(u => u.Id == user.Id);
        actual.Role.Should().Be(UserRole.Regular); actual.IsActive.Should().BeFalse(); actual.PasswordHash.Should().Be("updated-test-hash");
    }

    [PostgresFact]
    public async Task ActualHttpCookies_GrantAndRevokeRespectCurrentDatabasePermissionsAndPreserveSharedReferences()
    {
        var user = await Seed(); var other = await Seed();
        using var baseFactory = new SecurityWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>(); services.AddScoped(_ => _database.CreateContext());
            services.RemoveAll<IUserRepository>(); services.AddScoped<IUserRepository, UserRepository>();
            services.RemoveAll<ICategoryRepository>(); services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.RemoveAll<IStudyItemRepository>(); services.AddScoped<IStudyItemRepository, StudyItemRepository>();
            services.RemoveAll<IStudySessionRepository>(); services.AddScoped<IStudySessionRepository, StudySessionRepository>();
            services.RemoveAll<IProgressRecordRepository>(); services.AddScoped<IProgressRecordRepository, ProgressRecordRepository>();
        }));
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var loginToken = await ProgressHistoryTests.Token(client, "/Auth/Login");
        using var login = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = user.Email, ["Password"] = SecurityTestStore.OwnerPassword, ["__RequestVerificationToken"] = loginToken })); login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync("/Categories/Create")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var op = _database.CreateContext()) await new RoleProvisioner(op).ApplyAsync(user.Id, UserRole.Administrator, "test", "grant", true);
        var token = await ProgressHistoryTests.Token(client, "/Categories/Create"); var name = "HTTP category " + Guid.NewGuid();
        var fields = new Dictionary<string, string> { ["Name"] = name, ["Description"] = "Shared", ["IsShared"] = "true", ["__RequestVerificationToken"] = token };
        (await client.PostAsync("/Categories/Create", new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        Guid categoryId;
        await using (var context = _database.CreateContext())
        {
            categoryId = (await context.Categories.SingleAsync(c => c.Name == name)).Id;
            context.StudyItems.Add(new("Own item", "", StudyItemType.Other, DifficultyLevel.Beginner, 20, 3, categoryId, other.Id)); await context.SaveChangesAsync();
        }
        fields["Id"] = categoryId.ToString(); fields["__RequestVerificationToken"] = await ProgressHistoryTests.Token(client, "/Categories");
        (await client.PostAsync("/Categories/Delete", new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        await using (var op = _database.CreateContext()) await new RoleProvisioner(op).ApplyAsync(user.Id, UserRole.Regular, "test", "revoke", true);
        (await client.PostAsync("/Categories/Edit", new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var verify = _database.CreateContext();
        (await verify.Categories.SingleAsync(c => c.Id == categoryId)).IsDeleted.Should().BeFalse();
        (await new StudyItemRepository(verify).GetByUserIdAsync(other.Id)).Should().ContainSingle();
        (await verify.Users.SingleAsync(u => u.Id == other.Id)).Role.Should().Be(UserRole.Regular);
        await new RoleProvisioner(verify).ApplyAsync(user.Id, UserRole.Administrator, "test", "regrant", true);
        await verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"IsActive\" = false WHERE \"Id\" = {user.Id}");
        (await client.GetAsync("/Categories/Create")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [PostgresFact]
    public async Task Cli_InspectionGrantAndRevokeUseExactUuidRejectAmbiguityAndDoNotPrintSecrets()
    {
        var user = await Seed(); var services = new ServiceCollection(); services.AddScoped(_ => _database.CreateContext()); using var provider = services.BuildServiceProvider();
        var args = new[] { "--manage-user-role", "--user-id", user.Id.ToString(), "--role", "administrator", "--operator", "test", "--reason", "test grant" };
        using var output = new StringWriter();
        (await RoleProvisioningCommand.RunAsync(args, provider, output)).Should().Be(0);
        await using (var check = _database.CreateContext()) (await check.Users.SingleAsync(u => u.Id == user.Id)).Role.Should().Be(UserRole.Regular);
        (await RoleProvisioningCommand.RunAsync(args.Append("--confirm").ToArray(), provider, output)).Should().Be(0);
        (await RoleProvisioningCommand.RunAsync(args.Concat(new[] { "--user-id", user.Id.ToString(), "--confirm" }).ToArray(), provider, output)).Should().Be(2);
        args[4] = "regular";
        (await RoleProvisioningCommand.RunAsync(args.Append("--confirm").ToArray(), provider, output)).Should().Be(0);
        output.ToString().Should().Contain(user.Id.ToString()).And.NotContain(user.PasswordHash).And.NotContain("Host=").And.NotContain("Password=");
        await using var verify = _database.CreateContext(); (await verify.Users.SingleAsync(u => u.Id == user.Id)).Role.Should().Be(UserRole.Regular);
    }
}

public class PostgresRoleMigrationTests
{
    [PostgresFact]
    public async Task AdditiveMigration_PreservesPopulatedLegacyUsersCategoriesAndPasswordHashesWithSafeDefaults()
    {
        var database = new PostgresTestDatabase { InitialMigrationOnly = true }; await database.InitializeAsync();
        try
        {
            await using var context = database.CreateContext(); var id = Guid.NewGuid(); var email = id + "@example.test";
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\",\"FullName\",\"Email\",\"PasswordHash\",\"IsActive\",\"CreatedAt\") VALUES ({id}, 'Legacy', {email}, 'unchanged-test-hash', true, now())");
            var categories = await context.Categories.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync();
            (await Record.ExceptionAsync(() => new DatabaseSchemaCheck(context).VerifyAsync())).Should().BeOfType<InvalidOperationException>();
            await context.Database.MigrateAsync();
            await new DatabaseSchemaCheck(context).VerifyAsync();
            var users = await context.Users.AsNoTracking().ToListAsync(); users.Should().OnlyContain(u => u.Role == UserRole.Regular);
            users.Single(u => u.Id == id).PasswordHash.Should().Be("unchanged-test-hash");
            users.Single(u => u.Email == "demo@studyplanner.com").Role.Should().Be(UserRole.Regular);
            (await context.Categories.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync()).Should().BeEquivalentTo(categories);
            (await context.Set<RoleChangeAudit>().CountAsync()).Should().Be(0);
            (await context.Database.GetAppliedMigrationsAsync()).Should().HaveCount(3);
        }
        finally { await database.DisposeAsync(); }
    }
}
