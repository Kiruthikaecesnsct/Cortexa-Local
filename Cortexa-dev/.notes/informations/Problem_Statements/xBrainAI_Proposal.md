# Cortexa - Patent Seeding and Patent Harvesting MVP Proposal

This is the MVP / POC proposal for two modules only, Patent Seeding Engine and Patent Harvesting Engine. Nothing else from the bigger Cortexa platform is in scope right now.

Quick facts:

1) Scope is only the two engines mentioned above
2) Timeline is 1 month from approval
3) Team is 4 core developers supported by 10 junior developers
4) Demo will lead with University / Researcher persona (paper and thesis upload)
5) Deployment is live Azure from day one
6) The old design doc (CortexaAI-DesignDocuments.docx) is outdated. We take ideas from it but we are not reusing the architecture. This is a fresh design.
7) Each microservice is a fully independent codebase inside a monorepo. Each can be developed, tested, built, and deployed in isolation.
8) IaC (Terraform) is developed in parallel with the services and lives in its own folder in the same repo. (Possibily will go inside Deploy/Cloud folder)


## 1. What we are actually building and why

Cortexa is the tech platform behind Patent Gym. The one line that makes it different from every existing patent tool is this.

Normal patent tools start AFTER an invention exists. They answer "is this novel", "what prior art is there", "what claims can be written". Cortexa starts BEFORE that. It reads raw stuff that is not a patent yet (research papers, thesis, code, docs) and tells you where the patent opportunity is hiding.

Two engines come out of this idea.

Patent Harvesting Engine - find the patentable innovation that is ALREADY there, just hidden inside the asset and nobody noticed it.

Patent Seeding Engine - engineer NEW patent opportunities that do not exist yet, based on the product or research roadmap.

Both engines run on the same ingestion and analysis backbone. They only split at the reasoning stage. The whole point is, this should not feel like just another "summarize my document with AI" tool. The thing that makes it useful is a patentability verdict that an attorney can actually trust, because every score is backed by real evidence and you can click and see where it came from. Plus the seeding side can propose brand new IP, which generic tools do not do.

Why this is not a generic wrapper:

1) Every novelty / patentability claim is grounded on three independent sources - real patent office data, a pre loaded patent seed corpus, and a high powered LLM research pass. The score carries citations and a confidence band. If one source is down, the engine still works on the remaining sources and clearly says which sources were used. No black box number.

2) Dual model adversarial scoring. GPT and Claude can both score the same candidate independently and if they disagree we show the disagreement instead of hiding it. This is optional though, see the model section, user can also run just one model.

3) Seeding produces a claim seed lattice, not one patent. One asset becomes a family roadmap (core -> continuation -> platform -> system patent).

4) Provenance everywhere. Each extracted invention links back to the exact source, the paragraph in the paper or the file and line in the code. So verification takes seconds.


## 2. Scope

### 2.1 In scope

Both engines:

1) Upload PDF / DOCX papers and thesis (this is the lead path for demo)
2) Connect one git repo, public or private, Azure DevOps repo also included (feasibility path)
3) Batch upload of at least 100 documents
4) Invention candidate extraction with provenance
5) Prior art / similarity using the three source approach
6) 5 axis patentability scoring (Novelty, Inventiveness, Commercial, Strategic, Patentability)
7) Model switch between GPT and Claude, single or both
8) Results dashboard, per opportunity detail with drill down to evidence, export as PDF / JSON

Harvesting only:

1) Maturity classification - Mature (ready for drafting) vs Emerging signal (needs more engineering)
2) Ranked harvesting report

Seeding only:

1) Patent Opportunity Map (whitespace, defensive filing, adjacent invention, continuation)
2) Invention Disclosure (IDF) and abstract generation
3) Claim seed generation (seeds, not full claims)
4) Patent roadmap / invention lattice

### 2.2 Not in scope for MVP

1) Legal filing automation, attorney workflow, e-sign
2) Portfolio valuation, renewal tracking, competitor monitoring (those are other modules)
3) Full multi tenant billing and big RBAC matrix, we keep 2-3 roles only
4) Diagram / figure computer vision analysis. Text and code only for MVP. We design for it but not build now
5) Any language other than English


## 3. The two engines in detail

