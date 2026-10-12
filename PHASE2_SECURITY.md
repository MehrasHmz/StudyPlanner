# Phase 2: Authentication Hardening and CSRF Protection

Completed on 2026-10-09 in the existing StudyPlanner working tree. This report describes Phase 2 only and supplements PHASE1_SECURITY.md.

## A. Initial Security Findings

Baseline branch was `master`, tracking `origin/master`. Phase 1 source edits, its three test files, its report, PROJECT_REVIEW.md, global.json and Rider metadata were already uncommitted. They were preserved. Baseline build succeeded and all 70 existing tests passed, matching the Phase 1 report.

Confirmed vulnerabilities: BCryptHelper actually computed a single SHA256 pass with a shared literal salt; handlers duplicated that code; browser POST actions had no global/server antiforgery validation; logout accepted GET and was linked as an anchor. All existing POST forms already used MVC form tag helpers. No first-party fetch/AJAX mutation requests were found.

## B. Password Hashing Changes

Application defines IPasswordHashService and PasswordVerificationOutcome. Infrastructure implements this contract using Microsoft's PasswordHasher<User>, registered through DI with IdentityV3 compatibility and 220,000 iterations. No full Identity user store or replacement authentication scheme was added.

IdentityV3 uses PBKDF2-HMAC-SHA512 with a random 128-bit salt and 256-bit derived key. Its payload includes the format marker, PRF, iteration count and salt length. See the [ASP.NET Core implementation](https://raw.githubusercontent.com/dotnet/aspnetcore/v8.0.0/src/Identity/Extensions.Core/src/PasswordHasher.cs). The configured factor follows the currently published [OWASP PBKDF2-HMAC-SHA512 guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2); production capacity still needs load testing.

Stored modern values use `Identity:<framework Base64 payload>`. The prefix explicitly separates the maintained hasher family from historical SHA256 digests, while the framework's own payload retains versioning. New values are 93 characters and fit the existing 500-character column. Hash creation always uses the configured IdentityV3 hasher.

Verification checks canonical Base64, known framework markers, bounded salt/subkey metadata, supported PRF values and a positive iteration count no greater than 1,000,000 before calling the framework verifier. This guards malformed metadata from excessive allocation/work. Supported IdentityV2 payloads can be verified only for automatic upgrade; new hashes use V3. Raising the configured factor above the verification ceiling requires an intentional accompanying ceiling update.

Domain's only supporting change is SetPasswordHash, which assigns a nonempty already-generated hash; no cryptographic implementation was added to Domain. No password or hash logging was introduced.

## C. Legacy Password Migration

The historical scheme was exactly `Base64(SHA256(UTF8(password + "StudyPlannerSalt2024")))`, producing a canonical 44-character Base64 string containing 32 bytes.

Only unprefixed values with that exact shape enter the private legacy verification branch. That branch computes the comparison digest and uses CryptographicOperations.FixedTimeEquals. It exposes no legacy hash-creation API. Modern-prefixed malformed values never fall back to legacy verification. Random historical digest bytes matching Identity's version markers remain valid legacy values because format-family detection uses the explicit prefix, not the digest's first byte.

Correct legacy credentials yield SuccessRehashNeeded. LoginHandler generates a modern hash, assigns it, touches the user, invokes the existing repository Update and awaits SaveChangesAsync before returning the authenticated user. This is required because UserRepository reads users with AsNoTracking. Failed verification returns a generic credential error without Update, Save or hash changes. Successful modern verification writes nothing unless the framework returns SuccessRehashNeeded, such as for a lower stored iteration count or older supported format.

The seeded demo hash remains exactly the existing migration's value. The seed now uses that fixed existing value rather than calculating a new legacy hash. Demo credentials were verified through the actual EF design-time seed definition and LoginHandler, without connecting to PostgreSQL. Existing accounts are not reset or deleted.

## D. CSRF Protection

Program installs AutoValidateAntiforgeryTokenAttribute globally on MVC. Standard framework validation runs before action execution for unsafe methods. Every current mutation route below uses POST and is protected, including anonymous login/registration.

| Area | Protected POST endpoints |
| --- | --- |
| Authentication | /Auth/Login, /Auth/Register, /Auth/Logout |
| Study items | /StudyItems/Create, /StudyItems/Edit, /StudyItems/Delete, /StudyItems/ToggleComplete |
| Categories | /Categories/Create, /Categories/Edit, /Categories/Delete |
| Study sessions | /Sessions/Plan, /Sessions/CompleteSession |

Logout additionally requires authorization and HttpPost; GET returns 405 without clearing the authentication cookie. Its navigation anchor was replaced by a form/button with the same dropdown classes. Form tag helpers generate the token automatically, including the new logout form. No duplicate manual tokens or JavaScript dependencies were added.

Missing, invalid, cookie-mismatched and pre-login identity tokens receive HTTP 400 before mutations. Valid requests preserve their existing success views/redirects. The sole API endpoint, GET /api/categories/list, uses cookie authentication and is read-only, so it needs no unsafe-method token; no antiforgery exemptions were introduced. Any future unsafe MVC/API action will inherit the global filter unless deliberately configured otherwise.

A narrow supporting authentication fix handles ModelState/FluentValidation failures with generic form errors, preventing malformed login/registration submissions from becoming unhandled validation errors.

## E. Test Coverage

Final results: 167 passed, zero failed, zero skipped: 70 existing Phase 1/domain/planner tests, 27 AuthenticationSecurityTests and 70 AntiforgeryIntegrationTests.

Password coverage includes modern registration, header/work-factor and column size, distinct salts for identical passwords, correct/incorrect credentials, legacy migration and no writes on failure, subsequent modern login without writes, framework-requested rehash, actual model demo seed compatibility, malformed/unsupported values, metadata bounds, no modern-to-legacy fallback, supported IdentityV2 upgrade, digest-marker ambiguity and missing-account error behavior.

HTTP coverage includes each of the 12 mutation endpoints with missing, invalid and mismatched-cookie tokens; valid token-bearing actions; single framework token per rendered POST form; token invalidation after identity change; protected POST logout and harmless GET logout; modern registration followed by login; read-only cookie API behavior; and Phase 1 IDOR/membership/duplicate protections with valid CSRF tokens.

WebApplicationFactory hosts the actual Program in Testing, with real MVC filters, routing, Razor rendering, MediatR validation/handlers, password hashing and cookie sign-in/sign-out. Test clients sign in through the application's actual Login POST; redirects or missing authentication are not treated as evidence of CSRF rejection. Repository dependencies are controlled in-memory mocks that record mutation calls and hold real domain objects; Data Protection is ephemeral. No test authentication bypass or test-only production route was added.

## F. Build and Test Results

Working directory: `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`. SDK 8.0.131 was verified at `/Users/m/.dotnet/dotnet`.

```sh
# Baseline: 70 passed and successful build.
/Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
/Users/m/.dotnet/dotnet build StudyPlanner.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false

# Final complete suite: 167 passed.
/Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger 'trx;LogFileName=all-tests.trx' --results-directory /private/tmp/studyplanner-phase2-tests

# Final complete build: zero errors, one existing warning.
/Users/m/.dotnet/dotnet build StudyPlanner.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false

# Independent final security suites: 27 and 70 passed.
/Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~AuthenticationSecurityTests --logger 'trx;LogFileName=authentication.trx' --results-directory /private/tmp/studyplanner-phase2-tests
/Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~AntiforgeryIntegrationTests --logger 'trx;LogFileName=antiforgery.trx' --results-directory /private/tmp/studyplanner-phase2-tests

git diff --check -- src tests
git diff --name-only -- src/StudyPlanner.Infrastructure/Migrations
```

The whitespace check passed and the migration diff was empty. The only final build warning is the pre-existing NU1903 advisory for AutoMapper 12.0.1, left unchanged by scope restriction.

During implementation, a misplaced using directive was corrected after an initial build failed. The first combined security run then passed 92/93 cases: seed inspection failed because EF's runtime model omits seed configuration. That test was fixed to read IDesignTimeModel, not weakened or skipped; the final 167-case run passed. Four extra compatibility cases were added afterward. No final test/build command remained blocked.

`pg_isready -h localhost -p 5432` returned no response. `docker ps` found no running daemon socket. No PostgreSQL-backed, Docker or browser-rendering smoke test was performed. TestServer provides verified HTTP-pipeline behavior, not proof of actual PostgreSQL persistence or production deployment. The seed test exercises EF model configuration without database I/O.

## G. Modified Files

These 15 files are Phase 2 changes relative to the preserved Phase 1 working tree:

| File | Purpose |
| --- | --- |
| src/StudyPlanner.Application/Authentication/IPasswordHashService.cs | Application contract and verification outcomes |
| src/StudyPlanner.Application/Commands/Users/UserHandlers.cs | Secure registration and upgrade-on-login persistence |
| src/StudyPlanner.Domain/Entities/User.cs | Assignment of an already-generated hash |
| src/StudyPlanner.Infrastructure/Authentication/PasswordHashService.cs | Framework hashing, safe decoding and isolated legacy verification |
| src/StudyPlanner.Infrastructure/DependencyInjection.cs | Hasher/service registration and work-factor policy |
| src/StudyPlanner.Infrastructure/StudyPlanner.Infrastructure.csproj | Shared-framework reference for the built-in hasher |
| src/StudyPlanner.Infrastructure/Data/AppDbContext.cs | Preserve the existing seed hash without generating new legacy hashes |
| src/StudyPlanner.Web/Program.cs | Global antiforgery filter and public partial Program for HTTP tests |
| src/StudyPlanner.Web/Controllers/AuthController.cs | POST-only authorized logout and safe validation handling |
| src/StudyPlanner.Web/Views/Shared/_Layout.cshtml | Protected logout form, preserving navigation styling |
| tests/StudyPlanner.UnitTests/StudyPlanner.UnitTests.csproj | Microsoft.AspNetCore.Mvc.Testing 8.0.0, the sole new NuGet dependency, test-only |
| tests/StudyPlanner.UnitTests/AuthenticationSecurityTests.cs | 27 password/authentication regression cases |
| tests/StudyPlanner.UnitTests/AntiforgeryIntegrationTests.cs | 70 actual HTTP-pipeline cases |
| tests/StudyPlanner.UnitTests/SecurityWebApplicationFactory.cs | Isolated host, ephemeral protection and controlled repository fixtures |
| PHASE2_SECURITY.md | Scope, evidence, compatibility and limitations report |

## H. Database and Compatibility Impact

No schema migration is required; modern hashes fit the current column. No migration, EF mapping or repository implementation was changed. Only a successful legacy/outdated-hash login writes a replacement credential using the existing repository convention. Existing valid legacy accounts remain supported until they migrate, and wrong credentials do not replace them.

No passwords were reset, no accounts/data deleted and no production data accessed. Compatibility was verified in application tests and real HTTP-pipeline tests with controlled storage; persistence through an actual PostgreSQL connection remains unverified.

## I. Remaining Security Risks

- Unused legacy accounts retain weak hashes until successful login; account recovery/forced migration is a separate design.
- Login throttling/lockout and enforcing the existing IsActive flag were not part of these two requested controls and remain unimplemented.
- Session completion concurrency, category access policy and review recurrence remain unchanged.
- AutoMapper's existing vulnerability warning remains.
- Production TLS/cookie deployment, persistent Data Protection, workload capacity, actual database persistence and browser behavior still require operational verification.
- Canonical unprefixed historical SHA256 and prefixed supported Identity payloads are the supported storage contract; arbitrary/unmarked alternative hashes are rejected, never silently deleted or converted.

## J. Phase 2 Status

COMPLETE for the requested implementation and automated acceptance criteria: secure new hashes, compatible legacy verification, upgrade-on-login, failure/no-write behavior, configured server-side antiforgery, valid protected forms, zero mutations on CSRF rejection, preserved Phase 1 regressions, successful build and 167 passing tests. Database/browser limitations are explicitly documented above.

No Phase 3 changes, commits, pushes, deployment, branch resets or production-data modifications were performed.
