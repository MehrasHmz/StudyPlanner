# StudyPlanner Phase 5 Final Report

Verification date: October 10, 2026 (Asia/Tehran).

Repository: `/Users/m/RiderProjects/StudyPlannerProject/StudyPlanner`.

This report covers Phase 5 only. Earlier security work and unrelated uncommitted changes were preserved. No commit, push, merge, deployment, production configuration change, or subsequent development phase was performed.

## A. Initial Assessment

The application remains ASP.NET Core 8 Razor MVC with Onion Architecture, MediatR, FluentValidation, EF Core and PostgreSQL. The actual initial baseline was 193 passing .NET tests, including 16 real PostgreSQL cases. The initial build had no errors or warnings.

The dependency graph still contained the previously reported Npgsql, memory-cache and JSON vulnerabilities, plus test-only HTTP and regular-expression findings. Package versions alone did not describe the effective runtime, because shared-framework assemblies can be selected by the .NET host.

The frontend had duplicated authentication/landing CSS and scripts, heavy autoplay background media, inconsistent navigation and forms, and inconsistent dark/light presentation. The dashboard included a planning link to a POST-only action. Session completion displayed optional notes, but the non-nullable DTO property caused blank notes to fail MVC validation. The session results needed editable actual minutes rather than an opaque submission of planned values.

During actual browser verification, two additional regressions in the initial Phase 5 edits were found and fixed before delivery: missing hidden edit identifiers, and a visually hidden table-header label escaping its scroll container and causing mobile page overflow. Table badges were also prevented from wrapping into individual letters.

## B. Dependency Security

### Package Changes

| Package | Before | Final | Strategy |
| --- | --- | --- | --- |
| Microsoft.EntityFrameworkCore | 8.0.0 | 8.0.31 | Compatible EF Core 8 patch upgrade |
| Microsoft.EntityFrameworkCore.Design | 8.0.0 | 8.0.31 | Align design-time tools with core |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.0 | 8.0.11 | Compatible provider upgrade |
| Npgsql, transitive | 8.0.0 | 8.0.6 | Resolved through the updated provider |
| Microsoft.Extensions.Caching.Memory, transitive | 8.0.0 | 8.0.1 | Resolved through updated EF dependencies |
| System.Text.Json | 8.0.0, transitive | 8.0.6 | Direct minimum version constraint on an existing dependency |
| System.Net.Http, test graph | 4.3.0 | 4.3.4 | Targeted test-only version constraint |
| System.Text.RegularExpressions, test graph | 4.3.0 | 4.3.1 | Targeted test-only version constraint |

No test-framework migration or EF major-version upgrade was introduced. Direct constraints do not introduce a new functional library: these packages were already in the dependency graph.

The Infrastructure project's standalone restore resolves EF Relational 8.0.11 through the provider. The Web/test graph resolves EF Relational 8.0.31 through EF Design. Both stay within EF 8; the combined application graph and actual PostgreSQL execution were verified.

### Targeted Advisories

