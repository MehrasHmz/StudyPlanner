# StudyPlanner Project Review

Reviewed on 2026-10-09 against local working tree based on commit `2b96cf6`.

## Scope and Verification

Reviewed the first-party Domain, Application, Infrastructure and Web implementation, controllers, Razor views, styles, JavaScript, project dependencies, database configuration, initial migration, seed data, Docker setup, Makefile, README and unit tests. Third-party vendored library internals, IDE metadata and binary media content are outside the source review.

- Full solution build succeeded: zero errors, one NuGet warning.
- All 13 unit tests passed: six StudyItem tests, three StudySession tests and four planner tests.
- NuGet emitted NU1903 for AutoMapper 12.0.1, referencing GHSA-rvv3-g6hj-g44x. This records the tooling result; the advisory's applicability to application usage was not separately investigated.
- `make -n build` failed: `No rule to make target dotnet, needed by build`.
- PostgreSQL operations, container startup, browser rendering and authenticated end-to-end flows were not executed. Behavioral findings below come from source inspection unless explicitly described as a command result.
- Existing changes before review: modified Rider workspace metadata and untracked `global.json`. These were preserved.

The shell's default `dotnet` selects Homebrew SDK 10.0.401, which cannot satisfy the pinned SDK 8.0.131. A compatible installation exists at `/Users/m/.dotnet/dotnet`; the successful checks used that executable. The initial sandboxed test attempt failed on MSBuild internal pipe permissions; the approved rerun succeeded.

```sh
cd /Users/m/RiderProjects/StudyPlannerProject/StudyPlanner
/Users/m/.dotnet/dotnet test --no-restore
/Users/m/.dotnet/dotnet build StudyPlanner.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```

## Architecture and Ownership

| Project | Responsibilities | References |
| --- | --- | --- |
| StudyPlanner.Domain | Entities, invariants, enums, repository contracts, session selection | No project dependencies |
| StudyPlanner.Application | Commands, queries, handlers, DTOs, validation pipeline | Domain |
| StudyPlanner.Infrastructure | PostgreSQL EF Core context, entity mappings, repositories, migration and seeds | Application, Domain |
| StudyPlanner.Web | Startup, cookie authentication, MVC controllers, one API controller, Razor UI and assets | Application, Infrastructure, Domain |
| StudyPlanner.UnitTests | Domain and planner tests | Domain, Application |

Request flow: routing/authentication -> controller -> MediatR -> FluentValidation behavior -> handler -> domain/repository -> shared scoped AppDbContext -> PostgreSQL -> manually constructed DTO -> Razor or JSON response.

Repository instances share one scoped AppDbContext. Consequently, the single SaveChangesAsync in CompleteSessionHandler persists tracked session/item changes and newly added progress records together. No separate unit-of-work interface is implemented.

Application registers MediatR handlers and validators by assembly scan, transient validation behavior, AutoMapper and a singleton stateless planner. Infrastructure registers the DbContext and repositories as scoped services. AutoMapper is registered and referenced but no profiles or IMapper usage were found; DTO mapping is manual.

## Domain and Database Model

| Entity | Main fields and behavior |
| --- | --- |
| User | Name, normalized email, password hash, IsActive, timestamps; owns items, sessions and progress |
| Category | Name, description, soft deletion; shared globally, without UserId |
| StudyItem | Title, description, type, difficulty, duration, priority, next review, completion/deletion flags, user/category |
| StudySession | User, planned date, available time, status, planned item/minute totals |
| StudySessionItem | Session/item association, planned minutes and order |
| ProgressRecord | User/item, optional SessionId, completion time, minutes, optional score and notes |

Identifiers are GUIDs. AuditableEntity initializes CreatedAt using UTC; UpdatedAt changes only where Touch is explicitly called. Entity equality compares IDs without checking concrete entity type.

StudyItem enforces nonempty title, positive duration and priority 1-5. Its constructor and update methods do not check enum membership or referenced entity ownership/existence. Completing an item clears its review date; marking it incomplete makes it due immediately. Domain constructors for session, session item and progress do not enforce positive time or cross-entity consistency.

Session status transitions are Planned -> InProgress -> Completed; cancellation is allowed except after completion. AddItem recalculates totals but does not itself enforce time budget, unique item membership or matching SessionId.

