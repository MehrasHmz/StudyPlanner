# StudyPlanner Operations

Operate only the explicitly selected project/environment. Commands below use placeholders for real deployments. The verified local rehearsal project was `studyplanner-verify-20261012`; no production instance was deployed.

## Services And Health

```sh
docker compose -p '<project>' ps -a
docker compose -p '<project>' logs --tail 100 api
docker compose -p '<project>' logs --tail 100 migrate
docker compose -p '<project>' up -d --wait postgres
docker compose -p '<project>' restart api
curl --fail '<origin>/health/live'
curl --fail '<origin>/health/ready'
docker compose -p '<project>' stop api
docker compose -p '<project>' down
```

`down` preserves database/key/log volumes. Never use `down --volumes` as incident recovery. API/migration are non-root/read-only, drop capabilities, forbid privilege escalation and use `/tmp` tmpfs; Postgres needs writable persistent storage. Backend listener is loopback-only and PostgreSQL is not host-published by the base stack. Retain `/app/keys` across replacements; backup/restore keys independently with approved encryption and access. Logs may contain operational/internal exceptions; restrict them and never enable sensitive EF parameter logging on real accounts.

Liveness is public process availability. Readiness is public DB connectivity and safe 503 without error details. Missing/stale cookies do not block probes. Production startup fails closed for incompatible migration history or an active historical demo UUID; use the approved migration/demo procedure, not an environment bypass. Development auto-migrates and is unsuitable for public serving.

## Backup And Restore

Select source/database/host and backup owner explicitly. Use protected `.pgpass`/secret injection, not literal credentials. Dump files contain account hashes/private notes/audit data: keep them outside Git, mode 0600 and approved encrypted off-host storage.

```sh
pg_dump -Fc -d "$APPROVED_SOURCE_CONNECTION" -f "$PROTECTED_BACKUP_PATH"
chmod 600 "$PROTECTED_BACKUP_PATH"
createdb "$NEW_APPROVED_RESTORE_DATABASE"
pg_restore --exit-on-error --no-owner --no-privileges \
  -d "$APPROVED_RESTORE_CONNECTION" "$PROTECTED_BACKUP_PATH"
```

Connection variables must come from protected operator context; prefer standard PGHOST/PGPORT/PGUSER/PGDATABASE and a protected passfile so secrets are not expanded into process arguments. Do not use `--clean`, restore over a live/unknown database or drop user volumes. Restore ownership/grants explicitly for the target identities; no-owner/no-privileges preserves logical data, not deployment ACLs.

Verify all eight current tables: Users, Categories, StudyItems, StudySessions, StudySessionItems, ProgressRecords, RoleChangeAudits, __EFMigrationsHistory. Compare counts and deterministic sorted row fingerprints, migration IDs, foreign keys/uniqueness/role checks, private notes/hashes and role/audit transitions. Then start a separate approved app against the restored copy and check readiness/login/workflows. Never change the source connection or remove source data merely to test recovery.

Phase 10 rehearsal created/migrated/populated a synthetic database, restored into a distinct database and matched all eight tables; restore retained checks and Production app readiness. That proves local mechanics, not agreed RPO/RTO, off-host resilience, encryption, restoration monitoring or retention. Operators must approve these commitments and periodically rehearse them.

## Demo Account And Managers

See [Deployment Checklist](DEPLOYMENT_CHECKLIST.md) for `--disable-demo-account` preview/confirmation. It targets only the historical seed UUID and preserves records/hash; no public endpoint and no automatic deletion. Treat a repurposed UUID as a real identity decision requiring authorization. Activity changes emit a protected security event; an administrator downgrade adds a durable role audit. This is not an immutable activity ledger. Revoking manager access uses the existing UUID-targeted `--manage-user-role ... --role regular --confirm`; [ADMINISTRATION.md](ADMINISTRATION.md) covers scope, operator permissions and recovery from wrong assignments. Public registration cannot grant roles.

Operators needing stronger emergency revocation must drain in-flight requests as well as change authoritative account state; ordinary revocation blocks subsequent requests immediately.

## Common Failures

- Database unavailable/readiness 503: inspect selected Postgres service/health, network and protected connection secret; do not print credentials or reset the volume. Changing POSTGRES_PASSWORD in local env does not rotate an existing initialized database password.
- Schema gate: stop API, confirm release artifact and migration credentials, back up, execute the one-shot migration and inspect exit/logs. Do not edit historical migrations or run concurrent migration jobs.
- Active demo gate: inspect the exact UUID/environment, approve handling, run the explicit operator disable procedure and verify. Do not silently delete users or weaken the gate.
- Operator permission denied: use operator connection/secret directory, verify least-privilege grants/identity; do not broaden runtime privileges to make CLI operations convenient.
- 403 management: check active persisted Administrator status and current account; regular study access remains. Never add a client-side role flag or role claim workaround.
- 429 login/registration: obey Retry-After; inspect trusted edge/NAT grouping and approved limit policy, not account lockout or spoofed forwarding headers.
- Cookie lost after replacement: check protected key volume/application name and restore custody. Do not delete keys as routine cleanup. Key compromise requires an approved invalidation/rotation response.
- Failed restore/upgrade: preserve source, logs, backups and audit evidence; stop cutover and involve the database/release owner. Do not continue from an unverified partial state.

## Safe Updates And Incidents

Pin/review the approved release commit, rerun Release/full PostgreSQL/timer/browser/Docker/security checks and refresh advisories/image scans before updates. Back up before schema changes; retain keys and a known approved recovery path. .NET 8 support ends 2026-11-10; an approved supported-runtime maintenance change is mandatory before operating beyond that date, not an automatic major migration during project closure.

For suspected compromise: isolate ingress/drain writes, revoke affected manager/account access with operator approval, protect evidence and private logs, rotate secrets/keys through approved custody, restore only verified data into a separate target if necessary, and verify ownership/CSRF/roles before reopening. Assign notification/monitoring owners and record the incident; do not publish private progress, passwords or tokens in tickets/chat.

## Acceptance Evidence

Current evidence/report identifies local counts, engine versions, restore fingerprints, Docker state and exact delivered Git/CI SHA. Rehearsal artifacts in `/private/tmp/studyplanner-phase10-*` are ephemeral synthetic evidence, not retained production backups. See [Release Readiness](RELEASE_READINESS.md) for open gates. No public production deployment, release tag or image publication occurred.
