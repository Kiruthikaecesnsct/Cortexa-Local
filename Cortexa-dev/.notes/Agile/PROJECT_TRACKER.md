# {ProjectName} — Project Tracker

## Status

**In progress — Sprint 4 underway (US054–US057 done).**

---

## Work Item Tracking

Work-item JSON files live in their folder until completion, then are deleted on approval (history in git).

| Type | Cortexa layout | Phase layout | Flat layout | Action on approval |
|------|---|---|---|---|
| User Story | `.notes/Agile/UserStory/Sprint{n}/US{NNN}_*.json` | `.notes/Agile/Phase{P}/US_*.json` | `.notes/Agile/User_Stories/US-*.json` | JSON deleted by `/complete-item` |
| Bug | `.notes/Agile/Bugs/BUG{NNN}_*.json` | `.notes/Agile/bugs/BUG-*.json` | `.notes/Agile/Bugs/BUG-*.json` | JSON deleted by `/complete-item` |

Layout is auto-detected by the skills based on which folders exist. Cortexa uses the `UserStory/Sprint{n}/` nested folder layout.

---

## Sprint Plan

_The backlog table of contents and resume state. `/init-project new --continue` writes the rows (one per sprint, all `🔲 Pending`) on its first run, then fills `Stories`/`Points` and flips `Status` to `✅ Done` as it generates each sprint — one sprint per invocation. `Stories` holds the planned count until a sprint is generated, then the actual count. (Phase layout reuses this table with "Phase" headers.)_

| Sprint | Focus | Stories | Points | Status |
|--------|-------|---------|--------|--------|
| Sprint 1 (2026-06-15 to 2026-06-19) | Foundation and Core Platform: IaC, Identity, Gateway, Model Router, Vector Router, Frontend scaffold | 18 | 83 | ✅ Done |
| Sprint 2 (2026-06-22 to 2026-06-26) | Ingestion to Evidence: File parsing, repo clone, chunking, extraction, evidence triangulation, corpus load | 17 | 71 | ✅ Done |
| Sprint 3 (2026-06-29 to 2026-07-03) | Scoring, Engines, Orchestration: 5-axis scoring, harvesting, seeding, saga pipeline, frontend dashboards | 18 | 79 | ✅ Done |
| Sprint 4 (2026-07-06 to 2026-07-10) | DevOps, Integration, Demo Hardening: CI/CD pipelines, prod deployment, load test, hardening, demo run | 16 | 71 | 🔲 In Progress |
| Sprint 5 (2026-07-13 to 2026-07-17) | Frontend Redesign: Custom design system, app shell, pages (login, signup, upload, history, dashboard, detail, profile, git modal) | 10 | 10.5 | ✅ Done |

---

## Active Work Items

| ID | Title | Type | Status |
|----|-------|------|--------|
| US125 | Parse files into extraction units | User Story | In Progress |

---

## Backlog

_Bugs:_

