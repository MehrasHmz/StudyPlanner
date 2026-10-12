# Phase 5 Verification Evidence

Read `../PHASE5_REPORT.md` for the full English A-K report, exact commands, environment and limitations.

| File | Purpose |
| --- | --- |
| baseline.trx | Initial 193-test baseline |
| checkpoint-a.trx | Security checkpoint, 194 tests |
| phase5-final.trx | Final 201-test run, including real PostgreSQL and loaded assembly versions |
| phase5-postgres.trx | Separate final 16-case PostgreSQL run |
| nuget-audit.json | Online direct/transitive vulnerability audit of five projects |
| resolved-security-packages.json | Relevant resolved Infrastructure and Web package versions |
| browser-results.json | Actual Chrome version, 369 successful checks and screenshot inventory |
| screenshots/ | 151 actual browser screenshots |

The five tested sizes are 1440x900, 1024x768, 768x1024, 390x844 and 360x800. Dark and light screenshots cover fifteen page/state combinations. `navigation-360.png` additionally shows expanded mobile navigation.

Representative files:

- `screenshots/landing-1440-dark.png`
- `screenshots/login-390-dark.png`
- `screenshots/dashboard-1440-dark.png`
- `screenshots/dashboard-1024-light.png`
- `screenshots/items-360-light.png`
- `screenshots/category-edit-768-dark.png`
- `screenshots/item-create-390-light.png`
- `screenshots/session-plan-390-light.png`
- `screenshots/focus-1440-dark.png`
- `screenshots/focus-360-light.png`
- `screenshots/focus-long-title-360-dark.png`

Browser forms used synthetic accounts and a separate isolated preview database, not existing user data. TRX paths reflect this local machine. These files are test artifacts, not deployment configuration or a universal security/accessibility certificate.
