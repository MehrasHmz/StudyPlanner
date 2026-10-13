# StudyPlanner Release Readiness

Audit date: 2026-10-13. This is a tested local release candidate, not a production deployment or a production-readiness certification.

## Verified Baseline

Phases 1-6 are preserved in the existing history. Phase 7 was recovered, finished, tested and independently pushed as `e1c4c89fa301c03d434de7f07977dd042a87f947`; [its hosted CI passed](https://github.com/MehrasHmz/StudyPlanner/actions/runs/38449105966). Phase 8 adds focused security and integrity fixes without changing ownership models, historical migrations, the planner or Focus timer.

- Release build: zero warnings and errors.
- Full .NET suite: 276 passed, zero failures, zero skips; includes 29 real PostgreSQL cases.
- Timer suite: 19 passed, zero failures and skips.
- Chrome workflows: 497 checks, 180 screenshots, zero JavaScript errors, light/dark themes at 1440, 1024, 768, 390 and 360 pixels as applicable.
- NuGet direct/transitive audit: no reported vulnerable packages in five projects at audit time.
- Docker: build, Production-mode startup, migration exit 0, readiness, login and cookie survival after API recreation passed on an isolated local Compose project.
- Disposable PostgreSQL backup/restore: all seven tables matched row counts and sorted JSON row fingerprints; original database unchanged.

Session recording distinguishes planned time from explicitly entered actual effort. Scores and notes are optional and validated; subset completion and form error preservation work. History/details and private notes are owner-scoped and paginated. Recurring reviews, optimistic completion concurrency, CSRF, password upgrades and Focus independence remain covered. Legacy recorded sessions without progress do not fabricate actual minutes; titles remain current names, not immutable historical snapshots.

## Confirmed Findings And Fixes

- P1: unlimited authentication attempts. Login now permits 10 POST attempts/minute per socket IP; registration permits 5. Excess requests return 429 with Retry-After. Limits are separate, in-process fixed windows, not distributed protection or account lockout.
- P1: inactive accounts could sign in and existing cookies were not activity-checked. Login now rejects inactive accounts; authenticated requests reject missing/inactive users. Public health probes remain independent of stale cookies. Database failures fail closed.
- P1: shared category soft deletion could hide active study items through required-navigation joins. Owner-filtered item queries now preserve active items under archived categories; deleted items remain hidden and selectors exclude archived categories. Referenced-category deletion is refused, including references from soft-deleted items.
- P2: runtime-generated seed GUIDs/timestamps could cause unwanted future seed replacement. Seeds now match the historical migration snapshot; independent model builds and zero migration operations are tested. No migration or existing database records were changed.
- P2: exception messages in forms could reveal internal details. Forms use generic failures and server-side logging; non-development/non-test unhandled exceptions return a generic 500 response. Authentication failures are generic. Pagination inputs are bounded.

The deletion reference precheck is not a serializable reservation: a concurrent new reference remains possible. Archived-category read compatibility prevents those items disappearing. Required-navigation EF warnings have not been suppressed; other default filtered reads must be evaluated when new query paths are introduced.

## Category Policy: Unresolved Public-Release Blocker

Categories currently remain globally shared; authenticated users can still create and rename them and delete unreferenced ones. A reference guard is not authorization. Shared renaming can affect other users.

| Option | Consequence |
| --- | --- |
| A: Private owner-scoped categories | Adds ownership and migration/backfill decisions for existing shared references; stronger personal isolation. |
| B: Shared categories, privileged changes | Smallest fit for the current data model; requires explicit management permission and trusted administrator provisioning. Recommended. |
| C: Protected shared plus private categories | More flexible, but adds permission, naming, selector and migration complexity. |

Product approval and the identity/provisioning of privileged managers are outstanding. No user has silently been made an administrator and no ownership migration was invented. Resolve this P1 issue before exposing registration to untrusted users.

## Production Checklist

- Approve the category model and implement/test its exact permissions before public release.
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

Local evidence: `/private/tmp/studyplanner-phase8-tests/phase8-final.trx`, browser results under `/private/tmp/studyplanner-phase8-browser-evidence`, `/private/tmp/studyplanner-phase8-review-evidence` and `/private/tmp/studyplanner-phase8-forms-evidence`. These ephemeral synthetic artifacts and the disposable dump were not committed. The final external Phase 8 receipt records the pushed SHA and hosted CI result; this file intentionally does not claim future CI success.

Security implementation follows [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0) and [cookie principal validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-8.0). [Trusted forwarding guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/8.0/forwarded-headers-unknown-proxies) informs the deployment checklist; forwarded-header middleware was not enabled blindly.

## Status

Phase 8 scoped stabilization and audit are verified locally, subject to the exact Git/hosted CI receipt. Public release remains blocked by category management policy and environment-specific security/operational acceptance. No production deployment occurred. No Phase 9 was started.
