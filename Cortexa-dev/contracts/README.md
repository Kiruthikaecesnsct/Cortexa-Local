# Cortexa Contracts — v1.0.0

**Status: Frozen**

This directory contains the v1 interface contracts for the Cortexa platform. All service-to-service and client-to-gateway communication must conform to these definitions.

---

## Contents

| File | Purpose |
|------|---------|
| `rest/openapi.yaml` | OpenAPI 3.1 spec for the REST API served by the YARP API Gateway |
| `events/envelope.schema.json` | JSON Schema (draft 2020-12) for the shared Service Bus event envelope |
| `events/event-types.md` | Catalog of all 11 event types — topic names, publishers, subscribers, payload schemas |

---

## Versioning Policy

These contracts are frozen at v1.0.0.

- **Additive changes** — new optional fields in request/response bodies or event payloads — are allowed within v1. Increment `schema_version` in the event envelope when adding fields.
- **Breaking changes** — field removal, type changes, renamed fields, removed endpoints — require a new version. For REST this means a new path prefix (e.g. `/v2/...`). For events this means a new topic name (e.g. `ingestion.completed.v2`) with a deprecation window before the v1 topic is retired.

---

## How to Consume

**REST clients** — generate a typed client from `rest/openapi.yaml` using the toolchain for your language (e.g. `openapi-generator`, `nswag`, `openapi-typescript`). All requests must include a Bearer JWT in the `Authorization` header. The `X-Correlation-Id` header (UUID) should be supplied by the client; if omitted, the gateway generates one.

**Service Bus consumers** — validate incoming messages against `events/envelope.schema.json` before deserializing the `payload`. Use `events/event-types.md` to determine the payload shape for each `event_type`. Subscribe using the scoped `services` authorization rule on the Service Bus namespace (Send + Listen rights only).

---

## Correlation ID Flow

Every HTTP request carries an `X-Correlation-Id` UUID header. The API Gateway propagates this value to all downstream services. Every Service Bus event published during processing of that request carries the same value in the `correlation_id` field of the envelope. This single ID lets you trace a user action from the initial REST call through every pipeline stage and event in Application Insights.
