# Dead-Letter Queue Operations Runbook

## Purpose

The drain-dlq scripts peek and classify dead-letter messages in the Service Bus *-requested topics, which remain wired as KEDA autoscale triggers. Purge is gated and requires explicit confirmation.

The scripts cover every pipeline consumer subscription: `ingestion-requested/ingestion`, `extraction-requested/extraction`, `evidence-requested/evidence`, `scoring-requested/scoring`, `harvesting-requested/harvesting`, and `seeding-requested/seeding`, plus the Deep Seeding fan-out legs — `asset-embedding-requested/seeding` and `asset-embedding-completed/orchestrator` (US115), `digest-requested/seeding` and `digest-completed/orchestrator` (US116), `landscape-requested/seeding` and `landscape-completed/orchestrator` (US117), and `ideation-completed/orchestrator` and `seeding-report-requested/seeding` (US119). The `*-requested` consumer subscriptions are also monitored by the `DeadletteredMessages` metric alerts provisioned in the `service-bus` Terraform module (`local.dlq_alert_topics`).

### US119 grounded ideation loop entities

Two legs are added by the US119 grounded ideation loop:

- **`ideation-completed/orchestrator`** — seeding emits one `ideation.completed` message per document; the job-orchestrator saga consumes it. Likely failure mode: **orchestrator down or its saga handler rejecting the message** leaves messages redelivered until they dead-letter here. Because this is one message per document (not fanned-out), a non-zero DLQ count on this leg points at the orchestrator, not at seeding.
- **`seeding-report-requested/seeding`** — the orchestrator dispatches `seeding-report.requested` and seeding consumes it to assemble the final report. Likely failure mode: **report-assembly errors inside seeding** (LLM/model-router failures, malformed lattice, Cosmos write failures) dead-letter here. Inspect the `DeadLetterReason` in the classification table to distinguish transient (model-router timeout) from permanent (validation/parse) failures.

If a data-plane call fails (most commonly HTTP 401/403 when the identity lacks the Data Receiver role — see prerequisites below), the scripts print the failing entity and HTTP status and exit non-zero. They do **not** report a false "all clear" — a clean-looking table with a zero peeked count only appears when the queues are genuinely empty.

## Usage

### Classification (default)

Peek all dead-letter queues and print a classification table showing count per failure reason. No messages are removed.

**Bash:**

```bash
bash scripts/drain-dlq.sh
```

**PowerShell:**

```pwsh
pwsh scripts/drain-dlq.ps1
```

### Purge (gated)

Permanently delete all dead-letter messages. Requires confirmation.

**Bash:**

```bash
bash scripts/drain-dlq.sh --purge         # interactive confirmation
bash scripts/drain-dlq.sh --purge --force # bypass confirmation
```

**PowerShell:**

```pwsh
pwsh scripts/drain-dlq.ps1 -Purge         # interactive confirmation
pwsh scripts/drain-dlq.ps1 -Purge -Force  # bypass confirmation
```

## Required Prerequisites

The operator identity must hold Azure Service Bus Data Receiver role on the target namespace (cortexa-dev-bus) for peek and purge operations. The cortexa-terraform service principal does not hold this role by default and returns HTTP 401 SubCode=40100.

To run the script:

1. Grant the role to the cortexa-terraform principal for the maintenance window:

```bash
az role assignment create \
  --role "Azure Service Bus Data Receiver" \
  --assignee $(az ad sp list --display-name cortexa-terraform --query '[0].appId' -o tsv) \
  --scope $(az servicebus namespace show -g cortexa-dev-rg -n cortexa-dev-bus --query id -o tsv)
```

2. Or run from a user or managed identity that already holds Sender + Receiver on the namespace.

After the maintenance window, remove the temporary role assignment if one was created.

## Expected Output

A working classify run prints the peeked count equal to the DLQ count and a reason breakdown per subscription, for example:

