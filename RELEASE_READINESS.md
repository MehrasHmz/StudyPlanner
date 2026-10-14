# StudyPlanner Release Readiness

Audit date: 2026-10-14 (Phase 9). This is a tested local release candidate, not a production deployment or a production-readiness certification.

## Verified Baseline

Phases 1-6 are preserved in the existing history. Phase 7 was recovered, finished, tested and independently pushed as `e1c4c89fa301c03d434de7f07977dd042a87f947`; [its hosted CI passed](https://github.com/MehrasHmz/StudyPlanner/actions/runs/38449105966). Phase 8 adds focused security and integrity fixes without changing ownership models, historical migrations, the planner or Focus timer.

- Release build: zero warnings and errors.
- Full .NET suite: 308 passed, zero failures, zero skips; includes 40 real PostgreSQL cases. The Phase 8 baseline of 276/29 was rerun before changes.
- Timer suite: 19 passed, zero failures and skips.
- Chrome workflows: 759 checks, 260 screenshots, zero JavaScript errors, light/dark themes at 1440, 1024, 768, 390 and 360 pixels as applicable; includes regular/admin category management and operator grant/revoke.
- NuGet direct/transitive audit: no reported vulnerable packages in five projects at audit time.
- Docker: current Phase 9 build, populated migration exit 0, Production-mode startup, readiness, login, cookie survival after API recreation, one-shot operator grant/revoke and durable audit passed on an isolated local Compose project.
- Disposable PostgreSQL backup/restore: all seven tables matched row counts and sorted JSON row fingerprints; original database unchanged.

Session recording distinguishes planned time from explicitly entered actual effort. Scores and notes are optional and validated; subset completion and form error preservation work. History/details and private notes are owner-scoped and paginated. Recurring reviews, optimistic completion concurrency, CSRF, password upgrades and Focus independence remain covered. Legacy recorded sessions without progress do not fabricate actual minutes; titles remain current names, not immutable historical snapshots.

## Confirmed Findings And Fixes

- P1: unlimited authentication attempts. Login now permits 10 POST attempts/minute per socket IP; registration permits 5. Excess requests return 429 with Retry-After. Limits are separate, in-process fixed windows, not distributed protection or account lockout.
- P1: inactive accounts could sign in and existing cookies were not activity-checked. Login now rejects inactive accounts; authenticated requests reject missing/inactive users. Public health probes remain independent of stale cookies. Database failures fail closed.
- P1: shared category soft deletion could hide active study items through required-navigation joins. Owner-filtered item queries now preserve active items under archived categories; deleted items remain hidden and selectors exclude archived categories. Referenced-category deletion is refused, including references from soft-deleted items.
- P2: runtime-generated seed GUIDs/timestamps could cause unwanted future seed replacement. Seeds now match the historical migration snapshot; independent model builds and zero migration operations are tested. No migration or existing database records were changed.
- P2: exception messages in forms could reveal internal details. Forms use generic failures and server-side logging; non-development/non-test unhandled exceptions return a generic 500 response. Authentication failures are generic. Pagination inputs are bounded.

The deletion reference precheck is not a serializable reservation: a concurrent new reference remains possible. Archived-category read compatibility prevents those items disappearing. Required-navigation EF warnings have not been suppressed; other default filtered reads must be evaluated when new query paths are introduced.

## Category Policy: Approved And Implemented

Phase 9 explicitly approved Option B. Categories remain global and readable/selectable by authenticated users, but only active persisted Administrators may mutate the catalog. Web policy and all mutation handlers independently check authoritative state. Registration, existing users and demo accounts default to Regular. Unknown roles fail closed. No administrator is created automatically.

| Option | Consequence |
| --- | --- |
| A: Private owner-scoped categories | Adds ownership and migration/backfill decisions for existing shared references; stronger personal isolation. |
| B: Shared categories, privileged changes | Smallest fit for the current data model; requires explicit management permission and trusted administrator provisioning. Recommended. |
| C: Protected shared plus private categories | More flexible, but adds permission, naming, selector and migration complexity. |

The application-level policy blocker is resolved. Operator-controlled UUID-targeted CLI provisioning, explicit confirmation, atomic role/audit writes and immediate next-request revocation are implemented and tested. Read [Administrator Operations](ADMINISTRATION.md) before provisioning approved real managers; actual operator identities, credential custody and least-privilege database grants remain deployment decisions. No production account was provisioned.

## Production Checklist

- Assign legitimate managers through the documented operator command after verifying the deployment/UUID. Separate protected operator credentials from runtime credentials and approve least-privilege database grants; do not provision a known/default manager.
- Review the known initial demo account (`demo@studyplanner.com`) and legacy sample data. Disable or rotate it through an approved rollout; do not delete migration history or users' records. The audit did not modify a real user database.
- Terminate TLS at an approved reverse proxy, configure redirects/HSTS there, and restrict access to backend HTTP. Set `Security:RequireSecureCookies=true` (`REQUIRE_SECURE_COOKIES=true` in Compose). Local HTTP defaults remain usable, including local Production-mode smoke tests.
- Define trusted proxy addresses/networks and forwarded-header handling before using forwarded client IPs. Current limits use connection IP only and ignore spoofed X-Forwarded-For; a proxy/NAT can group clients. Add infrastructure/distributed abuse protection for multiple replicas; tune positive limits through Security settings or Compose variables.
- Supply real credentials through approved secret management, never source control. Existing local `.env` was preserved and remains ignored. Do not log passwords, cookie values, tokens or private notes; restrict existing exception/database logs and set retention/rotation. This audit did not certify a centralized logging system.
- Protect persistent Data Protection keys with deployment-specific encryption at rest and restricted permissions. Volume persistence is verified; encryption at rest is not configured/certified. Back up keys separately from databases for cookie continuity.
- Review runtime/base-image security with an authenticated container scanner; NuGet auditing is not an OS/container vulnerability scan. Rebuild current supported images and validate updates before release. Dependabot covers NuGet, Docker and Actions; action revisions are pinned.
- Choose migration ownership and credentials, back up before migration, rehearse on a restored database and run the one-shot migration service before API startup. Do not run competing migration jobs. Production startup does not automatically migrate; Development does.
- Define database backup retention, off-host storage, encryption and a restore schedule. The disposable test below proves mechanics, not production RPO/RTO or disaster recovery.
- Confirm readiness/liveness monitoring, alert routing, recovery ownership, log access and resource limits. API/migration are non-root, read-only, drop all capabilities and persist logs/keys; PostgreSQL needs its writable data volume.
- Perform final human acceptance on target browsers/devices, category permissions, timezone expectations, legacy session data, failed writes and operational recovery. Current automated coverage is not exhaustive accessibility/load testing.

## Safe Local Operation

Use a clearly isolated project name and unique test credentials. Configuration templates are in `.env.example`; do not commit the resulting `.env`. Compose binds the API to loopback and does not publish PostgreSQL.

```sh
docker compose -p studyplanner-verify-yourid config --quiet
docker compose -p studyplanner-verify-yourid up --build -d --wait
curl --fail http://127.0.0.1:8080/health/ready
docker compose -p studyplanner-verify-yourid ps -a
```

Stop only the named test stack when finished. Never remove existing user volumes as a recovery technique. Port conflicts require a separate `APP_PORT`.

Backup/restore pattern (replace placeholders with an explicitly disposable database and connection; never restore over an unknown existing database):

```sh
pg_dump -Fc -d "$DISPOSABLE_SOURCE" -f "$PRIVATE_BACKUP_PATH"
chmod 600 "$PRIVATE_BACKUP_PATH"
createdb "$NEW_DISPOSABLE_RESTORE"
pg_restore --exit-on-error --no-owner --no-privileges -d "$NEW_DISPOSABLE_RESTORE" "$PRIVATE_BACKUP_PATH"
```

Verify schema, migration history, counts and data fingerprints before acceptance. Keep backups outside the checkout. Production recovery requires a separate approved runbook and protected credentials.

## CI And Evidence

The existing GitHub workflow restores with vulnerability warnings treated as errors, builds Release, executes .NET/real PostgreSQL/timer tests and builds/starts disposable Docker services. It has no production deployment/publishing step. Browser and API-recreation smoke suites were run locally; they are not currently part of hosted CI.

Current local evidence: `/private/tmp/studyplanner-phase9-tests/phase9-final.trx`, browser results under `/private/tmp/studyplanner-phase9-browser-evidence`, `/private/tmp/studyplanner-phase9-release-evidence`, `/private/tmp/studyplanner-phase9-review-evidence` and `/private/tmp/studyplanner-phase9-forms-evidence`. Synthetic artifacts/dumps were not committed. The final external Phase 9 receipt records the pushed SHA and hosted CI result; this file intentionally does not claim future CI success. Phase 8 backup/restore evidence remains historical and was not represented as a newly repeated full restore audit.

Phase 9 adds migration `20261014075951_AddUserRolesAndRoleAudit`: safe Role default 0, checks, audit table/index. Populated legacy migration and credential/reference preservation passed on disposable PostgreSQL. Generated Up SQL contains no data updates/deletes/drops. Non-development/non-test startup refuses incompatible migration history before binding HTTP; a process-level test against a restored legacy schema verified refusal. One-shot migration precedes API startup in Compose. Do not serve the old build after the category authorization cutover or automatically run Down (it would remove role/audit data); see the explicit rollout/rollback guidance in ADMINISTRATION.md.

Security implementation follows [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0) and [cookie principal validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-8.0). [Trusted forwarding guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/8.0/forwarded-headers-unknown-proxies) informs the deployment checklist; forwarded-header middleware was not enabled blindly.

## Status

Phase 9 category governance is verified locally, subject to the final Git/hosted CI receipt. The approved category policy is implemented; public release still requires environment-specific security/operational acceptance and legitimate operator/manager provisioning. No production deployment occurred. No Phase 10 was started.
