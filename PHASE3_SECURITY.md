# Phase 3: Session Concurrency and Database Integrity

Date: 2026-10-09

Repository: `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`

Status: **COMPLETE AND PROVIDER-VERIFIED**

Provider verification applies to the actual PostgreSQL 15.18 instance used below. The Docker-configured PostgreSQL 16 image and a production deployment were not exercised.

## A. Initial Findings

The working tree already contained the uncommitted Phase 1 and Phase 2 work, their tests and reports, the Persian project report, `global.json`, and Rider metadata. These changes were preserved. The compatible SDK at `/Users/m/.dotnet/dotnet` reported 8.0.131. The baseline suite was rerun successfully: 167 passed, zero failed, zero skipped.

Confirmed race: `CompleteSessionHandler` loaded a tracked session, checked `Planned`, validated all submitted results, changed the session and items, added progress, then called one session-repository `SaveChangesAsync`. Two independent contexts could both read `Planned` before either saved. The session had no concurrency token, so its update matched only the primary key. Both transactions could therefore commit their own progress records, and the later update could overwrite the earlier completion graph.

The existing single-save transaction provided atomicity for each request, but did not coordinate the earlier state checks between requests. Sequential repeat rejection was already present and was preserved.

Investigation covered the four affected entities, handler and validator, repository contracts and implementations, entity mappings, controller, and previous security tests. All three repositories used by completion share the scoped `AppDbContext`. Session and item reads in this flow are tracked; the `Update` calls preserve original values for already-tracked entities. EF's automatic transaction setting is the default `WhenNeeded`.

The normal local PostgreSQL endpoint, `localhost:5432`, returned no response. Docker's daemon socket was absent. Existing Homebrew PostgreSQL binaries were available, making an isolated temporary provider test environment possible without installation or starting an existing database service.

## B. Chosen Concurrency Strategy

The existing `StudySession.Status` column is now an EF Core concurrency token, configured in Infrastructure. There is no process-local synchronization and no new Domain property.

The final session update checks both the ID and the originally loaded status. Schematic SQL, omitting other mapped columns:

```sql
UPDATE "StudySessions"
SET "Status" = @completed
WHERE "Id" = @session_id AND "Status" = @original_planned;
```

For two requests that originally loaded `Planned`, only one can commit this transition. After the winner commits, the competing update matches zero rows and EF throws `DbUpdateConcurrencyException`. The integration tests inspect actual emitted SQL to assert that the session UPDATE's WHERE clause contains the status condition.

