# Cortexa — CI/CD Setup

Cortexa runs its CI/CD on GitHub Actions only. All pipeline logic lives in `ci.yml`, `cd.yml`, and `release.yml` under `.github/workflows/`.

---

## 1. Platforms and Files

| Platform        | Location                  | Files                          |
| --------------- | ------------------------- | ------------------------------ |
| GitHub Actions  | `.github/workflows/`      | `ci.yml`, `cd.yml`, `release.yml` |

Each service is built and deployed by its own job. Pipelines do not cross service boundaries. A change to one service deploys only that service.

---

## 2. CI Pipeline (`ci.yml`)

Triggers on a PR from `dev` to `qa`. No other branch. No cloud access, no credentials. Stays green even before Azure dev resources exist.

Stages per service:

| Stage  | Python                          | .NET                              | Frontend          |
| ------ | ------------------------------- | --------------------------------- | ----------------- |
| format | `ruff format --check`           | `dotnet format --verify-no-changes` | `eslint`        |
| lint   | `ruff check`                    | analyzers (`-warnaserror`)        | `eslint`          |
| test   | `pytest`                        | `dotnet test`                     | `vitest`          |
| audit  | `pip-audit`                     | `dotnet list package --vulnerable`| `npm audit`       |
| build  | `docker build` (push: false)    | `docker build` (push: false)      | `pnpm build`      |

IaC gets its own CI stage: `terraform fmt -check`, `terraform validate`, `tflint`, `tfsec` over `deploy/`.

Branch protection: CI must be green before a PR merges.

---

## 3. CD Pipeline (`cd.yml`)

Triggers on a PR to `qa`. Deploys to the dev Azure environment. Gated on approval before the deploy stage.

| Stage      | Action                                                       |
| ---------- | ------------------------------------------------------------ |
| tf-plan    | `terraform plan` for dev                                     |
| approval   | Manual approval gate                                         |
| tf-apply   | `terraform apply` for dev                                    |
| build-push | Build and push changed service images to ACR                 |
| deploy     | Update each changed Container App to the new image           |
| smoke      | Hit health endpoints; fail the run if any is unhealthy       |

As of 2026-06-25 the dev environment is fully provisioned and CD is green end-to-end (run 28154567242: plan + gated apply both succeeded). The `Key Vault Secrets Officer` data-plane role the apply depends on is owned by Terraform — see §5.

Auth: OIDC workload identity federation. No static keys in the pipeline. The CI identity is a service principal, so Terraform cannot be run locally (the azurerm provider rejects CLI-as-SP auth) — plan/apply/import only run inside the pipeline.

CI and CD may be combined into one workflow if the team prefers, keeping the gate before deploy.

---

## 3.1 Frontend Deploy job (`frontend-deploy` in `cd.yml`)

Runs **in parallel with `build-push`**, after `tf-apply`. Triggers only when `frontend/**` files changed in the PR and the PR is from the same repo.

| Step | Action |
| ---- | ------ |
| Checkout | `actions/checkout` (pinned SHA) |
| Setup pnpm + Node | Same versions as `_frontend.yml` (pnpm 9.15.9, Node 24) |
| Install deps | `pnpm install --frozen-lockfile` |
| Build | `pnpm build` with `VITE_API_BASE_URL=/api` and `VITE_APP_VERSION=<git-sha>` injected |
| Deploy | `Azure/static-web-apps-deploy@v1` — `skip_app_build: true`, deploys `frontend/dist/` to `cortexa-dev-swa` |

**Token:** `SWA_DEV_DEPLOYMENT_TOKEN` repo-level GitHub secret (the SWA deployment token). Sourced securely — never hardcoded. See §5 for the full secrets inventory.

**No OIDC:** this job uses only `permissions: contents: read`. The SWA deployment token is a direct upload credential; OIDC is not needed.

**Production slot:** Because the SWA was provisioned via Terraform (not the Azure SWA GitHub App integration), the deployment token targets the production environment slot directly. Visiting `nice-beach-0701ce10f.7.azurestaticapps.net` after the job completes serves the Cortexa SPA.

**`staticwebapp.config.json`** lives in `frontend/public/`; Vite copies it into `dist/` during build — it reaches the SWA automatically with no extra step.

**`deployment_environment: ""`** is set explicitly on the deploy step. Without it the action auto-detects the PR context and creates a PR-numbered preview slot (e.g. `nice-beach-0701ce10f-141.eastus2.azurestaticapps.net`) instead of the production slot. The custom domain is bound to the production slot, so omitting this causes the placeholder "Congratulations" page to appear at the custom domain while the real content lands in an unreachable preview slot.

---

## 4. Release Pipeline (`release.yml`)

Triggers only on a version tag matching `v*.*.*` (e.g. `v0.1.0`). No PR trigger, no branch trigger. Deploys the full service set to the prod environment. Gated on approval before deploy.