- Npgsql 8.0.6 is outside the affected 8.x range, which ends before 8.0.3. [GHSA-x9vc-6hfv-hg8c](https://github.com/advisories/GHSA-x9vc-6hfv-hg8c).
- Caching.Memory 8.0.1 is the patched 8.x package for the reported denial-of-service advisory. [GHSA-qj66-m88j-hmgj](https://github.com/advisories/GHSA-qj66-m88j-hmgj).
- JSON 8.0.6 exceeds the patched package versions 8.0.4 and 8.0.5 for both reported advisories. [GHSA-hh2w-p6rv-4g7w](https://github.com/advisories/GHSA-hh2w-p6rv-4g7w), [GHSA-8g4q-xg66-9fp4](https://github.com/advisories/GHSA-8g4q-xg66-9fp4).

The final online NuGet audit included direct and transitive packages for all five solution projects. It returned **zero reported vulnerable packages**. Auditing was not disabled, warnings were not suppressed, and forced/no-cache restore with `NuGetAuditMode=all` succeeded.

Saved machine-readable evidence: `PHASE5_EVIDENCE/nuget-audit.json`. An empty vulnerability listing is a dated result from NuGet's advisory source, not a claim that the entire application is vulnerability-free.

### Effective Runtime

The verification machine used SDK 8.0.131 and installed Microsoft.NETCore.App / Microsoft.AspNetCore.App 8.0.31. The dependency regression test recorded actual loaded assembly locations and informational versions in the final TRX:

| Assembly | Actual Loaded Informational Version | Origin |
| --- | --- | --- |
| Npgsql | 8.0.6 | Application/test output |
| Microsoft.Extensions.Caching.Memory | 8.0.10 | ASP.NET Core shared framework 8.0.31 |
| System.Text.Json | 8.0.31 | .NET shared framework 8.0.31 |
| Microsoft.EntityFrameworkCore | 8.0.31 | Application/test output |

These observations concern this machine's execution. Production hosts and self-contained deployment outputs must separately be kept patched and inspected; a package update is not proof that an unknown deployment runtime is patched.

Checkpoint A completed before frontend work: 194 tests passed (193 existing plus the new dependency regression), including the 16 PostgreSQL cases.

## C. Frontend Improvements

### Shared Layout and Design System

The shared layout now serves landing, authentication, dashboard and management pages consistently. It provides a skip link, semantic main content, current-page navigation, responsive Bootstrap navigation, named theme controls and POST-only sign-out with antiforgery protection.

Reusable CSS tokens define neutral dark and light surfaces, readable text, restrained green actions and distinct success/warning/error/information colors. Controls have consistent spacing, focus indicators and modest corner radii. Repeated opacity/entrance effects were removed. Theme preference is restored before the stylesheet renders.

Existing Bootstrap 5.3.3 and Bootstrap Icons 1.11.3 assets were vendored locally, with licenses, rather than adding a framework or relying on runtime CDN requests.

### Pages

| Page or Component | Delivered Improvement |
| --- | --- |
| Landing | Static generated study-workspace image, visible StudyPlanner headline, account actions, useful next-section content; no autoplay video |
| Login / registration | Shared layout, labeled fields, autocomplete, named password visibility buttons, preserved attempted name/email and never a re-rendered password |
| Dashboard | Real data metrics, recent study items, restrained two-column work surface, planning and Focus actions; corrected planning link |
| StudyItems | Labeled filters, readable table, local horizontal table scrolling, named row actions, Focus entry per owned item |
| StudyItem create/edit | Associated labels, Razor model binding, default duration 30 and priority 3, validation summary focus, correctly bound hidden edit ID |
| Categories | Consistent cards/forms, labeled controls, shared confirmation dialog, correctly bound hidden edit ID; ownership policy unchanged |
| Session planner | Clear bounded duration input and Focus entry |
| Session results | Actual editable minutes, optional score/notes, sequential result binding, concise overview, explicit completion confirmation |
| Feedback | Persistent dismissible success messages, semantic error summaries, native confirmation dialogs with keyboard cancellation |

The nullable notes DTO and normalization to an empty domain string are the only new session-handler behavior in Phase 5. Existing ownership validation, transactional completion and concurrency checks remain intact. No planner algorithm was changed.

## D. Focus Mode Implementation

### Workflow and Ownership

Authenticated users can enter Focus from the main navigation, dashboard, StudyItem rows or planned-session items. The controller uses the trusted current-user identifier and existing owner-scoped queries. Foreign, missing and deleted selected items return 404. The selector lists at most 100 owned items; an item-specific entry is resolved separately.

Users can select an owned item or edit a custom task title up to 200 characters. Razor encodes server-provided titles, and client task rendering uses `textContent`, never executable HTML. A custom title remains intact after refreshing the same item-specific URL.

### Timer Modes and Accuracy

Countdown supports 25, 50 and 90 minute presets and integer custom durations from 1 to 480 minutes. Stopwatch starts at zero. Both support start, pause, resume and reset. Countdown completion displays a concise message and a start-another action. Changing modes resets stale timing references.

The pure timer module separates state calculations from DOM rendering. Elapsed time is accumulated from wall-clock timestamps; a single 250 ms interval only redraws the display. Delayed callbacks do not reduce time accuracy by losing interval ticks. Remaining time is clamped to zero, and the display uses HH:MM:SS.

Pause excludes paused time. Running state is synchronized before navigation/page hiding and when visibility changes. Event listeners and the update interval are cleaned up; repeated initialization is guarded. Browser verification covered clean destruction and reinitialization with exactly one interval.

Backwards wall-clock changes do not subtract accumulated elapsed time. Forward clock changes cannot be distinguished reliably from device sleep by a local wall-clock timer. Across reloads, an adjusted OS clock may also affect the restored elapsed time. There is no server clock or background-service guarantee, and this limitation is intentional and documented.

### Persistence and Progress

Local storage key: `studyplanner:focus:v1:<user-id>`. Persisted fields include version, task, entry item, mode, duration, status, elapsed time, running timestamp and optional clock preference. Invalid persisted data falls back to a fresh timer. Storage failures leave the current timer usable with a notice. Other tabs receive storage changes, and sign-out removes the current user's timer key.

Refresh restores running, paused and completed states. A countdown that expires while the Focus page is absent is completed when the user returns. Persistence is browser-local, not cross-device. Multiple tabs reflect saved changes but are not a server-coordinated timer lock.

Focus sends no mutation request and does not create ProgressRecords or complete StudyItems. The footer explicitly distinguishes local focus time from stored study progress. Completion sounds, notification permission requests and background media were not added.

### Fullscreen and Layout

The standalone view has a large central timer, task information, segmented timer modes, minimal controls, optional local clock, theme switching and an explicit exit to the dashboard. It shares the application's visual tokens.

The browser Fullscreen API was exercised successfully in actual Chrome. Unsupported/denied requests use a fixed full-viewport fallback. `fullscreenchange` updates the control state; Escape exits the fallback; stale fallback notices clear on exit. Native task dialogs support Escape cancellation. Long unbroken task titles wrap without covering the timer; very long mobile titles can require vertical scrolling.

## E. Files Modified

Paths below are relative to the repository stated at the beginning of this report. This list identifies Phase 5 changes, not every pre-existing dirty file.

### Security and Narrow Backend Changes

- `src/StudyPlanner.Infrastructure/StudyPlanner.Infrastructure.csproj`: EF/provider patches and JSON constraint.
- `src/StudyPlanner.Web/StudyPlanner.Web.csproj`: EF Design patch.
- `tests/StudyPlanner.UnitTests/StudyPlanner.UnitTests.csproj`: test-only HTTP/regex constraints.
- `src/StudyPlanner.Application/Commands/Sessions/SessionCommands.cs`: nullable optional result notes.
- `src/StudyPlanner.Application/Commands/Sessions/SessionHandlers.cs`: normalize omitted notes without schema changes.
- `src/StudyPlanner.Web/Controllers/AuthController.cs`: preserve attempted non-secret form values.

### Focus Additions

- `src/StudyPlanner.Web/Controllers/FocusController.cs`: authenticated owner-scoped GET workflow.
- `src/StudyPlanner.Web/Models/FocusViewModel.cs`: selector and item-specific entry model.
- `src/StudyPlanner.Web/Views/Focus/Index.cshtml`: standalone timer and task dialog.
- `src/StudyPlanner.Web/wwwroot/css/focus.css`: responsive Focus surface.
- `src/StudyPlanner.Web/wwwroot/js/focus-timer.js`: independently testable time/state calculations.
- `src/StudyPlanner.Web/wwwroot/js/focus.js`: DOM controls, storage and fullscreen lifecycle.

### Existing Views Updated

- `src/StudyPlanner.Web/Views/Shared/_Layout.cshtml`.
- `src/StudyPlanner.Web/Views/Home/Index.cshtml` and `Landing.cshtml`.
- `src/StudyPlanner.Web/Views/Auth/Login.cshtml` and `Register.cshtml`.
- `src/StudyPlanner.Web/Views/StudyItems/Index.cshtml`, `Create.cshtml` and `Edit.cshtml`.
- `src/StudyPlanner.Web/Views/Categories/Index.cshtml`, `Create.cshtml` and `Edit.cshtml`.
- `src/StudyPlanner.Web/Views/Sessions/Index.cshtml` and `Plan.cshtml`.

### Shared Assets, Tests and Evidence

- `src/StudyPlanner.Web/wwwroot/css/site.css`: shared design system and responsive fixes.
- `src/StudyPlanner.Web/wwwroot/js/site.js`: shared theme, password, confirmation and feedback behavior.
- `src/StudyPlanner.Web/wwwroot/js/theme.js`: early theme restoration.
- `src/StudyPlanner.Web/wwwroot/images/study-workspace.png`: static landing bitmap.
- `src/StudyPlanner.Web/wwwroot/vendor/bootstrap-5.3.3/bootstrap.min.css`, `bootstrap.bundle.min.js`, `LICENSE`: local existing-version assets.
- `src/StudyPlanner.Web/wwwroot/vendor/bootstrap-icons-1.11.3/font/bootstrap-icons.css`, `font/fonts/bootstrap-icons.woff2`, `LICENSE`: local existing-version icon assets.
- `tests/StudyPlanner.UnitTests/DependencySecurityTests.cs`: effective assemblies and concurrency mapping regression.
- `tests/StudyPlanner.UnitTests/FocusWorkflowTests.cs`: authorization, safe output, no progress writes and optional-notes HTTP coverage.
- `tests/frontend/focus-timer.test.cjs`: pure timer tests.
- `tests/frontend/verify-ui.cjs`: repeatable real-Chrome workflows and responsive screenshot checks.
- `PHASE5_REPORT.md` and `PHASE5_EVIDENCE/`: final report and saved audit/test/browser evidence.

Earlier Phase 1-4 files, the Phase 3 concurrency snapshot line, project SDK pin and unrelated IDE metadata were preserved. Original background video files were not deleted; updated views no longer load them.

## F. Tests

### Final Results

| Verification | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Complete .NET suite | 201 | 0 | 0 |
| Real PostgreSQL cases, included above and also rerun separately | 16 | 0 | 0 |
| Pure JavaScript timer tests | 19 | 0 | 0 |
| Real browser assertions | 369 | 0 | 0 |
| Full solution build | Successful | 0 errors | 0 warnings |

The browser count represents assertions in one end-to-end workflow, not 369 independent unit tests. The 16 PostgreSQL cases are already part of the 201 total and must not be double-counted.

Eight new .NET cases extend the 193-test baseline: one dependency test and seven Focus/HTTP cases. The latter cover anonymous access, owned selector contents and no writes, foreign/missing/deleted entry rejection, server title encoding and blank optional session notes. Existing password/legacy migration, ownership, antiforgery, validation and concurrency regressions remain passing.

JavaScript coverage includes timestamp countdown/stopwatch behavior, pause/resume/reset, delayed callbacks, refresh restoration, inactive completion, nonnegative time, clock reversal, mode changes, idempotent start, custom duration limits, bounded plain-text tasks and corrupt storage fallback.

Browser verification creates a fresh synthetic account in the isolated preview database. It submits real CSRF-bearing forms for registration, login, category/item creation and editing, planning, completion with 17 actual minutes and blank notes, and logout. It checks mobile navigation, theme persistence, task XSS-safe rendering, paused/running/completed restoration, leaving Focus during a countdown, fullscreen success and denial fallback, Escape, dialog cancellation, interval cleanup, long titles, optional clock and no Focus POST requests.

Initial diagnostic/harness runs were not all successful. The missing edit IDs and mobile table overflow were corrected. Harness selectors, expected session redirect and virtual-clock initialization were corrected. A diagnostic PostgreSQL invocation without its opt-in environment variable skipped the seven declarations; it was rerun with the isolated provider and expanded to all 16 passing cases. The table above describes the final successful runs, not those intermediate attempts.

### Exact Commands

Run from the repository directory. The connection below is only the existing isolated local test cluster; do not substitute a production or user-data database for browser tests.

```sh
/Users/m/.dotnet/dotnet restore StudyPlanner.sln --force --no-cache --disable-parallel -p:NuGetAudit=true -p:NuGetAuditMode=all

/Users/m/.dotnet/dotnet build StudyPlanner.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false

STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger 'trx;LogFileName=phase5-final.trx' --results-directory /private/tmp/studyplanner-phase5-tests

STUDYPLANNER_TEST_POSTGRES='Host=/private/tmp/studyplanner-phase3-pg.gxJnOo;Port=55439;Username=studyplanner_test;Database=postgres' /Users/m/.dotnet/dotnet test --no-build --no-restore --filter FullyQualifiedName~PostgresSessionConcurrencyTests --logger 'trx;LogFileName=phase5-postgres.trx' --results-directory /private/tmp/studyplanner-phase5-tests

/Users/m/.dotnet/dotnet list /Users/m/RiderProjects/StudyPlannerProject/StudyPlanner/StudyPlanner.sln package --vulnerable --include-transitive --format json

/Users/m/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node --test tests/frontend/focus-timer.test.cjs

NODE_PATH=/Users/m/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules /Users/m/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node tests/frontend/verify-ui.cjs

git diff --check
```

Playwright uses the available bundled dependency runtime and installed Chrome, with `channel: 'chrome'`. No npm dependency or large frontend test framework was added to the application. The default Playwright Chromium binary was unavailable, so installed Chrome was used instead.

Saved evidence includes `baseline.trx`, `checkpoint-a.trx`, `phase5-final.trx`, `phase5-postgres.trx`, `nuget-audit.json` and `browser-results.json` in `PHASE5_EVIDENCE`.

## G. Visual Verification

Actual rendered viewport matrix:

| Width | Height | Form Factor |
| ---: | ---: | --- |
| 1440 | 900 | Desktop |
| 1024 | 768 | Laptop |
| 768 | 1024 | Tablet portrait |
| 390 | 844 | Mobile |
| 360 | 800 | Narrow mobile |

Each of 15 representative states was rendered and captured at every viewport in both dark and light themes: landing, registration, login, empty dashboard, populated dashboard, category create/edit/list, item create/edit/list, session planner, session result form, normal Focus and Focus with a 200-character unbroken title. One additional screenshot captures expanded mobile navigation. Total: **151 screenshots**.

All 150 viewport/theme combinations passed document-width overflow checks and visible-image loading checks. Table scrolling is deliberately contained inside the table region. Representative actual images were visually inspected for typography, layout, forms and timer framing. Long-title Focus may scroll vertically on mobile but does not overlap the timer or overflow horizontally.

Screenshots are preserved in `PHASE5_EVIDENCE/screenshots/`, with names such as `dashboard-1440-dark.png`, `items-360-light.png`, `session-plan-390-light.png`, `focus-1440-dark.png` and `focus-long-title-360-dark.png`. `browser-results.json` contains the complete assertion and screenshot inventory, browser version and a `realFullscreen: true` result.

This was actual installed Chrome headless rendering on macOS, not a claim based only on media queries. Viewport resizing is not physical iOS/Android testing. Other browser engines, browser chrome, native-device safe areas and physical device sleep were not directly exercised. Timer delays and absence were tested with controlled clock advancement, plus pure state tests.

## H. Performance and Accessibility

Heavy autoplay background videos are no longer requested by updated pages. Google-font/CDN runtime dependencies were removed from these views. Shared CSS/JS replaces duplicated page scripts; system fonts avoid additional font downloads. Focus uses one update interval, no backend polling, no framework bundle and no audio/notification work.

The landing PNG is approximately 1.9 MB. It is substantially simpler than video playback but still a meaningful transfer; further image encoding optimization can be considered separately. No Lighthouse score, network-budget benchmark or formal layout-shift measurement is claimed.

Accessibility improvements include associated form labels, icon-control names, semantic navigation/main regions, a keyboard-accessible skip link, visible focus outlines, status/error announcements, native dialog cancellation and reduced-motion CSS. The timer itself does not produce a screen-reader announcement on every tick. Basic visible-control naming and POST-token checks passed across captured pages. Keyboard skip-link reachability and dialog Escape cancellation were actually tested.

These checks do not constitute a formal WCAG audit or assistive-technology certification. Full contrast auditing, screen-reader testing and native-device touch testing remain broader verification limitations, not silently claimed results.

## I. Database Compatibility

PostgreSQL 15.18 was exercised through the existing isolated Unix-socket cluster at `/private/tmp/studyplanner-phase3-pg.gxJnOo`, port 55439, with TCP listening disabled. Test fixtures create and drop only their own temporary test databases.

The 16 provider cases verify overlapping completion races, disjoint result subsets, independent sessions, shared items, sequential repeat rejection, invalid ownership/membership/results, cancellation rollback and database failure rollback. Concurrency remains based on the configured session Status token, with one winning transactional completion rather than automatic retry.

Browser testing used the separate synthetic preview database `studyplanner_phase5_preview_929d10c8`. It contains only seeded/sample and browser-test records created for this work. No existing user database was used for destructive testing.

Phase 5 added no schema migration, changed no database entity schema and rewrote no migration. The existing Phase 3 snapshot concurrency metadata line remains unchanged. The preview startup checked the existing migration history and reported no pending migrations.

Existing EF startup warnings about required StudyItem relationships with global soft-delete query filters remain. They are runtime model warnings, not build warnings, and were not addressed by unrelated relationship/schema changes in this phase.

## J. Remaining Risks

- Production runtime versions, deployment settings and self-contained artifacts were not inspected or deployed. Keep the actual host/runtime patched; the local successful audit is not a production security certificate.
- NuGet findings can change after the recorded date. Continue normal dependency auditing without suppressions.
- Local timer persistence is device/browser specific, can be cleared by users or storage policy, and does not provide durable server-side tracking or multi-tab locking.
- Clock adjustments and abrupt process termination are inherent browser timing/persistence limits. Controlled-delay tests are not a physical sleep certification.
- The owned-item selector and existing management category loading have a 100-item cap. No new pagination architecture or category ownership policy was added.
- Existing broad exception-to-UI handling in some management controllers and EF soft-delete relationship warnings remain outside the targeted Phase 5 scope.
- Formal WCAG/screen-reader audits, other browser engines, physical phones/tablets and production load testing remain unperformed.
- Original video files remain in source control, although updated pages no longer load them.
- The entire working tree still includes earlier uncommitted security work and unrelated IDE state. This phase is not a clean standalone Git commit; no prior changes were reverted.

### Local Preview

Preview: `http://127.0.0.1:5815`, Development environment, isolated synthetic preview database. The local app and isolated PostgreSQL cluster are intentionally left running so the user can try the result. This is loopback-only, not a deployment. The app process log is `/private/tmp/studyplanner-phase5-preview.log`.

Use the normal registration screen for a personal preview account. Do not treat browser-test credentials, sample accounts or the Development configuration as production setup.

## K. Phase Status

**COMPLETE AND VERIFIED**

All requested Phase 5 implementation workstreams are delivered, with a clean final solution build, 201 passing .NET tests, all 16 real PostgreSQL cases, 19 passing timer tests, 369 successful actual browser assertions, 151 saved screenshots and zero currently reported NuGet vulnerabilities. The browser-discovered edit and responsive defects were fixed and rerun successfully.

This status applies to the implementation and verification scope documented above. It does not claim universal browser/device support, formal accessibility certification, physical-sleep validation, production deployment verification or absence of all possible security flaws. No known blocking Phase 5 defect remains in the tested workflows. No further phase was started.
