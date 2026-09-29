# Cortexa — Developer Setup

How to get a local Cortexa dev environment running. Cortexa is a polyglot monorepo. You only need the toolchain for the service you work on, plus Docker for running the local stack.

---

## 1. Prerequisites

| Tool       | Version | Install                                                       |
| ---------- | ------- | ------------------------------------------------------------- |
| Python     | 3.14.x  | `uv python install 3.14`                                      |
| uv         | latest  | `curl -LsSf https://astral.sh/uv/install.sh \| sh`            |
| .NET SDK   | 8.0.x   | `apt-get install dotnet-sdk-8.0`                              |
| Node.js    | 24 LTS  | via nvm                                                       |
| pnpm       | latest  | `npm i -g pnpm`                                               |
| Docker     | latest  | distro package                                               |
| Terraform  | 1.7+    | HashiCorp apt repo                                            |
| Azure CLI  | latest  | `curl -sL https://aka.ms/InstallAzureCLIDeb \| bash`          |

All work runs inside the Docker dev container over a VSCode tunnel. No headed browsers. Use the tunnel public URL for manual checks, not localhost.

---

## 2. Clone and Bootstrap

```bash
git clone <repo-url> Cortexa
cd Cortexa
cp deploy/local/env/.env.example deploy/local/env/.env   # then fill in dev keys
```

Fill `.env` with your personal dev keys: AI Foundry, Anthropic, USPTO, EPO, a read-only git PAT, and a local JWT signing key. Never commit `.env`.

---

## 3. Run One Service

Python service:

```bash
cd services/<service>
uv sync
uv run uvicorn api.main:app --host 0.0.0.0 --port 8080 --reload
```

.NET service:

```bash
cd services/<service>
dotnet restore
dotnet run
```

Frontend:

```bash
cd frontend
pnpm install
pnpm dev
```

---

## 4. Run the Full Local Stack

The local stack uses emulators for Cosmos, Blob (Azurite), and a Qdrant container for vectors, plus a Postgres container for identity.

```bash
cd deploy/local
./deploy.sh            # builds changed services, starts the stack, runs health checks
./deploy.sh --dry-run  # show the plan without changing anything
./deploy.sh --force-rebuild  # rebuild all images
```

The gateway is the only exposed service, on `localhost:8080`. Everything else is internal to the compose network.

---

## 5. Environment Variables

See `deploy/local/env/.env.example` for the full list. Key ones:

| Variable          | Purpose                  |
| ----------------- | ------------------------ |
| COSMOS_ENDPOINT   | Cosmos emulator endpoint |
| BLOB_ENDPOINT     | Azurite endpoint         |
| VECTOR_BACKEND    | `qdrant` for local       |
| FOUNDRY_ENDPOINT  | AI Foundry dev endpoint  |
| ANTHROPIC_API_KEY | Claude dev key           |
| USPTO_API_KEY     | USPTO dev key            |
| EPO_OAUTH_SECRET  | EPO OAuth dev key        |
| GIT_CLONE_PAT     | Read-only git PAT        |
| JWT_SIGNING_KEY   | Local token signing      |

---

## 6. Run Tests

```bash
# Python service
cd services/<service> && uv run pytest

# .NET service
cd services/<service> && dotnet test

# Frontend
cd frontend && pnpm test
```

---

## 7. Seed the Vector Corpus (local)

Evidence search needs a seeded corpus. Load the demo corpus into the local Qdrant once:

```bash
cd deploy/local
./deploy.sh            # the script seeds the corpus on first run
```

---

## 8. Superadmin Recovery (identity service)

The identity service seeds one immutable superadmin account (`SuperAdminEmail` in config, default `superadmin@cortexa.co`) at startup. It has `is_system = true`, which blocks every application-level mutation path (no PATCH/DELETE endpoint can touch it, and SSO/Entra group mapping can never assign the `SuperAdmin` role). There is intentionally no recovery API — if the seeded password is lost, the only recovery path is direct database access:

```bash
psql "$CORTEXA_IDENTITY_DB"
```

```sql
-- Inspect the account
SELECT id, email, username, role, is_system, created_at FROM users WHERE is_system = true;

-- Reset the password hash directly (generate a bcrypt hash out-of-band, never paste a plaintext password here)
UPDATE users SET password_hash = '<new-bcrypt-hash>' WHERE email = 'superadmin@cortexa.co' AND is_system = true;
```

