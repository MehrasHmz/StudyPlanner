# StudyPlanner

ASP.NET Core 8 Razor MVC study planner with PostgreSQL, EF Core, MediatR and FluentValidation. The application includes authenticated study-item management, shared categories, session planning and completion, and a browser-local Focus timer. Onion Architecture is preserved.

## Docker Quick Start

Docker Desktop (Linux containers) and Docker Compose v2 are required. ARM64 macOS and AMD64 Linux are supported by the upstream .NET/PostgreSQL images; verify the image on your target architecture before deployment.

```sh
cp .env.example .env
# Replace POSTGRES_PASSWORD in .env with a unique local password.
docker compose config --quiet
docker compose up --build -d --wait
```

Open [StudyPlanner](http://localhost:8080). Set APP_PORT in .env if the port is occupied. The listener is loopback-only. PostgreSQL has no exposed host port.

Compose starts PostgreSQL, waits for its health check, runs the one-shot `migrate` service, and starts `api` only after migration succeeds. Production is the default ASP.NET environment; Swagger is enabled only in Development. The runtime image runs as non-root, with a read-only root filesystem, dropped capabilities and writable volumes limited to logs and Data Protection keys.

```sh
docker compose ps --all
docker compose logs -f api
docker compose logs migrate
docker compose exec api id
curl --fail http://localhost:8080/health/ready
docker compose restart api
docker compose down
```

`docker compose down` retains database, logs and key volumes. Do not use `down --volumes` against data you need. Back up the database before migrations or upgrades. A changed POSTGRES_PASSWORD in .env does not automatically rotate the password of an existing database volume.

Health endpoints: `/health/live` checks application availability; `/health/ready` checks database connectivity and returns 503 on failure without exception details. Readiness is not a schema-version check.

## Configuration and Secrets

Real credentials are not checked into appsettings. Configure `ConnectionStrings__DefaultConnection` through the environment or your secret manager. Compose reads its local password from ignored .env; local environment variables are not a substitute for a production secret manager.

Data Protection keys are mounted at `/app/keys`; retaining this volume preserves authentication and antiforgery cryptographic keys across container replacement. The local volume is not encrypted by the application. Protect it and database backups at the host level.

The existing initial migration includes a **demo account** (`demo@studyplanner.com` / `password123`) and sample records. Never expose a newly migrated database publicly until demo credentials and sample data have been handled through an approved procedure. This setup is a verified local container workflow, not a declaration that internet-facing production deployment is fully hardened. Production also requires HTTPS/reverse-proxy configuration, trusted forwarding, backup/restore policy, runtime patching and secret/key protection.

## Local Development

Install a stable .NET 8 SDK. global.json permits compatible .NET 8 feature-band roll-forward; it does not opt into prerelease or .NET 9/10 SDKs.

Start an isolated PostgreSQL server, configure the connection, then run:

```sh
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=StudyPlannerDev;Username=your_user;Password=your_local_password'
dotnet restore StudyPlanner.sln
dotnet run --project src/StudyPlanner.Web
```

The Development startup applies the existing migrations. In Production, apply migrations separately:

```sh
dotnet run --project src/StudyPlanner.Web -- --migrate-only
```

No migration is generated or rewritten by Docker. Inspect migration logs and stop on failure.

## Study Planning and Scheduled Reviews

The dashboard's Plan session action opens a safe GET form at `/Sessions`; `/Sessions/Plan` also accepts GET without creating a session. Actual planning requires an authenticated, antiforgery-protected POST with 1-480 available minutes. Invalid input is shown in the form and does not create a session.

Active unfinished items remain eligible under the existing duration/priority rules, even when their review date is in the future. Completed items become eligible again only when a scheduled review reaches its exact UTC timestamp (`NextReviewDate <= now`). A completed item without a review date remains completed and is not automatically due. Deleted and other users' items are excluded. Dashboard due counts cover active items with an arrived review date, including completed reviews; the count is not the number that fits a particular time budget.

Successful session completion appends progress, keeps completed items completed, and schedules the next review seven days after completion. Earlier progress is not overwritten. Existing valid-subset completion and per-session concurrency/replay protection remain in place. Completion state and review readiness are independent, so an item can correctly display both Completed and Review due. Date-only form input is interpreted as midnight UTC; labels explicitly show UTC for scheduled dates.

The application uses .NET's `TimeProvider` for planning, dashboard and completion timestamps. Tests supply a fixed clock. Focus Mode remains browser-local and never writes progress automatically.

## Recorded Activity and Session History

Session completion displays planned minutes separately and starts actual minutes blank. Select the items you studied and explicitly enter 1-480 actual minutes for each selected item. Optional scores use the existing 0-1 scale; optional notes are limited to 1,000 characters. Unselected items are omitted from completion and remain unchanged. Invalid input retains the submitted fields and creates no progress.

`/Sessions/History` is authenticated and paginated in SQL. `/Sessions/Details/{id}` shows only the current user's session and recorded activities, including soft-deleted study items. Planned sessions can be resumed at `/Sessions/Record/{id}` without any GET writes. Recorded totals sum stored ProgressRecords once, while completed-item totals count distinct item IDs. Dashboard study time also uses stored progress, never planned estimates.

Historical minutes are labeled **Recorded**, not independently verified actual effort: older forms prefilled planned estimates and the schema has no provenance flag to distinguish those records. They are preserved rather than reclassified. Titles are current study-item titles, not immutable historical snapshots. Focus remains independent and does not create ProgressRecords.

## Tests

```sh
dotnet build StudyPlanner.sln -c Release
dotnet test StudyPlanner.sln -c Release
node --test tests/frontend/focus-timer.test.cjs
```

Real PostgreSQL tests require `STUDYPLANNER_TEST_POSTGRES` pointing to an explicitly disposable PostgreSQL server with permission to create/drop databases. The fixtures create unpredictable test database names and remove only those databases. Without this variable, PostgreSQL declarations are skipped.

Browser scripts in `tests/frontend` require Playwright and installed Chrome and create synthetic accounts/items. Their default port 5815 is intended for an isolated preview, not a database containing real user data.

The new `tests/frontend/review-planning.test.cjs` defaults to loopback port 5816. It requires `STUDYPLANNER_REVIEW_TEST_DATABASE` named `studyplanner_phase6_browser_*` and `STUDYPLANNER_REVIEW_TEST_SOCKET` pointing to the isolated `/private/tmp/studyplanner-*` PostgreSQL socket (port 55439, role `studyplanner_test`). Start the preview against that same disposable database. It verifies navigation, two review cycles, invalid input, themes/responsiveness and Focus persistence, and writes screenshots/results under `/private/tmp/studyplanner-phase6-browser-evidence` by default. Its synthetic SQL setup must never target a production database.

## Continuous Integration

`.github/workflows/ci.yml` runs on pull requests and pushes to master/main. It performs vulnerability-audited restore, Release build, .NET tests with a real disposable PostgreSQL service, timer tests, Docker build and Compose smoke checks. Test results are retained as artifacts. Actions are pinned to verified commit SHAs; Dependabot checks NuGet, Docker and action updates weekly. The workflow has read-only repository permissions and does not publish images or deploy.

The workflow must run on GitHub after an authorized push to establish a hosted CI result; adding the workflow locally is not proof of remote execution.

## Repository Hygiene

.env files, build outputs, keys, logs, node_modules and personal IDE state are excluded. Keep .env.example as a placeholder template only. Existing phase reports/evidence describe historical verification dates; the current Docker/Git report records this work.

Git operations do not automatically imply release, push, registry publication or deployment. Review staged changes and credentials before publishing.

## Make Targets

`make build`, `make run`, `make test`, `make docker-up`, `make docker-down`, `make docker-logs`, `make docker-check`, `make clean`.

The clean target uses dotnet clean; it does not recursively delete arbitrary directories.