| ID | Title | Severity | Priority | Status |
|---|---|---|---|---|
| BUG001 | CI 'Detect changed services' fails with 'Resource not accessible by integration' | 1 (Critical) | 1 | Done |
| BUG002 | CI actions/checkout@v4 and dorny/paths-filter@v3 target deprecated Node.js 20 | 3 (Low) | 2 | Done |
| BUG004 | job_id silently dropped from IdfDraft and GenerateIdfResponse | 2 (High) | 2 | Done |
| BUG005 | Service Bus topic name dot/hyphen mismatch silently kills scoring event delivery | 1 (Critical) | 1 | Done |
| BUG006 | CI reusable workflows missing contents: read permission | 2 (High) | 1 | Done |
| BUG010 | CD pipeline OIDC login fails — pull_request subject not registered in Azure AD federated credential | 1 (Critical) | 1 | Done |
| BUG011 | CD pipeline Terraform plan fails — required TF_VAR_* secrets not injected in tf-plan and tf-apply jobs | 1 (Critical) | 1 | Done |
| BUG012 | CD workflow fails yamllint — inline comments and gpg command exceed 120-character line limit | 3 (Low) | 2 | Done |
| BUG013 | CD pipeline Terraform plan fails — Key Vault firewall blocks GitHub Actions runner IP during state refresh | 1 (Critical) | 1 | Done |
| BUG014 | Service Bus subscriptions missing for pipeline services | 1 (Critical) | 1 | Done |
| BUG045 | Terraform state drift blocks dev CD apply — three out-of-band resources missing from state | 1 (Critical) | 1 | Done |
| BUG046 | Consolidate GPT and Claude onto single cortexa-dev-ai-resource Foundry; remove separate AI resources | 2 (High) | 2 | Done |
| BUG047 | CD terraform apply blocked by AI resource purge permissions, Cosmos throughput mismatch, and Service Bus subscription drift | 1 (Critical) | 1 | Done |
| BUG048 | Loading spinner renders as unstyled black blob with clipped label — FluentProvider missing | 2 (High) | 1 | Done |
| BUG050 | Job-orchestrator returns 500 on every batch create — Cosmos SDK fails to load Newtonsoft.Json at runtime | 1 (Critical) | 1 | Done |
| BUG051 | Cosmos DB firewall blocks job-orchestrator — Container Apps egress IP rejected with 403 | 1 (Critical) | 1 | Done |
| BUG052 | Job-orchestrator batch create 500 — Cosmos partition-key mismatch from wrong serializer | 1 (Critical) | 1 | Done |
| BUG053 | Service Bus topics use hyphenated names but Python consumers listen on dotted topic names | 1 (Critical) | 1 | Done |
| BUG054 | Batch stop/cancel lifecycle endpoint missing in job-orchestrator | 2 (High) | 2 | Done |
| BUG058 | Batch delete blocked by missing Key Vault Secrets Officer permission on shared Container Apps identity | 1 (Critical) | 1 | Done |
| BUG059 | Shared Container Apps identity has excessive Key Vault Secrets Officer role — needs isolated orchestrator identity | 2 (High) | 2 | Done |
| BUG060 | Ingestion service /health depends on optional observability headers, blocks startup | 2 (High) | 2 | Done |
| BUG061 | Ingestion DocumentRepository.get() validates full write-model, dead-letters on orchestrator read-only seeds | 1 (Critical) | 1 | Done |
| BUG062 | Python completion events use int schema_version while orchestrator expects string, blocks saga deserialization | 1 (Critical) | 1 | Done |
| BUG063 | Python event envelopes missing required event_type field, orchestrator saga dead-letters completion | 1 (Critical) | 1 | Done |
| BUG065 | Extraction ModelCallFailed DNS failure — MODEL_ROUTER_URL points to bare service name not FQDN | 1 (Critical) | 1 | Done |
| BUG067 | Extraction receives 301 Moved Permanently from model-router HTTP to HTTPS — container app insecure ingress not allowed | 1 (Critical) | 1 | Done |
| BUG068 | Model-router request validation rejects max_tokens and non-default temperature for gpt-5.x models | 1 (Critical) | 1 | Done |
| BUG069 | Extraction fails to parse model-router response — nested usage tokens are camelCase but TokenUsage expects snake_case | 1 (Critical) | 1 | New |
| BUG083 | Scoring stage 100% failure — model_router_base config field name mismatch | 1 (Critical) | 1 | Done |
| BUG134 | patent_research 60s timeout may fail grok-4.3 under 95-candidate fan-out — needs p99 load-test validation | 2 (High) | 2 | New |

_Sprint 1 stories:_