Both engines share one pipeline. Upload -> Extraction -> Evidence -> Verdict. Only the reasoning part is different.

### 3.1 Shared pipeline

```text
Upload / Repo clone
   |
   v
Ingestion     parse, normalize, chunk, store raw + provenance map
   |
   v
Extraction    LLM pulls out "Invention Candidates"
              (claim, problem, mechanism, source span, tech field, IPC/CPC guess)
   |
   v
Evidence Triangulation   per candidate, in parallel:
   - Real Patent API search (PatentsView / Lens / EPO OPS)
   - Seed corpus vector search (pre loaded patents)   <-- via Vector Router
   - LLM deep research
       -> merged Evidence Bundle with citations + confidence
   |
   v
   split here
   |                              |
   v                              v
HARVESTING reasoning        SEEDING reasoning
```

### 3.2 Patent Harvesting Engine

Goal is to find IP that is already there.

Steps:

1) Patentability verdict - 5 axis scoring against the Evidence Bundle, one or both models
2) Maturity classification - Mature (substantially built, just strengthen it for drafting) vs Emerging signal (5-20 percent maturity, needs engineering). This is the Level 1 / Level 2 idea from the docs
3) Ranking - by uniqueness, feasibility, strategic value, patentability
4) Output - Harvesting report, ranked candidates, each with score, maturity, evidence citations, source provenance and a one line "why patentable / what is blocking it"

### 3.3 Patent Seeding Engine (this is the MVP primary)

Goal is to engineer IP that does not exist yet, from the same assets plus roadmap context. This covers the 6 functions from the summary doc.

1) Innovation Asset Mining - same ingestion, but here we give more weight to roadmap and vision docs
2) Patent Opportunity Identification - LLM finds whitespace, defensive filing, adjacent invention and continuation opportunities against the prior art landscape, then shows a Patent Opportunity Map (whitespace quadrant, novelty vs commercial value)
3) Patentability Scoring - same 5 axes but applied on the PROPOSED inventions
4) Invention Disclosure Generation - auto draft the IDF, abstract, background, summary
5) Claim Seed Generation - seeds not full claims. Example, "AI anomaly detection" becomes seeds like adaptive threshold mechanism, context aware anomaly engine, multi layer inference architecture
6) Patent Roadmap (invention lattice) - core -> continuation 1/2 -> platform -> system patent

The demo moment for university persona. Researcher uploads a paper or thesis and in a few minutes gets back something like - Patentability 82/100, 14 similar patents (cited), 5 novel claim seeds, Filing recommendation High, Commercial potential Medium. And every number can be expanded to see the evidence behind it.


## 4. Architecture

Microservices, no single monolith backend. Mix of Python and C# / .NET 8, chosen per service based on what fits. Each service is a fully self-contained codebase - its own project file, its own Dockerfile, its own CI pipeline, its own test suite. The monorepo holds them together but nothing couples them at the code level.

### 4.1 Monorepo structure

```
/
├── services/
│   ├── api-gateway/          C# .NET 8 (YARP)
│   ├── ingestion/            Python FastAPI
│   ├── extraction/           Python FastAPI
│   ├── evidence/             Python FastAPI
│   ├── vector-router/        Python FastAPI
│   ├── scoring/              Python FastAPI
│   ├── seeding/              Python FastAPI
│   ├── harvesting/           Python FastAPI
│   ├── job-orchestrator/     C# .NET 8
│   ├── model-router/         C# .NET 8
│   └── identity/             C# .NET 8
├── frontend/                 React + TypeScript (Vite)
└── deploy/                      Terraform (all Azure resource definitions)
```

Each folder under `services/` and `frontend/` is a standalone deployable unit. A developer working on one service never needs to build or run any other service to make progress. Service boundaries are enforced by network contracts (REST / Service Bus), not shared libraries.

### 4.2 The 11 services