`concurrency` is keyed on the tag ref (`release-${{ github.ref_name }}`) with `cancel-in-progress: false` — a prod deploy is never cancelled mid-flight by a second tag push.

| Stage      | Action                                                                  |
| ---------- | ------------------------------------------------------------------------ |
| tf-plan    | `terraform plan` for `deploy/environments/prod`                          |
| approval   | Manual approval gate (`production` GitHub Environment)                   |
| tf-apply   | `terraform apply` for prod                                               |
| build-push | Build and push **all 11** service images to `cortexaprodacr.azurecr.io`, tagged with both `github.sha` and the version tag (`github.ref_name`) |
| deploy     | Update each of the 11 Container Apps (`cortexa-prod-*`) to the version-tagged image |
| smoke      | Hit health endpoints through `cortexa-prod-api-gateway`; fail the run if any is unhealthy |

Unlike `cd.yml`, `release.yml` has no `_changes.yml` change-detection gate — a tag push has no meaningful diff base, so every release deploys the full set of 11 services to keep prod images and prod infra consistent with the tagged commit.

### Prerequisites (status: in place, prod values pending)

- **`production` GitHub Environment**: ✅ exists with 1 required reviewer (gates `tf-apply`). Distinct from `dev-deploy`.
- **`production-plan` GitHub Environment**: ✅ exists, no reviewers (ungated plan/build/deploy/smoke).
- **OIDC federated credentials** `github-actions-production-plan` and `github-actions-production`: ✅ exist on the app registration. `release.yml` uses environment-scoped subjects (Azure AD has no glob support, so tag subjects like `ref:refs/tags/v*.*.*` cannot work). See `AZURE_STATUS.md`.
- **Prod secrets/variables**: ✅ registered on both prod environments, but the **secrets are `REPLACE_ME_*` placeholders** and the **variables mirror dev** (open IP rules). Replace the secrets and lock the IP variables before the first real release — see `AZURE_STATUS.md` → Prod Environment. `AZURE_CLIENT_ID`/`TENANT_ID`/`SUBSCRIPTION_ID` resolve from repo level. If a separate prod app registration is ever used, add its credentials under the `production` Environment rather than repo-level to keep dev and prod isolated.
- **Has never run**: prod infra is not yet provisioned; the first `v*.*.*` tag will create it.

### Rollback

To roll back a bad release, either re-push a tag pointing at a known-good SHA (delete the bad tag, retag the good commit, push), or re-run a prior successful `release.yml` workflow run from the Actions UI — both redeploy the last-known-good image set without a new commit.

---

## 5. Secrets in CI

- No static cloud keys. CI authenticates to Azure with OIDC workload identity.
- Application secrets (LLM keys, patent keys, PATs, JWT key) live in Key Vault, read at runtime by the services, not injected by CI.
- The only CI-held config is the OIDC federation identity, the SWA deployment token, and non-secret resource names.
- `SWA_DEV_DEPLOYMENT_TOKEN` — repo-level GitHub secret. The Static Web App deployment token for `cortexa-dev-swa`. Used by the `frontend-deploy` job in `cd.yml` to upload the built SPA to the production slot. Retrieved once via `az staticwebapp secrets list --name cortexa-dev-swa --resource-group cortexa-dev-rg --query "properties.apiKey" -o tsv` and stored with `gh secret set SWA_DEV_DEPLOYMENT_TOKEN`.

### OIDC Subject Patterns

GitHub OIDC issues different subject claims depending on how a job is configured. The `azure/login` step only succeeds if Azure AD has a federated credential whose subject matches.

| Job (workflow) | `environment:` set | Subject emitted | Federated credential |
|---|---|---|---|
| `tf-plan` (cd.yml) | `dev-plan` | `repo:AlphaGhostUSMC/Cortexa:environment:dev-plan` | `github-actions-dev-plan` |
| `tf-apply` (cd.yml) | `dev-deploy` | `repo:AlphaGhostUSMC/Cortexa:environment:dev-deploy` | `github-actions-dev-deploy` |
| `build-push` matrix (cd.yml) | none | `repo:AlphaGhostUSMC/Cortexa:pull_request` | `github-actions-pr` |
| `deploy` matrix (cd.yml) | none | `repo:AlphaGhostUSMC/Cortexa:pull_request` | `github-actions-pr` |
| `smoke` (cd.yml) | `dev-plan` | `repo:AlphaGhostUSMC/Cortexa:environment:dev-plan` | `github-actions-dev-plan` |
| `tf-plan` (release.yml) | `production-plan` | `repo:AlphaGhostUSMC/Cortexa:environment:production-plan` | `github-actions-production-plan` |
| `tf-apply` (release.yml) | `production` | `repo:AlphaGhostUSMC/Cortexa:environment:production` | `github-actions-production` |
| `build-push` matrix (release.yml) | `production-plan` | `repo:AlphaGhostUSMC/Cortexa:environment:production-plan` | `github-actions-production-plan` |
| `deploy` matrix (release.yml) | `production` | `repo:AlphaGhostUSMC/Cortexa:environment:production` | `github-actions-production` |
| `smoke` (release.yml) | `production-plan` | `repo:AlphaGhostUSMC/Cortexa:environment:production-plan` | `github-actions-production-plan` |