| ID | Title | Sprint | Points | Status |
|---|---|---|---|---|
| US001 | Build Terraform core modules | Sprint 1 | 5 | Done |
| US002 | Build Terraform data modules | Sprint 1 | 5 | Done |
| US003 | Build Terraform compute modules | Sprint 1 | 5 | Done |
| US004 | Build Terraform AI Foundry module | Sprint 1 | 3 | Done |
| US005 | Compose dev environment | Sprint 1 | 5 | Done |
| US006 | Create budget alert | Sprint 1 | 2 | Done |
| US007 | Design identity domain and schema | Sprint 1 | 5 | Done |
| US008 | Implement password login endpoint | Sprint 1 | 5 | Done |
| US009 | Implement refresh and role claims endpoints | Sprint 1 | 5 | Done |
| US010 | Implement Entra ID OIDC login endpoint | Sprint 1 | 5 | Done |
| US011 | Set up API Gateway routing | Sprint 1 | 3 | Done |
| US012 | Implement gateway auth middleware | Sprint 1 | 5 | Done |
| US013 | Implement model-router provider adapters | Sprint 1 | 5 | Done |
| US014 | Implement model-router modes and grounding | Sprint 1 | 5 | Done |
| US015 | Implement vector-router backends | Sprint 1 | 5 | Done |
| US016 | Publish REST and event contracts v1 | Sprint 1 | 3 | Done |
| US017 | Build frontend scaffold | Sprint 1 | 3 | Done |
| US018 | Build login screen | Sprint 1 | 5 | Done |

_Sprint 2 stories:_

| ID | Title | Sprint | Points | Status |
|---|---|---|---|---|
| US019 | Build PDF and DOCX text parser for ingestion | Sprint 2 | 3 | Done |
| US020 | Build git repository clone adapter for ingestion | Sprint 2 | 5 | Done |
| US021 | Build text chunker for ingestion pipeline | Sprint 2 | 3 | Done |
| US022 | Build provenance map builder for ingestion | Sprint 2 | 5 | Done |
| US023 | Implement ingestion storage and event publishing | Sprint 2 | 5 | Done |
| US024 | Build extraction prompt builder and model-router client | Sprint 2 | 5 | Done |
| US025 | Build extraction candidate parser with provenance linking | Sprint 2 | 3 | Done |
| US026 | Implement extraction storage and event publishing | Sprint 2 | 3 | Done |
| US027 | Build patent API adapter (USPTO and EPO) | Sprint 2 | 5 | Done |
| US028 | Implement evidence corpus search via vector-router | Sprint 2 | 3 | Done |
| US029 | Implement evidence LLM deep research | Sprint 2 | 5 | Done |
| US030 | Build evidence triangulation and merge logic | Sprint 2 | 5 | Done |
| US031 | Implement evidence storage and event publishing | Sprint 2 | 3 | Done |
| US032 | Implement seed corpus load into vector store | Sprint 2 | 5 | Done |
| US033 | Build frontend upload and batch submission UI | Sprint 2 | 5 | Done |
| US034 | Build frontend batch and document progress polling UI | Sprint 2 | 5 | Done |
| US035 | Wire patent API keys into Key Vault and adapter config | Sprint 2 | 3 | Done |

_Sprint 3 stories:_

| ID | Title | Sprint | Points | Status |
|---|---|---|---|---|
| US036 | Build scoring grounded prompt generator | Sprint 3 | 3 | Done |
| US037 | Implement 5-axis scoring parser with grounding validation | Sprint 3 | 3 | Done |
| US038 | Implement dual-model scoring with agreement flag | Sprint 3 | 5 | Done |
| US039 | Implement scoring storage and event publishing | Sprint 3 | 3 | Done |
| US040 | Implement maturity classifier for harvesting | Sprint 3 | 2 | Done |
| US041 | Implement harvesting ranker (novelty, feasibility, strategic, patentability) | Sprint 3 | 3 | Done |
| US042 | Implement harvesting report assembler with citations | Sprint 3 | 5 | Done |
| US043 | Implement seeding opportunity map generator | Sprint 3 | 5 | Done |
| US044 | Implement seeding IDF generator (abstract, background, summary) | Sprint 3 | 5 | Done |
| US045 | Implement seeding claim seed generator | Sprint 3 | 5 | Done |
| US046 | Implement seeding invention lattice generator | Sprint 3 | 5 | Done |
| US047 | Implement orchestrator saga state machine | Sprint 3 | 5 | Done |
| US048 | Implement orchestrator event wiring and pipeline driving | Sprint 3 | 5 | Done |
| US049 | Implement orchestrator retry with backoff and dead-letter queue | Sprint 3 | 5 | Done |
| US050 | Implement orchestrator batch fan-out with concurrency cap | Sprint 3 | 5 | Done |
| US051 | Build frontend results dashboard (harvesting and seeding views) | Sprint 3 | 5 | Done |
| US052 | Build frontend opportunity detail view (score, evidence, provenance drill-down) | Sprint 3 | 5 | Done |
| US053 | Build frontend export feature (PDF and JSON) | Sprint 3 | 5 | Done |