1) **api-gateway** - C# .NET 8, YARP reverse proxy. Auth enforcement, routing, request shaping for the frontend. The only service the frontend talks to directly.
2) **ingestion** - Python FastAPI. Parse PDF / DOCX, clone git repo (GitHub, Azure DevOps, private), chunk text, build provenance map, store to Blob and Cosmos DB.
3) **extraction** - Python FastAPI. LLM invention candidate extraction. Calls Model Router for the LLM call. Outputs structured Invention Candidates with source spans.
4) **evidence** - Python FastAPI. Real patent API calls (USPTO, EPO OPS), LLM deep research synthesis, merge + dedup into Evidence Bundle with citations and confidence band.
5) **vector-router** - Python FastAPI. Standalone adapter for vector / embedding operations. Azure AI Search primary, Qdrant as secondary. Evidence service and any other service that needs vector search calls this service. Switching backends requires only config change here.
6) **scoring** - Python FastAPI. 5 axis patentability scoring. Calls Model Router for single or dual model pass. Produces scored verdict with per-axis breakdown and grounded citations.
7) **seeding** - Python FastAPI. Opportunity map generation, IDF drafting, claim seed generation, invention lattice / roadmap. Calls Model Router and consumes scored verdicts.
8) **harvesting** - Python FastAPI. Maturity classification (Mature vs Emerging), ranking by uniqueness / feasibility / strategic value, harvesting report assembly.
9) **job-orchestrator** - C# .NET 8. Saga-based pipeline coordinator. Tracks batch state for 100+ document runs. Publishes and consumes Azure Service Bus events. Handles fan-out, retries, and status reporting.
10) **model-router** - C# .NET 8. Adapter layer for all LLM calls. Primary is Azure AI Foundry (latest GPT-4o or o-series model). Secondary is Anthropic API (Claude). Runtime switchable per request. Dual-model adversarial mode supported. All responses carry grounding citations.
11) **identity** - C# .NET 8. Custom auth service. JWT-based sessions. Email/password for POC. Microsoft Entra ID as secondary login option. Roles: Researcher, Reviewer, Admin.

### 4.3 Service communication

Sync (REST): frontend -> api-gateway -> any service for interactive requests.
Async (Azure Service Bus): job-orchestrator fans out pipeline stages. Ingestion, extraction, evidence, scoring, seeding, harvesting each consume and publish events. Enables 100+ doc batches without blocking.

### 4.4 Azure resource mapping

1) Container hosting - Azure Container Apps, one app per service, autoscale on queue depth (KEDA)
2) Frontend hosting - Azure Static Web Apps or Azure App Service (Linux), depending on feature needs
3) AI models primary - Azure AI Foundry, latest available GPT model (GPT-4o or o-series). All AI calls go through Azure AI Foundry first.
4) AI models secondary - Anthropic API via model-router adapter for Claude
5) Vector / embedding - Azure AI Search primary (via vector-router service), Qdrant self-hosted on Container Apps as secondary
6) Relational DB - PostgreSQL (Azure Database for PostgreSQL Flexible Server), used by identity service
7) Document / candidate / evidence store - Azure Cosmos DB (NoSQL)
8) Raw files - Azure Blob Storage
9) Async messaging - Azure Service Bus (Standard tier minimum, Premium for production)
10) Secrets - Azure Key Vault
11) Container registry - Azure Container Registry
12) Logging / monitoring - Application Insights + Log Analytics Workspace
13) CI/CD - GitHub Actions (primary) + Azure Pipelines YAML (secondary, same logic)

All of the above are defined in Terraform under `/deploy/`. No Azure resource is created manually.

### 4.5 Frontend

React + TypeScript on Vite. Located at `/frontend/` as a standalone codebase. Component library decided at kickoff (Confirmed as Fluent UI v9). Main screens: Upload / batch and repo connect, Job progress, Results dashboard (harvesting report or seeding opportunity map), Opportunity detail with score -> evidence -> provenance drill down, Export. (Will have a initial Figma design file to iterate on before frontend development starts)

## 5. Model strategy

The LLM models are adapter based, implemented inside the model-router service.

1) Primary model: Azure AI Foundry - always the latest available GPT model. This is the default for all LLM calls.
2) Secondary model: Claude via Anthropic API. Available as an alternative or for dual-model adversarial scoring.
3) If both are enabled, the engine can score the same candidate with both models and surface agreement / disagreement explicitly.
4) If only one is configured, only that one is used. Single GPT, single Claude, or both - runtime configurable.
5) Switch happens at runtime through config or request header, no redeploy.

Grounding rule - every LLM verdict must cite the retrieved evidence. No ungrounded score is allowed, single model or dual.


## 6. Prior art / Evidence Triangulation