Do not clear `is_system` or change `role` on this row outside of a deliberate, reviewed migration — both are the only things preventing lockout-by-mutation and privilege drift through SSO group sync.

---

## 9. Account Lockout and Disabled-User Enforcement (identity service)

**Failed-login lockout**

`POST /auth/login` tracks failed attempts per user in the `failed_login_attempts` table. Policy is config-driven under the `Lockout` section (`appsettings.json` / `appsettings.Development.json`):

| Key                 | Default | Meaning                                                          |
| -------------------- | ------- | ----------------------------------------------------------------- |
| `MaxAttempts`        | `5`     | Number of failed attempts in the current window before lockout   |
| `WindowSeconds`      | `900`   | Rolling window (seconds) an attempt counter stays "hot"           |
| `BaseLockoutSeconds` | `30`    | Lockout duration applied the first time an account is locked      |
| `MaxLockoutSeconds`  | `3600`  | Ceiling on lockout duration however many times it recurs          |

Each additional lockout for the same account doubles the previous lockout duration (`BaseLockoutSeconds * 2^lockout_count`), capped at `MaxLockoutSeconds`. The attempt counter and window are tracked with a single atomic `INSERT ... ON CONFLICT DO UPDATE` statement (see `FailedLoginRepository.RegisterFailureAsync`) so concurrent failed logins from the same account can't race each other into an inconsistent count. If the last failed attempt is older than `WindowSeconds`, the next failure restarts the window at attempt 1 instead of accumulating indefinitely.

While locked, `/auth/login` returns `429 Too Many Requests` with a `Retry-After` header (seconds until unlock). A successful login resets the counter (`IFailedLoginRepository.ResetAsync`).

**Disabled-user enforcement**

Disabling a user (`PATCH /admin/users/{id}/disable`) rotates that user's `security_stamp` and revokes every active refresh token for that user immediately. `POST /auth/login` rejects disabled accounts with `403 Forbidden` before a token is issued, and `POST /auth/refresh` rejects a disabled user's refresh token with `403 Forbidden` even if the token itself is otherwise still valid. Role changes and role-permission changes also rotate the affected security stamp(s) so any already-issued JWT is flagged stale the next time a caller checks it against `GET /internal/users/{id}/status` (consumed by the gateway, not by end clients).

**Admin/superadmin unlock procedure**

An Admin or SuperAdmin can clear a lockout without waiting for it to expire:

```bash
curl -X POST "https://<identity-host>/admin/orgs/<org-id>/users/<user-id>/unlock" \
  -H "Authorization: Bearer <admin-or-superadmin-jwt>"
```

- Requires the `AdminOrSuperAdmin` authorization policy (JWT `role` claim of `Admin` or `SuperAdmin`).
- An `Admin` caller may only unlock users inside their own organization (`org_id` claim must match the `{org-id}` route segment); a `SuperAdmin` may unlock any organization's user.
- On success the endpoint returns `204 No Content`, resets the failed-attempt counter, and writes a `UserUnlocked` audit log entry (visible via `GET /admin/audit`, SuperAdmin only).

---

## 10. Organizations and Multi-Tenancy (identity service)

The `organizations` table backs multi-tenant scoping. Every non-superadmin user carries a nullable `org_id` FK; the superadmin account keeps `org_id = null` forever because `AssignOrganization` on `User` goes through the same `EnsureMutable`/`is_system` guard as every other mutation.

**Lifecycle**

- **Create**: no dedicated bootstrap step is required for the seeded tenant — the `BackfillDefaultOrganization` migration inserts a `Default Organization` row with a fixed, deterministic id (`OrganizationConstants.DefaultOrganizationId`) and assigns every pre-existing non-superadmin user to it. New registrations (`RegisterHandler`) are assigned to the same default org at creation time, so no new orphans are produced going forward. Additional organizations are created through `Organization.Create(name)`.
- **Delete**: organizations are **soft-deleted only** — call `Organization.SoftDelete()`, which stamps `deleted_at`. There is no hard-delete path in application code, and the `users.org_id` foreign key is `ON DELETE RESTRICT`, so even a manual `DELETE FROM organizations` is rejected by Postgres while any user still references that org.

**Why restrict, not cascade**

