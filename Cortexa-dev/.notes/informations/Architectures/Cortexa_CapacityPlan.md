# Cortexa Capacity Plan

**Author:** Alpha  
**Date:** 2026-07-05  
**Purpose:** Governing calculation for KEDA autoscale settings and load test thresholds. Ensures burst LLM demand saturates the single Foundry deployment and overflows as clean 429/503 + Retry-After, never TaskCanceled storms.

---

## Overview

Every LLM call — extraction, evidence patent/deep-research, and scoring — funnels through one model-router instance to one Azure AI Foundry gpt-5.5 deployment. The Foundry concurrency limiter enforces a hard ceiling of 24 concurrent in-flight requests. Pipeline throughput is Foundry-bound, not replica-bound. This plan sets per-service replica maximums and KEDA message-count thresholds such that peak concurrent LLM demand stays at or just above 24. Excess demand surfaces as HTTP 429/503 with Retry-After headers, absorbed by per-service retry budgets.

---

## Measured Inputs

All values verified from source files.

| Parameter | Value | Source File | Notes |
|-----------|-------|-------------|-------|
| **Foundry ceiling** | 24 concurrent | `services/model-router/src/Api/appsettings.json` line 16 | `DeploymentOptions.gpt-5.5.MaxInFlight = 24`. The alias `gpt` resolves to `gpt-5.5` (line 112). All primary workload routes to this one deployment. |
| **Evidence per-replica sessions** | 4 | `services/evidence/src/evidence/infrastructure/config/settings.py` line 58 | `consumer_max_concurrent_sessions = 4` |
| **Evidence LLM research concurrency per replica** | 3 | Same file, line 40 | `llm_research_max_concurrency = 3`. Deep-research is one branch of the evidence fan-out. Effective per-replica LLM concurrency ≈ sessions × research-concurrency = 4 × 3 = 12. |
| **Evidence LLM research retry budget** | 400s | Same file, line 39 | `llm_research_total_retry_budget_seconds = 400.0` |
| **Scoring per-replica sessions** | 6 | `services/scoring/src/scoring/infrastructure/config/settings.py` line 29 | `consumer_max_concurrent_sessions = 6`. Roughly 1 LLM call per session. |
| **Scoring retry budget** | 80s | Same file, line 36 | `model_router_total_retry_budget_seconds = 80.0` |
| **Extraction concurrency per replica** | 4 | `services/extraction/src/extraction/infrastructure/config/settings.py` line 14 | `extraction_concurrency = 4`. Processes chunks in parallel. Each chunk makes 1 LLM call. |
| **Extraction retry budget** | 200s | Same file, line 12 | `model_call_total_retry_budget_seconds = 200.0` |
| **Job-orchestrator sessions** | 8 | `services/job-orchestrator/src/Infrastructure/Configuration/ServiceBusSettings.cs` line 20 | `MaxConcurrentSessions = 8`. Saga driver; no direct LLM calls. |
| **Ingestion sessions** | None set | `services/ingestion/src/ingestion/infrastructure/config/settings.py` | No `consumer_max_concurrent_sessions` defined. Ingestion clones repos and chunks — no LLM calls. |
| **Harvesting sessions** | None set | `services/harvesting/src/harvesting/infrastructure/config/settings.py` | No session concurrency setting found. Harvesting ranks and filters — no LLM calls. |
| **Seeding sessions** | None set | `services/seeding/src/seeding/infrastructure/config/settings.py` | No session concurrency setting found. Seeding generates patent-proposal lattices with LLM calls, but concurrency is not session-bounded. Suspect concurrency controlled at the batch-processing level or implicitly by single-replica deployment. |

---

## The Governing Inequality

The sum of peak concurrent LLM requests from all replicas across all services must stay at or just above the Foundry ceiling:

**∑ (max_replicas_service × per_replica_LLM_concurrency_service) ≤ Foundry_MaxInFlight + overhead_margin**

Terms:
- **max_replicas_service**: KEDA maximum replica count for a service.
- **per_replica_LLM_concurrency_service**: concurrent LLM calls one replica can make.
- **Foundry_MaxInFlight**: 24 (gpt-5.5 deployment limit).
- **overhead_margin**: small cushion (2–4) to keep Foundry saturated and surface overflow as 429 rather than leaving capacity idle.

Services not listed have zero LLM concurrency and do not contribute to the sum.

---

## Derived Outputs

### Per-Service Max Replicas

Calculated to saturate Foundry at ~26–28 peak concurrent LLM calls (24 Foundry + 2–4 overflow).

