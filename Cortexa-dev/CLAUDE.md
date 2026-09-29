# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

<!-- claude-config
main_branch: dev
work_items_dir: .notes/Agile
release_branch: main
# Polyglot monorepo: no single solution_file or build_cmd. Commands are per-stack
# and run from inside the relevant service/ or frontend/ folder. The skills should
# pick the command set matching the files a work item touches.
build_cmd: per-stack — see build_commands below
test_cmd: per-stack — see build_commands below
format_cmd: per-stack — see build_commands below
lint_cmd: per-stack — see build_commands below
solution_file: none (independent per-service projects)
stacks: [python3.14-fastapi, dotnet8, react-typescript-vite, terraform]
build_commands:
  python:
    build: uv sync
    test: uv run pytest
    format: uv run ruff format --check .
    lint: uv run ruff check .
    audit: uv run pip-audit
  dotnet:
    build: dotnet build -warnaserror
    test: dotnet test
    format: dotnet format --verify-no-changes
    lint: dotnet build -warnaserror
    audit: dotnet list package --vulnerable
  frontend:
    build: pnpm build
    test: pnpm test
    format: pnpm exec eslint .
    lint: pnpm exec eslint .
    audit: pnpm audit
  terraform:
    build: terraform -chdir=deploy/environments/dev validate
    format: terraform fmt -check -recursive deploy
    lint: tflint --chdir=deploy
    audit: tfsec deploy
release_artifacts:
  # One container image per service, built from each service's own Dockerfile and
  # pushed to Azure Container Registry. Example for one service:
  # - name: api-gateway
  #   build_cmd: docker build -t Cortexa-api-gateway:${VERSION} services/api-gateway
  #   output_path: Cortexa-api-gateway:${VERSION}
  #   docker_image: true
  #   filename_template: Cortexa-api-gateway-v${VERSION}.tar
-->

---

## Project Overview

Cortexa reads raw research material — papers, thesis files, code — and finds where the patent opportunity is hiding. Two engines run on one shared pipeline. The **Harvesting Engine** finds patentable inventions already buried in an asset. The **Seeding Engine** proposes new patent opportunities from the asset plus roadmap context. Every patentability score is backed by three independent evidence sources (live patent APIs, a pre-loaded patent corpus searched by vectors, and an LLM deep-research pass) with citations. The MVP delivers these two engines as 11 independent microservices in a monorepo, deployed to Azure.

---

## Key Reference Documents

| Document | Path |
|----------|------|
| Architecture | `.notes/informations/Architectures/Cortexa_Architecture.md` |
| Low-Level Design | `.notes/informations/Architectures/Cortexa_LowLevelDesign.md` |
| Implementation Guide | `.notes/informations/Architectures/Cortexa_ImplementationGuide.md` |
| Project Plan | `.notes/informations/Architectures/Cortexa_ProjectPlan.md` |
| Deployment Design | `.notes/informations/Architectures/Cortexa_DeploymentDesign.md` |
| Coding Standards | `.notes/informations/Development_Notes/CODING_STANDARDS.md` |
| Developer Setup | `.notes/informations/Development_Notes/DEVELOPER_SETUP.md` |
| CI/CD Setup | `.notes/informations/Development_Notes/CI_SETUP.md` |
| Azure Status | `.notes/informations/Development_Notes/AZURE_STATUS.md` |
| DLQ Runbook | `.notes/informations/Development_Notes/DLQ_RUNBOOK.md` |

---

## Tech Stack

Polyglot monorepo. Each service is an independent codebase; services talk only over REST and Azure Service Bus (no shared libraries).

| Area | Technology |
|------|-----------|
| Python services | Python 3.14, FastAPI, Pydantic v2, httpx, uv (ingestion, extraction, evidence, vector-router, scoring, seeding, harvesting) |
| .NET services | C# / .NET 8, YARP (api-gateway), Npgsql (identity) (api-gateway, identity, model-router, job-orchestrator) |
| Frontend | React, TypeScript, Vite, Fluent UI v9, pnpm |
| LLM | Azure AI Foundry (GPT) primary, Anthropic Claude secondary, via model-router |
| Vector | Azure AI Search primary, Qdrant secondary, via vector-router |
| Data | Azure Cosmos DB (pipeline), Azure PostgreSQL Flexible (identity), Azure Blob Storage (raw files) |
| Messaging | Azure Service Bus |
| Hosting | Azure Container Apps (one app per service), Azure Static Web Apps (frontend) |
| Secrets / Registry / Monitoring | Azure Key Vault, Azure Container Registry, Application Insights + Log Analytics |
| IaC | Terraform (`deploy/`), dev + prod environments |
| CI/CD | GitHub Actions |

