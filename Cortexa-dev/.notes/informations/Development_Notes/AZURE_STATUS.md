# Azure Infrastructure Status

**Last verified:** 2026-06-25 (BUG028 — live-checked via `az` and `gh`)  
**Environment:** dev  
**Subscription:** Microsoft Azure Sponsorship (`9835c61d-6a4a-454e-8d09-1b383899f7ab`)  
**Resource group:** `cortexa-dev-rg`

---

## Azure CLI Access

The CLI inside this dev container is **pre-authenticated** as the `cortexa-terraform` service principal. No login step is needed. Use `az` commands directly.

```bash
az account show   # confirms active auth — appId/client-id: 8cde29ab-8582-4bf9-9cde-11578fe36b28
```

**The session identity is a service principal, and that has two consequences later Claude must not trip over:**

- **No Microsoft Graph directory read.** `az ad app show`, `az ad app federated-credential list`, and any `--assignee <upn>` Graph lookup fail with `Insufficient privileges to complete the operation`. Use `az ad sp show --id <appId>` (works) and reference principals by **object id**, not name. This is also why pipeline `az role assignment create` calls must use `--assignee-object-id ... --assignee-principal-type ServicePrincipal` rather than `--assignee`.
- **Terraform cannot run locally.** The `azurerm` provider rejects Azure-CLI auth when the logged-in identity is a service principal (`Authenticating using the Azure CLI is only supported as a User`). So `terraform plan/apply/import` cannot be run from this container — they only work inside the CI pipeline via OIDC. Local `terraform validate` / `terraform fmt` / `terraform init -backend=false` still work (no Azure auth needed).

---

## App Registration (Service Principal)

`cortexa-terraform` has **three identifiers that are easy to confuse** — using the wrong one is a common failure:

| Field | Value | Used for |
|-------|-------|----------|
| Name | `cortexa-terraform` | — |
| Application (client) ID | `8cde29ab-8582-4bf9-9cde-11578fe36b28` | GitHub secret `AZURE_CLIENT_ID`; `azure/login` client-id; `az ad sp show --id <this>` |
| **Application object ID** | `5a12eb1a-64a1-4cdb-bef7-df20ca6c88a3` | `az ad app federated-credential ...` (the `--id` arg) **only** |
| **Service principal object ID** | `da3f8e92-d9ad-44c7-924e-ffe616e8eaaf` | **role assignments**; GitHub secret `DEPLOYER_OBJECT_ID` / `TF_VAR_deployer_object_id` |
| Tenant ID | `ce6b40b4-42e6-488c-8f56-ffb603c43c66` | GitHub secret `AZURE_TENANT_ID`; `TF_VAR_tenant_id` |

> Role assignments and `DEPLOYER_OBJECT_ID` use the **SP object id** `da3f8e92…`. Federated credentials use the **app object id** `5a12eb1a…`. They are different objects — do not substitute one for the other.

### Role Assignments (live, verified 2026-06-25)

| Role | Scope |
|------|-------|
| Contributor | `cortexa-dev-rg` |
| Contributor | `cortexa-prod-rg` |
| User Access Administrator | subscription |
| Storage Blob Data Contributor | `cortexatfstate` storage account |
| Key Vault Secrets Officer | `cortexa-dev-kv` |

`Key Vault Secrets Officer` on `cortexa-dev-kv` is **owned by Terraform** (`azurerm_role_assignment.deployer_secrets`) and tracked in dev state as of BUG028. Do not grant it a second way (no `az role assignment create` in the pipelines) — a duplicate creator causes `409 RoleAssignmentExists`. See CI_SETUP.md §5 for the ownership model and the 409-recovery runbook.

---

## Key Vault RBAC — Least Privilege Restored (BUG058 → BUG059 RESOLVED)

**As of BUG059 (2026-07-01):** Least privilege restored. The shared Container Apps managed identity (`cortexa-dev-ca-identity`, used by 10 services: ingestion, extraction, evidence, vector-router, scoring, seeding, harvesting, api-gateway, identity, model-router) is reverted to read-only `Key Vault Secrets User`. The job-orchestrator now runs on a dedicated managed identity (`cortexa-dev-orchestrator-identity`) holding vault-wide `Key Vault Secrets Officer` so it can delete per-batch git PAT secrets (`batch-git-pat-{batchId}`) during batch-delete cascade.

