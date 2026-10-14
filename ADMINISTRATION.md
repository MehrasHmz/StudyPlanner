# Shared Category Administration

Categories remain global. Only active accounts with persisted role `Administrator` may create, edit, rename or delete them. Regular users can view/select active categories and manage only their own study data. Administrator status does not bypass private item/session/progress ownership.

## Roles And Enforcement

`Users.Role` is a non-null integer: `0` Regular, `1` Administrator, with database default 0 and a check constraint. Existing users, registration and the demo seed remain Regular. Unknown values are rejected by the database; authentication rejects unknown stored values and management requires exact Administrator status.

The `ManageSharedCategories` policy checks the current account through the server-side authenticated user UUID. Create GET/POST, Edit GET/POST and Delete POST are protected. All three mutation handlers independently require the same authoritative permission; no role/user ID is accepted in category commands. Non-HTTP Application hosts default to an anonymous identity and deny management unless a trusted host supplies an authenticated caller.

No role claim grants management. Each privileged request and handler checks current persisted state without a role cache. Granting access works on the next request with an existing valid cookie; revoking it produces 403 on subsequent management requests while normal user access remains. Deactivation/missing accounts/invalid role state invalidate cookies. Already-running requests that passed a check are not cancelled retroactively; drain traffic if an emergency revocation requires that stronger boundary.

Regular users do not see catalog mutation links, including the StudyItem form's new-category link. Forbidden management URLs return a themed 403 page with a return link. The existing read-only `/api/categories/list` remains authenticated and has no mutation route. CSRF and category reference/deletion safeguards remain enabled.

## Operator-Only Provisioning

There is no HTTP grant/revoke route, public bootstrap, first-user promotion, email-driven promotion or seeded administrator. The operator command runs once, never starts an HTTP listener and does not migrate implicitly.

Only an authorized deployment/database operator may use it. Select the correct environment and database explicitly via approved secret management; do not pass passwords/connection strings as CLI arguments, write them into Git, or put credentials in the audit reason. The operator identity is a declared audit label, not a newly implemented authentication authority. Host/secret/database access establishes operator trust.

1. Confirm deployment/project and intended database using an approved read-only database session.
2. Resolve the exact existing account UUID; verify account identity out of band. Never choose the first row or rely solely on a similar email/name. Example read-only SQL:

```sql
SELECT "Id", "Email", "IsActive", "Role" FROM "Users" WHERE "Id" = '<verified-uuid>';
```

3. Select `ASPNETCORE_ENVIRONMENT` and `ConnectionStrings__DefaultConnection` through the protected operator environment. Build the approved Release artifact, then inspect the exact intended action:

```sh
dotnet src/StudyPlanner.Web/bin/Release/net8.0/StudyPlanner.Web.dll \
  --manage-user-role --user-id '<verified-uuid>' --role administrator \
  --operator '<authorized-operator-label>' --reason '<approved-ticket-or-reason>'
```

This displays UUID, active status, current role and requested role. Without `--confirm` it is inspection only. No password/hash/connection string is printed. Use a known writable log directory and the same approved configuration as the deployment; do not use Development automatic migration as a provisioning shortcut.

4. Review the target/environment, then repeat the identical command with `--confirm` to grant. Confirming is explicit authorization by the operator, not an automatic application startup action.
5. Inspect the target again, confirm the durable audit entry, and verify category management on a subsequent authenticated request.
6. To revoke, use `--role regular` with the same exact UUID, an explicit reason and `--confirm`. Verify role 0 and absence of management controls/403 responses with an existing cookie. Inactive accounts may be revoked but cannot be granted Administrator.
7. For an incorrect assignment, immediately revoke the incorrect UUID, verify the audit trail and investigate any catalog changes; grant the correct account only after independent verification. Never delete audit records or reset a database to undo privileges.

Container equivalent, using an already selected deployment project and an operator connection injected by the protected environment:

