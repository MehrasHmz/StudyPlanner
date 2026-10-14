using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StudyPlanner.Application;
using StudyPlanner.Infrastructure;
using StudyPlanner.Infrastructure.Data;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using StudyPlanner.Web.Security;
using StudyPlanner.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args.Contains("--manage-user-role", StringComparer.Ordinal) ? Array.Empty<string>() : args);
Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).Enrich.FromLogContext().WriteTo.Console().WriteTo.File("logs/studyplanner-.log", rollingInterval: RollingInterval.Day).CreateLogger();
builder.Host.UseSerilog();

var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().SetApplicationName("StudyPlanner")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<ActiveUserCookieEvents>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IAuthorizationHandler, CategoryManagementAuthorization>();
builder.Services.AddAuthorization(options => options.AddPolicy(CategoryManagementRequirement.Policy,
    policy => policy.RequireAuthenticatedUser().AddRequirements(new CategoryManagementRequirement())));
builder.Services.AddRateLimiter(options =>
{
    void AddAuthenticationLimit(string policy, string setting, int defaultLimit)
    {
        var limit = builder.Configuration.GetValue<int?>(setting) ?? defaultLimit;
        if (limit <= 0) throw new InvalidOperationException("Authentication rate limits must be positive.");
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    }
    AddAuthenticationLimit("auth-login", "Security:LoginAttemptsPerMinute", 10);
    AddAuthenticationLimit("auth-register", "Security:RegistrationAttemptsPerMinute", 5);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsync("Too many authentication attempts. Please try again shortly.", ct);
    };
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Configuration.GetValue<bool>("Security:RequireSecureCookies")
            ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.EventsType = typeof(ActiveUserCookieEvents);
    });

var app = builder.Build();
if (args.Contains("--manage-user-role", StringComparer.Ordinal))
{
    Environment.ExitCode = await RoleProvisioningCommand.RunAsync(args, app.Services, Console.Out);
    return;
}
if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else if (!app.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
        await scope.ServiceProvider.GetRequiredService<IDatabaseSchemaCheck>().VerifyAsync();
    app.UseExceptionHandler(error => error.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("An unexpected error occurred. Please try again.");
    }));
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
    try { return await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program { }