**Why vault-wide Officer, not a prefix-scoped role?** Azure Key Vault RBAC data-plane roles cannot scope by secret-name prefix (`batch-git-pat-*`). The only available scopes are (a) the entire vault, or (b) a single named secret (via the secret's resource id). Since batch IDs are runtime GUIDs not known at provisioning time, the only option that permits deleting batch secrets without enumerating every future ID is vault-wide Officer. A custom RBAC role definition could narrow the permitted actions to just secret delete (instead of the full Officer set/purge/backup/restore suite), but it would still be vault-wide. The compromise: a dedicated identity with vault-wide Officer is the achievable hardening — only the job-orchestrator holds this grant, and it is isolated from the 10 other services by identity boundary.

**Identity assignment:** The job-orchestrator Container App is assigned ONLY the dedicated orchestrator identity (not both). This keeps `AZURE_CLIENT_ID` and the KEDA Service Bus workload-identity scaler unambiguous (BUG059 constraint). The orchestrator identity receives the same data-plane access the shared identity holds (Service Bus Sender/Receiver, Blob Contributor, Cosmos data contributor, AI Search Index Data Contributor) so the orchestrator retains all capabilities it had when it shared the common identity — just separated onto its own principal.

**Prior state (BUG058):** The shared identity temporarily held `Key Vault Secrets Officer` as a stopgap to unblock batch-delete cascade. This granted all 11 services set/delete on every vault secret, including JWT signing keys, DB connection strings, patent API keys, and HMAC secrets — a security over-privilege that BUG059 closes.

---

## Container Apps (dev) — All Running

| Container App | Status |
|---------------|--------|
| `cortexa-dev-api-gateway` | Running |
| `cortexa-dev-ingestion` | Running |
| `cortexa-dev-extraction` | Running |
| `cortexa-dev-evidence` | Running |
| `cortexa-dev-vector-router` | Running |
| `cortexa-dev-scoring` | Running |
| `cortexa-dev-seeding` | Running |
| `cortexa-dev-harvesting` | Running |
| `cortexa-dev-model-router` | Running |
| `cortexa-dev-job-orchestrator` | Running |
| `cortexa-dev-identity` | Running |

**api-gateway public FQDN:** `cortexa-dev-api-gateway.proudsmoke-86efe866.eastus.azurecontainerapps.io`

All backend Container Apps are internal-only. The api-gateway is the sole public entry point and proxies all service routes (e.g. `/ingestion/health`, `/scoring/health`).

To verify live status:
```bash
az containerapp list --resource-group cortexa-dev-rg \
  --query "[].{name:name, state:properties.runningStatus}" -o table
```

---

## Other Provisioned Resources (dev)

| Resource | Name |
|----------|------|
| Key Vault | `cortexa-dev-kv` |
| Container Registry | `cortexadevacr` |
| Cosmos DB | `cortexa-dev-cosmos` |
| PostgreSQL Flexible Server | `cortexa-dev-pg-3` |
| Service Bus | `cortexa-dev-bus` |
| AI Foundry (GPT + Claude + embedding) | `cortexa-dev-ai-resource` (consolidated — see BUG046) |
| AI Search | `cortexa-dev-search` |
| Blob Storage | `cortexadevstorage` |
| Log Analytics | `cortexa-dev-law` |
| Application Insights | `cortexa-dev-ai` |
| Static Web App | `cortexa-dev-swa` (Standard SKU, api-gateway linked as backend — US072) |
| Container Apps Environment | `cortexa-dev-ca-env` |

Terraform state is stored in storage account `cortexatfstate` in resource group `cortexa-tfstate-rg`.

---

## CI/CD Pipeline Status

| Pipeline | Trigger | Target | Status (2026-06-25) |
|----------|---------|--------|--------|
| `ci.yml` | PR → `qa` | Lint + test + build, no cloud | Green |
| `cd.yml` | PR → `qa` | Terraform + deploy to dev | **Green** — OIDC, environments, and KV role all live; CD run 28154567242 succeeded end-to-end (plan + gated apply) |
| `release.yml` | Git tag `v*.*.*` | Terraform + deploy to prod | Configured — never run (prod infra not yet provisioned; prod secrets are placeholders, see below) |

Workflow files present in `.github/workflows/`: `ci.yml`, `cd.yml`, `release.yml`, `import-kv-role.yml` (one-shot KV-role import/recovery — see CI_SETUP.md §5), and the reusable callables `_changes.yml`, `_build-push.yml`, `_deploy-service.yml`, `_python-service.yml`, `_dotnet-service.yml`, `_frontend.yml`.

### GitHub Secrets — repository level (live)

| Secret | Set | Notes |
|--------|-----|-------|
| `AZURE_CLIENT_ID` | Yes | the app/client id `8cde29ab…` |
| `AZURE_TENANT_ID` | Yes | resolves `TF_VAR_tenant_id` for every job |
| `AZURE_SUBSCRIPTION_ID` | Yes | — |
| `SWA_DEV_DEPLOYMENT_TOKEN` | Yes | SWA deployment token for `cortexa-dev-swa`; used by `frontend-deploy` in `cd.yml` to upload the React SPA to the production slot (US073) |

Repository-level **variables**: none. All `TF_VAR_*` non-secret config lives at the environment level (below).

### GitHub Environments (live — all exist)

| Environment | Used by | Reviewers / gate |
|-------------|---------|------------------|
| `dev-plan` | `cd.yml` tf-plan + smoke | none (0 protection rules) |
| `dev-deploy` | `cd.yml` tf-apply | none (0 protection rules) |
| `production-plan` | `release.yml` tf-plan, build-push, deploy, smoke | none (0 protection rules) |
| `production` | `release.yml` tf-apply | **1 protection rule (required reviewer)** — gates the prod apply |

### Per-environment secrets and variables (live)

Each of the four environments carries the same shape. `AZURE_*` come from repo level (not re-registered per env, except dev which redundantly duplicates the two Azure ones — harmless).

| Name | Type | `dev-plan` | `dev-deploy` | `production-plan` | `production` |
|------|------|:--:|:--:|:--:|:--:|
| `DEPLOYER_OBJECT_ID` | secret | ✅ | ✅ | ✅\* | ✅\* |
| `POSTGRESQL_ADMIN_PASSWORD` | secret | ✅ | ✅ | ✅\* | ✅\* |
| `TFPLAN_PASSPHRASE` | secret | ✅ | ✅ | ✅\* | ✅\* |
| `PATENT_USPTO_API_KEY` | secret | ✅ | ✅ | ✅\* | ✅\* |
| `PATENT_EPO_CONSUMER_KEY` | secret | ✅ | ✅ | ✅\* | ✅\* |
| `PATENT_EPO_OAUTH_SECRET` | secret | ✅ | ✅ | ✅\* | ✅\* |
| `PATENT_LENS_API_KEY` | secret | ✅ | ✅ | ✅\* | ✅\* |
| `AZURE_CLIENT_ID` | secret | ✅ (dup of repo) | ✅ (dup of repo) | — (repo) | — (repo) |
| `AZURE_TENANT_ID` | secret | ✅ (dup of repo) | ✅ (dup of repo) | — (repo) | — (repo) |
| `BUDGET_CONTACT_EMAILS` | var | ✅ | ✅ | ✅ | ✅ |
| `BUDGET_MONTHLY_AMOUNT` | var | ✅ | ✅ | ✅ | ✅ |
| `KEY_VAULT_ALLOWED_IP_RULES` | var | ✅ | ✅ | ✅ | ✅ |
| `POSTGRESQL_ALLOWED_IP_RANGES` | var | ✅ | ✅ | ✅ | ✅ |

> **\* Prod secrets are placeholder values** (`REPLACE_ME_*`), set in BUG027 so `release.yml` resolves without error. They MUST be replaced with real prod values before the first `v*.*.*` tag, in particular a prod-specific `POSTGRESQL_ADMIN_PASSWORD`, the real `DEPLOYER_OBJECT_ID` (if a separate prod SP is used), `TFPLAN_PASSPHRASE`, and the patent keys.
>
> **Prod variable values currently MIRROR dev** (`KEY_VAULT_ALLOWED_IP_RULES = ["0.0.0.0/0"]`, `POSTGRESQL_ALLOWED_IP_RANGES = allow-all`, budget = 500). Review and lock these down for prod — the open IP rules are a real prod security hole if left as-is.

### OIDC Federated Credentials (on App Registration — all live)

These exist on the `cortexa-terraform` app registration (**app object id** `5a12eb1a-64a1-4cdb-bef7-df20ca6c88a3` — the `--id` arg for `az ad app federated-credential`). They are confirmed working: `cd.yml` authenticates and runs green. The session SP cannot list them (`Insufficient privileges`), so verify by a green pipeline run rather than `az ad app federated-credential list`.

| Credential name | Subject | Used by |
|----------------|---------|---------|
| `github-actions-pr` | `repo:AlphaGhostUSMC/Cortexa:pull_request` | `cd.yml` build-push + deploy (same-repo PRs only) |
| `github-actions-dev-plan` | `repo:AlphaGhostUSMC/Cortexa:environment:dev-plan` | `cd.yml` tf-plan + smoke |
| `github-actions-dev-deploy` | `repo:AlphaGhostUSMC/Cortexa:environment:dev-deploy` | `cd.yml` tf-apply |
| `github-actions-production-plan` | `repo:AlphaGhostUSMC/Cortexa:environment:production-plan` | `release.yml` tf-plan, build-push, deploy, smoke |
| `github-actions-production` | `repo:AlphaGhostUSMC/Cortexa:environment:production` | `release.yml` tf-apply |

> **Note 1:** The `pull_request` subject is not scoped to a specific base branch — any same-repo PR-triggered job without an `environment:` block emits it. The `cd.yml` trigger (`branches: [qa]`) plus the `full_name` guard on `build-push`/`deploy` limit exposure. `tf-plan` and `smoke` use the tighter `dev-plan` subject.
> **Note 2:** Azure AD subjects are exact-match strings. Glob patterns (e.g., `ref:refs/tags/v*.*.*`) are not supported and will never authenticate. Use `environment:X` subjects for all jobs where possible.

If a credential is ever missing (new environment, recreated app), recreate it from the authenticated dev container. The `--id` is the **app object id**, NOT the SP object id:

```bash
APP_OID="5a12eb1a-64a1-4cdb-bef7-df20ca6c88a3"
ISSUER="https://token.actions.githubusercontent.com"
AUDIENCE="api://AzureADTokenExchange"

az ad app federated-credential create --id "$APP_OID" --parameters "{\"name\":\"github-actions-pr\",\"issuer\":\"$ISSUER\",\"subject\":\"repo:AlphaGhostUSMC/Cortexa:pull_request\",\"audiences\":[\"$AUDIENCE\"]}"

az ad app federated-credential create --id "$APP_OID" --parameters "{\"name\":\"github-actions-dev-plan\",\"issuer\":\"$ISSUER\",\"subject\":\"repo:AlphaGhostUSMC/Cortexa:environment:dev-plan\",\"audiences\":[\"$AUDIENCE\"]}"

az ad app federated-credential create --id "$APP_OID" --parameters "{\"name\":\"github-actions-dev-deploy\",\"issuer\":\"$ISSUER\",\"subject\":\"repo:AlphaGhostUSMC/Cortexa:environment:dev-deploy\",\"audiences\":[\"$AUDIENCE\"]}"

az ad app federated-credential create --id "$APP_OID" --parameters "{\"name\":\"github-actions-production-plan\",\"issuer\":\"$ISSUER\",\"subject\":\"repo:AlphaGhostUSMC/Cortexa:environment:production-plan\",\"audiences\":[\"$AUDIENCE\"]}"

az ad app federated-credential create --id "$APP_OID" --parameters "{\"name\":\"github-actions-production\",\"issuer\":\"$ISSUER\",\"subject\":\"repo:AlphaGhostUSMC/Cortexa:environment:production\",\"audiences\":[\"$AUDIENCE\"]}"
```

---

## Patent API Key Status

| Secret name | Key Vault (`cortexa-dev-kv`) | Status |
|-------------|------------------------------|--------|
| `epo-consumer-key` | Set (2026-06-24) | Active |
| `epo-oauth-secret` | Set (2026-06-24) | Active |
| `uspto-api-key` | Set (2026-07-03) | Active — live-probed 200 OK against `/api/v1/patent/applications/search` |
| `lens-api-key` | Set | Active |

Evidence service reads these at startup via managed identity. After updating a secret value in Key Vault, restart the `cortexa-dev-evidence` container app to pick up the new credential:
```bash
az containerapp revision restart \
  --name cortexa-dev-evidence \
  --resource-group cortexa-dev-rg \
  --revision $(az containerapp revision list --name cortexa-dev-evidence --resource-group cortexa-dev-rg --query "[0].name" -o tsv)
```

---

## Prod Environment

Prod infrastructure (`cortexa-prod-rg`) **exists but is empty** — no resources, and `cortexa-prod-kv` does not exist yet. The Terraform config at `deploy/environments/prod/` has never been applied. It will be provisioned on the first `release.yml` run triggered by a `v*.*.*` tag.

Pipeline plumbing for prod is in place and verified: `release.yml` has the `TF_VAR_*` injection and gpg-encrypted plan artifact (BUG027), the `production-plan`/`production` GitHub Environments exist with the full secret/var set, the OIDC federated credentials exist, and the `production` environment has its required-reviewer gate.

**Before the first prod release, complete these (none are code changes):**
1. Replace the placeholder `REPLACE_ME_*` prod secrets on `production-plan` and `production` with real values (see the per-environment table above).
2. Review/lock the prod `KEY_VAULT_ALLOWED_IP_RULES` and `POSTGRESQL_ALLOWED_IP_RANGES` variables — they currently mirror dev's open values.
3. After the first apply creates `cortexa-prod-kv`, the `Key Vault Secrets Officer` role is created and tracked by Terraform automatically (no manual grant). If a 409 ever occurs for prod, run `import-kv-role.yml` with `environment=prod` (see CI_SETUP.md §5).
4. Prod `deploy/environments/prod/` already has `static_web_app_sku_tier/size = "Standard"` as defaults and `linked_backend_resource_id` wired to the prod api-gateway Container App — the linked backend will be created automatically on the first `release.yml` apply.

---

## Static Web App — Backend Link (US072)

**As of US072 (2026-06-25):** `cortexa-dev-swa` is upgraded to **Standard SKU** (~$9/month) and the `cortexa-dev-api-gateway` Container App is linked as its backend via Terraform (`azapi_resource` — `Microsoft.Web/staticSites/linkedBackends@2023-01-01`). This lets the React SPA call `/api/*` and Azure proxies those requests server-side to the gateway (same-origin, no CORS).

**Verify after CD apply:**
```bash
az staticwebapp backends show \
  --name cortexa-dev-swa \
  --resource-group cortexa-dev-rg
```
Expected: output lists `cortexa-dev-api-gateway` as the linked backend.

**Cost note:** Standard SWA is ~$9/month (dev). Prod mirrors the same configuration and will incur the same ~$9/month once provisioned.

---

## api-gateway CORS wiring (BUG026 follow-up)

The api-gateway reads allowed origins from `Cors:AllowedOrigins` (ASP.NET Core config). Terraform wires these via the `gateway_cors_allowed_origins` input variable on `module.container_apps`, which expands to `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, … env vars on all Container Apps (other services ignore them). Default is `[]` — no origins allowed.

**Dev:** The dev SWA default hostname is `nice-beach-0701ce10f.7.azurestaticapps.net`. The `GATEWAY_CORS_ALLOWED_ORIGINS` GitHub Actions variable on the `dev-plan` and `dev-deploy` environments must be set to:
```
["https://nice-beach-0701ce10f.7.azurestaticapps.net"]
```

Set it once via:
```bash
gh variable set GATEWAY_CORS_ALLOWED_ORIGINS \
  --env dev-plan \
  --body '["https://nice-beach-0701ce10f.7.azurestaticapps.net"]'

gh variable set GATEWAY_CORS_ALLOWED_ORIGINS \
  --env dev-deploy \
  --body '["https://nice-beach-0701ce10f.7.azurestaticapps.net"]'
```

**Prod:** After the first prod apply, retrieve the SWA hostname and set the variable on both prod environments:
```bash
terraform -chdir=deploy/environments/prod output static_web_app_default_host_name
# Then:
gh variable set GATEWAY_CORS_ALLOWED_ORIGINS --env production-plan --body '["https://<prod-swa-host>"]'
gh variable set GATEWAY_CORS_ALLOWED_ORIGINS --env production     --body '["https://<prod-swa-host>"]'
```

**No cycle:** the SWA hostname is supplied as a variable value, not read from `module.static_web_app.default_host_name`, which would create a `container_apps → static_web_app → container_apps` dependency cycle and break `terraform plan`. The value is stable once the SWA is provisioned; it only changes if the SWA resource is destroyed and re-created.

---

## Terraform State Drift (BUG045)

**Resolved 2026-06-29.** Three Azure resources existed live AND in Terraform config but were missing from the dev Terraform state. This blocked all CD pipeline `terraform apply` runs with "resource already exists — needs to be imported into State."

**Resources imported into state (idempotently, in `.github/workflows/cd.yml` tf-plan job):**

1. **KV secret** (root level): `azurerm_key_vault_secret.platform["job-orchestrator-cosmos-uri"]`
   - Resource ID: `https://cortexa-dev-kv.vault.azure.net/secrets/job-orchestrator-cosmos-uri` (versionless form, resolved at import time via `az keyvault secret show`)
2. **Cosmos DB batches container**: `module.cosmos_db.azurerm_cosmosdb_sql_container.this["batches"]`
   - Resource ID: `/subscriptions/9835c61d-6a4a-454e-8d09-1b383899f7ab/resourceGroups/cortexa-dev-rg/providers/Microsoft.DocumentDB/databaseAccounts/cortexa-dev-cosmos/sqlDatabases/cortexa-pipeline/containers/batches`
3. **Service Bus batch.created topic**: `module.service_bus.azurerm_servicebus_topic.this["batch.created"]`
   - Resource ID: `/subscriptions/9835c61d-6a4a-454e-8d09-1b383899f7ab/resourceGroups/cortexa-dev-rg/providers/Microsoft.ServiceBus/namespaces/cortexa-dev-bus/topics/batch-created`
   - Note: State key is DOTTED (`batch.created`) because `for_each = toset(local.topics)` and `local.topics` uses dotted names; the Azure resource name is HYPHENATED (`batch-created`) per the module's resource block.

**App-managed secrets (NOT managed by Terraform):**

Key Vault secrets `identity-admin-email` and `identity-admin-initial-password` are written by the identity service at runtime via managed identity. They are NOT declared in Terraform and must NOT be imported. If they appear in future drift audits, they are expected out-of-band resources that Terraform is not responsible for.

---

## Container Apps Health Probes and KEDA Autoscaling (US059)

**As of US059 (2026-06-28):** The container-apps module now defines health probes and KEDA autoscaling rules for all 11 services. Implementation follows **Option A** from the US059 design: only `job-orchestrator` scales on Service Bus queue depth (it genuinely consumes the `orchestrator` subscriptions on the `*.completed` topics), while all other services scale on HTTP concurrency (they are HTTP-driven via the gateway).

### Health Probes

All services expose `/health` on their native port (Python services on 8000, .NET services on 5000). Three probes are configured per service:

- **Liveness probe:** checks every 30 seconds, 5-second timeout, fails after 3 consecutive failures.
- **Readiness probe:** checks every 10 seconds, 3-second timeout, fails after 3 consecutive failures, succeeds after 1 success.
- **Startup probe:** checks every 10 seconds, 5-second timeout. `identity` gets 30 failures (300 seconds cold-start window for EF Core migrate + admin seed), all other services get 10 failures (100 seconds).

### KEDA Autoscaling

**Service Bus scaling (job-orchestrator only):**
- `custom_scale_rule` with `custom_rule_type = "azure-servicebus"`
- Monitors the `scoring.completed` topic, `orchestrator` subscription (configurable via `orchestrator_servicebus_topic` and `orchestrator_servicebus_subscription` variables)
- Message count threshold: 5 (configurable via `servicebus_scaler_message_count` variable)
- **Authentication:** workload identity (the `azurerm_user_assigned_identity.ca_identity` assigned to every app). No connection string secret. The UAMI has `Azure Service Bus Data Receiver` role on the namespace (granted in `deploy/environments/{dev,prod}/main.tf`). In azurerm 4.x, omitting the `authentication` block entirely signals identity-based auth.

**HTTP concurrency scaling (all other 10 services):**
- `http_scale_rule` with `concurrent_requests = 50` (configurable via `http_concurrent_requests` variable)
- No Service Bus scale rule — the `*.requested` topics have no queue consumers; the pipeline orchestrator dispatches those as HTTP calls through the gateway, not via message consumption.

**Replica bounds:**
- Dev: `min_replicas = 0` (scale-to-zero, except `identity` which is in `warm_services`), `max_replicas = 5`
- Prod: `min_replicas = 1` (always-on), `max_replicas = 10`

### Architecture Decision Record

The pipeline is **HTTP-orchestrated, not queue-worker**. Only `job-orchestrator` consumes Service Bus (the `orchestrator` subscriptions on the `*.completed` + `engine.completed` session topics). The six `*.requested` topics are dispatch-only — the job orchestrator publishes them, but no service subscribes. Instead, the orchestrator calls the target service over HTTP through the gateway. Therefore:
- **job-orchestrator** gets ONE Service Bus KEDA scale rule (queue depth on its consumed subscription).
- **All other 10 services** get HTTP concurrency scale rules (HTTP-driven workload).

This matches the saga orchestration pattern documented in `Cortexa_Architecture.md` and avoids giving HTTP-only services a Service Bus scale rule pointing at a subscription they never consume.

### Verification (after CD apply)

Check the scale rules and probes are live:
```bash
# Verify job-orchestrator has the Service Bus scale rule:
az containerapp show \
  --name cortexa-dev-job-orchestrator \
  --resource-group cortexa-dev-rg \
  --query "properties.template.scale" -o json

# Verify another service (e.g. ingestion) has the HTTP scale rule:
az containerapp show \
  --name cortexa-dev-ingestion \
  --resource-group cortexa-dev-rg \
  --query "properties.template.scale" -o json

# Verify probes are defined (check any service):
az containerapp show \
  --name cortexa-dev-api-gateway \
  --resource-group cortexa-dev-rg \
  --query "properties.template.containers[0].probes" -o json
```

Expected outputs:
- `job-orchestrator`: `rules` contains one `custom` rule with `type: azure-servicebus` and `metadata.topicName: scoring.completed`.
- `ingestion` (and 9 others): `rules` contains one `http` rule with `metadata.concurrentRequests: "50"`.
- All services: `probes` array has three entries (`liveness`, `readiness`, `startup`).

---

## BUG047: Terraform Apply 403 Purge + Cosmos Throughput + SB Subscription Import (2026-06-29)

**Issue:** The dev CD `Terraform apply (gated)` job failed with four interdependent errors after BUG045 and BUG046 merged.

**Root causes:**

1. **Cognitive account purge 403.** BUG046 removed `module.ai_foundry` (cortexa-dev-aifoundry, eastus) and `module.ai_foundry_claude` (cortexa-dev-claude, eastus2). On destroy, azurerm's default `purge_soft_delete_on_destroy` runs a `DeletedAccountsPurge` post-step that the CI OIDC SP isn't authorized for. The account delete succeeded; only the purge post-step failed 403. Both accounts remain soft-deleted.
2. **Cosmos batches container throughput mode drift.** The live container uses manual throughput (400 RU) but the module declares `autoscale_settings`. Cosmos rejects in-place manual to autoscale conversion, returning 400.
3. **Service Bus orchestrator subscription missing from state.** The subscription exists live (namespace cortexa-dev-bus, topic batch-created, subscription orchestrator) but was missing from state. BUG045 imported only the topic, not the subscription. Apply fails with resource-already-exists collision.

**Resolution:**

- **Purge-on-destroy disabled.** `deploy/environments/{dev,prod}/versions.tf` provider blocks now include `features { cognitive_account { purge_soft_delete_on_destroy = false } }`. Apply no longer depends on purge succeeding.
- **Manual purge runbook (cortexa-dev-aifoundry / cortexa-dev-claude).** One-shot purge attempts run in the CD tf-plan job. If the CI SP gains purge rights later, the accounts will be purged automatically on the next pipeline run. If purge fails 403, the accounts remain soft-deleted indefinitely. This is safe — with purge-on-destroy disabled, they do not block future applies. To purge manually from a session with `Cognitive Services Contributor` + `Cognitive Services Usages Reader` at subscription scope, run:
  ```bash
  az cognitiveservices account purge --name cortexa-dev-aifoundry --resource-group cortexa-dev-rg --location eastus
  az cognitiveservices account purge --name cortexa-dev-claude --resource-group cortexa-dev-rg --location eastus2
  ```
- **Cosmos batches container migrated to autoscale.** A guarded migrate step in the CD tf-plan job checks `az cosmosdb sql container throughput show` for `resource.autoscaleSettings`. If null or None (manual), it runs `az cosmosdb sql container throughput migrate --throughput-type autoscale`. Idempotent — skips if already autoscale.
- **Service Bus orchestrator subscription imported.** A fourth idempotent `terraform import` block (matching the BUG045 style) checks for `module.service_bus.azurerm_servicebus_subscription.subscriptions["batch.created__orchestrator"]` in state and imports it if absent, resolving the Azure resource ID via `az servicebus topic subscription show`.

**Current state (2026-06-29):**

- purge-on-destroy is disabled in dev and prod.
- The two soft-deleted accounts (cortexa-dev-aifoundry, cortexa-dev-claude) are present but do not block apply.
- The batches container uses autoscale throughput (400 RU max).
- The orchestrator subscription is in state.

---

## BUG132: Honest Agreement-Level Labeling for Single-Mode Scoring (2026-07-07)

**Issue:** `scoring.dual_mode_enabled` defaults to `false` in dev by design, but `SingleModelScorer` hardcoded `agreement_level=fallback_single` on every run — a label that reads as a degrade even though single-mode is intentional. Separately, `ProviderRouter.RouteDualAsync` (model-router) hardcoded Anthropic as the dual-mode secondary; since every Anthropic catalog entry is `Enabled: false` in dev, any dual-mode attempt would 401 against a disabled secondary instead of routing to the enabled `DeepSeek-V4-Pro` Foundry secondary — the same class of bug as BUG129.

**Fix:**
- New `AgreementLevel.SingleConfigured = "single_configured"` for deliberate single-mode runs. `AgreementLevel.FallbackSingle` is preserved unchanged and now means only "dual mode was attempted and degraded to single" (secondary unavailable/timeout/parse failure).
- `DualScoringVerdict` / `BuildVerdictRequest` / `StoredVerdict` / the `scoring.completed` event payload all carry an optional `single_reason` (default `None`, so old Cosmos verdict documents without the field still deserialize). `SingleModelScorer` sets `single_reason="dual_mode_disabled"`; `DualModelScorer` sets `secondary_timeout` / `secondary_unavailable` / `secondary_parse_failed` depending on the failure.
- `ProviderRouter.RouteDualAsync` now resolves the enabled secondary from `IModelCatalog.ResolveEnabledSecondary()` (role=`secondary`, `Enabled: true`) instead of hardcoding Anthropic — in dev this picks `DeepSeek-V4-Pro` (Foundry). If no secondary is enabled at all, the router returns the primary result with a reasoned failed secondary instead of a blind Anthropic 401.

**Reading the signal going forward:** in dev, `agreement_level=single_configured` on every verdict is expected and healthy. `agreement_level=fallback_single` means dual mode was actually attempted and the secondary failed — that is the one worth investigating.

---

## BUG127: Foundry `api-version` Pinned to GA `v1` (2026-07-06)

**Issue:** model-router's Foundry calls hit `POST {endpoint}/openai/v1/chat/completions?api-version=preview`. A single upstream 500 from Foundry had no retry/backoff and no fallback to Anthropic, so it took down the calling pipeline stage (extraction, evidence, etc.). `Foundry.ApiVersion` defaulted to `"preview"`, which was a suspected contributor to the instability per this bug's investigation notes — the preview surface is less stable than the GA v1 surface.

**Verified fix:** `Foundry.ApiVersion` default changed from `"preview"` to `"v1"`. Confirmed via the published Azure AI Foundry v1 OpenAPI spec (`specification/ai/data-plane/OpenAI.v1/azure-v1-v1-generated.json` in `Azure/azure-rest-api-specs`) that the `api-version` query parameter on `/openai/v1/*` operations is typed `AzureAIFoundryModelsApiVersion`, an enum with exactly two values — `"v1"` (the default, GA) and `"preview"`. This matches the Microsoft Learn v1 API guidance ("api-version is no longer a required parameter with the v1 GA API"; GA endpoint URLs should not carry a dated `api-version` like `2025-04-01-preview`). The chat-completions fields this service sends (`max_completion_tokens`/`max_tokens`, `temperature`, `stream`, `reasoning_effort`, `response_format: json_object`) are all part of the GA v1 chat-completions surface — nothing in `FoundryProvider`/`FoundryWireTypes` depends on a preview-only feature, so there was no reason to keep defaulting to `"preview"`.

**Also added as part of this fix:**
- Retry-only resilience handler (`Microsoft.Extensions.Http.Resilience` `AddResilienceHandler`, exponential backoff + jitter) on the Foundry `HttpClient`, configurable via `Foundry.MaxRetries` (default 3) and `Foundry.RetryBaseDelayMs` (default 300ms). Retries only 5xx/408/`HttpRequestException` (the package default `ShouldHandle` predicate) — 4xx is never retried.
- `Router.EnableFallback` (default `true`): on a retryable Foundry failure (`ModelProviderException`, `FoundryTimeoutException`), `ProviderRouter` now calls the Anthropic provider directly as a fallback, bypassing the model catalog's `Enabled` gate. If both providers fail, the API returns `502` with the combined error detail instead of crashing the caller.

**Not changed:** the model catalog's `claude-*` entries remain `Enabled: false` — fallback routing calls the Anthropic provider implementation directly, not through the catalog.

---

## BUG046: AI Resource Consolidation (2026-06-29)

**Issue:** Terraform managed two separate AI Foundry resources (`cortexa-dev-aifoundry` kind=OpenAI, `cortexa-dev-claude` kind=AIServices), but the manually-created `cortexa-dev-ai-resource` consolidated account (kind=AIServices) was already deployed with GPT and could host Claude + embedding on a single resource.

**Resolution (see BUG047 above for purge follow-up):** New Terraform module `deploy/modules/ai-resource/` replaces both `ai-foundry` and `ai-foundry-claude`. The consolidated resource:

- **Name:** `cortexa-dev-ai-resource`
- **Location:** `eastus2` (differs from dev environment default `eastus`, acceptable — Cosmos already sits in `eastus2`)
- **Kind:** `AIServices` (multi-service Foundry account, not GPT-only OpenAI)
- **Live deployments (as of 2026-06-29):**
  - `gpt-5.5` (model gpt-5.5, version 2026-04-24, GlobalStandard)
  - `gpt-5.4` (model gpt-5.4, version 2026-03-05, GlobalStandard)
  - Claude deployments (Opus 4.8 / Sonnet 4.6) are **config-ready but disabled** (`enable_claude_deployment = false`) — quota request pending, Alpha deploys manually when granted.
  - Embedding: `text-embedding-3-large` deployment is **enabled** (`enable_embedding_deployment = true`). Gated behind toggle so first CD apply can flip to `false` if model availability or quota blocks deployment.

**Endpoints (NON-SENSITIVE, safe in outputs/config):**

- Azure OpenAI: `https://cortexa-dev-ai-resource.openai.azure.com`
- Project: `https://cortexa-dev-ai-resource.services.ai.azure.com/api/projects/cortexa-dev-ai`
- Cognitive Services: `https://cortexa-dev-ai-resource.cognitiveservices.azure.com/`
- Claude (Anthropic passthrough): `https://cortexa-dev-ai-resource.services.ai.azure.com/anthropic`

**Key Vault secret name convergence:** Both `model-router-foundry-api-key` and `model-router-anthropic-api-key` now carry the **same** API key value (the consolidated resource's key), sourced from `TF_VAR_ai_resource_api_key` GitHub secret (`AI_RESOURCE_API_KEY` on dev-plan / dev-deploy environments). Old secret names (`ai-foundry-api-key`, `ai-foundry-claude-api-key`) removed.

**CD import step (`.github/workflows/cd.yml`):** The "Import out-of-band resources" step now adopts three resources into state:

1. `module.ai_resource.azurerm_cognitive_account.this` ← `cortexa-dev-ai-resource`
2. `module.ai_resource.azurerm_cognitive_deployment.gpt["gpt-5.5"]` ← live `gpt-5.5` deployment
3. `module.ai_resource.azurerm_cognitive_deployment.gpt["gpt-5.4"]` ← live `gpt-5.4` deployment

Removing the two old modules from `dev/main.tf` WILL destroy `cortexa-dev-aifoundry` and `cortexa-dev-claude` on the next apply — this is intended.

**Key rotation (required post-merge):** After the first successful CD apply, the API key for `cortexa-dev-ai-resource` must be rotated (it was leaked in chat). Steps:

1. Regenerate key via Azure portal or `az cognitiveservices account keys regenerate --name cortexa-dev-ai-resource --resource-group cortexa-dev-rg --key-name key1`
2. Update `AI_RESOURCE_API_KEY` GitHub secret on both `dev-plan` and `dev-deploy` environments.
3. Verify services restart and fetch the new key from Key Vault (Container Apps picks up Key Vault secret changes on pod restart).

**Prod parity:** The same module swap applies to `deploy/environments/prod/`. Prod uses **managed identity only** (`local_authentication_enabled = false`), so no AI key secrets are written to Key Vault — services authenticate via Container Apps managed identity with Cognitive Services OpenAI User RBAC. Prod resource does not exist yet; Terraform will create it (no import needed).

---

## Patent URL Canonicalization (BUG182)

**As of 2026-07-12:** Evidence service now canonicalizes all Google Patents URLs at runtime via `canonical_google_patents_url`. Corpus evidence URLs derive from the stored record's publication number (id field) at read time, so already-loaded corpus records automatically render correct links without re-loading the corpus. BigQuery row mapping, USPTO adapter hit mapping, and corpus vector-search hit mapping all route through the shared canonicalizer.
