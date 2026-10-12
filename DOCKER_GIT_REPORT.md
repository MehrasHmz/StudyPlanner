# Docker and Git Completion Report

Date: October 12, 2026, Asia/Tehran.

## Delivered

- Multi-stage .NET 8 image, compatible with global.json's stable .NET 8 SDK roll-forward.
- Release publish and runtime-only final image; application runs as UID/GID 1654, not root.
- Compose PostgreSQL health gating, a separate successful one-shot migration service and application readiness checks.
- Loopback-only application port and no PostgreSQL host-port publication.
- Read-only application root filesystem, dropped Linux capabilities and no-new-privileges.
- Persistent named volumes for database data, authentication/Data Protection keys and application logs.
- Public minimal `/health/live` and `/health/ready` endpoints; readiness returns 503 without exception details when the database cannot be contacted.
- Real passwords removed from tracked appsettings; connection configuration is external. Existing local .env was retained, not overwritten or committed.
- Git/Docker exclusions for environment files, private keys, certificates, IDE state and build artifacts.
- Modern `docker compose` Make targets and corrected README instructions.
- GitHub CI for audited restore, Release build, real PostgreSQL tests, timer tests and Compose smoke verification.
- GitHub Actions pinned to verified upstream commit SHAs, read-only workflow permissions and weekly Dependabot configuration.
- All previously uncommitted application changes, tests and historical reports/evidence captured in Git without reverting user work.
- Personal Rider files removed from Git tracking only; the actual local files remain intact.

## Actual Verification

| Check | Result |
| --- | --- |
| Release solution build | 0 errors, 0 warnings |
| Complete .NET suite | 216 passed, 0 failed, 0 skipped |
| Real PostgreSQL cases, included above | All 16 passed |
| Timer unit tests | 19 passed, 0 failed |
| Current direct/transitive NuGet audit | 0 reported vulnerable packages across five projects |
| Docker image build on local ARM64 Docker Desktop | Successful |
| Compose configuration and startup | Successful |
| PostgreSQL and application health | Healthy |
| One-shot migration | Exit code 0 |
| Runtime user and hardening inspection | UID 1654, read-only root, all capabilities dropped |
| Runtime .NET / ASP.NET Core | 8.0.31 / 8.0.31 |
| Container registration/login and Focus | Successful, no JavaScript errors |
| Authentication after forced API-container recreation | Preserved through the key volume |
| Development and Production startup | Both exercised; final verification stack uses Production |
| Runtime filesystem | No .env, .git or .idea present |
| Git diff whitespace checks | Passed |
| Focused staged-content private-key/token pattern scan | 199 text files checked, no flagged files |
| YAML syntax checks | CI and Dependabot parsed successfully |

The token-pattern check is not a complete secret-scanning certification. NuGet auditing is not an OS/container-CVE scan. Other CPU architectures and hosted GitHub execution were not directly tested.

New `ContainerHealthTests` cover unauthenticated liveness and unavailable-database readiness. `tests/frontend/docker-smoke.cjs` registers a synthetic account, logs in, recreates only the API container, confirms readiness and the surviving authentication cookie, and opens Focus Mode. It rejects project names outside an explicit disposable verification/CI naming pattern.

## Exact Verification Commands

Run from `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`:

```sh
docker compose -p studyplanner-verify-20261012 config --quiet
docker compose -p studyplanner-verify-20261012 build --pull
ASPNETCORE_ENVIRONMENT=Production docker compose -p studyplanner-verify-20261012 up -d --wait --wait-timeout 180
docker compose -p studyplanner-verify-20261012 ps --all
docker compose -p studyplanner-verify-20261012 exec -T api id
docker compose -p studyplanner-verify-20261012 exec -T api dotnet --list-runtimes

/Users/m/.dotnet/dotnet build StudyPlanner.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-items.CW0oC4;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test StudyPlanner.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger 'trx;LogFileName=docker-git.trx' --results-directory /private/tmp/studyplanner-docker-tests
/Users/m/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node --test tests/frontend/focus-timer.test.cjs
/Users/m/.dotnet/dotnet list /Users/m/RiderProjects/StudyPlannerProject/StudyPlanner/StudyPlanner.sln package --vulnerable --include-transitive --format json

ASPNETCORE_ENVIRONMENT=Production STUDYPLANNER_DOCKER_TEST_PROJECT=studyplanner-verify-20261012 NODE_PATH=/Users/m/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules /Users/m/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node tests/frontend/docker-smoke.cjs
git diff --cached --check
```

The real PostgreSQL tests used the existing isolated Unix-socket fixture, not the user's original Docker database. Docker smoke verification used only newly created `studyplanner-verify-20261012_*` volumes. Existing `studyplanner_postgres_data` and unrelated volumes were not mounted into tests, deleted, upgraded or modified.

## Git State

- Branch: `master`, tracking `origin/master`.
- Remote: `git@github.com:MehrasHmz/StudyPlanner.git`.
- Remote fetch completed successfully; no push was performed.
- Implementation commit: `f97e0b2` (`feat: harden StudyPlanner and deliver verified Docker workflows`).
- This report and final ignore-rule refinements are recorded in a separate local documentation/hygiene commit.
- Existing configured Git identity was used; global Git configuration was not changed.

Run `git log -2 --oneline` for the final two commit IDs. No force push, history rewrite, merge, registry publication or deployment occurred. Hosted CI can only be reported successful after an authorized push and an actual GitHub run.

## Running Local Stack

The isolated verification stack is intentionally left running at **http://127.0.0.1:8080**, in Production environment, with only sample and synthetic test data. It is not the original user database and is not an internet deployment.

To stop it while retaining its data:

```sh
docker compose -p studyplanner-verify-20261012 down
```

The default project described in README uses different volume names. Stop the verification stack first if you need port 8080 for the default project. The pre-existing .env explicitly selected Development; verification overrode that setting in the process environment without modifying the file. Choose Production in your own .env when intentionally operating that mode.

## Operational Limits

- Before public deployment, handle the existing migration's known demo account and sample records. Existing migration files were not rewritten.
- HTTPS, reverse-proxy trust, secret management, protected/encrypted key storage, backups and restore testing require environment-specific setup. None were silently deployed.
- Application readiness verifies database connectivity, not schema revision or business-data correctness.
- Data Protection keys are preserved but not application-encrypted at rest in this local volume configuration.
- Changing a password in .env does not rotate an existing PostgreSQL role's password.
- Base-image and NuGet feeds can change. Rebuild with current images and re-audit before release; no claim of a CVE-free OS image is made.
- Existing EF soft-delete/required-navigation model warnings remain unrelated technical debt, separate from the zero-warning Release build.

## References

[Microsoft .NET container non-root/port conventions](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/containers), [Docker Compose startup dependencies](https://docs.docker.com/compose/how-tos/startup-order/), [Docker Compose secret guidance](https://docs.docker.com/compose/how-tos/use-secrets/).

## Status

Local Docker and Git work is complete and verified. Remote push, hosted CI execution, image publication and production deployment are intentionally not claimed as completed.
