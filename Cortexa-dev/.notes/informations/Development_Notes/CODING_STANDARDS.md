# Cortexa — Coding Standards

These are the project-specific conventions for Cortexa. They sit on top of the global engineering standards and ACE rules in `/root/.claude/CLAUDE.md`. This doc does not repeat the ACE rules — read them there.

Cortexa is a polyglot monorepo: Python FastAPI services, C#/.NET 8 services, and a React/TypeScript frontend. Each language section below applies only to that stack. The cross-cutting rules apply everywhere.

---

## 1. Cross-Cutting Rules (all stacks)

- Services share no code. No shared library between services. Copy a contract shape, do not import it.
- All I/O is async. Propagate the cancellation token (.NET `CancellationToken`, Python async cancellation) from the request to the outbound call.
- Config-driven. No hardcoded URLs, ports, paths, or secrets. Read from environment variables; secrets come from Key Vault in cloud, `.env` locally.
- Every API response uses the shared error envelope (LLD §3.1). Every event uses the shared event envelope (LLD §3.2).
- A correlation id flows from the gateway through every REST call and every Service Bus message. Always pass it through.
- Never log a secret, token, or full LLM prompt that may carry sensitive asset text. Log a fingerprint (`sha256:<first-8-hex>`) when you must reference a secret.
- Every LLM verdict call goes through model-router with evidence references. Never call a provider directly. Never request a verdict without evidence.
- Every vector or embedding call goes through vector-router. Never call AI Search or Qdrant directly.

---

## 2. Python Services (FastAPI)

### 2.1 Layout and naming

- Layers: `domain/`, `application/`, `infrastructure/`, `api/` (see LLD §4.1).
- Modules and functions: `snake_case`. Classes: `PascalCase`. Constants: `UPPER_SNAKE`.
- Interfaces are `Protocol` classes in `domain/`, named for the role (`CandidateRepository`, not `ICandidateRepository`).
- DTOs are Pydantic models suffixed `Dto`.

### 2.2 Async and types

- Route handlers and any function doing I/O are `async def`.
- Use `httpx.AsyncClient` for HTTP; async Azure SDK clients for Cosmos, Service Bus, Blob.
- Type every function signature. Pydantic validates request and response bodies.

### 2.3 Errors

- Expected outcomes return a result or raise a typed domain error (LLD §4.7). Do not use exceptions for normal control flow.
- A single exception handler maps domain errors to the error envelope with the right status.

### 2.4 Tooling

- Format and lint with `ruff` (`ruff format`, `ruff check`). Zero warnings to merge.
- Test with `pytest` and `pytest-asyncio`.
- Audit with `pip-audit` in CI.

---

## 3. .NET Services (C# / .NET 8)

### 3.1 Layout and naming

- Projects: `Domain`, `Application`, `Infrastructure`, `Api` per service.
- Interfaces prefixed `I` (`IModelProvider`, `IUserRepository`). Classes `PascalCase`. Locals and parameters `camelCase`.
- Commands `{Verb}{Entity}Command`, queries `Get{Entity}Query`, DTOs `{Entity}Dto`.

### 3.2 Async and DI

- Async methods return `Task`/`Task<T>` and take a `CancellationToken` last parameter, propagated to every await.
- Register dependencies in `Program.cs` against interfaces. Inject through constructors.

### 3.3 Errors

- Expected outcomes use a `Result<T>` object. Faults throw. Middleware maps both to the error envelope.
- Build with `-warnaserror`. No warnings to merge.

### 3.4 Tooling

- Format with `dotnet format`. Test with `dotnet test`.
- Audit with `dotnet list package --vulnerable` in CI.

---

## 4. Frontend (React + TypeScript + Vite + Fluent UI v9)

### 4.1 Layout and naming

- Feature-first folders under `src/features/` (LLD §7.1).
- Components `PascalCase`, hooks `useCamelCase`, files match the export name.
- Use Fluent UI v9 components and design tokens. No ad-hoc inline styles for spacing or color — use tokens.

### 4.2 Data and state

- All API calls go through the shared `ApiClient`. No raw `fetch` in components.
- The client attaches the bearer token and correlation id, and refreshes the token once on a 401.
- Client types mirror backend DTOs.

### 4.3 Tooling

- Lint with `eslint`. Type-check with `tsc`. Test with `vitest`.
- Audit with `npm audit` in CI.

---

## 5. Service Bus and Messaging

- Each consumer follows the consumer loop pattern (Implementation Guide §7.2): complete on success, abandon on retryable error, dead-letter on fatal error.
- Event payloads carry `schema_version`. Bump it for breaking changes; keep readers tolerant of older versions.
- Never block a consumer on a long synchronous call. Offload and ack quickly where possible.

---

## 6. Logging

- Structured JSON logs to Application Insights. Include the correlation id and service name on every entry.
- Levels: ERROR (failed and not recovered), WARN (recovered or degraded path), INFO (lifecycle), DEBUG (dev detail), TRACE (fine detail, dev only).
- Do not log request bodies that contain asset text at INFO or below.

---

## 7. Testing Conventions

Follow the global review/test policy in `/root/.claude/CLAUDE.md`. For this project, the developer agent must invoke the tester for: scoring axis logic, evidence triangulation merge, the orchestrator saga state machine, identity auth, and any validator. Skip the tester for pure layout, config wiring, and simple DTOs, with a one-line reason in the post-task report.

---

## 8. Secrets and Config

- Secrets live in Key Vault (cloud) or `.env` (local, gitignored). Never in code, never in logs, never in responses.
- Inter-service URLs and tuning values come from config.
- The `.env.example` file lists every variable with a placeholder. Keep it current.