An identity store must never lose accounts as a side effect of a tenant operation. If the FK were `ON DELETE CASCADE`, deleting an organization would silently delete every user in it. `RESTRICT` forces an explicit decision: reassign or soft-delete the users first, or leave the organization as soft-deleted (queries filter it out via the EF global query filter on `deleted_at`) while its users keep functioning with their existing `org_id`. This keeps deletion auditable and prevents accidental mass account loss.

**JWT claim**

`JwtTokenService` emits an `org_id` claim only when `User.OrganizationId` is not null. Superadmin tokens omit the claim entirely rather than emitting an empty string — treat the absence of `org_id` in a token as "this principal is not tenant-scoped," not as "tenant id is empty."

---

## 11. Seed Corpus Loading from Google BigQuery (US107)

This is an on-demand operational job, not part of any deployed service. It seeds the vector store with patent records from the BigQuery public patent dataset.

### Prerequisites

Install the optional BigQuery dependencies:

```bash
cd services/evidence
uv sync --extra bigquery
```

### One-Shot Load via Gateway (Recommended)

The deployed vector-router is internal-only with no VNet, so `deploy/load-corpus.sh` loads the corpus through the public api-gateway (which proxies `/evidence/corpus/bulk-load` behind JWT auth). This is the only path that works without a VNet or port-forward.

#### Required Environment Variables

| Variable                      | Purpose                                                          |
| ----------------------------- | ---------------------------------------------------------------- |
| `GATEWAY_URL`                 | Public gateway URL (e.g. `https://cortexa-dev-api-gateway.<domain>`) |
| `LOGIN_EMAIL`                 | Identity service account with `documents:read` permission        |
| `LOGIN_PASSWORD`              | Account password (never logged)                                 |
| `GCP_PROJECT_ID`              | GCP project ID (example: `white-hub-501206-d5`)                  |
| `GCP_SERVICE_ACCOUNT_PATH`    | Path to the gitignored service account key JSON file             |

#### Optional Query Bounds

| Variable                      | Default              | Purpose                                                      |
| ----------------------------- | -------------------- | ------------------------------------------------------------ |
| `BIGQUERY_ROW_LIMIT`          | `1000`               | Maximum rows to fetch                                        |
| `BIGQUERY_COUNTRY`            | `US`                 | Two-letter country code                                      |
| `BIGQUERY_DATE_FROM`          | `0` (unbounded)      | Publication date lower bound, YYYYMMDD                       |
| `BIGQUERY_DATE_TO`            | `0` (unbounded)      | Publication date upper bound, YYYYMMDD                       |
| `BIGQUERY_CPC_PREFIX`         | `` (all codes)       | CPC code prefix filter                                       |
| `BIGQUERY_MAX_BYTES_BILLED`   | `322122547200` (~300 GiB) | Hard cap on bytes scanned per query                   |
| `BATCH_SIZE`                  | `100`                | Records per upload batch                                     |
| `CORPUS_DUMP_PATH`            | `data/corpus/bigquery_dump.jsonl` | Local JSONL output path                  |
| `AI_SEARCH_ENDPOINT`          | `https://cortexa-dev-search.search.windows.net` | AI Search endpoint for `--verify` |
| `AI_SEARCH_INDEX_NAME`        | `cortexa-corpus`     | AI Search index name for `--verify`                          |

#### Run Command

```bash
cd deploy
./load-corpus.sh
```

The script runs a BigQuery dry-run cost estimate first, then dumps the corpus to a local JSONL file, prompts for confirmation (showing estimated scan size and record count), and uploads the records in batches to the gateway. Use `FORCE=1` or `./load-corpus.sh --yes` to skip the confirmation prompt for non-interactive use.

Add `--verify` (or `VERIFY=1`) to print the AI Search index document count before and after the load, so each run confirms what landed:

```bash
./load-corpus.sh --verify
```

Verification reads the index count directly from AI Search over an Azure AD token (the service is AAD-only), so the running principal needs the **Search Index Data Reader** role on `cortexa-dev-search`. It is best-effort — if the `az` CLI or the role is missing it prints `unavailable` and the load still succeeds. The index does not exist until the first successful load creates it, so a before-count of `0` is expected on the first run. To view it visually instead: Azure Portal → `cortexa-dev-search` → Indexes → `cortexa-corpus` → Search explorer.

### Alternative: Direct In-VNet Load

If running from an environment with direct access to the internal vector-router (e.g. a Container App inside the VNet, or via port-forward), the Python script can load the corpus directly:

