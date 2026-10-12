# Phase 1: Authorization and Ownership Fixes

Implemented and verified on 2026-10-09. This report supersedes the ownership/session-completion findings in the historical PROJECT_REVIEW.md; other findings in that review remain outside this phase.

## Confirmed Initial Vulnerabilities

- StudyItem edit, delete and completion-toggle handlers loaded by ID without checking the caller's ownership. Only the edit GET checked ownership.
- Edit POST error handling could load another user's DTO by ID while rendering an error view.
- Session completion did not receive caller identity or check session ownership, submitted item membership, item ownership or duplicate results.
- The completion handler started and changed tracked entities while still processing unvalidated results. A late invalid item could leave earlier objects modified in memory before the request failed.
- The controller silently filtered malformed results and could report success without executing completion.

## Modified Files

| File under repository root | Purpose |
| --- | --- |
| src/StudyPlanner.Application/Commands/StudyItems/StudyItemCommands.cs | Require caller UserId for edit/delete/toggle commands |
| src/StudyPlanner.Application/Commands/StudyItems/StudyItemHandlers.cs | Check ownership, missing resources, empty caller and soft deletion before mutations |
| src/StudyPlanner.Application/Queries/StudyItems/StudyItemQueries.cs | Require caller UserId for single-item details |
| src/StudyPlanner.Application/Queries/StudyItems/StudyItemQueryHandlers.cs | Return no DTO for a foreign, missing or deleted item |
| src/StudyPlanner.Application/Commands/Sessions/SessionCommands.cs | Require caller UserId for completion |
| src/StudyPlanner.Application/Commands/Sessions/SessionHandlers.cs | Validate owner/state/all results before any mutation or progress addition |
| src/StudyPlanner.Application/Commands/Sessions/InvalidSessionCompletionException.cs | Distinguish expected invalid completion from internal failures |
| src/StudyPlanner.Application/Validators/Validators.cs | Validate caller, nonempty results, unique/nonempty IDs and positive minutes |
| src/StudyPlanner.Web/Controllers/StudyItemsController.cs | Set trusted caller identity, scope error-view reads and return safe 404 responses |
| src/StudyPlanner.Web/Controllers/SessionsController.cs | Use claims identity, reject model-binding failures, remove silent filtering and map expected failures |
| tests/StudyPlanner.UnitTests/StudyPlanner.UnitTests.csproj | Reference Web for controller tests; no additional NuGet packages |
| tests/StudyPlanner.UnitTests/OwnershipTests.cs | 18 item-handler/query regression cases |
| tests/StudyPlanner.UnitTests/SessionCompletionSecurityTests.cs | 21 completion-handler/validation regression cases |
| tests/StudyPlanner.UnitTests/OwnershipControllerTests.cs | 18 controller cases using real MediatR handlers and validation pipeline |
| PHASE1_SECURITY.md | Implementation, verification and limitations report |

## Enforcement and Responses

The Web boundary gets UserId from the authenticated NameIdentifier claim. Edit explicitly replaces any submitted command UserId with that claim. Delete/toggle/completion construct their commands using the claim; their action parameters do not accept a caller UserId.

Application mutation handlers independently enforce the resource's UserId against the command's trusted caller before invoking domain mutation methods. The ownership-aware detail query also prevents error-view data disclosure. Existing repository contracts remain sufficient; no repository or database changes were needed.

Completion verifies that the session exists and belongs to the caller, that its status is Planned, that results are nonempty with positive minutes and unique nonempty item IDs, and that every submitted ID is a session member whose current item exists, is not deleted, and belongs to the same caller. All item lookups and checks finish before StartSession, item mutation or ProgressRecord creation.

Valid completions still start and complete the session, mark reported items completed, set their existing seven-day review date, add progress and perform one SaveChangesAsync. Infrastructure already supplies all repositories with the same scoped EF Core context, so that save persists the complete unit of work using EF's normal SaveChanges transaction. This atomic persistence reasoning is based on source and EF usage; it was not verified against PostgreSQL in this phase.

- Missing and foreign items/sessions produce generic HTTP 404 responses.
- Malformed results, duplicates, unrelated/foreign/deleted members and invalid session states produce generic HTTP 400 responses.
- No failed completion sets the success message. Unexpected completion persistence failures propagate rather than being mislabeled as invalid user input.
- A nonempty valid subset of session items remains accepted, preserving existing handler behavior. This phase does not introduce an all-items requirement.

## Regression Coverage

57 new cases plus 13 existing tests passed: 70 total, zero failed, zero skipped.

Coverage includes legitimate owner edit/delete/toggle; foreign, missing, deleted and empty-caller mutations; scoped DTO queries; foreign/missing sessions; same-owner unrelated items; foreign items both inside and outside a session; a missing/deleted later member; duplicates; null/empty results; invalid minutes/IDs; each non-Planned state; valid full/subset completion; sequential repeat completion; forged form UserId; safe controller responses; model-binding failures; and unexpected persistence errors.

Tests exercise real handlers and real domain objects. Controller tests also exercise the actual MediatR/FluentValidation pipeline. Repositories are mocked, with assertions that invalid requests leave domain state unchanged, create no progress and call no update/save methods. These are application/controller tests, not live database or HTTP-server tests.

## Exact Verification

Working directory: `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`.

```sh
/Users/m/.dotnet/dotnet test -m:1 -p:UseSharedCompilation=false -nodeReuse:false
/Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
/Users/m/.dotnet/dotnet build StudyPlanner.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
git diff --check -- src tests
```

The first test command restored the changed test project graph and passed 69 tests. After narrowing the completion exception mapping and adding its regression case, the final test command passed 70 tests. The full solution build passed with zero errors and one existing NU1903 warning for AutoMapper 12.0.1. The diff whitespace check passed. No verification command remained blocked.

The executable above uses the installed SDK 8.0.131 required by global.json; the shell's Homebrew dotnet installation selects a different SDK. Build/test commands ran with approved sandbox escalation for internal MSBuild/test-runner communication.

## Remaining Risks and Scope

- Sequential duplicate completion is rejected, but simultaneous valid completion requests are not serialized. No row locking, concurrency token or uniqueness constraint was introduced; concurrent completion integrity needs a separate change and provider-level tests.
- Categories remain global and editable by any authenticated user; their ownership/role policy is outside the requested StudyItem/session changes.
- Existing password hashing, antiforgery omissions, review recurrence, UI and unrelated operational issues were not changed, as requested.
- No PostgreSQL, Docker, browser or end-to-end checks were executed. Actual provider rollback and concurrent-request behavior remain unverified.
- Existing command/detail-query signatures now require caller identity; every current production call site was updated. Future entry points must obtain this identity from a trusted authentication context.
- Domain entities, repository interfaces/implementations, EF mappings, migrations, seed data and database schema are unchanged. No user data was deleted. Valid owner redirects and form layouts are preserved.
- Existing Rider metadata changes, global.json and the historical review were preserved. No changes were committed or deployed.
