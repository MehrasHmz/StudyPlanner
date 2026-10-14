# Deployment Acceptance Checklist

This checklist is for an approved deployment, not authorization to deploy. No host/domain/TLS certificate, cloud provider, operator identity or RPO/RTO has been invented. Local acceptance and Production deployment are separate gates.

## Before Cutover

- Assign deployment/security/database operators and approve the environment, domain, architecture and maintenance window.
- Use the verified Release commit; refresh NuGet and authenticated image scanning. .NET 8 ends support on **2026-11-10**: approve a supported-runtime maintenance decision before operating beyond that date. No major runtime migration was performed in final acceptance. [Microsoft lifecycle notice](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).
- Resolve the image-scan gate: Docker Scout is installed but requires operator login; Phase 10 scan was blocked, not clean. After authorized Docker Desktop/login, scan the exact built image with `docker scout cves --format sarif --output <protected-report-path> studyplanner:local`. Review OS/.NET/application findings and patch availability separately; do not suppress them.
- Select database and secrets explicitly; keep passwords, connection files and operator tickets out of Git and shell history. The root `.env` remains local-only. Templates under `deployment/` contain placeholders and no credentials.
- Review existing runtime/operator/migration identities. Migration identity alone owns schema changes. Runtime uses column-limited Users INSERT/UPDATE, ordinary study/category writes and migration-history SELECT, not Role/audit/DDL rights. Operator may update Role/IsActive and insert/read audits. The `least-privilege.sql` template was tested on disposable PostgreSQL; review inherited/PUBLIC rights separately and never apply it blindly to real databases.
- Ensure database CONNECT rights and credential rotation separately; grant template intentionally does not create/login users or change passwords. Do not use a database-owner runtime identity for public deployment.
- Protect Data Protection keys (`/app/keys`) through approved platform encryption at rest, access controls and separate recovery custody. Persistence is verified; encryption at rest is not configured/certified. File secrets are not a secret manager or encryption scheme.
- Approve TLS termination, backend network restrictions, host allowlist and redirect/HSTS at the edge. Set `REQUIRE_SECURE_COOKIES=true`/`Security__RequireSecureCookies=true`, `AllowedHosts` to approved hosts and do not publish backend HTTP/database ports publicly. The production overlay enforces these settings but does not provide a proxy/certificate.
- Current app does not consume forwarded headers; do not enable automatic unrestricted forwarding. If client-IP/scheme forwarding is needed, configure exact trusted proxies/networks and test forged/untrusted headers. Socket-IP limits may group NAT/proxy clients; replicas need approved edge/distributed abuse protection.
- Back up database and protected keys before migration; rehearse restoration and approve backup storage, encryption, access, retention, monitoring and RPO/RTO. Do not substitute the local rehearsal for an SLA.

## Ordered Release Procedure

Use an explicitly selected Compose project/database. Add the reviewed production overlay only after supplying real protected secret paths/approved hosts; never use placeholder values as production approval.

```sh
docker compose -p '<approved-project>' config --quiet
docker compose -p '<approved-project>' build
docker compose -p '<approved-project>' up -d --wait postgres
docker compose -p '<approved-project>' run --rm --no-deps migrate
```

Production overlay pattern: `docker compose --env-file <protected-config> -f docker-compose.yml -f deployment/compose.production.example.yml -p <approved-project> ...`. Runtime/migration connection secrets mount at `/run/secrets/ConnectionStrings__DefaultConnection`; KeyPerFile values override earlier placeholders. Do not print expanded `compose config` containing secrets. Config/secret filenames and permissions must be reviewed by operators.

Inspect historical seed UUID before serving:

```sh
docker compose -p '<approved-project>' run --rm --no-deps api \
  --disable-demo-account --user-id 00000000-0000-0000-0000-000000000001 \
  --operator '<authorized-label>' --reason '<approved-release-ticket>'
```

This is preview only. Independently confirm the environment and actual account identity, especially if that historical UUID has been repurposed. Only then repeat with `--confirm`, using **operator** credentials, not a column-restricted runtime connection. With file secrets, inject a separately protected operator directory and set `Secrets__Directory` to it; otherwise existing runtime file secrets would override an environment connection. No public disable/reactivation route exists. The command preserves hashes/study records, deactivates the exact account, revokes any manager role with durable role audit and records a security event. Missing UUID selects nobody; invalid/duplicate targets are refused.

Fresh installations still execute historical seed insertion; upgrades retain existing records. No historical migration was rewritten and no unknown database is automatically changed. Production refuses the active historical UUID before HTTP binding. A hash upgrade/rotation alone does not satisfy this gate. Repurposed-account handling requires explicit operator review, not blind deactivation.

Provision approved real category managers through [ADMINISTRATION.md](ADMINISTRATION.md), never registration/first-user logic. Confirm role grants/revocations and same-cookie behavior; protect operator audit/credentials.

```sh
docker compose -p '<approved-project>' up -d --wait --wait-timeout 180
docker compose -p '<approved-project>' ps -a
curl --fail '<approved-origin>/health/live'
curl --fail '<approved-origin>/health/ready'
```

Schema history must match the artifact; migration and demo-account gates run before Production listens. Readiness thereafter checks DB connectivity, not continuous schema drift.

## Accept And Monitor

Run synthetic smoke checks only in the approved test space: registration/login/logout, own item/category selection, regular denial/admin management, revocation/deactivation, actual completion/history, reviews and Focus. Verify CSRF, HTTP-to-HTTPS redirect, Secure cookies, host rejection, key persistence after replacement and protected logs on the real topology. Monitor availability, readiness, latency, errors, disk/volume growth and backup jobs; define alert/incident owners and log retention.

Do not accept Production until image/runtime security, TLS/proxy/hosts, secrets/encrypted keys, real least-privilege grants, demo handling, managers, backups/restore and monitoring are proven. Stop on any failed gate. Never force-push, publish an image or tag a release as an implicit part of this checklist.

## Rollback

Drain/stop writes and preserve current backup/key/audit evidence. Prefer an approved forward fix; do not automatically run migration Down or remove volumes. Never restore over an unknown/live database. An older pre-role app would re-enable unrestricted category writes and is not a safe fallback after authorization cutover. Restore to a separately approved database, verify schema/records/constraints, then explicitly switch connection under an approved plan.