| Service | Per-Replica LLM Concurrency | Max Replicas | Peak Concurrent LLM Calls | Rationale |
|---------|----------------------------|--------------|--------------------------|-----------|
| **evidence** | 12 | 2 | 24 | 4 sessions × 3 research concurrency = 12. Two replicas saturate Foundry exactly. A third would push to 36 and generate constant 429s. |
| **scoring** | 6 | 2 | 12 | 6 sessions × 1 LLM call/session = 6. Two replicas contribute 12 concurrent calls. |
| **extraction** | 4 | 3 | 12 | 4 concurrent chunks × 1 LLM call/chunk = 4. Three replicas contribute 12 concurrent calls. |
| **seeding** | Unknown | 1 | Unknown | No session concurrency config found. Assume single-replica until LLM fan-out is clarified. Scale conservatively. |
| **job-orchestrator** | 0 | 2 | 0 | Saga driver, no LLM calls. Scale on queue depth to handle saga dispatch volume. |
| **ingestion** | 0 | 2 | 0 | Cloning and chunking; no LLM calls. |
| **harvesting** | 0 | 2 | 0 | Ranking and filtering; no LLM calls. |

**Min replicas = 0** for all services (dev scale-to-zero).

**Aggregate LLM concurrency at peak:** 24 (evidence) + 12 (scoring) + 12 (extraction) = **48 concurrent calls** if all services burst simultaneously. Foundry absorbs 24 and rejects 24 as 429. Retry budgets (evidence 400s, scoring 80s, extraction 200s) absorb the overflow.

**Deliberate over-provisioning:** Peak 48 is intentional. The alternative — capping at exactly 24 — leaves replicas idle waiting for Foundry slots. By allowing replicas to fan out beyond 24, we ensure Foundry runs hot and 429s are surfaced predictably. The retry budgets turn 429 into eventual success, not failure.

### KEDA Message-Count Thresholds

The `messageCount` setting defines backlog-per-replica before KEDA adds a new replica. Align to per-replica session concurrency to avoid thrash.

| Service | Per-Replica Sessions | Recommended `messageCount` | Rationale |
|---------|---------------------|---------------------------|-----------|
| **evidence** | 4 | 4 | One replica per 4 backlogged messages. Scales tightly to demand. |
| **scoring** | 6 | 6 | One replica per 6 backlogged messages. |
| **extraction** | Not session-based | 5 | Extraction uses `extraction_concurrency = 4` but is not session-driven. Set threshold at 5 to trigger scaling after ~one full chunk batch is queued. |
| **seeding** | Unknown | 10 | Conservative. Scale cautiously until LLM concurrency is measured. |
| **job-orchestrator** | 8 | 10 | Saga driver. Scale on completed-event backlog. Threshold of 10 allows modest queue buildup before adding capacity. |
| **ingestion** | No setting | 5 | No LLM bottleneck. Threshold of 5 is reasonable for cloning/chunking work. |
| **harvesting** | No setting | 5 | Ranking/filtering. Same reasoning. |

These thresholds apply to the Service Bus scaler when the Terraform phase wires each queue consumer to its subscription. Job-orchestrator already uses the Service Bus scaler (line 314 of `deploy/modules/container-apps/main.tf`). Evidence, scoring, extraction, ingestion, harvesting, and seeding currently use HTTP scalers (lines 325-331) — that will change in the Terraform phase.

---

## Expected Throughput Ceiling

Sustained throughput is **Foundry-bound**, not replica-bound. All LLM work shares the single gpt-5.5 deployment with 24 concurrent in-flight requests.

### Per-Candidate LLM Latency (Observed)

From 2026-07-05 E2E run (memory link: `e2e-20260705-scoring-upsert-partitionkey.md`):
- Evidence stage: ~55 seconds to 3 minutes per candidate (multi-source fan-out including deep-research).
- Extraction: ~18–25 seconds per chunk at max_tokens 8192 (memory: `bug081-extraction-timeout-loop.md`).
- Scoring: not individually timed, but faster than evidence (simple LLM call, no multi-source fan-out).

### Throughput Estimate

Assume an average document produces 10 candidates (observed 12/8/18 in recent runs; round to 10 for planning). Each candidate requires:
- 1 scoring LLM call (~10–15s).
- 1 evidence bundle (3 sources: patent APIs + corpus + deep-research). Deep-research LLM call dominates at ~60–90s.

Per-candidate wall time is evidence-dominated: **~60–90 seconds** of LLM work per candidate (scoring + evidence deep-research).