EF maps six tables with PostgreSQL UUIDs, integer enums and timestamp-with-time-zone dates. Email and category name are unique. User relationships cascade on physical deletion; item references from progress/session items and category references use Restrict. ProgressRecord.SessionId is a scalar without a configured foreign key to StudySessions. No unique constraint prevents duplicate session/item pairs.

Category and StudyItem have global soft-deletion query filters. Required category joins combined with the category filter can hide still-active study items when their category is deleted. This affects repository methods that Include Category and therefore dashboard/planning/list behavior; provider-level behavior was not exercised.

There is one checked-in migration, `20260712092810_InitialCreate`, and its designer/snapshot. It seeds one demo user, three categories and four sample items. The migration embeds fixed timestamps and GUIDs, while current model seed construction uses DateTime.UtcNow and randomly generated StudyItem IDs. Future migration scaffolding can therefore produce unintended seed deletions/insertions and date changes.

## User Workflows

| Workflow | Implementation and details |
| --- | --- |
| Anonymous home | Home.Index renders standalone Landing view |
| Registration | Validates command, checks email, hashes password, inserts User, redirects to login |
| Login | Reads user by email, compares hash, issues cookie with ID/name/email claims, redirects to dashboard |
| Dashboard | Loads all user items and sessions, sums recorded minutes, counts completion/review, selects five newest items |
| Study item management | List/search/sort with ten items per page; create/edit; soft delete; completion toggle |
| Categories | Shared list/create/edit/soft delete; controller uses a maximum 100-item query for edit lookup and selectors |
| Session planning | Accepts time budget, selects user items, stores a new Planned session, renders its items |
| Session completion | Posts hidden estimated times as actual times, adds progress, completes submitted items and session |
| JSON API | Authorized GET `/api/categories/list`; returns at most 100 categories |

Search checks lowercased title/description. Sort fields are title, priority, difficulty and created; unknown sort defaults to newest first. Page and page size are not validated. Category list has a paginated result but its view has no pagination controls.

The API uses the same cookie authentication as MVC. AuthDto.AccessToken is an unused `token-<guid>` string, not a JWT or configured bearer credential. Swagger is enabled only in Development and has no explicit authentication scheme configuration.

Registration email checks lowercase input without trimming, while User.SetEmail trims and lowercases. Whitespace variants can bypass the precheck and then hit the unique database constraint. IsActive is not checked in LoginHandler.

There are no implemented password reset, email verification, profile management, session history/detail retrieval UI, timer, per-item result editing, progress history screen or review interval adaptation from score. ProgressRecordDto and several repository methods have no current UI consumer.

## Planner Algorithm

StudySessionPlanner filters completed items and items longer than the total budget. It scores each remaining item as `priority * 20 + difficultyWeight * 10 + reviewBonus`, sorts descending, then greedily takes each item that fits remaining time. Difficulty weights are 1, 2 and 3. Equal scores preserve input order, whose repository query has no explicit ordering.

| Review age in days | Bonus |
| --- | --- |
| At least 14 overdue | 200 |
| At least 7 overdue | 150 |
| At least 3 overdue | 100 |
| At least 1 overdue | 60 |
| Due now or less than 1 day overdue | 40 |
| Future review | max(0, 20 + overdueDays * 5) |
| No review date | 0 |

Future-dated items can still be selected. The algorithm prioritizes score, not maximum time utilization or globally optimal total score. Selection is approximately O(n log n). It does not independently exclude soft-deleted items, relying on repository filters.

## Interface and Assets

Razor MVC is the actual frontend; there is no SPA, Node package manifest or frontend bundler. Layout, Landing, Login and Register load Bootstrap 5.3.3, Bootstrap Icons 1.11.3 and Inter from external CDNs. Vendored Bootstrap/jQuery/validation assets exist, but the main views use CDN Bootstrap; the validation partial is not referenced by the current form views.

Dark mode is default. The theme is saved under `studyplanner-theme` in localStorage and applied through data-theme/data-bs-theme. Theme, password visibility, scroll behavior and animation logic are inline and duplicated across standalone pages/layout. `wwwroot/js/site.js` contains additional initialization but is not included by those views. Template scoped layout CSS is also not linked.

The shared stylesheet contains 1,265 lines, including theme tokens, components, viewport-scaled sizes, breakpoints at 991/767/480/400 pixels, landscape adjustments, printing and reduced-motion rules. Standalone auth views each contain 413 lines, and Landing contains 557, with substantial inline styling duplication. Forms use name-based inputs and ViewBag errors instead of strongly bound field helpers; failed create submissions do not repopulate input values, and failed edit submissions generally reload saved values.

