variable "project" {
  type        = string
  default     = "cortexa"
  description = "Project short name"
}

variable "environment" {
  type        = string
  default     = "dev"
  description = "Environment name"
}

variable "location" {
  type        = string
  default     = "eastus"
  description = "Azure region for all resources"
}

variable "tenant_id" {
  type        = string
  description = "Azure AD tenant ID — must be provided via tfvars or environment variable"
}

variable "resource_group_name" {
  type        = string
  default     = "cortexa-dev-rg"
  description = "Name of the Azure resource group"
}

variable "key_vault_allowed_ip_rules" {
  type        = list(string)
  description = "IP ranges permitted to access Key Vault (at least one required)"
}

variable "cosmos_throughput" {
  type        = number
  default     = 1000
  description = "Autoscale max throughput RU/s for each Cosmos container (dev: 1000 — Azure minimum for autoscale)"
}

variable "cosmos_allowed_ip_ranges" {
  type        = set(string)
  default     = ["0.0.0.0"]
  description = "Cosmos IP firewall entries for dev. Default \"0.0.0.0\" accepts connections from within public Azure datacenters, letting the Container Apps env (dynamic egress IP, no VNet) reach Cosmos."
}

variable "postgresql_admin_password" {
  type        = string
  sensitive   = true
  description = "Admin password for PostgreSQL flexible server — never hardcode; use TF_VAR_postgresql_admin_password"
}

variable "postgresql_sku" {
  type        = string
  default     = "B_Standard_B1ms"
  description = "SKU for the PostgreSQL flexible server"
}

variable "postgresql_allowed_ip_ranges" {
  type = list(object({
    name     = string
    start_ip = string
    end_ip   = string
  }))
  description = "Firewall rules granting access to the PostgreSQL server (at least one required)"
}

variable "service_bus_sku" {
  type        = string
  default     = "Standard"
  description = "Service Bus namespace SKU (dev: Standard, prod: Premium)"
}

variable "service_bus_dlq_alert_threshold" {
  type        = number
  default     = 10
  description = "Dead-letter message count that triggers the DLQ alert on each dispatch topic"
}

variable "service_bus_dlq_alert_email_receivers" {
  type        = list(string)
  default     = []
  description = "Email addresses that receive DLQ alert notifications; action group is omitted when empty"
}

variable "static_web_app_location" {
  type        = string
  description = "Azure region for Static Web App (limited region support)"
  default     = "eastus2"
}

variable "static_web_app_sku_tier" {
  type        = string
  description = "SKU tier for Static Web App (Free or Standard)"
  default     = "Standard"
}

variable "static_web_app_sku_size" {
  type        = string
  description = "SKU size for Static Web App (must match sku_tier)"
  default     = "Standard"
}

variable "ai_search_sku" {
  type        = string
  description = "SKU for Azure AI Search (basic, standard, etc.)"
  default     = "basic"
}

variable "ai_search_replica_count" {
  type        = number
  description = "Number of replicas for Azure AI Search"
  default     = 1
}

variable "ai_search_partition_count" {
  type        = number
  description = "Number of partitions for Azure AI Search"
  default     = 1
}

variable "embedding_dimensions" {
  type        = number
  default     = 1536
  description = "Embedding vector dimensions (1536 for text-embedding-3-large) — used by both the AI Search index schema and the EMBEDDING_DIMENSIONS environment variable passed to vector-router"
}

variable "ai_resource_api_key" {
  type        = string
  sensitive   = true
  description = "API key for the consolidated AI resource (cortexa-dev-ai-resource). Supplied via TF_VAR_ai_resource_api_key environment variable from GitHub secret AI_RESOURCE_API_KEY. NEVER hardcode. Both model-router-foundry-api-key and model-router-anthropic-api-key Key Vault secrets receive this value."
}

variable "deployer_object_id" {
  type        = string
  description = "Service principal object ID of the principal running terraform apply. Gets Key Vault Secrets Officer so it can write connection strings. Get with: az ad sp show --id <AZURE_CLIENT_ID> --query id -o tsv"
}

variable "budget_monthly_amount" {
  type        = number
  description = "Monthly budget cap in USD — alert fires at 80 % and 100 % of this value"
}

variable "budget_time_period_start" {
  type        = string
  default     = "2026-06-01T00:00:00Z"
  description = "RFC3339 first-of-month date when the budget period begins (must be first day of month)"
}

variable "budget_contact_emails" {
  type        = list(string)
  description = "Email addresses that receive budget alert notifications"
}

# Patent API credentials — never hardcode; supply via TF_VAR_ environment variables
variable "patent_uspto_api_key" {
  type        = string
  sensitive   = true
  description = "USPTO OpenData API key — use TF_VAR_patent_uspto_api_key"
}

variable "patent_epo_consumer_key" {
  type        = string
  sensitive   = true
  description = "EPO OPS OAuth client_id — use TF_VAR_patent_epo_consumer_key"
}

variable "patent_epo_oauth_secret" {
  type        = string
  sensitive   = true
  description = "EPO OPS OAuth client_secret — use TF_VAR_patent_epo_oauth_secret"
}

variable "patent_lens_api_key" {
  type        = string
  sensitive   = true
  description = "Lens.org patent API Bearer token — use TF_VAR_patent_lens_api_key"
}

variable "postgresql_location" {
  type        = string
  default     = "centralus"
  description = "Azure region for PostgreSQL Flexible Server (eastus and eastus2 blocked for this subscription)"
}

variable "postgresql_name_suffix" {
  type        = string
  default     = "-3"
  description = "Suffix for PostgreSQL server name; avoids Azure name reservation conflict after region change"
}

variable "cosmos_location" {
  type        = string
  default     = "eastus2"
  description = "Azure region for Cosmos DB (eastus has capacity constraints for this subscription)"
}

variable "enable_budget" {
  type        = bool
  default     = false
  description = "Create Cost Management budget alert. Requires EA/Web Direct/MCA subscription type."
}

variable "gateway_cors_allowed_origins" {
  type        = list(string)
  default     = ["*"]
  description = "Origins permitted by the api-gateway CORS policy. Supply as a JSON string array via TF_VAR_gateway_cors_allowed_origins or GATEWAY_CORS_ALLOWED_ORIGINS GitHub Actions variable. Dev defaults to [\"*\"] (allow any origin) so a local frontend can hit the deployed backend without a CD round-trip; lock this down to [\"https://<swa-default-host>\"] in prod. See AZURE_STATUS.md — CORS wiring."
}

# Sentry error tracking (US106, Phase 1 — dev only). Backend service DSNs, keyed by
# service name. Never hardcode; supply via TF_VAR_sentry_dsns, sourced in CI from the
# SENTRY_DSNS GitHub secret (JSON map, env-scoped on dev-plan / dev-deploy).
variable "sentry_dsns" {
  type        = map(string)
  sensitive   = true
  description = "Sentry DSN per backend service (ingestion, extraction, evidence, vector-router, scoring, seeding, harvesting, api-gateway, identity, model-router, job-orchestrator) — use TF_VAR_sentry_dsns"
}