#### Required Environment Variables (in-VNet)

| Variable                      | Purpose                                                          |
| ----------------------------- | ---------------------------------------------------------------- |
| `CORPUS_SOURCE`               | Set to `bigquery`                                                |
| `VECTOR_ROUTER_URL`           | Internal vector-router endpoint (e.g. `http://cortexa-dev-vector-router.internal.<env-default-domain>`) |
| `MODEL_ROUTER_URL`            | Internal model-router endpoint (required by loader config)       |
| `GCP_PROJECT_ID`              | GCP project ID                                                   |
| `GCP_SERVICE_ACCOUNT_PATH`    | Path to the gitignored service account key JSON file             |

Plus the same optional BigQuery query bounds as above.

#### Run Command (in-VNet)

```bash
cd services/evidence
python -m evidence.scripts.load_bigquery_corpus
```

### Service Account Key Handling

The GCP service account key is never stored in Azure Key Vault or GitHub Secrets. Supply it locally. The key file is gitignored under three patterns: the exact filename (`white-hub-501206-d5-82e49e302370.json`), the `credentials/` directory, and the wildcard `*.iam.gserviceaccount.com.json`.

Rotate the service account key after the load.

### Cost Estimation

Both paths log the query scan size in bytes (and GiB) before executing. BigQuery scans full columns regardless of `LIMIT`, so a normal filtered pull (country + CPC + date) scans roughly 230 GiB even for a few hundred rows. This is **free**: BigQuery's permanent always-free tier covers 1 TB of query processing per month, so a ~230 GiB query is about 23% of the monthly free allowance and costs $0. The `BIGQUERY_COUNTRY`, `BIGQUERY_DATE_FROM`, and `BIGQUERY_DATE_TO` filters prune the scan; `BIGQUERY_ROW_LIMIT` and `BIGQUERY_CPC_PREFIX` only shape corpus volume, not bytes scanned. Test with a small `BIGQUERY_ROW_LIMIT` first.

`BIGQUERY_MAX_BYTES_BILLED` is a hard ceiling (default ~300 GiB): the reader compares the dry-run estimate against it and aborts before running the real query, and BigQuery itself rejects the query (no charge) if it would scan more. The default sits above a normal filtered pull (~230 GiB) and below a pathological unfiltered scan; both are under the 1 TB free tier. The two cost meters are separate — BigQuery bytes scanned (Google, covered by the 1 TB/month free tier; only ~$6.25/TB beyond it) and the Azure embedding + AI Search cost per record (billed to Azure, scales with the number of records loaded, not BigQuery bytes).

---

## 12. Common Troubleshooting

| Symptom                                   | Fix                                                          |
| ----------------------------------------- | ------------------------------------------------------------ |
| Service starts but cannot reach Cosmos    | Confirm the Cosmos emulator container is healthy; check COSMOS_ENDPOINT |
| Scoring returns UNGROUNDED_VERDICT         | The evidence bundle is empty; confirm the corpus is seeded and patent keys are set |
| Gateway returns 401 for everything         | JWT_SIGNING_KEY mismatch between identity and gateway; align them |
| Frontend cannot reach the API             | Use the gateway URL, not a service URL; check the API client base URL |
| `uv sync` fails                           | Confirm Python 3.14 is the active interpreter                |
| Docker build slow                         | Use `./deploy.sh` so only changed services rebuild           |

---

## 13. RBAC Configuration

### Role hierarchy

Four roles, ascending privilege:

```
Researcher < Reviewer < Admin < SuperAdmin
```

| Role         | Who it is                                                                 |
| ------------ | ------------------------------------------------------------------------- |
| `Researcher` | Researcher working with documents. Can upload, submit jobs, read results. |
| `Reviewer`   | Read-only consumer of results. Cannot upload or submit jobs.              |
| `Admin`      | Manages users in their own organization. Cannot create SuperAdmin accounts. Cannot demote the last active Admin in an org. |
| `SuperAdmin` | Platform-wide access. Bypasses all RBAC route checks. Immutable system account. Cannot be assigned through any API or Entra group mapping. |

### Permission catalog

Permissions are named capabilities stored in the `permissions` table, seeded at identity service startup. The current catalog:

