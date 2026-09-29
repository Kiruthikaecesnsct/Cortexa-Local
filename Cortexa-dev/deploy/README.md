# Cortexa — Terraform Infrastructure

## Module Catalog

| Module | Purpose | Key Inputs | Key Outputs |
|--------|---------|------------|-------------|
| `modules/key-vault` | Azure Key Vault with RBAC, purge protection, network ACLs | `project`, `environment`, `tenant_id`, `allowed_subnet_ids`, `allowed_ip_rules` | `vault_id`, `vault_uri`, `vault_name` |
| `modules/container-registry` | Azure Container Registry for service images | `project`, `environment`, `sku`, `admin_enabled` | `login_server`, `registry_id`, `admin_username`*, `admin_password`* |
| `modules/monitoring` | Log Analytics workspace + workspace-based Application Insights | `project`, `environment`, `retention_in_days` | `law_id`, `law_workspace_id`, `app_insights_id`, `connection_string`*, `instrumentation_key`* |
| `modules/blob-storage` | Storage account + private blob containers | `project`, `environment`, `name_suffix`, `containers` | `account_name`, `account_id`, `primary_blob_endpoint`, `container_names`, `primary_connection_string`* |

`*` = sensitive output, never printed in plain-text logs.

## Naming Convention

All resources follow `{project}-{environment}-{resource-type-suffix}`:

- Key Vault: `cortexa-dev-kv`
- Log Analytics: `cortexa-dev-law`
- Application Insights: `cortexa-dev-ai`
- Resource Group: `cortexa-dev-rg`

Storage accounts and ACR names strip hyphens and are lowercased to meet Azure naming rules
(alphanumeric only, 3–24 chars for storage, 5–50 for ACR).

## How to Add an Environment

1. Copy `environments/dev/` to `environments/{env}/` (e.g. `environments/prod/`).
2. Update `variables.tf` defaults (`environment = "prod"`, `resource_group_name = "cortexa-prod-rg"`).
3. Copy `terraform.tfvars.example` to `terraform.tfvars` and fill in the real `tenant_id`.
4. Adjust module arguments for prod SKUs (e.g. `sku = "Premium"` for ACR, higher `retention_in_days`).
5. Run from inside the new environment directory:
   ```bash
   terraform init
   terraform plan -out=tfplan
   terraform apply tfplan
   ```

## Quick Start (dev)

```bash
cd deploy/environments/dev
cp terraform.tfvars.example terraform.tfvars
# edit terraform.tfvars — set tenant_id
terraform init
terraform plan -out=tfplan
terraform apply tfplan
```
