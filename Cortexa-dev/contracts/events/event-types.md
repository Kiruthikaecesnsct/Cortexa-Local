# Cortexa Event Types — v1

> Version: 1.0.0 | Status: Frozen | All events use the envelope schema in `envelope.schema.json`

Every message on the Azure Service Bus must conform to the shared `EventEnvelope`. The `payload` fields listed here populate the `payload` property of that envelope. All `batch_id` and `document_id` fields in the payload are redundant with the envelope-level fields — they are included in the payload so consumers that only deserialize the payload can still reconstruct context without parsing the outer envelope.

---

## Event Catalog

---

### `ingestion.requested`

| Field | Value |
| ----- | ----- |
| Topic | `ingestion.requested` |
| Publisher | `job-orchestrator` |
| Subscriber | `ingestion` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch this document belongs to." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document to ingest." },
  "blob_uri": { "type": "string (uri)", "required": true, "description": "Azure Blob Storage URI of the raw uploaded file or cloned repo archive." },
  "source_type": { "type": "string", "required": true, "enum": ["file", "repo"], "description": "Whether the asset is a standalone file or a git repository archive." }
}
```

---

### `extraction.requested`

| Field | Value |
| ----- | ----- |
| Topic | `extraction.requested` |
| Publisher | `job-orchestrator` |
| Subscriber | `extraction` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch this document belongs to." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document whose chunks must be analyzed for invention candidates." }
}
```

---

### `evidence.requested`

| Field | Value |
| ----- | ----- |
| Topic | `evidence.requested` |
| Publisher | `job-orchestrator` |
| Subscriber | `evidence` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch context." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document whose candidates need evidence lookup." },
  "candidate_ids": { "type": "array of string (uuid)", "required": true, "description": "Extraction candidates to research. One evidence bundle will be produced per candidate." }
}
```

---

### `scoring.requested`

| Field | Value |
| ----- | ----- |
| Topic | `scoring.requested` |
| Publisher | `job-orchestrator` |
| Subscriber | `scoring` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch context." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document whose candidates must be scored." },
  "candidate_ids": { "type": "array of string (uuid)", "required": true, "description": "Candidates to score. Each must have an existing evidence bundle." }
}
```

---

### `harvesting.requested`

| Field | Value |
| ----- | ----- |
| Topic | `harvesting.requested` |
| Publisher | `job-orchestrator` |
| Subscriber | `harvesting` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch context." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document to analyze for buried patentable inventions." },
  "candidate_ids": { "type": "array of string (uuid)", "required": true, "description": "Scored candidates eligible for harvesting evaluation." }
}
```

---

### `seeding.requested`

| Field | Value |
| ----- | ----- |
| Topic | `seeding.requested` |
| Publisher | `job-orchestrator` |
| Subscriber | `seeding` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch context." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document to analyze for new patent opportunity proposals." },
  "candidate_ids": { "type": "array of string (uuid)", "required": true, "description": "Scored candidates used as context for seeding new opportunities." }
}
```

---

### `ingestion.completed`

| Field | Value |
| ----- | ----- |
| Topic | `ingestion.completed` |
| Publisher | `ingestion` |
| Subscriber | `job-orchestrator` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch this document belongs to." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document that was ingested." },
  "blob_uri": { "type": "string (uri)", "required": true, "description": "The original blob URI that was processed." },
  "chunk_count": { "type": "integer", "required": true, "description": "Number of text chunks stored. Zero is valid for empty files." },
  "provenance_map_id": { "type": "string (uuid)", "required": true, "description": "ID of the ProvenanceMap record stored in Cosmos that maps chunk IDs back to source locations." }
}
```

---

### `extraction.completed`

| Field | Value |
| ----- | ----- |
| Topic | `extraction.completed` |
| Publisher | `extraction` |
| Subscriber | `job-orchestrator` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch context." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document whose extraction finished." },
  "candidate_ids": { "type": "array of string (uuid)", "required": true, "description": "IDs of all InventionCandidate records created for this document. Empty array means no candidates were found." }
}
```

---

### `evidence.completed`

| Field | Value |
| ----- | ----- |
| Topic | `evidence.completed` |
| Publisher | `evidence` |
| Subscriber | `job-orchestrator` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch context." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document context." },
  "candidate_id": { "type": "string (uuid)", "required": true, "description": "The specific candidate this evidence bundle covers. One event is emitted per candidate." },
  "evidence_bundle_id": { "type": "string (uuid)", "required": true, "description": "ID of the EvidenceBundle record stored in Cosmos." },
  "sources_used": { "type": "array of string", "required": true, "enum_values": ["PatentApi", "SeedCorpus", "LlmResearch"], "description": "Which of the three evidence sources contributed at least one hit to this bundle." }
}
```

---

### `scoring.completed`

| Field | Value |
| ----- | ----- |
| Topic | `scoring.completed` |
| Publisher | `scoring` |
| Subscriber | `job-orchestrator` |

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch context." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document context." },
  "candidate_id": { "type": "string (uuid)", "required": true, "description": "The candidate that was scored. One event per candidate." },
  "verdict_id": { "type": "string (uuid)", "required": true, "description": "ID of the Verdict record stored in Cosmos." },
  "composite_score": { "type": "number (0–100)", "required": true, "description": "Weighted composite patentability score." }
}
```

---

### `harvesting.completed` (alias → `engine.completed`)

Harvesting publishes to the shared `engine.completed` topic with `engine: "harvesting"`. See [`engine.completed`](#enginecompleted) below.

---

### `seeding.completed` (alias → `engine.completed`)

Seeding publishes to the shared `engine.completed` topic with `engine: "seeding"`. See [`engine.completed`](#enginecompleted) below.

---

### `engine.completed`

| Field | Value |
| ----- | ----- |
| Topic | `engine.completed` |
| Publisher | `harvesting` and `seeding` |
| Subscriber | `job-orchestrator` |

Both the harvesting and seeding services publish to this single topic. The `engine` field tells the orchestrator which engine finished so it can advance the batch state machine correctly.

**Payload:**

```json
{
  "batch_id": { "type": "string (uuid)", "required": true, "description": "The batch this report belongs to." },
  "document_id": { "type": "string (uuid)", "required": true, "description": "The document that was analyzed." },
  "engine": { "type": "string", "required": true, "enum": ["harvesting", "seeding"], "description": "Discriminator identifying which engine produced this report." },
  "report_id": { "type": "string (uuid)", "required": true, "description": "ID of the HarvestingResult or SeedingResult record stored in Cosmos." }
}
```

---

## Versioning Policy

All events in this catalog use `schema_version: 1` in the envelope.

- **Additive changes** (new optional payload fields): allowed within v1. Increment `schema_version` in the envelope.
- **Breaking changes** (field removal, type change, renamed fields): require a new topic name (e.g. `ingestion.completed.v2`) and a deprecation window for existing subscribers.
- Consumers must ignore unknown fields in the payload to stay forward-compatible.