Two autoplay background videos total approximately 48 MB under wwwroot/videos, with another approximately 48 MB copy outside the repository. Images/logos outside StudyPlanner are not served by the Web project. Videos have no poster images. Reduced-motion rules hide the dashboard hero video but do not stop/hide the standalone landing/auth videos. Several icon-only controls lack accessible names and labels generally lack explicit for/id associations. Browser accessibility/layout behavior was not measured.

## Startup and Operations

Program creates Serilog console and rolling daily file logging, configures MVC, application/infrastructure services, Swagger and cookie authentication, then serves static files, routing, authentication, authorization and conventional controller routes.

- Development startup synchronously applies migrations and enables Swagger. Startup requires an available database in this environment.
- Production does not automatically migrate the database. No production exception-handler middleware, HSTS or HTTPS-redirection middleware is configured.
- The cookie ticket lifetime is seven days. SignInAsync does not set IsPersistent, so this is not an explicit persistent-browser-login feature.
- Local HTTP profile uses port 5274; HTTPS profile also uses 7275. Local database configuration targets PostgreSQL on localhost:5432.
- Docker uses .NET 8 SDK/runtime images and PostgreSQL 16 Alpine. Compose exposes app 8080 and database 5432, waits for database health, and keeps PostgreSQL data in a named volume.
- Compose defaults to Development and requires user/password variables. `.env.example` supplies placeholders; credentials are omitted from this report.
- App logs and ASP.NET Data Protection keys have no explicit persistent Compose mounts. Container replacement can invalidate authentication cookies.
- Docker build context includes global.json; a future SDK image incompatible with its pin can affect build selection. This was not tested with Docker.
- Makefile puts shell commands on target declaration lines as prerequisites instead of tab-indented recipes. README's make commands therefore do not work as documented.
- `.gitignore` excludes build artifacts, logs and .env, but Rider metadata remains tracked. No CI workflow or dedicated lint/format configuration was found in the inspected repository.

## Findings and Evidence

### High Priority

1. **Cross-user mutation of study items.** GET Edit checks ownership, but POST Edit, Delete and ToggleComplete send ID-only commands; handlers load by ID and do not compare UserId with the current principal. An authenticated user with another item's ID can mutate it. Evidence: `src/StudyPlanner.Web/Controllers/StudyItemsController.cs:62` and `src/StudyPlanner.Application/Commands/StudyItems/StudyItemHandlers.cs:19`.
2. **Session completion trusts client IDs.** CompleteSessionCommand has no caller UserId, its handler does not check session ownership, and submitted item IDs are not checked against session membership or ownership. Duplicate results are also accepted. Evidence: `src/StudyPlanner.Application/Commands/Sessions/SessionCommands.cs:6` and `SessionHandlers.cs:42`.
3. **Antiforgery tokens are not validated.** Razor form tag helpers can emit tokens, but no ValidateAntiForgeryToken attribute or global automatic validation filter is configured. Cookie-authenticated mutations therefore lack explicit server token enforcement. Evidence: `src/StudyPlanner.Web/Program.cs:13` and controller POST methods. Cookie SameSite behavior does not replace token validation.
4. **Password storage uses a fast hash and fixed salt.** Despite the helper name BCryptHelper, it computes one SHA256 pass with an application-wide literal salt. Evidence: `src/StudyPlanner.Application/Commands/Users/UserHandlers.cs:21` and `:40`.
5. **Scheduled reviews never reactivate completed items.** Completion sets IsCompleted=true and NextReviewDate=now+7 days, while the planner and dashboard exclude completed items. Those items remain excluded even after the scheduled date unless manually toggled incomplete. Evidence: `src/StudyPlanner.Application/Commands/Sessions/SessionHandlers.cs:50`, `src/StudyPlanner.Domain/Services/StudySessionPlanner.cs:11`, and `src/StudyPlanner.Application/Queries/Users/UserQueryHandlers.cs:20`.

### Functional and Consistency Issues

