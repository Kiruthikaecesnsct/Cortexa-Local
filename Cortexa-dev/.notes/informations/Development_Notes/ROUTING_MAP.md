# Cortexa — Frontend-to-Backend Routing Map

Documents how browser requests from the React SPA reach individual backend microservices through the Azure Static Web App proxy and the api-gateway YARP router.

---

## SWA Path-Stripping Behavior

Azure Static Web Apps Standard tier with a linked backend applies a fixed transformation to outbound proxy requests: the `/api` segment is stripped from the URL path before forwarding to the backend.

**Example:**

```
Browser → POST https://<swa-host>/api/auth/login
SWA     → POST https://<gateway-fqdn>/auth/login
```

This is a platform-level behavior, not configurable in `staticwebapp.config.json`. The SPA's `VITE_API_BASE_URL` must therefore be `/api` (not `/api/v1`) so that the gateway receives the gateway's own path prefix — not an extra `/v1` segment it has no route for.

**Verification status:** Confirmed from Azure SWA documentation. Empirical verification (live request trace through the proxy) is deferred to US076 — the SWA is still on Free SKU pending the US072 Terraform CD run that upgrades it to Standard.

---

## Routing Table

`baseURL` = `VITE_API_BASE_URL` = `/api`

All axios requests are relative to `baseURL`. Full browser URL = `https://<swa-host>/api{path}`.

| Frontend path | Full browser URL prefix | After SWA strip | Gateway route matched | Backend cluster | Backend endpoint | Status |
|---|---|---|---|---|---|---|
| `POST /auth/login` | `/api/auth/login` | `/auth/login` | `/auth/{**catch-all}` | identity | `POST /auth/login` | ✅ Works |
| `POST /auth/refresh` | `/api/auth/refresh` | `/auth/refresh` | `/auth/{**catch-all}` | identity | `POST /auth/refresh` | ✅ Works |
| `POST /auth/entra` | `/api/auth/entra` | `/auth/entra` | `/auth/{**catch-all}` | identity | `POST /auth/entra` | ✅ Works (BUG027 — frontend call aligned to `/auth/entra`) |
| `POST /batches/start` | `/api/batches/start` | `/batches/start` | — | — | — | ❌ No `/batches/*` gateway route |
| `POST /batches` | `/api/batches` | `/batches` | — | — | — | ❌ No `/batches/*` gateway route |
| `GET /batches/{id}/status` | `/api/batches/{id}/status` | `/batches/{id}/status` | — | — | — | ❌ No `/batches/*` gateway route; orchestrator owns this under `/orchestrator/batches/{id}/status` |
| `GET /harvesting/batches/{id}/results` | `/api/harvesting/batches/{id}/results` | `/harvesting/batches/{id}/results` | `/harvesting/{**catch-all}` | harvesting | `GET /batches/{id}/results` | ❌ Harvesting service has no GET results endpoint |
| `GET /seeding/batches/{id}/results` | `/api/seeding/batches/{id}/results` | `/seeding/batches/{id}/results` | `/seeding/{**catch-all}` | seeding | `GET /batches/{id}/results` | ❌ Seeding service has no GET results endpoint |
| `GET /scoring/verdicts/{id}` | `/api/scoring/verdicts/{id}` | `/scoring/verdicts/{id}` | `/scoring/{**catch-all}` | scoring | `GET /verdicts/{id}` | ❌ Scoring service only has POST endpoints |

---

## Gateway Route Inventory

Defined in `services/api-gateway/src/Api/appsettings.json`. No `/api/v1/*` or `/batches/*` routes exist.

| Gateway path pattern | Backend cluster | Notes |
|---|---|---|
| `/auth/{**catch-all}` | identity | `AllowAnonymous` metadata — bypasses JWT middleware |
| `/ingestion/{**catch-all}` | ingestion | — |
| `/extraction/{**catch-all}` | extraction | — |
| `/evidence/{**catch-all}` | evidence | — |
| `/scoring/{**catch-all}` | scoring | — |
| `/harvesting/{**catch-all}` | harvesting | — |
| `/seeding/{**catch-all}` | seeding | — |
| `/orchestrator/{**catch-all}` | job-orchestrator | — |
| `/model/{**catch-all}` | model-router | — |
| `/vector/{**catch-all}` | vector-router | — |
| Health routes (10×) | per-cluster | `PathSet: /health` transform; path rewritten to `/health` regardless of input suffix |

---

## Known Gaps (pre-existing, out of scope for US074)

These mismatches exist between the frontend paths and the backend routes. They predate this story and require separate work items to resolve.

| ID | Gap | Fix approach |
|---|---|---|
| ~~G1~~ | ~~`POST /auth/login_entra` — frontend uses `login_entra`, identity endpoint is `/auth/entra`~~ | ✅ Resolved (BUG027) — frontend call renamed to `/auth/entra` |
| G2 | `/batches/*` — no gateway route; frontend calls `/batches/start` (upload) and `/batches/{id}/status` (polling) | Add `/batches/{**catch-all}` gateway route pointing to a service that owns batch lifecycle, OR move calls to service-specific paths |
| G3 | `GET /harvesting/batches/{id}/results` — harvesting has no GET results endpoint | Add GET results endpoint to harvesting service |
| G4 | `GET /seeding/batches/{id}/results` — seeding has no GET results endpoint | Add GET results endpoint to seeding service |
| ~~G5~~ | ~~`GET /scoring/verdicts/{id}` — scoring only has POST endpoints~~ | ✅ Resolved (BUG140) — opportunity detail now renders from in-memory batch results already loaded by the dashboard; scoring stays POST-only, no GET endpoint was added |

US076 (verify end-to-end frontend login through the deployed SWA) will surface G1 and G2 first in a live environment.
