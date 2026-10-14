# StudyPlanner Release Readiness

Audit date: 2026-10-14 (Phase 10 final acceptance). Functional product completion and release-candidate acceptance are verified locally; Production deployment is NOT APPROVED. External acceptance gates remain explicit below.

## Verified Baseline

Phases 1-6 are preserved in the existing history. Phase 7 was recovered, finished, tested and independently pushed as `e1c4c89fa301c03d434de7f07977dd042a87f947`; [its hosted CI passed](https://github.com/MehrasHmz/StudyPlanner/actions/runs/38449105966). Phase 8 adds focused security and integrity fixes without changing ownership models, historical migrations, the planner or Focus timer.

- Release build: zero warnings and errors.
- Full .NET suite: 317 passed, zero failures, zero skips; includes 47 real PostgreSQL cases. Phase 9's 308/40 baseline was rerun before final acceptance; nine focused release/configuration/permission tests were added without skips.
- Timer suite: 19 passed, zero failures and skips.
- Chrome workflows: 759 checks/260 screenshots; additional WebKit workflows: 524 checks/160 screenshots. Both cover progress/history and administrator governance; Chrome also reruns reviews and form validation. Another 92 keyboard/contrast/reduced-motion/HTTP acceptance checks passed. Total: 1,375 checks, 420 screenshots, zero JavaScript errors; representative widths 1440/1024/768/390/360. These are browser engines, not physical devices or a complete accessibility certification.
- NuGet direct/transitive audit: no reported vulnerable packages in five projects at audit time.
- Docker: current Phase 9 build, populated migration exit 0, Production-mode startup, readiness, login, cookie survival after API recreation, one-shot operator grant/revoke and durable audit passed on an isolated local Compose project.
- Phase 10 disposable PostgreSQL backup/restore: all eight tables including private account/progress and role audits matched counts/sorted JSON row fingerprints; source unchanged. Restored database retains role checks/constraints and a separate Production app passed readiness. Backup mode 0600, outside Git. No production RPO/RTO commitment is inferred.

Session recording distinguishes planned time from explicitly entered actual effort. Scores and notes are optional and validated; subset completion and form error preservation work. History/details and private notes are owner-scoped and paginated. Recurring reviews, optimistic completion concurrency, CSRF, password upgrades and Focus independence remain covered. Legacy recorded sessions without progress do not fabricate actual minutes; titles remain current names, not immutable historical snapshots.

## Confirmed Findings And Fixes

- P1: unlimited authentication attempts. Login now permits 10 POST attempts/minute per socket IP; registration permits 5. Excess requests return 429 with Retry-After. Limits are separate, in-process fixed windows, not distributed protection or account lockout.
- P1: inactive accounts could sign in and existing cookies were not activity-checked. Login now rejects inactive accounts; authenticated requests reject missing/inactive users. Public health probes remain independent of stale cookies. Database failures fail closed.
- P1: shared category soft deletion could hide active study items through required-navigation joins. Owner-filtered item queries now preserve active items under archived categories; deleted items remain hidden and selectors exclude archived categories. Referenced-category deletion is refused, including references from soft-deleted items.
- P2: runtime-generated seed GUIDs/timestamps could cause unwanted future seed replacement. Seeds now match the historical migration snapshot; independent model builds and zero migration operations are tested. No migration or existing database records were changed.
- P2: exception messages in forms could reveal internal details. Forms use generic failures and server-side logging; non-development/non-test unhandled exceptions return a generic 500 response. Authentication failures are generic. Pagination inputs are bounded.
- Final release: an explicit UUID-targeted `--disable-demo-account` operator procedure preserves hashes/data, deactivates the historical seed and revokes any Administrator status atomically. Production refuses an active historical seed UUID before binding HTTP. No deletion, automatic disable of an unknown database or migration rewrite occurs. Existing legacy authentication was tested before explicit mitigation; inactive accounts/cookies are denied afterwards.
- Established KeyPerFile support reads protected secret files from `/run/secrets` or an operator-selected directory; production overlay/templates and tested column-level PostgreSQL grants separate runtime/migration/operator duties. Host rejection and Secure-cookie configuration have regression coverage.

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
- Inspect the historical demo UUID/environment and authorize the documented disable procedure before Production startup, including fresh installs/upgrades. Do not reproduce known credentials, delete seed/user data or silently modify a repurposed account. See OPERATIONS.md and DEPLOYMENT_CHECKLIST.md. No real user database was changed.
- Terminate TLS at an approved reverse proxy, configure redirects/HSTS there, and restrict access to backend HTTP. Set `Security:RequireSecureCookies=true` (`REQUIRE_SECURE_COOKIES=true` in Compose). Local HTTP defaults remain usable, including local Production-mode smoke tests.
- Define trusted proxy addresses/networks and forwarded-header handling before using forwarded client IPs. Current limits use connection IP only and ignore spoofed X-Forwarded-For; a proxy/NAT can group clients. Add infrastructure/distributed abuse protection for multiple replicas; tune positive limits through Security settings or Compose variables.
- Supply real credentials through approved secret management, never source control. Existing local `.env` was preserved and remains ignored. Do not log passwords, cookie values, tokens or private notes; restrict existing exception/database logs and set retention/rotation. This audit did not certify a centralized logging system.
- Protect persistent Data Protection keys with deployment-specific encryption at rest and restricted permissions. Volume persistence is verified; encryption at rest is not configured/certified. Back up keys separately from databases for cookie continuity.
- Image scan is BLOCKED: installed Docker Scout requires login, and the user explicitly chose to leave this as a Production gate. No OS/base/.NET CVE verdict or clean-image claim is made. NuGet audit has no reported vulnerabilities, which is distinct. Operator must authenticate and scan the exact accepted image before public release.
- Built runtime is Microsoft.NETCore.App/Microsoft.AspNetCore.App 8.0.32. .NET 8 support ends **2026-11-10**, per [Microsoft](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/). It remains supported on this audit date, but deployment beyond that date needs an explicitly approved supported-runtime maintenance change; no unapproved major upgrade was performed.
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

Current local evidence: `/private/tmp/studyplanner-phase10-tests/phase10-final.trx`, browser results in `/private/tmp/studyplanner-phase10-{chrome,webkit}-*`, acceptance timings in `/private/tmp/studyplanner-phase10-acceptance/results.json`, protected synthetic recovery dump outside Git. External final receipt records exact Git/hosted CI results, not assumed future success.

Bounded local rehearsal: 80 GET requests, concurrency 4, zero failures, median 2.02 ms, p95 9.18 ms, maximum 15.54 ms. These are loopback measurements, not Production capacity guarantees. Landing image is approximately 1.9 MiB; measured resource/navigation timings are in the acceptance artifact. History/items use SQL pagination; dashboard loads owned active items and authenticated requests add account lookups. No speculative cache, unbounded stress or unrelated optimization was introduced. Large catalogs/replicas require separate operational sizing.

Phase 9 adds migration `20261014075951_AddUserRolesAndRoleAudit`: safe Role default 0, checks, audit table/index. Populated legacy migration and credential/reference preservation passed on disposable PostgreSQL. Generated Up SQL contains no data updates/deletes/drops. Non-development/non-test startup refuses incompatible migration history before binding HTTP; a process-level test against a restored legacy schema verified refusal. One-shot migration precedes API startup in Compose. Do not serve the old build after the category authorization cutover or automatically run Down (it would remove role/audit data); see the explicit rollout/rollback guidance in ADMINISTRATION.md.

Security implementation follows [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0) and [cookie principal validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-8.0). [Trusted forwarding guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/8.0/forwarded-headers-unknown-proxies) informs the deployment checklist; forwarded-header middleware was not enabled blindly.

## Status

FUNCTIONALLY COMPLETE; RELEASE CANDIDATE ACCEPTED subject to final delivery receipt; PRODUCTION DEPLOYMENT NOT APPROVED. Final scoped product development may close after verified Git/CI delivery. Remaining work is deployment acceptance and maintenance: authenticated image scan, support-horizon decision, approved TLS/proxy/hosts, secrets/encrypted keys, real service-role grants/operators/managers, backups/monitoring and approved demo mitigation on the actual target. No public deployment, tag, image publication or Phase 11 occurred.