Per the decision, combine all three when available and fall back to whatever is up.

1) Real patent API (live prior art lookup)
2) Pre loaded seed corpus - curated set of patents, embedded and stored in the vector DB before demo. Fast offline similarity, tunable toward the demo research field. Accessed via vector-router.
3) LLM deep research - reasoning over non patent literature and synthesis via Azure AI Foundry

Output is one Evidence Bundle per candidate. Merged hits, dedup, per claim citations, flags showing which source contributed, and a confidence band. This is the audit trail that makes the score defensible.

### Patent data sources

1) USPTO / PatentsView - FREE. Moved to USPTO Open Data Portal (data.uspto.gov). Free API key. US patents only. Primary live lookup.
2) EPO OPS (Open Patent Services) - FREE up to ~4 GB/week quota, paid only if we cross that. Free OAuth key. Worldwide coverage. Secondary for international prior art.
3) Lens.org - programmatic API is paid commercial. Dropped for POC.

Seed corpus sources (free, no live API needed):

1) Google Patents Public Datasets on BigQuery - open, worldwide, minimal BigQuery compute cost.
2) USPTO bulk data (bulkdata.uspto.gov) - free bulk download, self-hosted.

All patent API calls go through a single adapter inside the evidence service so the source can be swapped without code change elsewhere.

### Vector / Embedding DB

Primary: Azure AI Search (vector + semantic). Secondary: Qdrant (self-hosted on Container Apps). Both exposed through the standalone vector-router service. Switching backends is a config change in vector-router only, no other service changes.


## 7. Identity

1) Custom Identity Service (identity/) with its own PostgreSQL database.
2) POC auth: email or username + password, JWT session, minimal.
3) Microsoft Entra ID as secondary login. Company Microsoft accounts can be used for POC testing.
4) Roles: Researcher, Reviewer, Admin.


## 8. IaC

All Azure infrastructure is defined as code in `/deploy/` using Terraform. No resource is created manually. IaC development runs in parallel with service development from day one. we will have dev and prod environments in Azure, with the same Terraform code deploying to both but different config (SKUs, secrets, etc). Only the dev environment is used during development and testing, prod is reserved for final staging and demo.

Structure under `/deploy/`:

```
deploy/
├── modules/
│   ├── container-apps/
│   ├── cosmos-db/
│   ├── service-bus/
│   ├── ai-foundry/
│   ├── ai-search/
│   ├── blob-storage/
│   ├── key-vault/
│   ├── container-registry/
│   ├── postgresql/
│   ├── monitoring/
│   └── static-web-app/
├── environments/
│   ├── dev/
│   └── prod/
└── main.tf
```

Terraform state is stored in Azure Blob Storage backend. Environment variables and secrets are injected via Azure Key Vault, never hardcoded. The `dev` environment uses minimal SKUs (scale-to-zero, free/basic tiers where available) to keep Azure credit spend low during development. (The deploy folder structure can be changed, this is just an example. The key point is that all Azure resources are defined in Terraform and there is no manual creation of any resource or roles.) only one engineer (will have access to the Azure subscription with full admin rights) will be responsible for the Terraform code and deployment, but all engineers will have read-only access to the resources created by Terraform for debugging and monitoring.

## 9. CI/CD

Both GitHub Actions workflows and Azure Pipelines YAML are maintained in sync. Each service has its own pipeline that builds, tests, pushes to Azure Container Registry, and deploys to Container Apps independently. Pipelines do not cross service boundaries.

The IaC pipeline (Terraform plan + apply) runs separately and is gated on approval.

there should be two yaml files for this, common for both GitHub and Azure Pipelines
1) ci.yml which will have format, lint, test, audit, build and deny for each service. This runs only on PR form dev to qa , no other branch. (dev azure environment)
2) cd.yml which will have terraform plan and apply, and also deploy each service to Azure Container Apps. This runs only qa pr, no other branch along with above. CD pipeline is gated on approval before deploy stage (again deployment in dev) (ci and cd can be comnibed) (initial the dev resource may not be available, so CD can fail until we have the dev environment ready, but we can still run CI for code quality and tests without the Azure resources)
3) For production deployment, we can have a separate pipeline called release.yml which will be triggered only during the version tag release and not on any branch. This will deploy to prod environment and that we show during demo. This pipeline is also gated on approval before deploy stage.