> Azure AD subjects are exact-match strings. Glob patterns like `ref:refs/tags/v*.*.*` are not supported and will never authenticate. Use `environment:X` subjects for all OIDC-authenticated jobs.

cd.yml matrix jobs (`build-push`, `deploy`) intentionally omit `environment:` — adding it would create one GitHub deployment record per service per run (11 × 2 = 22 records). They use the `pull_request` subject and are guarded by `github.event.pull_request.head.repo.full_name == github.repository` to block fork PRs.

### Key Vault Secrets Officer Role — Terraform is the Sole Owner

The deploying service principal (`cortexa-terraform`, object id `da3f8e92-d9ad-44c7-924e-ffe616e8eaaf`) needs the **Key Vault Secrets Officer** data-plane role on each environment's Key Vault. Without it, `terraform plan` cannot refresh the existing `azurerm_key_vault_secret.platform` resources and fails with `403 ForbiddenByRbac` (the vaults run in RBAC authorization mode).

**Ownership model: Terraform owns this role assignment, and nothing else creates it.** The `azurerm_role_assignment.deployer_secrets` resource in `deploy/environments/{dev,prod}/secrets.tf` is the single creator. It anchors `time_sleep.rbac_propagation` (120s), which gates all 12 secret writes — so on a fresh environment Terraform creates the vault → grants the role → waits for propagation → writes secrets, in that order, with no 403.

Do **not** re-introduce an `az role assignment create` step into `cd.yml` or `release.yml`. An earlier iteration did (as an idempotent self-healing step); it created the same `(principal, role, scope)` tuple out-of-band, which then collided with the Terraform-managed resource and made `terraform apply` fail with `409 RoleAssignmentExists`. Azure rejects duplicate tuples regardless of assignment name, so two creators always conflict.

#### Recovering from a 409 (assignment exists in Azure but not in Terraform state)

This happens if the role was ever created outside Terraform, or if Terraform state is lost while the vault still exists. The fix is to **import** the existing assignment into state (never delete it — deleting re-triggers the 403 on the next plan):

1. Find the assignment resource id:
   ```bash
   az role assignment list \
     --scope "$(az keyvault show --name cortexa-dev-kv --query id -o tsv)" \
     --query "[].id" -o tsv
   ```
2. Dispatch the one-shot **`import-kv-role.yml`** workflow (it runs `terraform import azurerm_role_assignment.deployer_secrets <id>` under CI OIDC — local Terraform cannot authenticate, because the azurerm provider rejects Azure-CLI-as-service-principal auth):
   ```bash
   gh workflow run import-kv-role.yml \
     -f environment=dev \
     -f assignment_id="<resource id from step 1>"
   ```
3. Re-run CD. `terraform apply` now sees the role in state and no-ops on it instead of 409.

`import-kv-role.yml` is `workflow_dispatch`-only and supports both `dev` and `prod` (it selects the matching GitHub Environment and `deploy/environments/{env}`). The same recovery runbook is noted in both `secrets.tf` files.

**Status (2026-06-25): all of the below are done and verified.** `cd.yml` runs green end-to-end against dev (run 28154567242). The list is kept as the reproduction recipe for a fresh org/repo or a prod cutover. See `AZURE_STATUS.md` for the live inventory of environments, secrets, variables, and federated credentials.

1. ✅ `dev-plan` GitHub Environment (no required reviewers).
2. ✅ `production-plan` GitHub Environment (no required reviewers).
3. ✅ `dev-deploy` GitHub Environment (no reviewers).
4. ✅ `production` GitHub Environment (1 required reviewer — gates prod apply).
5. ✅ All five `az ad app federated-credential create` commands in `AZURE_STATUS.md`.
6. ✅ Per-environment secrets + variables registered on all four environments (prod secrets are placeholders pending real values — see `AZURE_STATUS.md`).

---

## 6. Branch Protection and Status Checks

| Branch | Protection                                          |
| ------ | --------------------------------------------------- |
| `dev`  | PRs only; CI green required                          |
| `qa`   | PRs only; CI green required; CD approval gate         |
| `main` | PRs only; release via tag; release approval gate      |

Required status checks: format, lint, test, audit, build for the changed service, plus the IaC checks when `deploy/` changes.

---

## 7. Release / Tag Workflow

1. Cut a `release/v{X.Y.Z}` branch from `qa`.
2. Final checks pass.
3. Merge to `main` and tag `v{X.Y.Z}`.
4. The tag triggers `release.yml`.
5. Approve the prod deploy gate.
6. Smoke checks confirm the demo environment is healthy.

