# Cortexa local preview stack

Runs the entire Cortexa monorepo - the 11 backend services plus the frontend -
on any machine with Docker, previewing the checked-out working tree exactly as
it sits on disk (uncommitted changes included). The scripts perform **no git
operations**.

## Prerequisites

Docker Desktop with Compose >= 2.24, and nothing else. No language runtime,
package manager, or service unit is installed on the host. Run `doctor` first
on an unfamiliar machine:

```powershell
deploy\local\preview.ps1 doctor
```

```bash
deploy/local/preview.sh doctor
```

## One manual step

Copy the committed template to `.env.preview` (gitignored) and fill in the
cloud keys:

```bash
cp deploy/local/.env.preview.example deploy/local/.env.preview
```

Required: `GEMINI_API_KEY`, `QDRANT_URL`, `QDRANT_API_KEY`, and `ACCEPT_EULA=Y`
(consents to the Service Bus emulator's and SQL Server Linux's EULAs - see the
links in the template). `SENTRY_DSN` and the two secondary LLM provider keys
are optional and can stay blank. Everything else - PostgreSQL, Cosmos DB,
Blob Storage, Service Bus - runs locally via emulators with well-known
credentials; nothing else in this file needs editing.

## Start it

```powershell
deploy\local\preview.ps1 start
```

```bash
deploy/local/preview.sh start
```

Brings the whole stack up, waits for every service's `/health` endpoint, and
prints a URL banner.

## Port map

Deterministic offsets from `port_base: 5070` (see `services.yaml`). A second
project on the same machine would use `5170`.

| Port | Service | Role |
|---|---|---|
| 5070 | frontend | React SPA (Vite dev server) |
| 5080 | api-gateway | YARP reverse proxy, entry point for the frontend |
| 5081 | identity | Auth, users, permissions (Postgres-backed) |
| 5082 | job-orchestrator | Saga orchestration, batches, documents |
| 5083 | model-router | LLM routing (Gemini primary) |
| 5084 | evidence | Patent evidence gathering |
| 5085 | extraction | Document extraction |
| 5086 | harvesting | Harvesting engine |
| 5087 | ingestion | Document ingestion (incl. DOCX->PDF) |
| 5088 | scoring | Patentability scoring |
| 5089 | seeding | Seeding engine (deep seeding pipeline) |
| 5090 | vector-router | Vector search routing (Qdrant Cloud backend) |
| 5100 | Cosmos Data Explorer | Browse local Cosmos DB emulator data |

Dependency data ports (Postgres 5432, Cosmos 8081, Azurite 10000, Service Bus
5672) stay unpublished - every service reaches them by container name inside
the stack.

## Actions

| Action | What it does |
|---|---|
| `start [--only a,b] [--skip-seed]` | Validate, build, bring the whole stack up, wait healthy, print URLs. Safe to re-run. |
| `update [--only a,b] [--skip-seed]` | Rebuild and roll changed services - run after editing a Dockerfile, lockfile, or dependency version (hot reload alone won't pick those up). |
| `seed` | No-op today - this project has no seed script. Kept for parity with the script surface contract. |
| `stop [--deep-clean] [--wipe-local-volumes]` | Removes running containers. State and images are kept unless a flag is passed. `--wipe-local-volumes` prompts for a typed confirmation and refuses if any dependency is cloud-placed. |
| `reset [--skip-seed]` | `stop` then `start`. Never touches persistent state. |
| `status` | `docker compose ps` for this stack. |
| `logs [service]` | Follow logs, all services or one. |
| `doctor` | Diagnoses without changing anything - Docker/Compose version, env file, every missing key by name, port availability, placement table, workload status. |

PowerShell uses named switches (`-Only`, `-SkipSeed`, `-DeepClean`,
`-WipeLocalVolumes`, `-Service`) instead of `--` flags; everything else is
identical.

## Placement table

See `deploy/local/services.yaml` for the authoritative matrix. Summary:

| Dependency | Placement | Why |
|---|---|---|
| PostgreSQL | local | Standard `postgres:18` container |
| Cosmos DB | local | Linux emulator (`vnext-latest`), HTTPS only |
| Blob Storage | local | Azurite, well-known `devstoreaccount1` key |
| Service Bus | local | Official emulator + SQL Server Linux companion |
| Gemini | **cloud** | No local equivalent for an LLM exists |
| Qdrant Cloud | **cloud** | Deliberate choice over a local Qdrant container |
| Sentry | cloud, optional | Every service treats an empty DSN as disabled |

To flip a `local` dependency to `cloud` (or vice versa), edit its entry in
`services.yaml` and supply/remove the matching key in `.env.preview` - both
scripts read that file as the single source of truth.

## Troubleshooting

**Hot reload not firing.** Confirm the edited file is under the service's
bind mount (`deploy/local/docker-compose.preview.yml`), and that the anonymous
volumes shadowing `node_modules`/`.venv`/`obj`+`bin` are actually anonymous
(not accidentally pointed at a host path). .NET and Python/Vite watchers are
already set to poll (`DOTNET_USE_POLLING_FILE_WATCHER`, `--reload-delay`,
`CHOKIDAR_USEPOLLING`) - Docker Desktop bind mounts deliver no inotify events
on Windows/macOS. If Vite specifically still doesn't reload, the next step is
`server.watch.usePolling` in `frontend/vite.config.ts` (not currently set -
ask before adding it, since that's application code).

**Port conflict.** `doctor` names every busy port. Free it, or change
`port_base` in `services.yaml` (every published port shifts with it).

**Cosmos DB / job-orchestrator / a Python worker fails with a TLS/certificate
error.** The Cosmos DB Linux emulator only serves HTTPS with a self-signed
certificate it regenerates on every start; `start` fetches that certificate
into `deploy/local/certs/cosmos-emulator.pem` once the emulator reports
healthy, and every Cosmos-dependent container imports it via
`deploy/local/scripts/dev-entrypoint.sh` before the app starts. If this still
fails: confirm `deploy/local/certs/cosmos-emulator.pem` exists and is
non-empty (delete it and re-run `start` to force a re-fetch), and check that
`curlimages/curl` was pullable (the fetch runs in a throwaway container on
`cortexa-network`).

**Service Bus emulator won't come up.** Confirm `ACCEPT_EULA=Y` in
`.env.preview` - `start` refuses to launch the `servicebus` profile without
it. The emulator also depends on a SQL Server Linux companion (`mssql`) that
needs a few seconds to accept connections; the emulator's own
`SQL_WAIT_INTERVAL` (default 15s) handles that internally. If entities are
missing, check `deploy/local/servicebus/config.json` against
`deploy/modules/service-bus/main.tf` (the real Azure topology) - they should
match topic-for-topic.

**A cloud dependency is unreachable.** `doctor` TCP-probes nothing for
Gemini/Qdrant/Sentry specifically (they're plain HTTPS APIs, not
host:port endpoints to probe) - check the key is present and correct in
`.env.preview`, and that the machine has outbound internet access.

**Stale image after a dependency bump.** Run `update`, or `stop --deep-clean`
then `start` for a full image rebuild.