_Sprint 4 stories:_

| ID | Title | Sprint | Points | Status |
|---|---|---|---|---|
| US054 | Set up GitHub Actions CI for all services | Sprint 4 | 5 | Done |
| US055 | Set up Azure Pipelines CI (parity with GitHub Actions) | Sprint 4 | 3 | Done |
| US057 | Build release pipeline for version tags | Sprint 4 | 3 | Done |
| US059 | Deploy all 11 services to Azure Container Apps | Sprint 4 | 5 | Done |
| US060 | Build integration test suite for cross-service pipeline | Sprint 4 | 5 | Done |
| US061 | Build E2E test suite (upload to export) | Sprint 4 | 5 | Done |
| US062 | Run 100-document load test on prod | Sprint 4 | 5 | New |
| US063 | Verify model switch functionality at runtime | Sprint 4 | 3 | New |
| US064 | Verify email/password and Entra ID authentication | Sprint 4 | 3 | New |
| US065 | Set up observability dashboards and alerts | Sprint 4 | 5 | New |
| US066 | Security review and hardening | Sprint 4 | 5 | New |
| US067 | Validate cost and scale-to-zero dev configuration | Sprint 4 | 3 | New |
| US068 | Demo dry run on prod with seeding scenario | Sprint 4 | 5 | New |
| US069 | Bug burn-down for P0 and P1 issues | Sprint 4 | 5 | New |
| US070 | Prepare demo dataset and corpus | Sprint 4 | 3 | New |
| US071 | Store EPO OPS keys in Key Vault and enable EPO integration in evidence service | Sprint 4 | 1 | Done |
| US072 | Upgrade dev Static Web App to Standard and link the API Gateway backend | Sprint 4 | 1.5 | Done |
| US073 | Build and deploy the React frontend to the Static Web App in CD | Sprint 4 | 1.5 | Done |
| US075 | Auto-apply identity EF migrations on startup and seed an initial admin user | Sprint 4 | 1.5 | Done |
| US076 | Verify end-to-end frontend login through the deployed Static Web App | Sprint 4 | 1 | Done |
| US081 | Implement client-facing batch lifecycle API in job-orchestrator | Sprint 4 | 1.8 | Done |
| US084 | Wire evidence service as Service Bus consumer | Sprint 4 | 2 | Done |
| US085 | Wire scoring service as Service Bus consumer | Sprint 4 | 2 | Done |
| US086 | Wire harvesting service as Service Bus consumer | Sprint 4 | 3 | Done |
| US087 | Wire seeding service as Service Bus consumer | Sprint 4 | 3 | Done |
| US089 | Frontend cleanup UI for failed/incomplete batches | Sprint 4 | 1.5 | Done |
| US090 | Automated retention / TTL sweeper for stale batches | Sprint 4 | 1.5 | Done |
| US091 | Orphan / inconsistency reconciliation | Sprint 4 | 1.5 | Done |
| US108 | Parallelize per-candidate pipeline processing via per-candidate Service Bus sessions | Sprint 4 | 8 | Done |
| US109 | KEDA-autoscale pipeline consumers on Service Bus depth and validate a 100-document load test | Sprint 4 | 5 | Done |
| US113 | Rich, informative Batch Progress experience — elapsed/started time, ETA, per-stage detail, single vs multi-file | Sprint 4 | 1 | Done |

