using StudyPlanner.Domain.Enums;
using StudyPlanner.Infrastructure.Administration;
using StudyPlanner.Infrastructure.Data;

namespace StudyPlanner.Web.Security;

public static class RoleProvisioningCommand
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services, TextWriter output)
    {
        const string usage = "Usage: --manage-user-role --user-id <UUID> --role <administrator|regular> --operator <identity> --reason <reason> [--confirm]. Without --confirm: inspection only. Select the deployment connection/environment explicitly.";
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal); bool confirmed = false;
            if (args.Length == 0 || args[0] != "--manage-user-role") throw new ArgumentException();
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                if (key == "--confirm") { if (confirmed) throw new ArgumentException(); confirmed = true; continue; }
                if (key is not ("--user-id" or "--role" or "--operator" or "--reason") || ++i >= args.Length || !values.TryAdd(key, args[i])) throw new ArgumentException();
            }
            if (values.Count != 4 || !Guid.TryParseExact(values["--user-id"], "D", out var id) || id == Guid.Empty) throw new ArgumentException();
            var role = values["--role"] switch { "administrator" => UserRole.Administrator, "regular" => UserRole.Regular, _ => throw new ArgumentException() };
            using var scope = services.CreateScope();
            var provisioner = new RoleProvisioner(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            var target = await provisioner.InspectAsync(id, role);
            await output.WriteLineAsync($"Target UUID: {target.UserId}; active: {target.Active}; current: {target.CurrentRole}; requested: {target.RequestedRole}.");
            if (!confirmed) { await output.WriteLineAsync("Inspection only. No changes applied. Repeat with --confirm after verifying the environment and target."); return 0; }
            var changed = await provisioner.ApplyAsync(id, role, values["--operator"], values["--reason"], true);
            await output.WriteLineAsync(changed ? "Role change committed with durable audit record." : "Requested role already assigned. No change or duplicate audit record.");
            return 0;
        }
        catch (ArgumentException) { await output.WriteLineAsync(usage); return 2; }
        catch (InvalidOperationException) { await output.WriteLineAsync("Provisioning refused. Verify the existing account, active status, schema and stored role. No role change committed."); return 1; }
        catch (Exception) { await output.WriteLineAsync("Provisioning failed. No successful change is confirmed; inspect authoritative state before retrying. Internal database/credential details are not displayed."); return 1; }
    }
}
