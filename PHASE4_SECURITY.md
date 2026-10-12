# Phase 4: Dependency Security and AutoMapper Vulnerability Remediation

Date: 2026-10-09

Repository: `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`

## A. Initial Findings

The repository was on `master`, tracking `origin/master`, with the uncommitted work from Phases 1-3, existing reports, SDK configuration and Rider metadata. Those changes were preserved. `/Users/m/.dotnet/dotnet --version` returned 8.0.131, matching `global.json`.

Baseline build: zero errors and one `NU1903` warning for AutoMapper 12.0.1. The baseline suite was rerun with the existing isolated PostgreSQL fixture enabled: **189 passed, zero failed, zero skipped**.

The Application project directly referenced both `AutoMapper` and `AutoMapper.Extensions.Microsoft.DependencyInjection`, version 12.0.1. Those packages flowed transitively into Infrastructure, Web and UnitTests. Domain did not reference them.

The original advisory is [GHSA-rvv3-g6hj-g44x / CVE-2026-32933](https://github.com/advisories/GHSA-rvv3-g6hj-g44x), rated High. It concerns uncontrolled recursive mapping of deeply nested object graphs, potentially terminating the process with stack exhaustion. Version 12.0.1 falls within its affected range. No first-party mapping invocation exposing this behavior was found; this phase removes unnecessary vulnerable dependency exposure rather than claiming a demonstrated exploit in the application.

## B. Usage Investigation

All five project files, project references, source, Razor files, tests, dependency registrations and resolved assets were examined. Searches included AutoMapper, IMapper, MapperConfiguration, CreateMap, Profile, AddAutoMapper, ProjectTo, mapping-related names and reflection/service-resolution patterns.

Only these production references were found:

| Location | Reference | Required by a working mapping flow? |
| --- | --- | --- |
| `src/StudyPlanner.Application/StudyPlanner.Application.csproj` | AutoMapper 12.0.1 | No |
| Same project file | AutoMapper.Extensions.Microsoft.DependencyInjection 12.0.1 | No |
| `src/StudyPlanner.Application/DependencyInjection.cs` | `AddAutoMapper(Assembly.GetExecutingAssembly())` | No |

There were no mapping profiles, CreateMap calls, IMapper constructor dependencies, mapper service lookups, ProjectTo calls or reflection-based AutoMapper use in first-party code. Assembly scanning for MediatR, FluentValidation and EF configurations is unrelated and was retained.

Actual DTO construction is manual in:

- User registration/login handlers: AuthDto and UserDto.
- Study item query handlers: detail and paginated StudyItemDto values.
- Category query handler: CategoryDto values and pagination.
- Dashboard query handler: counts and recent item DTOs.
- Session planning handler: StudySessionDto and member DTOs.

Historical reports mention AutoMapper, and generated baseline assets contained the packages. Neither is active mapping functionality. The new regression test mentions AutoMapper only to assert its absence; it does not depend on the removed package.

## C. Chosen Remediation

**Scenario A: remove unused AutoMapper infrastructure.**

Both package references and the single DI registration were removed. No replacement mapping library, upgrade, mapping rewrite, DTO change or new abstraction was introduced.

Dependencies were restored with the compatible SDK. Both packages are absent from every project's resolved package graph, and from the generated Web and UnitTests runtime dependency manifests. The Application assembly no longer references AutoMapper and does not register mapper services.

No NuGet auditing setting was disabled or changed, and no NoWarn entry or vulnerability suppression was added. All remaining direct package versions stayed pinned to their original values.

## D. Modified Files

These are the complete Phase 4 changes relative to the preserved initial working tree:

| File | Responsibility |
| --- | --- |
| `src/StudyPlanner.Application/StudyPlanner.Application.csproj` | Remove the two unused AutoMapper package references |
| `src/StudyPlanner.Application/DependencyInjection.cs` | Remove the unused AddAutoMapper registration |
| `tests/StudyPlanner.UnitTests/DependencyRemovalRegressionTests.cs` | Four focused DI, DTO, planning and dashboard regression tests |
| `PHASE4_SECURITY.md` | This English report |

The production change removes only three lines. SHA-256 fingerprints of 148 pre-existing source/test-tree files were captured before the edits and compared afterward: only the two listed production files changed, one test file was added, and no existing file was removed. Previous security source/tests and all migration files, including the Phase 3 snapshot, remained byte-for-byte unchanged during this phase.

## E. Dependency Audit

### Before and After

The compatible SDK's relative-solution-path package-list command encountered `MSB0001` about an unrooted path. Retrying with the absolute solution path succeeded. Both the full package listing and the online vulnerability query completed successfully; advisory lookup was not blocked.

The vulnerability query included direct and transitive packages and used `https://api.nuget.org/v3/index.json`. Before remediation it reported the original AutoMapper advisory and additional advisories described below. After remediation, the AutoMapper advisory was absent, while those unrelated findings remained.

Resolved package counts, including direct and transitive package libraries but excluding project libraries:

| Project | Before | After | AutoMapper after |
| --- | ---: | ---: | --- |
| Domain | 0 | 0 | Absent |
| Application | 11 | 5 | Absent |
| Infrastructure | 39 | 36 | Absent |
| Web | 67 | 64 | Absent |
| UnitTests | 178 | 175 | Absent |

These are per-project counts, not globally unique totals. The comparison found no added packages or changed versions among retained packages.

Application also stopped resolving Microsoft.CSharp 4.7.0, Microsoft.Extensions.Options 6.0.0, Microsoft.Extensions.Primitives 6.0.0 and System.Runtime.CompilerServices.Unsafe 6.0.0 as consequences of removing the AutoMapper chain. Infrastructure, Web and UnitTests each dropped Microsoft.CSharp 4.7.0 along with the two AutoMapper packages. Shared dependencies required by other libraries remained intact.

### Remaining Advisories

**The post-change graph is not vulnerability-free.** It contains five other flagged package identities with six unique advisories, all rated High by the feed. These were already present in the baseline and were not introduced by this phase.

| Package/version | Advisory and reported issue | Resolved in | Recommended priority |
| --- | --- | --- | --- |
| Npgsql 8.0.0 | [GHSA-x9vc-6hfv-hg8c](https://github.com/advisories/GHSA-x9vc-6hfv-hg8c): protocol message size overflow enabling SQL injection | Infrastructure, Web, tests | P1: review a compatible patched provider dependency path and rerun PostgreSQL tests |
| Microsoft.Extensions.Caching.Memory 8.0.0 | [GHSA-qj66-m88j-hmgj](https://github.com/advisories/GHSA-qj66-m88j-hmgj): hash-flooding denial of service | Infrastructure, Web, tests | P1: review compatible patched dependency/runtime assets |
| System.Text.Json 8.0.0 | [GHSA-hh2w-p6rv-4g7w](https://github.com/advisories/GHSA-hh2w-p6rv-4g7w): asynchronous enumerable deserialization DoS; [GHSA-8g4q-xg66-9fp4](https://github.com/advisories/GHSA-8g4q-xg66-9fp4): JsonExtensionData complexity DoS | Infrastructure, Web, tests | P1: assess loaded runtime assets and apply a compatible patch covering both findings |
| System.Net.Http 4.3.0 | [GHSA-7jgj-8wvc-jh57](https://github.com/advisories/GHSA-7jgj-8wvc-jh57): authentication information disclosure on redirects | Tests only in the resolved solution graphs | P2: triage the legacy test dependency chain and actual runtime asset selection |
| System.Text.RegularExpressions 4.3.0 | [GHSA-cmhx-cq75-c4mj](https://github.com/advisories/GHSA-cmhx-cq75-c4mj): regex denial of service | Tests only in the resolved solution graphs | P2: triage the legacy test dependency chain and actual runtime asset selection |

The advisory-specific minimum patched versions listed by those sources are Npgsql 8.0.3, Caching.Memory 8.0.1, System.Text.Json 8.0.4 for the first JSON issue and 8.0.5 for the second, System.Net.Http 4.3.4, and RegularExpressions 4.3.1. These historical minimum fixes are not a blanket recommendation to pin those exact versions; a future patch should evaluate current compatible versions and the complete graph.

Dependency introduction was investigated in the resolved assets:

- Npgsql is introduced by Npgsql.EntityFrameworkCore.PostgreSQL 8.0.0.
- Caching.Memory is introduced by Microsoft.EntityFrameworkCore 8.0.0.
- System.Text.Json has parents including Microsoft.Extensions.DependencyModel and configuration/logging packages.
- The legacy HTTP and regex package identities are introduced through NETStandard.Library 1.6.1 from xUnit components; regex also has a System.Xml.ReaderWriter parent.

No first-party JsonExtensionData or DeserializeAsyncEnumerable use was found. That is not proof of overall non-exploitability. Advisory detection is based on resolved package versions; assessment of the actual deployed framework assemblies, application reachability and third-party usage is separate and was not completed here. P1/P2 are proposed engineering priorities, not revised advisory severity scores.

Updating these unrelated dependencies was not required for AutoMapper removal and is explicitly outside this phase. They were documented rather than silently modernized. A successful vulnerability-query exit code means the query ran, not that its findings are clean.

## F. Dependency Injection Verification

The new production-registration test builds the Application and Infrastructure service collection with `ValidateOnBuild = true` and `ValidateScopes = true`, then resolves:

- MediatR and representative registration, dashboard and session-completion handlers.
- CompleteSessionValidator and the registration request's ValidationBehavior pipeline.
- The study session planner.
- AppDbContext and all five typed repositories.
- The framework PasswordHasher and the application's PasswordHashService.

This graph-resolution test does not connect to PostgreSQL. It verifies that removing the DI extension did not remove services required by legitimate handlers.

Separate tests host the real Program through the existing WebApplicationFactory, execute manual DTO queries and session planning, and sign in using the actual cookie/CSRF workflow before rendering the dashboard. The factory's existing repositories are controlled mocks, while MVC, Razor, routing, MediatR and authentication are real. Actual repository/database behavior is covered separately by the unchanged PostgreSQL suite.

Application startup configuration, cookie configuration, global antiforgery, Infrastructure DI and Domain entities were not edited.

## G. Test Results

| Verification | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Baseline full suite with PostgreSQL | 189 | 0 | 0 |
| Final full suite with PostgreSQL | 193 | 0 | 0 |
| Focused dependency-removal suite | 4 | 0 | 0 |
| Independent PostgreSQL suite | 16 | 0 | 0 |

Targeted counts are subsets of the full suite, not additional tests beyond 193. All earlier 189 cases passed without changes to their assertions.

The four new tests cover validated service resolution without mapper infrastructure, full-field manual item/category/dashboard DTO contracts, session/member planning DTOs, and real application startup with authenticated dashboard rendering.

The unchanged provider suite still verifies competing completion, default and single-statement batching, disjoint valid subsets, sequential repeats, independent/shared-item sessions, authorization and invalid results, commit-time rollback and cancellation rollback.

PostgreSQL 15.18 was used through the same approved isolated temporary cluster created in Phase 3. Each run created a fresh randomly named test database and applied existing migrations. A final catalog query returned zero remaining fixture databases. The cluster was stopped after testing. No application database, production connection string, real user data or Docker volume was used. PostgreSQL 16/Docker was not executed.

Working directory for the following commands: `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`.

```sh
# Baseline suite, before edits.
STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test --no-build --no-restore --logger 'trx;LogFileName=baseline.trx' --results-directory /private/tmp/studyplanner-phase4-tests

# Restore after package removal; security auditing was not disabled.
/Users/m/.dotnet/dotnet restore StudyPlanner.sln --force --no-cache --disable-parallel

# Complete final suite.
STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger 'trx;LogFileName=phase4-full.trx' --results-directory /private/tmp/studyplanner-phase4-tests

# Focused regression suites.
/Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~DependencyRemovalRegressionTests --logger 'trx;LogFileName=dependency-removal.trx' --results-directory /private/tmp/studyplanner-phase4-tests
STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~PostgresSessionConcurrencyTests --logger 'trx;LogFileName=postgres.trx' --results-directory /private/tmp/studyplanner-phase4-tests

# Both commands executed before and after remediation using absolute paths.
/Users/m/.dotnet/dotnet list /Users/m/RiderProjects/StudyPlannerProject/StudyPlanner/StudyPlanner.sln package --include-transitive --format json
/Users/m/.dotnet/dotnet list /Users/m/RiderProjects/StudyPlannerProject/StudyPlanner/StudyPlanner.sln package --vulnerable --include-transitive --format json
```

The connection above identifies a private Unix socket and contains no password. The instance is now stopped; use the safe isolated setup documented in PHASE3_SECURITY.md before reproducing provider tests. The existing opt-in skip behavior remains unchanged when `STUDYPLANNER_TEST_POSTGRES` is absent.

TRX files are under `/private/tmp/studyplanner-phase4-tests`. Their counters were inspected independently using xmllint and match the table. Temporary files are not permanent archived evidence.

## H. Build Results

Baseline and final builds used:

```sh
/Users/m/.dotnet/dotnet build StudyPlanner.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
git diff --check
```

- Baseline: zero errors, one AutoMapper NU1903 warning.
- Post-removal restore: completed for all five projects without a reported warning.
- Final complete solution build: **zero errors, zero warnings**.
- Whitespace validation: passed.

No warning suppression was introduced. The zero-warning build is not evidence that the transitive advisory list is empty: the explicit include-transitive security audit found the six remaining advisories in Section E. Those findings are identified and retained openly in this report.

## I. Compatibility

Phase 1 ownership, trusted claims, membership validation, duplicate rejection and valid-subset completion remain intact. Phase 2 modern hashing, legacy upgrade, cookie login, antiforgery and POST logout remain intact. Phase 3 concurrency tokens, safe 409 mapping and PostgreSQL transaction rollback remain intact.

Existing item create/edit and category flows covered by earlier HTTP tests passed. New tests additionally verify startup, dashboard and manual DTO mapping. No DTO/API contract, manual mapping implementation, production UI, planner behavior or authentication feature was changed.

Package comparison found only removals from the resolved graph. Retained versions were unchanged. All pre-existing migration files were unchanged from the Phase 4 baseline; the previously intentional Phase 3 snapshot modification was preserved.

No sensitive configuration or credentials were added. No commit, push, merge, deployment or history rewrite was performed. Previous reports remain historical snapshots and were not rewritten to hide their original findings.

## J. Remaining Risks

- Five other flagged package identities and six High advisories remain; they need separately scoped remediation and compatibility verification.
- Runtime asset selection and production exploitability of those findings have not been fully assessed.
- Advisory data changes over time; this audit represents the source responses obtained on the report date, not a guarantee against future disclosures.
- Provider tests used PostgreSQL 15.18, not the configured PostgreSQL 16 container or production deployment.
- Startup/dashboard HTTP tests use controlled repository storage and do not constitute browser visual verification or a full production startup/migration exercise.
- Previously documented category policy, login throttling, planner/review semantics and operational issues remain outside this phase.

No Phase 5 work was started.

## K. Final Phase Status

**COMPLETE AND VERIFIED**

AutoMapper usage was audited, unused packages and DI registration were removed, and the original advisory is absent from the restored graph. The online direct/transitive audit completed and its remaining findings are documented. All 193 configured tests passed, including the 189 original cases and 16 actual PostgreSQL cases; independent targeted suites also passed. The solution builds with zero errors and zero warnings.

This status applies to the focused AutoMapper remediation and compatibility checks. It does not describe the entire dependency graph as free of vulnerabilities or certify a production deployment.