---

## Build Commands

Polyglot monorepo — there is no single build command. Each service is built, tested, and linted from inside its own folder using the toolchain for its stack. Run the command set that matches the files a work item touches.

**Python services** (`services/{ingestion,extraction,evidence,vector-router,scoring,seeding,harvesting}/`) — Python 3.14 + uv:

```bash
uv sync                        # install/build
uv run pytest                  # test
uv run pytest tests/test_foo.py::test_bar  # single test
uv run ruff format --check .   # format check
uv run ruff check .            # lint
uv run pip-audit               # dependency audit
```

**.NET services** (`services/{api-gateway,identity,model-router,job-orchestrator}/`) — C# / .NET 8:

```bash
dotnet build -warnaserror              # build (lint-equivalent: warnings are errors)
dotnet test                            # test
dotnet test --filter "FullyQualifiedName~TestClass.TestMethod"  # single test
dotnet format --verify-no-changes      # format check
dotnet list package --vulnerable       # dependency audit
```

**Frontend** (`frontend/`) — React + TypeScript + Vite, pnpm:

```bash
pnpm install                   # install
pnpm build                     # build
pnpm test                      # test
pnpm exec eslint .             # lint + format check
pnpm audit                     # dependency audit
```

**IaC** (`deploy/`) — run all Terraform commands from the **repo root**, not from inside `deploy/`:

```bash
terraform fmt -check -recursive deploy            # format check (run from repo root)
terraform fmt -recursive deploy                   # auto-fix formatting
terraform -chdir=deploy/environments/dev init     # init (once, or after module changes)
terraform -chdir=deploy/environments/dev validate # validate
tflint --chdir=deploy                             # lint (uses deploy/.tflint.hcl)
tfsec deploy                                      # security scan
```

Each service ships its own `Dockerfile`; release artifacts are one container image per service, pushed to Azure Container Registry.

---

## Python Environment Rules

If this project contains any Python code, **never** invoke Python using the system Python or any globally installed interpreter. Always use the project-local virtual environment defined in `.pyvenv`.

### How it works

`.pyvenv` is a gitignored file at the repo root. The project owner creates it manually and it defines the exact virtual environment to use, along with the activation command. Claude reads this file before running any Python command.

**Before running any Python command:**

1. Read `.pyvenv` at the repo root.
2. Extract the activation command from the code block inside it.
3. Run that activation command, then run the Python command in the same shell session.

**If `.pyvenv` does not exist:**

Do not run any Python code. Stop and ask Alpha to create `.pyvenv` before continuing.

---

## Terraform Infrastructure Architecture

### Module Catalog

All modules live under `deploy/modules/`. Every module shares the same four base inputs: `project`, `environment`, `location`, `resource_group_name`. All accept a `tags` map.

| Module | Resources Created | Key Non-Standard Inputs | Sensitive Outputs |
|--------|------------------|------------------------|-------------------|
| `key-vault` | Key Vault (RBAC, purge protection, network ACLs) | `tenant_id`, `allowed_ip_rules`, `allowed_subnet_ids` | — |
| `container-registry` | Azure Container Registry | `sku` (Basic/Standard/Premium), `admin_enabled` | `admin_password` |
| `monitoring` | Log Analytics Workspace + Application Insights | `retention_in_days` | `connection_string`, `instrumentation_key` |
| `blob-storage` | Storage account + private containers | `name_suffix`, `containers` (list of names) | `primary_connection_string` |
| `cosmos-db` | Cosmos account + `cortexa-pipeline` database + 10 containers | `throughput` (autoscale RU/s, dev default 400) | `primary_connection_string` |
| `postgresql` | PostgreSQL Flexible Server + `identity` database + firewall rules | `admin_password` (sensitive), `sku_name`, `allowed_ip_ranges`, `public_network_access_enabled` | `connection_string` |
| `service-bus` | Service Bus namespace + 11 topics + subscriptions + scoped auth rule | `sku` (Standard dev, Premium prod) | `primary_connection_string` |
| `ai-foundry` | Cognitive Services account (AIServices kind) + GPT cognitive deployment | `gpt_model_name`, `gpt_model_version`, `gpt_deployment_name`, `deployment_sku_name`, `deployment_capacity` (TPM quota) | `primary_key` |

