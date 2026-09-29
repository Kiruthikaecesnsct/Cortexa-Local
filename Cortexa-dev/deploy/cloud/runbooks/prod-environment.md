# Cortexa — Prod Environment Runbook (US058)

Operational runbook for `deploy/environments/prod/`. Covers access, configuration,
cost estimate, and the verification steps that are still outstanding because no
Terraform installation exists on this dev machine.

---

## 1. What This Environment Is

`deploy/environments/prod/` provisions a second, fully isolated Azure resource
group (`cortexa-prod-rg`) so the demo and batch-processing workloads run on
always-on, higher-tier infrastructure, separate from `cortexa-dev-rg`. Prod and
dev share the same Terraform module catalog (`deploy/modules/`); only the
environment-level `variables.tf` defaults and `terraform.tfvars` values differ.

### Modules provisioned (12 in prod, one more than dev — `budget-alert`)

| Module | Prod-specific configuration |
|--------|------------------------------|
| `key-vault` | RBAC-based; `deployer_object_id` granted Key Vault Secrets Officer via `secrets.tf` |
| `container-registry` | `sku = "Premium"` (geo-replication, VNet support) |
| `monitoring` | Log Analytics + workspace-based Application Insights |
| `blob-storage` | Containers: `raw-files`, `corpus` |
| `cosmos-db` | `throughput = 1000` (autoscale max RU/s, reduced from 4000 to fit budget) per container, 8 containers, public network access disabled |
| `postgresql` | `sku_name = "GP_Standard_D2s_v3"`, `public_network_access_enabled = false` (prod keeps this secure; dev overrides to `true` as a stopgap) |
| `service-bus` | `sku = "Standard"` (dropped from Premium to fit budget — VNet isolation not exercised by this workload) |
| `container-apps` | `min_replicas = 1` for all 11 services — **always-on, no scale-to-zero** |
| `static-web-app` | `sku_tier/sku_size = "Standard"`, region `eastus2` (SWA has limited region support) |
| `ai-search` | `sku = "standard"`, `replica_count = 2`, `partition_count = 1` |
| `ai-foundry` | GPT-4o, `deployment_sku_name = "GlobalStandard"`, `deployment_capacity = 50` (50K TPM) |
| `budget-alert` | Resource-group budget at `var.budget_monthly_amount`, alerts at 80% and 100% |

---

## 2. Access

| Resource | How to access |
|----------|----------------|
| Resource group | Azure Portal → `cortexa-prod-rg`, or `az group show -n cortexa-prod-rg` |
| Key Vault secrets | `az keyvault secret show --vault-name <vault_uri host> --name <secret-name>` — secret names are listed in the `key_vault_secret_names` output |
| Container Apps logs | Azure Portal → each `cortexa-prod-{service}` Container App → Log stream, or query Log Analytics workspace (`law_id` output) |
| ACR | `az acr login --name cortexaprodacr` (login server from `login_server` output) |
| Cosmos DB | Connect via managed identity from each service; `cosmos_endpoint` output gives the URI for diagnostics |
| PostgreSQL | `postgresql_fqdn` output; connect with `psql` using the admin credentials supplied via `TF_VAR_postgresql_admin_password` (never stored in tfvars) |
| Static Web App | `static_web_app_default_host_name` output |
| Budget alert | Azure Portal → Cost Management + Billing → Budgets → `cortexa-prod-budget` |

Sensitive Terraform outputs (`*_connection_string`, `*_primary_key`, `postgresql_connection_string`, `static_web_app_api_key`) are also written into Key Vault by `secrets.tf` — that is the runtime source of truth for services. Do not pull these into CI logs or shell history.

---

## 3. Prerequisites Before Any Real Apply