```sh
docker compose -p '<approved-project>' run --rm --no-deps \
  -e ConnectionStrings__DefaultConnection api \
  --manage-user-role --user-id '<verified-uuid>' --role regular \
  --operator '<authorized-operator-label>' --reason '<approved-revocation-reason>' --confirm
```

The one-shot container does not publish ports. Do not accidentally point test commands at an existing production deployment. Production provisioning is not authorized by local test approval.

Phase 10 file-secret support: if the production overlay mounts `/run/secrets`, those values override environment placeholders. Run provisioning with a separately protected operator-secret directory (`Secrets__Directory`), rather than assuming an environment connection overrides the runtime secret. See DEPLOYMENT_CHECKLIST.md. Production also requires the explicit historical demo-account procedure before serving; no manager may be granted to an inactive account.

## Atomicity, Audit And Permissions

Grant/revoke lock the exact user row (`FOR UPDATE`), recheck account state, update Role and insert a `RoleChangeAudits` record in one transaction. Audit stores target UUID, previous/new roles, operator label, bounded reason and UTC time. Concurrent identical grants serialize; repeated identical operations return no change and do not create duplicate audit records. Invalid/missing/empty/duplicate targets, unknown roles, missing confirmation, inactive grants or invalid audit metadata are refused. Errors do not print internal database/credential details; a connection failure at commit should be reconciled by re-inspecting authoritative state before retrying.

Required operator database access: CONNECT, schema USAGE, SELECT on Users (the utility materializes the existing account), UPDATE on Users.Role including row-lock rights, and INSERT/SELECT on RoleChangeAudits for mutation and verification. The GUID audit key needs no sequence. Provisioning does not need schema-owner/DDL privileges; migrations need a separately approved migration identity.

For production least privilege, separate operator credentials from the running web application's credentials. Review PostgreSQL grants so the runtime can INSERT only safe registration columns (omit Role and use default 0), UPDATE normal account/password columns but not Role, and cannot INSERT/UPDATE/DELETE audit rows. The existing local Compose database owner is appropriate for disposable testing, not this separation. No actual production grants or credentials were modified here.

Durable audit is not an append-only cryptographically protected ledger: a database owner could alter it. Restrict audit modification/deletion and export security records under an approved retention/monitoring policy. No privileged HTTP audit view exposes account/private progress data.

## Migration And Deployment

New additive migration: `20261014075951_AddUserRolesAndRoleAudit`. It adds Role with default 0, the audit table, role checks and the target/time index. Existing IDs, credentials, categories, references and history are not rewritten. Historical migrations are unchanged. The current model snapshot includes the new schema; the demo seed's only role addition is Regular.

Order: back up the approved database, stop/drain old writers if required, run the one-shot `--migrate-only` service with migration credentials, verify migration success, then start the new API. Compose already waits for the migration service to exit successfully. Non-Development/non-Testing startup also requires applied migration IDs to match the built artifact before binding HTTP. Development continues its existing automatic migration behavior; the operator command never applies migrations.

Do not automatically run the migration Down as an application rollback: it removes roles and durable audit data. Prefer forward recovery, or restore an approved backup under an explicit rollback plan. Running the old application on the new schema would re-expose unprivileged category writes; do not serve the old build after this authorization cutover without independent access controls. The new build refuses older/newer migration histories.

Verification used synthetic accounts/disposable PostgreSQL databases only: populated legacy migration/defaults, exact-target grant/revoke, audit rollback on failure, concurrent/idempotent grants, HTTP cookies with real persistence, reference safeguards, stale ordinary account updates and inactive accounts. Production assignment was not performed.

## Remaining Release Requirements

Approve actual operator identities, credential custody and least-privilege DB grants; explicitly provision legitimate managers. Demo-account handling, TLS/trusted proxies, secure cookies, key encryption, protected logs, off-host backups/restore, image scanning and monitored operational acceptance remain production prerequisites. No default manager is supplied.

Policy implementation follows [ASP.NET Core policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-8.0); persisted state, not stale role claims, is authoritative here.