This follows [EF Core's concurrency-token mechanism](https://learn.microsoft.com/en-us/ef/core/saving/concurrency). PostgreSQL's default Read Committed behavior waits for a competing updater when necessary and rechecks the updated row against the WHERE condition; see [PostgreSQL transaction isolation](https://www.postgresql.org/docs/15/transaction-iso.html#XACT-READ-COMMITTED). The synchronization is enforced by the database and therefore works across application instances, not just threads in one process.

Explicit row locking would require a longer transaction covering reads and a new persistence abstraction. A separate conditional state-transition command would require coordinating that command with subsequent writes. Neither was necessary for the existing tracked, single-save operation. Reusing the state column was the smallest solution protecting this transition without schema changes.

The state is changed from Planned through InProgress to Completed in memory; the one save persists the final Completed state. There is no current business operation reopening Completed sessions to Planned, which is an important assumption of this state-based token.

## C. Transaction Behavior

The business sequence remains:

1. Read the session and verify trusted caller ownership.
2. Require Planned state.
3. Validate result shape, uniqueness and positive minutes.
4. Validate every submitted item's session membership, existence, ownership and deletion status.
5. Apply session/item changes and add progress records.
6. Save all tracked changes once through the session repository.

The conditional session update, item updates and progress inserts participate in the same EF-managed transaction. The completion operation writes multiple entities, so the configured automatic transaction behavior provides this boundary. No transaction is required around the earlier reads for this particular optimistic state-transition guarantee. See [EF Core transaction behavior](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

If the concurrency update fails, none of the losing request's database changes commit, even if progress INSERTs or item UPDATEs executed earlier in the transaction. Unexpected persistence errors and cancellation also propagate rather than becoming success or validation responses.

`StudySessionRepository.SaveChangesAsync` translates only concurrency failures involving a StudySession into `SessionCompletionConflictException`. For any save failure, it clears the shared change tracker so abandoned mutations cannot be written by an accidental later save in that scope. Other exception types are rethrown unchanged. No automatic retry was introduced.

The controller forwards `HttpContext.RequestAborted` to MediatR. The handler already propagates its cancellation token through repository calls. EF/Npgsql manage transaction and connection cleanup; real-database tests verify rollback and successful completion from a fresh context after commit failure and cancellation.

The guarantee is scoped to the current single-save completion workflow and configured EF transactions. Disabling automatic transactions, reopening sessions, introducing additional saves, or bypassing this persistence path would require a new review.

## D. Code Changes

Paths below are relative to the repository root shown above. They identify Phase 3 changes, not the pre-existing uncommitted Phase 1/2 changes.

| File | Responsibility |
| --- | --- |
| `src/StudyPlanner.Infrastructure/Configurations/EntityConfigurations.cs` | Configure session Status as a concurrency token |
| `src/StudyPlanner.Infrastructure/Repositories/Repositories.cs` | Translate expected session concurrency errors and clear failed tracked work |
| `src/StudyPlanner.Application/Commands/Sessions/SessionCompletionConflictException.cs` | Provider-independent expected-conflict contract |
| `src/StudyPlanner.Web/Controllers/SessionsController.cs` | Safe HTTP 409 mapping and request cancellation propagation |
| `src/StudyPlanner.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | One metadata-only line reflecting the concurrency-token mapping |
| `tests/StudyPlanner.UnitTests/SessionConcurrencyTests.cs` | Model, application, controller and real HTTP-pipeline regression tests |
| `tests/StudyPlanner.UnitTests/PostgresTestDatabase.cs` | Explicitly opt-in, isolated database fixture and skip attributes |
| `tests/StudyPlanner.UnitTests/PostgresSessionConcurrencyTests.cs` | Actual repository/provider concurrency, rollback and compatibility tests |
| `PHASE3_SECURITY.md` | This report and reproduction instructions |

No package was added. No repository interface, generic Unit of Work, Domain entity, password-hashing implementation, UI, planner or category policy was changed in this phase.

### Migration Compatibility

No new database column, index, constraint or migration is needed: concurrency-token configuration changes the generated UPDATE predicate, not the database schema. The snapshot deliberately adds `.IsConcurrencyToken()` on the existing integer Status property. Historical migration code and its designer remain unchanged. There is no DDL or data migration to apply for this phase.

The existing initial migration was successfully applied to the isolated test database. Pre-existing nondeterministic seed metadata was not refactored.

## E. Tests

**22 new configured test cases were added: six application/HTTP cases and 16 PostgreSQL cases.** All 167 earlier tests remain present and passed in the final full run.

### Application and HTTP: Six Cases

- Confirm the concurrency token uses the existing non-shadow Status property and default transaction policy.
- Propagate an expected conflict, an unexpected persistence exception and cancellation without retry: three cases.
- Return a safe 409 from the controller, omit a success message and forward the request cancellation token.
- Return 409 through the actual HTTP pipeline with real cookie login and a valid Razor antiforgery token, without exposing internal SQL details.

The last HTTP case injects an expected repository failure; it verifies response mapping and Phase 2 integration, not database concurrency by itself.

### Actual PostgreSQL: 16 Cases

| Scenario | Cases | Verified result |
| --- | ---: | --- |
| Same session, full results or disjoint subsets, using default batching or one statement per batch | 4 | Exactly one success, one controlled conflict and only the winner's progress/item outcome |
| Sequential repeat from a fresh context | 1 | Rejected with no additional progress |
| Independent sessions with separate items | 1 | Both concurrent completions succeed |
| Different sessions sharing the same StudyItems | 1 | Both succeed and retain their own progress records |
| Foreign session, unrelated item, foreign member, deleted member, duplicate result, invalid minutes and empty results | 7 | No persisted completion or progress |
| Real database failure at transaction commit | 1 | All intended writes roll back; fresh completion subsequently succeeds |
| Cancellation before commit after DML has executed | 1 | All writes roll back and a fresh request can complete |

Race tests use independent DbContext instances and actual EF repositories. A test-only save interceptor holds both requests after validation and mutation, before either issues persistence SQL. Both original states are asserted as Planned before release. There are no arbitrary timing sleeps. Each attempt has a 30-second cancellation bound, the gate has a 20-second bound, and database commands have a 15-second timeout.

The tests assert winning progress IDs, owner, minutes, score and notes, session state and item completion flags. Disjoint-subset cases also verify that losing items stay incomplete. A losing context has no tracked pending changes, and a subsequent empty save writes nothing.

Rollback testing uses a deferred constraint trigger only in the generated test database. It raises PostgreSQL SQLSTATE `23514` at commit, after both progress INSERTs, session UPDATE and item UPDATEs have executed. A separate observer records those executed commands. Reads from an independent context then confirm Planned state, unchanged completion/review fields and zero progress. The trigger is removed in a finally block.

The cancellation test uses a test-only transaction interceptor to cancel immediately before commit after real DML. This is real PostgreSQL rollback testing with simulated cancellation timing, not a claim of testing every network cancellation scenario.

For disjoint valid subsets, the winning request's subset completes the session; the losing subset is not merged into it. That preserves existing subset semantics and makes the race outcome explicit.

## F. PostgreSQL Verification

**Actual provider verification: PASSED.**

- Server: PostgreSQL 15.18, Homebrew, arm64 macOS; confirmed with `SELECT version()`.
- Provider: the project's existing Npgsql/EF Core 8 dependencies and actual repository implementations.
- Cluster: `/private/tmp/studyplanner-phase3-pg.gxJnOo/data`.
- Endpoint: Unix socket in the private temporary directory, port identifier 55439; no TCP listener.
- Isolation: each configured test run creates a fresh `studyplanner_phase3_<random GUID>` database and applies existing migrations.
- Cleanup: the fixture drops only its own generated database. A catalog query after verification returned zero matching test databases.
- Service cleanup: `pg_ctl ... -m fast -w stop` completed successfully. The temporary server is not left running. Temporary cluster/log files remain available until normal temporary-directory cleanup.

No system software was installed, no existing database was reset, no Docker volume was changed, and no real user data was modified. The temporary cluster uses trust authentication only through the Unix socket in its private directory. No database password was printed.

Application configuration uses the named `DefaultConnection`; Docker config targets `postgres:16-alpine`. Tests never use that application connection string. They require the separate `STUDYPLANNER_TEST_POSTGRES` environment variable to identify an explicitly approved disposable server with permission to create databases.

When that variable is absent, PostgreSQL tests are explicitly skipped with an explanatory reason, not replaced by SQLite, EF InMemory or mocks. The unconfigured discovery check reported seven skipped test methods, because the two skipped theories do not expand their rows; the configured provider run executed all 16 cases.

### Reproduction With Installed PostgreSQL Tools

Run from the repository root. Use only an explicitly disposable local environment, never production. These commands create a new private cluster rather than restarting an existing service:

```sh
PGROOT=$(mktemp -d /private/tmp/studyplanner-phase3-pg.XXXXXX)
initdb -D "$PGROOT/data" -A trust -U studyplanner_test --no-locale --encoding=UTF8
pg_ctl -D "$PGROOT/data" -l "$PGROOT/server.log" -o "-c listen_addresses='' -k $PGROOT -p 55439" -w start
export STUDYPLANNER_TEST_POSTGRES="Host=$PGROOT;Port=55439;Username=studyplanner_test;Database=postgres"

/Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
/Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~PostgresSessionConcurrencyTests

pg_ctl -D "$PGROOT/data" -m fast -w stop
unset STUDYPLANNER_TEST_POSTGRES
```

Always stop the temporary instance after testing, including after a test failure. Restricted execution environments may require approval for PostgreSQL shared memory/socket creation and test-runner sockets. On a machine without the compatible SDK/tools, those prerequisites must be arranged separately; no installation was performed here.

## G. Build Results and Executed Commands

Working directory for .NET and Git commands: `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`.

The final full and provider-only commands used this non-secret temporary connection value as a command-local environment variable:

```sh
STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres'
```

It was assigned immediately before the relevant command, not to the production configuration. To rerun now, start a disposable instance first; the instance used in this report has been stopped.

Executed verification commands:

```sh
/Users/m/.dotnet/dotnet --version

# Baseline, before edits: 167 passed, zero failed/skipped.
/Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger 'trx;LogFileName=baseline.trx' --results-directory /private/tmp/studyplanner-phase3-tests

# Final full suite: 189 passed, zero failed/skipped.
STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger 'trx;LogFileName=phase3-full.trx' --results-directory /private/tmp/studyplanner-phase3-tests

# Independent actual-provider run: 16 passed, zero failed/skipped.
STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~PostgresSessionConcurrencyTests --logger 'trx;LogFileName=postgres.trx' --results-directory /private/tmp/studyplanner-phase3-tests

# App/HTTP-only run: 6 cases, no PostgreSQL dependency.
/Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~StudyPlanner.UnitTests.SessionConcurrencyTests --logger 'trx;LogFileName=application-http-only.trx' --results-directory /private/tmp/studyplanner-phase3-tests

# Unconfigured discovery check: 6 passed and 7 explicitly skipped PostgreSQL methods.
/Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~SessionConcurrencyTests --logger 'trx;LogFileName=application-http.trx' --results-directory /private/tmp/studyplanner-phase3-tests

/Users/m/.dotnet/dotnet build StudyPlanner.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
git diff --check
git diff -- src/StudyPlanner.Infrastructure/Migrations
```

Final build: **zero errors, one warning**. The warning is the existing AutoMapper 12.0.1 `NU1903` advisory, intentionally unchanged by scope. Whitespace validation passed. Migration review showed only the deliberate single concurrency-metadata line in the snapshot.

The first sandboxed baseline attempt could not open the test-runner socket; an approved execution then passed all 167 baseline tests. During implementation, a test assertion's unsupported expression-tree syntax was corrected. An initial 186-case run had three test-fixture failures: missing Notes in the HTTP form and a gate unable to handle an empty post-conflict save. Those fixture defects were corrected without weakening assertions. Subsequent 186- and 189-case runs passed; the final full verification passed all 189.

TRX evidence is in `/private/tmp/studyplanner-phase3-tests`; it is temporary storage, not a permanent repository artifact. Final full-suite totals count each configured test once; targeted reruns do not add to 189.

## H. Compatibility

- Trusted claims remain the source of caller identity.
- Foreign or missing sessions still produce 404 through the controller.
- Invalid membership, duplicate results, malformed input and sequential repeat still produce the existing 400 behavior.
- Only a genuine save-time session concurrency conflict now produces safe HTTP 409.
- Valid complete and valid-subset workflows retain their success redirect.
- Concurrent completion of different sessions, including shared items, remains supported.
- Secure password hashing, legacy upgrade-on-login, global antiforgery and POST logout were not changed and passed their existing regression tests.
- Phase 1 ownership and pre-mutation validation remain unchanged and passed.
- No SQL details are serialized in the expected conflict response; unexpected persistence failures remain distinguishable and are not swallowed.
- No commit, push, merge, deployment or Git-history operation was performed.

## I. Remaining Risks

This is provider verification for the scoped completion operation, not a general production-readiness certificate.

- PostgreSQL 16 in Docker, production infrastructure, browser UI and load behavior remain unverified.
- The token protects session state transitions, not every possible concurrent change to session metadata or StudyItems by other workflows. Different sessions can share items; progress remains separate, while the shared item's review fields keep the existing last-writer behavior.
- Any future reopening of Completed sessions, multiple-save workflow or direct SQL completion path needs explicit compatibility review. Direct writes bypassing application safeguards are not made safe by EF metadata alone.
- No broad uniqueness constraint was added. Existing historical duplicate progress, if any, was not investigated or repaired in a real user database.
- Transport/connection loss around commit can leave an unknown client-visible outcome; no automatic non-idempotent retry was added. A later request must reload current session state.
- Existing AutoMapper warning, category policy, login throttling, spaced-review behavior, nondeterministic seed data, Makefile and deployment concerns remain outside this phase.

No unrelated phase was started.

## J. Phase Status

**COMPLETE AND PROVIDER-VERIFIED**

The implemented status transition prevents two competing completions from both committing for one Planned session. The final suite passed 189 cases, including all 167 existing cases and 16 actual PostgreSQL concurrency/integrity cases. An independent provider run also passed all 16. Real commit failure and cancellation rolled back the intended unit of work, and previous authorization, subset and CSRF behavior remained intact.

This designation is supported by PostgreSQL 15.18 testing with the project's EF Core/Npgsql repositories. It does not assert PostgreSQL 16 container execution or full production deployment verification.