| Permission                   | Meaning                              |
| ---------------------------- | ------------------------------------ |
| `documents:read`             | Read documents                       |
| `documents:write`            | Upload and manage documents          |
| `jobs:submit`                | Submit analysis jobs                 |
| `jobs:read`                  | View job status and results          |
| `admin:users:read`           | List all users                       |
| `admin:users:write`          | Create and manage users              |
| `admin:permissions:read`     | View permission catalog              |
| `admin:permissions:write`    | Modify role-permission map           |
| `reports:read`               | Read patent reports                  |
| `reports:export`             | Export patent reports                |

### Default role-permission map

| Role         | Permissions                                                                                      |
| ------------ | ------------------------------------------------------------------------------------------------ |
| `Researcher` | `documents:read`, `documents:write`, `jobs:submit`, `jobs:read`, `reports:read`                  |
| `Reviewer`   | `documents:read`, `jobs:read`, `reports:read`, `reports:export`                                  |
| `Admin`      | All Researcher permissions, plus `reports:export`, `admin:users:read`, `admin:users:write`       |
| `SuperAdmin` | All permissions (bypasses route checks via role claim — permission array is not evaluated)       |

The role-permission map is editable at runtime by a SuperAdmin: `PUT /admin/permissions/{role}`. Changes take effect on the next token issuance (re-login). Existing tokens retain the permissions that were in their `perms` claim at mint time.

### Route-to-permission enforcement

The gateway (YARP) evaluates the `perms` claim on every request. Routes with no RBAC metadata in the YARP config are denied by default even with a valid token.

| Route prefix    | Required permission      |
| --------------- | ------------------------ |
| `/auth/**`      | anonymous                |
| `/ingestion/**` | `documents:write`        |
| `/extraction/**`| `documents:read`         |
| `/evidence/**`  | `documents:read`         |
| `/scoring/**`   | `reports:read`           |
| `/harvesting/**`| `reports:read`           |
| `/seeding/**`   | `reports:read`           |
| `/orchestrator/**`| `jobs:read`            |
| `/batches/**`   | `jobs:submit`            |
| `/model/**`     | `jobs:read`              |
| `/vector/**`    | `documents:read`         |
| `/admin/**`     | `admin:users:read`       |

SuperAdmin callers bypass all route permission checks. The gateway checks the `role` claim before evaluating `perms`.

### How permissions are issued at login

The identity service mints a `perms` JWT claim (JSON array) by querying `RolePermissions` for the user's assigned role. This happens at login and refresh. Any runtime change to `RolePermissions` does not affect already-issued tokens.

### Entra ID group-to-role mapping

Entra login maps group IDs or names to roles and organization IDs via `appsettings.json`:

```json
"EntraId": {
  "GroupMappings": {
    "<entra-group-id>": { "Role": "Admin", "OrganizationId": "<org-id>" }
  },
  "DefaultOrganizationId": "00000000-0000-0000-0000-000000000001"
}
```

Users whose Entra groups are not in the map land in the default organization as `Researcher`. The `SuperAdmin` role cannot be assigned through group mapping — the group mapper explicitly rejects it.

### Multi-tenancy scope

- Every non-SuperAdmin user belongs to exactly one organization (`org_id` FK).
- The JWT carries an `org_id` claim. The gateway strips any client-supplied `X-Org-Id` header and re-injects it from the JWT claim. Clients cannot set this header.
- Backend services receive `X-Org-Id` from the gateway only and must scope all queries to that value.
- An Admin can only see and manage users in their own organization.

### RBAC troubleshooting

| Symptom | Fix |
| ------- | --- |
| 403 on a route the user should access | Decode the JWT at jwt.io and check the `perms` claim. Compare to the route table above. If perms are stale, re-login to get a fresh token. |
| `perms` claim missing or empty | The role-permission seeder may not have run. Check identity service startup logs for "Permission seeded" entries. Run the identity service with `ASPNETCORE_ENVIRONMENT=Development` to seed defaults. |
| Admin gets 403 on `/admin/**` | Confirm the JWT `role` claim is exactly `Admin` (case-sensitive) and the `perms` array includes `admin:users:read`. |
| User sees another org's data | This is a bug. `X-Org-Id` must come from the JWT `org_id` claim injected by the gateway. Verify the gateway's `ContextHeaderTransformProvider` is wired. |
| Entra login lands user in wrong org | Check `EntraId.GroupMappings` in config. The user's Entra group ID must match a key in the map. |
