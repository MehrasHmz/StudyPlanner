using Serilog;
using StudyPlanner.Infrastructure.Administration;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.Web.Security;

public static class DemoAccountReleaseCommand
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services, TextWriter output)
    {
        try
        {
            var values = new Dictionary<string, string>(); bool confirm = false;
            if (args.FirstOrDefault() != "--disable-demo-account") throw new ArgumentException();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--confirm") { if (confirm) throw new ArgumentException(); confirm = true; continue; }
                var key = args[i];
                if (key is not ("--user-id" or "--operator" or "--reason") || ++i >= args.Length || !values.TryAdd(key, args[i])) throw new ArgumentException();
            }
            if (values.Count != 3 || !Guid.TryParseExact(values["--user-id"], "D", out var id)) throw new ArgumentException();
            using var scope = services.CreateScope(); var tool = new DemoAccountRelease(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            var user = await tool.InspectAsync(id);
            if (user == null) { await output.WriteLineAsync("Historical seed UUID is absent; no account selected or changed."); return 0; }
            await output.WriteLineAsync($"Target UUID: {user.Id}; active: {user.IsActive}; role: {user.Role}; intended action: deactivate, revoke any administrator role, preserve credentials and all study data.");
            if (!confirm) { await output.WriteLineAsync("Inspection only. Verify the database and actual account identity out of band before repeating with --confirm."); return 0; }
            var changed = await tool.DisableAsync(id, values["--operator"], values["--reason"], true);
            if (changed) Log.Information("Security: historical demo UUID {UserId} deactivated by operator {Operator}; reason {Reason}", id, values["--operator"], values["--reason"]);
            await output.WriteLineAsync(changed ? "Account deactivated. Study data and password hash preserved." : "Already inactive and Regular; no change."); return 0;
        }
        catch (ArgumentException) { await output.WriteLineAsync("Usage: --disable-demo-account --user-id <exact-historical-UUID> --operator <identity> --reason <ticket> [--confirm]. No automatic account selection."); return 2; }
        catch (Exception) { await output.WriteLineAsync("Release procedure refused or failed. Recheck authoritative state and environment; internal credentials are not displayed."); return 1; }
    }
}