```
  Topic:             evidence-requested
  Subscription:      evidence
  DLQ count:         200  (peeked: 200)
  Reasons:
    PermanentError                                       200
```

If the count is non-zero but `(peeked: 0)` with no reasons, the operator identity is missing the data-plane role above.

## Monitoring

The standing BUG135 DeadletteredMessages metric alerts already cover ongoing monitoring. No additional alert is needed.

### Seeding mode and idle deep-seeding legs (US121)

Seeding runs in one of two modes, fixed per batch at creation from the `seeding_mode` config value (`legacy` or `deep`). In `legacy` mode the deep-seeding preparation legs — `asset-embedding`, `digest`, and `landscape` — never fire: the job-orchestrator gates their fan-out behind deep mode, so no messages are ever published to those topics for a legacy batch. As a result an empty DLQ on the deep-seeding topics under a legacy batch is expected by design and is **not** evidence that those legs are healthy; there is simply nothing flowing through them. Only treat DLQ depth on the deep legs as a health signal when the batch under investigation was created in `deep` mode. No topics change between the two modes.

## EPO Search Rate Limiting (BUG147)

EPO OPS advertises a per-tenant search throttling ceiling of 5 req/s (via the `x-throttling-control` response header, `search=green:5`). Under Container Apps fan-out, multiple evidence replicas calling EPO concurrently could collectively exceed that ceiling and get throttled. EPO does not use HTTP 429 for this — it signals throttling with HTTP 403 (carrying an `x-throttling-control` non-green indicator) and, when its own upstream is overloaded, HTTP 500.

### Per-replica token bucket

There is no Redis or other distributed store in the current architecture, so the rate limit cannot be coordinated centrally across replicas. Instead each evidence replica runs its own independent in-process token bucket, sized so the fleet total stays under the EPO ceiling:

```
replicas x per-replica-rps = fleet rate
10       x 0.4              = 4.0 req/s   (under EPO's 5.0 req/s ceiling)
```

`epo_search_target_rps` (4.0) is the fleet target; `epo_search_max_rps_per_replica` (0.4) is `epo_search_target_rps / evidence_max_replicas`, computed once and stored as an explicit overridable value rather than derived at runtime.

### Hard requirement: keep replica count and setting in sync

Terraform's evidence Container App `max_replicas` (`deploy/environments/{env}/main.tf`, module `container_apps`, and `deploy/modules/container-apps/main.tf` `lookup(var.service_max_replicas, "evidence", var.max_replicas)`) and the `evidence_max_replicas` setting (`services/evidence/src/evidence/infrastructure/config/settings.py`, env var `EVIDENCE_MAX_REPLICAS`) **must always be changed together**. `evidence_max_replicas` is the denominator the per-replica token bucket divides the fleet target by — if the real replica count is raised or lowered without updating this value, the rate limiter silently desyncs from the actual fleet size and either under-utilizes EPO's ceiling or lets the fleet exceed it.

### Building an alert query

The adapter code emits a structured `epo_throttle` log event whenever an EPO search call is throttled or retried. Fields (confirmed against the shipped adapter code):

- `status_code` — HTTP status of the throttled response (403 for a throttle-classified forbidden, or 500 for upstream "busy" — EPO does not use 429 for this signal)
- `throttle_kind` — classification of the throttle signal (`403-throttle` or `500-busy`)
- `x_throttling_control` — raw EPO `x-throttling-control` header value
- `search_colour` — parsed throttle colour band (`green`/`yellow`/`red`/`black`) from that header
- `search_limit` — parsed numeric rate limit from that header
- `retry_after` — `Retry-After` header value, if present
- `backoff_slept` — seconds actually slept before retrying
- `attempt` — retry attempt number (bounded by `epo_throttle_max_retries`)

Use this event name and field set to build a Log Analytics / Application Insights query (e.g. count of `epo_throttle` events by `search_colour` over time, or alert when `epo_throttle` events with `status_code in (403, 500)` exceed a threshold per window) against the evidence Container App's log stream.