_Sprint 5 stories:_

| ID | Title | Sprint | Points | Status |
|---|---|---|---|---|
| US072 | App shell and sidebar navigation | Sprint 5 | 1.5 | Done |
| US073 | Login page redesign | Sprint 5 | 0.5 | Done |
| US074 | Sign up and registration page | Sprint 5 | 1.0 | Done |
| US075 | Upload documents page | Sprint 5 | 1.0 | Done |
| US076 | Connect Git repository modal | Sprint 5 | 0.75 | Done |
| US077 | Job history page | Sprint 5 | 1.0 | Done |
| US078 | Results dashboard page | Sprint 5 | 1.5 | Done |
| US079 | Opportunity detail page | Sprint 5 | 1.5 | Done |
| US080 | Profile page | Sprint 5 | 0.75 | Done |

_Sprint 5 stories — Deep Seeding Engine epic: all 7 stories (US115–US121) ✅ Done._

_Sprint 6 stories — Identity & RBAC v2 epic (backlog, not yet started):_

| ID | Title | Sprint | Points | Status |
|---|---|---|---|---|
| US093 | Organization entity and multi-tenant data model | Sprint 6 | 3 | Done |
| US094 | Permission catalog and editable role-permission map | Sprint 6 | 3 | Done |
| US095 | Org-scoped user management API for Admin | Sprint 6 | 3 | Done |
| US103 | RBAC end-to-end verification and documentation | Sprint 6 | 1.5 | Done |

_Sprint 7 stories — Patent evidence hardening epic (backlog, from 2026-07-02 live test):_

| ID | Title | Sprint | Points | Status |
|---|---|---|---|---|
| US104 | Patent sources fail loud not silent — per-source observability, quota handling, no silent degradation | Sprint 7 | 3 | Done |
| US105 | Align all three patent adapters to researched contracts + abstract/claims enrichment for Four-Corners | Sprint 7 | 3 | Done |

---

## Backlog — 2026-06-27 deployment assessment

_Post-deployment live assessment findings (8 bugs identified during Azure Container Apps assessment)._

_All assessment bugs resolved._

---

## Backlog — dev CD failures

_All dev CD failure bugs resolved._

---

## Session Notes

_Older session notes are deleted. Full history is in git._

**2026-10-06 (355)**: US124 ✅ Done. Added the WPF desktop collector shell (Application, Infrastructure, Presentation on net10.0-windows plus Collector.Tests): Generic Host with appsettings, appsettings.local and usersettings layering, Windows Credential Manager store via LibraryImport (UTF-8, 2560-byte cap), SQLite cache schema v1 with versioned runner, gateway `/auth/login` sign-in and single-flight `/auth/refresh` with the refresh cookie handled manually, background refresh worker, bearer handler for the collector server client, and sign-in and settings screens with light and high-contrast themes. Gates: 311/312 tests (emulator test skipped), build -warnaserror and format clean, review PASS. Verified live: app launches, cache created, login route reached through the gateway.

**2026-10-06 (354)**: US123 ✅ Done. Added `POST /collector/batches/knowledge` to the collector server: gateway-equivalent HS256 JWT check plus a fail-closed identity stamp check (`jobs:submit`, `org_id`), field and unit_kind x source_kind validation, server-side filename cleanup, Idempotency-Key replay via UUIDv5 batch ids with a saga lookup before any write, and engine/model fields from the Cosmos config row. Gates: 207/208 tests (emulator test skipped), build -warnaserror and format clean, review PASS. Verified live against identity: 201 create, 200 replay with no duplicate rows, 401/400/422 paths, bidi filename stripped.