## 10. Implementation order (for planning and user story creation)

When converting this proposal into architecture, LLD, and user stories, the work should be structured in this order:

1. IaC - all Azure resource definitions first. Every service depends on Azure resources existing.
2. Identity Service - auth is a prerequisite for everything else.
3. API Gateway - routing layer needed before any service is callable from frontend.
4. Model Router - needed by extraction, scoring, seeding, harvesting.
5. Vector Router - needed by evidence.
6. Ingestion Service - start of the data pipeline.
7. Extraction Service - depends on ingestion output.
8. Evidence Service - depends on extraction output and vector-router.
9. Scoring Service - depends on evidence output.
10. Harvesting Service - depends on scoring output.
11. Seeding Service - depends on scoring output.
12. Job Orchestrator - ties the pipeline together for batch runs.
13. Frontend - all backend services must have stable contracts before full frontend build.
14. DevOps - CI/CD pipelines per service, IaC pipeline, environment promotion.
15. Testing - integration and E2E test suites, load testing for 100-doc batch.

User stories and implementation tasks must follow this order so dependencies are never blocked.


## 11. Future integration

This MVP is one module of the larger Cortexa platform (reference: CortexaAI folder). Every design decision here must not create a wall against future modules. Principles:

1) Service contracts (REST + Service Bus schemas) are versioned from day one. New modules can subscribe to existing events or call existing APIs without modifying MVP services.
2) The model-router, vector-router, ingestion, and evidence services are designed as platform utilities, not point solutions. Future modules (portfolio valuation, competitor monitoring, filing automation, etc.) should be able to reuse them directly.
3) The job-orchestrator saga pattern scales to new pipeline types without rewriting - add a new saga for each new engine.
4) Identity roles and JWT claims are extensible. New roles or permission scopes for future modules do not require breaking changes.
5) Cosmos DB collections and Blob Storage containers are namespaced per engine. Future modules get their own namespaces without collision.
6) The Azure AI Foundry integration in model-router is not hardcoded to patent tasks. Any future module that needs LLM calls uses the same router.


## 12. Risks and how we handle them

1) Claude not in Foundry - handled by the router adapter, Claude goes through Anthropic API. Azure AI Foundry (GPT) is always the primary.
2) Patent API access or rate limit problems in 1 month - the three source design means seed corpus and LLM still work if the API is slow or blocked.
3) 100 doc cost and latency - queue + autoscale (KEDA), async batch, cache embeddings, cap concurrency.
4) Azure spend - scale to zero on Container Apps dev environment, dev/basic SKUs, budget alert in week 1.
5) Scope creep into other modules - hard scope table in section 2, other modules clearly deferred.
6) LLM making up novelty - mandatory grounding + citations, dual model disagreement is surfaced not hidden.
7) Repo location changing (GitHub vs Azure DevOps) - both pipeline formats maintained.
8) Service coupling drift - enforced by the rule that services only communicate over network contracts. No shared library dependencies between services.


## 13. POC is accepted if

1) We upload 100+ research docs and the batch finishes on Azure.
2) We connect one git repo (public or private / Azure DevOps) and extract candidates.
3) Seeding produces Opportunity Map + IDF + claim seeds + roadmap for the demo paper, with the scored card.
4) Harvesting produces a ranked report with Mature vs Emerging classification.
5) Every score drills down to cited evidence and source provenance.
6) Model switch works at runtime - single GPT (Azure AI Foundry), single Claude, or both.
7) All 11 services run as independent containers on Azure Container Apps.
8) Login works with both email/password and company Microsoft account.
9) All Azure resources are created and managed by Terraform, nothing manual.


## 14. Open items for kickoff (not blocking approval)

1) Pick React component library (Fluent UI v9 vs shadcn).
2) Pick identity DB - leaning PostgreSQL (Azure Database for PostgreSQL Flexible Server).
3) Decide seed corpus domain to match the demo paper field.
4) Confirm Azure subscription ID, budget cap, and AI Foundry quota for latest GPT model.
5) Confirm Anthropic API key for Claude secondary.
6) Patent API keys day 1 - register free USPTO Open Data Portal key + EPO OPS OAuth key.
7) Confirm Terraform state backend storage account name and resource group.