6. **Auth validation exceptions are unhandled.** Login catches UnauthorizedAccessException and Register catches InvalidOperationException, but both commands have FluentValidation validators that throw ValidationException. Invalid submissions can become server errors rather than field errors. Evidence: `src/StudyPlanner.Web/Controllers/AuthController.cs:36` and `:51`.
7. **Broken dashboard quick action.** The Plan Session anchor issues GET to Sessions.Plan, whose only action is HttpPost. Evidence: `src/StudyPlanner.Web/Views/Home/Index.cshtml:143` and `src/StudyPlanner.Web/Controllers/SessionsController.cs:16`.
8. **Global category mutation.** Any authenticated user can rename or delete every category because Category has no user or role scope. The desired sharing policy is not documented. Deleting a category can also hide items from other users through required joins. Evidence: `src/StudyPlanner.Domain/Entities/Category.cs` and `src/StudyPlanner.Infrastructure/Configurations/EntityConfigurations.cs:24`.
9. **Session state is not retry friendly.** Completion unconditionally starts a session before completing it; a repeated submission to an already completed session throws. No separate start/cancel/history workflow is exposed. Evidence: `src/StudyPlanner.Application/Commands/Sessions/SessionHandlers.cs:45`.
10. **Progress does not measure actual effort.** The view sends PlannedMinutes as MinutesSpent with empty score/notes and has no result editing. SessionsController can also show success for an empty valid-results list without completing anything. Evidence: `src/StudyPlanner.Web/Views/Sessions/Plan.cshtml` and `src/StudyPlanner.Web/Controllers/SessionsController.cs:34`.
11. **Incomplete validation.** UpdateCategory has no validator; UpdateStudyItem does not enforce description length, enum membership or category existence; result minutes/scores/membership are not validated by CompleteSessionValidator; pagination is unchecked. Evidence: `src/StudyPlanner.Application/Validators/Validators.cs`.
12. **Category lookup caps.** Edit and item category selectors only load the first 100 categories; Category Index cannot navigate later pages through its UI. Evidence: `src/StudyPlanner.Web/Controllers/CategoriesController.cs:29`, `StudyItemsController.cs:105`, and `Views/Categories/Index.cshtml`.
13. **Seed definitions are nondeterministic.** Dynamic IDs/timestamps in HasData differ from the checked-in snapshot on later model construction. Evidence: `src/StudyPlanner.Infrastructure/Data/AppDbContext.cs:49` and `:52`.
14. **Form error handling loses input and exposes exception messages.** Create fields have no binding to returned commands; edits reload persisted DTOs; several controllers display raw Exception.Message. Evidence: create/edit views and catch blocks in category/item/session controllers.
15. **Date normalization relabels instants.** StudyItem.ToUtc uses SpecifyKind rather than converting an actual Local date; DbContext only relabels Unspecified values. Date-only form input is interpreted as UTC midnight without a user timezone policy. Evidence: `src/StudyPlanner.Domain/Entities/StudyItem.cs:48` and `src/StudyPlanner.Infrastructure/Data/AppDbContext.cs:25`.

### Maintainability and Operational Gaps

16. The tested Makefile build target fails as documented above; commands need actual recipes before README make shortcuts can be relied on.
17. AutoMapper is unused and generated a high-severity vulnerability warning during build/test. Removal or an evaluated upgrade would eliminate the unused dependency surface.
18. Dashboard queries materialize all items and sessions to compute small counts/recent lists; search lowercases columns and scans substring matches. These patterns need reconsideration if data grows.
19. No tests currently cover handlers, authorization, cookie/antiforgery behavior, validation rendering, EF mappings, seed stability, review recurrence or HTTP routes. The passing 13 tests do not validate those areas.
20. The README's production-ready, spaced-repetition and account-isolation descriptions exceed what the current source and checks establish. Privacy and Error view/model template remnants have no corresponding Home actions configured.

## Practical Map for Future Changes

- Access control: StudyItemsController, SessionsController, command records/handlers and repository lookup contracts.
- Review scheduling: StudyItem completion semantics, StudySessionPlanner, CompleteSessionHandler and dashboard due counts together.
- Password/auth changes: UserHandlers, AuthController, validators, seed hash/migrations and cookie configuration.
- Data model changes: entities -> EntityConfigurations -> AppDbContext -> migration/snapshot -> related DTOs and handlers.
- User-facing forms: matching Razor view, controller binding/error handling, command validator and DTO.
- Theme/layout changes: site.css, shared layout and standalone Landing/Login/Register views; site.js currently has no loaded entry point.
- Deployment changes: Program, Dockerfile, docker-compose.yml, .env.example, global.json, Makefile and README.

Priority for subsequent implementation is ownership checks and antiforgery, then session/result integrity and password storage, followed by review recurrence and validation UX. This review adds documentation only; no application behavior was changed.