1. **Terraform CLI** (see Section 5 — currently not installed on this machine).
2. `az login` with a principal that has Contributor (or Owner, for the initial role assignment bootstrap) on the target subscription.
3. `terraform.tfvars` created from `terraform.tfvars.example`, with:
   - real `tenant_id`
   - `key_vault_allowed_ip_rules` set to the actual CI runner / operator IP(s)
   - `postgresql_allowed_ip_ranges` set to real ranges
   - `deployer_object_id` from `az ad signed-in-user show --query id -o tsv`
   - `budget_contact_emails` set to a real distribution list
4. Secrets supplied via environment variables only, never committed:
   ```bash
   export TF_VAR_postgresql_admin_password="..."
   export TF_VAR_patent_uspto_api_key="..."
   export TF_VAR_patent_epo_consumer_key="..."
   export TF_VAR_patent_epo_oauth_secret="..."
   ```
5. `production` GitHub Environment configured with required reviewers (per `CI_SETUP.md` §4) if applying through `release.yml` rather than locally.

---

## 4. Apply Procedure (once Terraform is installed — see Section 5)

```bash
cd deploy/environments/prod
terraform init
terraform plan -out=tfplan
terraform apply tfplan
```

After apply succeeds, confirm health:

- Hit each Container App's health endpoint through `cortexa-prod-api-gateway` (the only externally-exposed app).
- Confirm Cosmos DB, PostgreSQL, Service Bus, AI Search, and AI Foundry all show "Succeeded" provisioning state in the portal or via `az resource list -g cortexa-prod-rg -o table`.
- Confirm the 10 Key Vault secret names exist (`key_vault_secret_names` output).
- Confirm the budget alert resource exists and `time_period_start` matches the intended month.

---

## 5. Verification Status — OUTSTANDING / UNVERIFIED

**Terraform is not installed on this development machine.** This was confirmed
directly (not a permissions or PATH issue) — there is no `terraform` binary
available to execute. As a result, the following checks from the standard
IaC workflow could **not** be run and their output is **not** included in
this runbook. No `fmt`/`validate`/`plan` output below has been fabricated.

| Check | Status | Command Alpha must run manually (with Terraform installed) |
|-------|--------|--------------------------------------------------------------|
| Format check | **OUTSTANDING** | `terraform fmt -check -recursive deploy` (run from repo root) |
| Init | **OUTSTANDING** | `terraform -chdir=deploy/environments/prod init` |
| Validate | **OUTSTANDING** | `terraform -chdir=deploy/environments/prod validate` |
| Plan | **OUTSTANDING** | `terraform -chdir=deploy/environments/prod plan -out=tfplan` |
| Lint | **OUTSTANDING** | `tflint --chdir=deploy` |
| Security scan | **OUTSTANDING** | `tfsec deploy` |
| Apply | **OUTSTANDING — requires explicit approval** | `terraform -chdir=deploy/environments/prod apply tfplan` |
| Post-apply health checks | **OUTSTANDING** | See Section 4 checklist, run after a real apply |

Until these commands have been run by Alpha (or by a CI runner with Terraform
available) and have passed, treat `environments/prod/` as **reviewed but
unverified**. The acceptance criteria "applies successfully" and "all
resources pass health checks" from US058 remain open until Section 4's apply
procedure has actually been executed against Azure.

**Action required from Alpha:** install Terraform (matching the `~> 3.0`
azurerm provider pin in `deploy/versions.tf`), then run the six commands above
in order, in a shell authenticated against the target Azure subscription,
before any prod apply is considered safe.

---

## 6. Cost Estimate vs. $2,000/month Budget Cap

Estimate basis: Azure East US public list pricing, pay-as-you-go, no
reservations/savings plans applied. These are **estimates for planning
purposes**, not a quote — actual billing depends on real usage (RU
consumption, AI Foundry token volume, egress, etc.). No Azure Pricing
Calculator run or `az` cost query was performed (no live Azure connection
per environment rules); figures are derived from published list prices for
the exact SKUs configured in `terraform.tfvars.example`.