At Foundry max throughput (24 concurrent in-flight):
- 24 candidates processed concurrently.
- Each takes ~75s (midpoint of 60–90s).
- Throughput ≈ 24 candidates / 75s ≈ **0.32 candidates/second** ≈ **19 candidates/minute**.

For documents:
- 10 candidates/document.
- **~1.9 documents/minute** sustained.

This is the **Foundry ceiling** with zero queueing overhead. Real throughput will be slightly lower due to retry backoff, network latency, and extraction/ingestion stages that don't parallelize perfectly.

**100-document batch:** ~53 minutes at ceiling throughput (100 / 1.9). Add extraction and ingestion overhead (cloning, chunking, LLM candidate generation): estimate **60–75 minutes** total wall time for a 100-document batch in ideal conditions (no cold starts, no service restarts, Foundry stable).

---

## Load Test Thresholds (Phase 4)

### Wall-Clock Ceiling

**Pass threshold:** 100-document batch completes in ≤90 minutes.  
**Rationale:** 60–75 minute ideal ceiling + 15-minute buffer for cold starts, KEDA scale-up delay, retry backoff, and queueing variance.

If the run exceeds 90 minutes, investigate:
- Cold-start delays (first replica spin-up time).
- KEDA scale-up lag.
- Retry storm (429 count vs retry budget).
- Evidence deep-research timeout rate.

### Azure Cost Ceiling (Dev Environment)

Order-of-magnitude estimate for 100-document run:
- **Container Apps compute:** 7 services × 2–3 replicas average × 90 minutes. Dev tier (0.25 vCPU, 0.5 GiB/replica) ≈ $0.02/replica-hour. ~21 replica-hours × $0.02 ≈ **$0.42**.
- **Foundry GPT-5.5 inference:** 1000 candidates × (10k input + 4k output tokens avg) × $0.0001/1k tokens ≈ **$1.40**.
- **Cosmos DB RU/s:** autoscale 400 RU/s baseline, burst to ~1000 RU/s during peak writes. 90 minutes ≈ **$0.10**.
- **Service Bus messaging:** 100 batches × ~50 messages/batch × $0.0000001/message ≈ **$0.01** (negligible).
- **Blob Storage + Log Analytics:** **<$0.10**.

**Total estimate:** **~$2.00–$2.50** per 100-document run in dev.

**Pass threshold:** ≤$3.00.  
**Rationale:** Allows headroom for retry-induced extra LLM calls and Cosmos burst. If cost exceeds $3, investigate retry count and LLM token usage.

---

## Remediation Follow-Up (Out of Scope for US109)

If the live load test hits the Foundry 24-in-flight ceiling and 429 retry storms block forward progress (i.e. retry budgets are exhausted and batches fail):

### Option 1: Raise Foundry TPM Quota
Request Azure AI Foundry quota increase for the gpt-5.5 deployment. Current `MaxInFlight = 24` likely maps to a tokens-per-minute (TPM) quota. Doubling TPM would allow `MaxInFlight = 48`.

### Option 2: Add a Second Foundry Deployment
Deploy a second gpt-5.x deployment (e.g. gpt-5.4) with its own 24-in-flight limit. Update model-router to load-balance across both deployments (round-robin or least-in-flight). Total capacity becomes 48 concurrent in-flight requests.

### Option 3: Reduce Per-Replica LLM Concurrency
Lower evidence `llm_research_max_concurrency` from 3 to 2, or scoring sessions from 6 to 4. This reduces peak concurrent demand but also lowers replica throughput — more replicas needed for the same workload.

**Recommendation:** If remediation is needed, prefer Option 2 (second deployment) over Option 1 (quota increase) for dev because it proves the load-balancing code path before prod. Option 3 is a last resort — it reduces efficiency.

**Action:** File a new user story if the Phase 4 load test demonstrates sustained 429 exhaustion. Do not implement remediation preemptively.

---

## Summary of Key Values for Terraform Phase

| Service | min_replicas | max_replicas | messageCount |
|---------|--------------|--------------|--------------|
| evidence | 0 | 2 | 4 |
| scoring | 0 | 2 | 6 |
| extraction | 0 | 3 | 5 |
| seeding | 0 | 1 | 10 |
| job-orchestrator | 0 | 2 | 10 |
| ingestion | 0 | 2 | 5 |
| harvesting | 0 | 2 | 5 |

All services scale on `azure-servicebus` custom_rule_type with workload-identity auth (no connection-string secret). The scaler's `metadata.namespace` is derived from `var.service_bus_namespace_fqdn` by stripping `.servicebus.windows.net`. Topic and subscription names are per-service — match the `*_requested_topic` and `*_subscription` settings from each service's config.

---

**End of Capacity Plan**