### Resource Naming

All resources follow `{project}-{environment}-{suffix}` (e.g. `cortexa-dev-kv`). Storage accounts and ACR names strip hyphens and truncate to meet Azure limits (storage: 24 chars, ACR: 50 chars).

### Terraform Directory Layout

```
deploy/
├── versions.tf                  # canonical provider version reference (azurerm ~> 3.0)
├── .tflint.hcl                  # tflint azurerm ruleset config
├── modules/{module-name}/       # reusable modules (main.tf, variables.tf, outputs.tf)
└── environments/
    └── dev/
        ├── versions.tf          # provider block with features {}
        ├── main.tf              # module instantiation + resource group
        ├── variables.tf         # all environment inputs
        ├── outputs.tf           # all environment outputs
        └── terraform.tfvars.example
```

`terraform.tfvars` is gitignored. Copy the `.example` file and supply `tenant_id`. Supply `postgresql_admin_password` via environment variable (`export TF_VAR_postgresql_admin_password="..."`), never in tfvars.

### Non-Obvious Design Decisions

**Cosmos DB**: All 10 pipeline containers (`documents`, `chunks`, `provenance_maps`, `candidates`, `evidence_bundles`, `verdicts`, `reports`, `harvesting`, `batches`, `seeding`) partition on `/batch_id`. The `seeding` container backs the seeding REST `generate-lattice` write path; batch-pipeline seeding results live in `reports` (engine=`seeding`). The `batches` container is the saga state store the job-orchestrator reads/writes via `CosmosSagaRepository`; without it every batch lifecycle endpoint returns 500. Access is via managed identity — the module does not export a primary key, only the connection string.

**Service Bus**: Topic names replace dots with hyphens in Terraform resource names (e.g. `ingestion.requested` → resource key `ingestion-requested`). The module creates a scoped `services` authorization rule with Send+Listen rights only (no Manage); the `primary_connection_string` output returns this scoped string, not the root namespace key. The 11 pipeline topics map to the saga events in `Cortexa_Architecture.md`.

**PostgreSQL**: `public_network_access_enabled` defaults to `false` (secure). The dev environment explicitly overrides it to `true` as a stopgap until VNet/private-endpoint integration is provisioned. Set to `false` in prod and wire `azurerm_private_endpoint` instead.

**Adding a prod environment**: Copy `deploy/environments/dev/` to `deploy/environments/prod/`, update `environment` default to `prod`, and raise SKUs: ACR `Premium`, Service Bus `Premium`, PostgreSQL `GP_Standard_D2s_v3`, Cosmos `throughput = 4000`.

---

## Azure DevOps Boards Integration

`.azure/` contains scripts that push work-item JSON files to Azure DevOps Boards.

**Setup** (once):

```bash
cp .azure/config.json.example .azure/config.json
# fill in: organization, project, team, pat (Personal Access Token), defaultAssignee
```

**Push a work item**:

```bash
# Auto-detects type from JSON's workItemType field:
.azure/manageboards.sh --json .notes/Agile/UserStory/Sprint1/US001_MyStory.json

# Explicit type override:
.azure/manageboards.sh --userstory --json path/to/story.json
.azure/manageboards.sh --bug       --json path/to/bug.json
```

User Story JSONs with a `Tasks` array automatically create child Task work items linked to the parent story. `.azure/config.json` is gitignored.

---

## Development Workflow

One user story at a time:

1. Ensure `.notes/informations/Architectures/` docs exist.
2. Either pick an existing work item (set its ID in `.notes/instruction.xml`) or create one via `/create-user-story` or `/create-bug`.
3. Run `/start-item`.
4. Review the generated file table. On approval, Alpha runs `/complete-item`.

See the global config at `/root/.claude/CLAUDE.md || ~/.claude/CLAUDE.md` for engineering standards, ACE rules, and the review/test policy.