| Resource | Configuration | Estimated $/month |
|----------|----------------|--------------------|
| Container Apps (11 services) | Consumption plan, 0.25 vCPU / 0.5Gi each, `min_replicas=1` (always-on, no scale-to-zero) | ~$310 |
| Azure AI Search | Standard (S1), 2 replicas × 1 partition | ~$500 |
| Cosmos DB | 8 containers × 1000 RU/s autoscale max (assume ~50% average utilization) | ~$350 |
| PostgreSQL Flexible Server | GP_Standard_D2s_v3 (2 vCPU/8GiB) + storage | ~$140 |
| Service Bus | Standard tier | ~$10 |
| Container Registry | Premium | ~$50 |
| Static Web App | Standard | ~$9 |
| Azure AI Foundry (GPT-4o) | GlobalStandard, 50K TPM provisioned capacity | ~$200–400 (varies with actual token usage; this line is usage-sensitive, not flat) |
| Key Vault + Blob Storage + Monitoring | Low-volume secrets/logs/storage | ~$30 |
| Budget Alert | No cost (Cost Management feature) | $0 |
| **Total (estimated)** | | **~$1,600–1,800/month** |

### Resolved: this fits under the $2,000/month budget cap with $200–400 of headroom.

The original estimate (with Cosmos DB at 4000 RU/s autoscale max and Service
Bus Premium) came in around $3,300–3,500/month, roughly $1,300–1,500 over
the cap. Two mitigations were applied to `terraform.tfvars.example` (and the
matching `variables.tf` defaults) to close the gap, in order of impact:

1. **Cosmos DB**: `cosmos_throughput` dropped from 4000 to 1000 RU/s
   autoscale max per container — saves ~$1,050/mo. Autoscale only bills for
   RU/s actually consumed up to the max, so this is a ceiling reduction, not
   a guaranteed throughput cut; it assumes the workload's real RU
   consumption stays below the new 1000 RU/s ceiling. Monitor actual RU
   consumption after the first real apply and raise the ceiling if
   throttling (429s) appears under production load.
2. **Service Bus**: `service_bus_sku` dropped from Premium to Standard —
   saves ~$660/mo. The `service-bus` module (`deploy/modules/service-bus/`)
   provisions only the namespace, 11 topics, subscriptions (including the 5
   session-aware ones), and a scoped Send+Listen auth rule — it does not
   provision any VNet/private-endpoint integration that would require
   Premium. Standard fully supports all 11 topics, sessions, and the
   saga/event flow in `Cortexa_Architecture.md`; Premium's VNet isolation
   and dedicated-capacity guarantees were not being exercised by this
   workload, so dropping to Standard does not reduce functionality.

`ai_search_replica_count = 2` (HA) was left unchanged per the US058
acceptance criteria, so the AI Search line stays at ~$500/mo rather than the
~$250/mo single-replica alternative previously listed as a fallback option.

Applying both mitigations together brings the estimate to roughly
**$1,600–1,800/month**, under the $2,000 cap with headroom for AI Foundry
usage variability. The `budget-alert` module still fires its 80%/100%
notifications regardless of this estimate — it does not block spend, it only
notifies after the fact — so it remains the safety net if actual usage
(RU consumption, AI Foundry tokens) runs higher than estimated.

---

## 7. Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `terraform apply` fails writing Key Vault secrets with 403 | RBAC propagation race | `secrets.tf` already inserts a 120s `time_sleep` after the role assignment; if it still 403s, re-run `terraform apply` — the role assignment itself is idempotent |
| Cosmos/PostgreSQL unreachable from a service | Public network access is disabled by design in prod | Services must reach these via managed identity / private networking, not public endpoints; confirm the Container App's user-assigned identity has the right data-plane role |
| Container App image not found on first apply | `image_tag` defaults to `latest`, which may not exist in a fresh ACR | CD/release pipeline pushes real images after the Terraform apply; the first apply intentionally references a placeholder tag |
| Budget alert never fires | `time_period_start` not on the first of a month, or `budget_contact_emails` empty | Both are required fields in `terraform.tfvars` — there is no default |
