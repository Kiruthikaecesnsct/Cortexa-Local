locals {
  common_tags = {
    project     = var.project
    environment = var.environment
    managed_by  = "terraform"
  }
}

resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.location
  tags     = local.common_tags
}

module "key_vault" {
  source              = "../../modules/key-vault"
  project             = var.project
  environment         = var.environment
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  tenant_id           = var.tenant_id
  allowed_ip_rules    = var.key_vault_allowed_ip_rules
  tags                = local.common_tags
}

module "container_registry" {
  source              = "../../modules/container-registry"
  project             = var.project
  environment         = var.environment
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.common_tags
}

module "monitoring" {
  source              = "../../modules/monitoring"
  project             = var.project
  environment         = var.environment
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.common_tags
}

module "blob_storage" {
  source              = "../../modules/blob-storage"
  project             = var.project
  environment         = var.environment
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  containers          = ["raw-files", "corpus", "viewable-docs"]
  tags                = local.common_tags
}

module "cosmos_db" {
  source              = "../../modules/cosmos-db"
  project             = var.project
  environment         = var.environment
  location            = var.cosmos_location
  resource_group_name = azurerm_resource_group.rg.name
  throughput          = var.cosmos_throughput
  # Dev: public access enabled and scoped to Azure datacenters until VNet/private-
  # endpoint integration is provisioned. The Container Apps env has no VNet, so it
  # egresses from a dynamic public IP; the "0.0.0.0" rule admits Azure-internal
  # traffic without pinning that rotating IP. Set false + wire a private endpoint
  # in prod. Mirrors the postgresql dev stopgap below.
  public_network_access_enabled = true
  allowed_ip_ranges             = var.cosmos_allowed_ip_ranges
  tags                          = local.common_tags
}

module "postgresql" {
  source              = "../../modules/postgresql"
  project             = var.project
  environment         = var.environment
  location            = var.postgresql_location
  resource_group_name = azurerm_resource_group.rg.name
  admin_password      = var.postgresql_admin_password
  sku_name            = var.postgresql_sku
  allowed_ip_ranges   = var.postgresql_allowed_ip_ranges
  name_suffix         = var.postgresql_name_suffix
  # Dev: public access enabled until VNet/private-endpoint integration is provisioned.
  # Set to false in prod and wire azurerm_private_endpoint instead.
  public_network_access_enabled = true
  tags                          = local.common_tags
}

module "service_bus" {
  source              = "../../modules/service-bus"
  project             = var.project
  environment         = var.environment
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = var.service_bus_sku
  tags                = local.common_tags

  dlq_alert_threshold       = var.service_bus_dlq_alert_threshold
  dlq_alert_email_receivers = var.service_bus_dlq_alert_email_receivers
}

module "container_apps" {
  source = "../../modules/container-apps"

  project             = var.project
  environment         = var.environment
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.common_tags

  log_analytics_workspace_id = module.monitoring.law_id
  acr_id                     = module.container_registry.registry_id
  acr_login_server           = module.container_registry.login_server
  key_vault_uri              = module.key_vault.vault_uri
  service_bus_namespace_fqdn = "${module.service_bus.namespace_name}.servicebus.windows.net"
  cosmos_endpoint            = module.cosmos_db.endpoint
  blob_account_url           = module.blob_storage.primary_blob_endpoint
  ai_search_endpoint         = module.ai_search.endpoint
  embedding_endpoint         = module.ai_resource.embedding_endpoint
  embedding_deployment       = module.ai_resource.embedding_deployment_name
  embedding_dimensions       = var.embedding_dimensions

  # Dev: scale-to-zero for cost savings. Explicit here (matches module default)
  # so the prod always-on change above is visibly backward-safe.
  min_replicas = 0

  # Dev: raised to 10 replicas for US111 sizing. All services fall back to this cap.
  max_replicas = 10

  # identity runs EF Core migrate + admin seed and several Key Vault fetches before
  # it serves traffic, so a cold start can exceed the CD smoke-check timeout. Keep
  # one replica warm. job-orchestrator and api-gateway also remain warm (BUG049) to
  # prevent 20s+ cold-start 500s on the first batch endpoint call after idle.
  warm_services = ["identity", "job-orchestrator", "api-gateway"]

  # CORS: allow the dev SWA origin so direct browser calls to the gateway FQDN are
  # not blocked. Supplied via GATEWAY_CORS_ALLOWED_ORIGINS GitHub Actions variable
  # (JSON array) — no hardcoded origins in committed files.
  gateway_cors_allowed_origins = var.gateway_cors_allowed_origins

  # Sentry error tracking (US106, Phase 1 — dev only). Frontend is excluded — its DSN
  # is a build-time value wired separately (Phase 2/3), not a Container App secret.
  sentry_dsn_secret_ids     = { for k, s in azurerm_key_vault_secret.sentry : k => s.versionless_id }
  sentry_traces_sample_rate = 0.1

  # US121 per-stage LLM telemetry: seeding ships its structured llm_call log records to
  # Application Insights via configure_azure_monitor. Wired from the existing shared
  # app-insights-connection-string platform secret; only seeding consumes it for now.
  appinsights_connection_string_secret_ids = {
    seeding = azurerm_key_vault_secret.platform["app-insights-connection-string"].versionless_id
  }

  # BUG133: evidence's Source-3 LLM deep-research call times out against the batch's
  # primary evidence model, so it's pinned to the faster gpt-5.4 deployment instead.
  # gpt-5.4 already exists in the ai-resource module's gpt_deployments default map
  # (module "ai_resource" above) — no new AI Foundry deployment resource needed here.
  llm_research_model = "gpt-5.4"

  # BUG147: EPO OPS per-replica search rate limiting. evidence_max_replicas MUST be
  # kept equal to this app's resolved max_replicas — evidence has no service_max_replicas
  # override above, so it resolves to the module-wide max_replicas (10) set below. It is
  # the denominator the evidence service's per-replica EPO token bucket divides
  # epo_search_target_rps by: 10 replicas x 0.4 req/s each = 4.0 req/s fleet-wide, under
  # EPO's 5.0 req/s advertised ceiling. If evidence's replica count ever changes
  # (module-wide max_replicas above, or a future service_max_replicas["evidence"]
  # override), update evidence_max_replicas in the same change.
  epo_search_ceiling_rps            = 5.0
  epo_search_target_rps             = 4.0
  evidence_max_replicas             = 10
  epo_search_max_rps_per_replica    = 0.4
  epo_throttle_retry_budget_seconds = 8.0
  epo_throttle_max_retries          = 2

  # US109 Phase 2: KEDA Service Bus scalers for session consumers. Each service listed
  # here scales on its subscription backlog depth (messageCount threshold per replica).
  # Services NOT in this map scale on HTTP concurrency (api-gateway, identity,
  # model-router, vector-router). Topic names are hyphenated (live Azure resource names).
  # Thresholds derived from the US109 capacity plan (per-replica session concurrency).
  servicebus_scaler_services = {
    evidence         = { topic = "evidence-requested", subscription = "evidence", message_count = 4 }
    scoring          = { topic = "scoring-requested", subscription = "scoring", message_count = 6 }
    extraction       = { topic = "extraction-requested", subscription = "extraction", message_count = 5 }
    seeding          = { topic = "seeding-requested", subscription = "seeding", message_count = 10 }
    job-orchestrator = { topic = "scoring-completed", subscription = "orchestrator", message_count = 10 }
    ingestion        = { topic = "ingestion-requested", subscription = "ingestion", message_count = 5 }
    harvesting       = { topic = "harvesting-requested", subscription = "harvesting", message_count = 5 }
  }

  # US115 Deep Seeding: seeding drains a SECOND subscription (asset-embedding-requested,
  # whole-asset vector embedding before ideation) on top of its primary seeding-requested
  # scaler above. The app-keyed servicebus_scaler_services map allows only one rule per
  # app, so this second rule is supplied as a flat list entry. Mirrors seeding's
  # message_count = 10 per-replica session concurrency from the US109 capacity plan.
  #
  # US116 Deep Seeding: seeding drains a THIRD subscription (digest-requested, the
  # asset digest built before ideation) on top of seeding-requested and
  # asset-embedding-requested. Same flat-list mechanism — the app-keyed
  # servicebus_scaler_services map allows only one rule per app. Mirrors seeding's
  # message_count = 10 per-replica session concurrency from the US109 capacity plan.
  #
  # US117 Deep Seeding: seeding drains a FOURTH subscription (landscape-requested,
  # the patent landscape built after the digest) on top of seeding-requested,
  # asset-embedding-requested, and digest-requested. Same flat-list mechanism.
  # Mirrors seeding's message_count = 10 per-replica session concurrency from the
  # US109 capacity plan.
  #
  # US119 Deep Seeding: seeding drains a FIFTH subscription (seeding-report-requested,
  # the final report-assembly dispatch from the orchestrator) on top of the four above.
  # Same flat-list mechanism. Mirrors seeding's message_count = 10 per-replica session
  # concurrency from the US109 capacity plan.
  #
  # US119 note: ideation.completed needs NO orchestrator scaler — it emits one message
  # per document, and the orchestrator already scales on scoring-completed, so a
  # dedicated scaler here would add no capacity.
  servicebus_extra_scaler_rules = [
    {
      service       = "seeding"
      rule_name     = "servicebus-asset-embedding-depth"
      topic         = "asset-embedding-requested"
      subscription  = "seeding"
      message_count = 10
    },
    {
      service       = "seeding"
      rule_name     = "servicebus-digest-depth"
      topic         = "digest-requested"
      subscription  = "seeding"
      message_count = 10
    },
    {
      service       = "seeding"
      rule_name     = "servicebus-landscape-depth"
      topic         = "landscape-requested"
      subscription  = "seeding"
      message_count = 10
    },
    {
      service       = "seeding"
      rule_name     = "servicebus-seeding-report-depth"
      topic         = "seeding-report-requested"
      subscription  = "seeding"
      message_count = 10
    },
  ]

  # US111 scaling reconciliation. Evidence Source-3 LLM targets the SEPARATE gpt-5.4
  # deployment (15000K TPM), not the contended gpt-5.5 (5000K TPM) used by scoring/
  # extraction. model-router FoundryConcurrencyLimiter is MaxInFlight=24 PER deployment
  # PER model-router replica; model-router scales to 10 replicas, yielding 240 in-flight
  # capacity for gpt-5.4. Evidence at 10 replicas, with per-replica LLM concurrency cap
  # of 4 (set in Part B settings.py), produces 40 concurrent gpt-5.4 calls, well below
  # 240 limiter and 15000K TPM. Safe. Scoring at 10 replicas hits gpt-5.5 (5000K TPM);
  # binding constraint is per-deployment TPM, not replica count. Safe at quota level;
  # confirm under the load test.
}

module "static_web_app" {
  source = "../../modules/static-web-app"

  project             = var.project
  environment         = var.environment
  location            = var.static_web_app_location
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.common_tags

  sku_tier = var.static_web_app_sku_tier
  sku_size = var.static_web_app_sku_size

  # Standard SKU: link api-gateway Container App so /api/* requests proxy server-side.
  linked_backend_resource_id = module.container_apps.container_app_ids["api-gateway"]
  linked_backend_region      = azurerm_resource_group.rg.location
}

module "ai_search" {
  source = "../../modules/ai-search"

  project             = var.project
  environment         = var.environment
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.common_tags

  sku             = var.ai_search_sku
  replica_count   = var.ai_search_replica_count
  partition_count = var.ai_search_partition_count
}

module "ai_resource" {
  source = "../../modules/ai-resource"

  project             = var.project
  environment         = var.environment
  location            = "eastus2"
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.common_tags

  # Dev: API key auth enabled for bootstrap; prod disables.
  local_authentication_enabled = true

  # Claude config-ready but disabled pending quota approval
  enable_claude_deployment = false
  model_provider_org_name  = "WiseWork"

  # Embedding deployment enabled; gate exists for graceful fallback if quota blocks.
  # Capacity/SKU come from the module default (GlobalStandard/5000K TPM, quota-verified
  # headroom) after the Standard tier's 350K TPM regional ceiling was saturated (BUG104).
  enable_embedding_deployment = true
}

# Reader access — view-only for observers (logs, metrics, resources)
locals {
  reader_principal_ids = [
    "a10edde6-8bc8-43ff-b443-5f9febc502bf", # Kiruthika R
    "4cf88ab0-157d-426e-82ce-5b5bafae1628", # Sriram C S
    "3b0344e1-944b-4d0c-a8b6-c161a0d29cf0", # Sruthi S
  ]
}

resource "azurerm_role_assignment" "reader" {
  for_each = toset(local.reader_principal_ids)

  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Reader"
  principal_id         = each.value
}

# Data-plane read access for observer users. Built-in Reader grants resource
# enumeration but not data-plane access (KV secret values, Log Analytics queries,
# metrics query). These three roles grant least-privilege data-plane read only.
resource "azurerm_role_assignment" "reader_kv_secrets" {
  for_each = toset(local.reader_principal_ids)

  scope                = module.key_vault.vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value
  principal_type       = "User"
}

resource "azurerm_role_assignment" "reader_log_analytics" {
  for_each = toset(local.reader_principal_ids)

  scope                = module.monitoring.law_id
  role_definition_name = "Log Analytics Reader"
  principal_id         = each.value
  principal_type       = "User"
}

resource "azurerm_role_assignment" "reader_monitoring" {
  for_each = toset(local.reader_principal_ids)

  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Monitoring Reader"
  principal_id         = each.value
  principal_type       = "User"
}

# Custom role for Container App live log stream viewing. The portal live Log Stream
# requires the logstream/action DataAction plus getAuthToken (to mint the dev-API token).
# No built-in role grants these read-only; exec/action and debug/action (interactive
# console) are deliberately excluded to keep the role view-only. Role name is globally
# unique within the tenant, so it's suffixed with the environment.
resource "azurerm_role_definition" "containerapp_logstream_viewer" {
  name        = "Container App Log Stream Viewer (${var.environment})"
  scope       = azurerm_resource_group.rg.id
  description = "Read-only live log stream for Container Apps. No write, delete, or console/exec."

  permissions {
    actions = [
      "Microsoft.App/containerApps/read",
      "Microsoft.App/containerApps/revisions/read",
      "Microsoft.App/containerApps/revisions/replicas/read",
      "Microsoft.App/containerApps/getAuthToken/action",
    ]
    data_actions = [
      "Microsoft.App/containerApps/logstream/action",
    ]
  }

  assignable_scopes = [azurerm_resource_group.rg.id]
}

resource "azurerm_role_assignment" "reader_logstream" {
  for_each = toset(local.reader_principal_ids)

  scope              = azurerm_resource_group.rg.id
  role_definition_id = azurerm_role_definition.containerapp_logstream_viewer.role_definition_resource_id
  principal_id       = each.value
  principal_type     = "User"
}

# Cosmos DB data-plane RBAC. Cosmos uses its own role system, separate from Azure
# control-plane RBAC, so this is required in addition to any azurerm_role_assignment.
# Grants the shared Container Apps managed identity read/write on all Cosmos containers
# via managed identity (no keys). Environment-level (not in the cosmos-db module) to
# avoid a dependency cycle: container_apps already depends on cosmos_db.
#
# A matching grant already exists in dev (created out-of-band to unblock the live
# environment). Unlike control-plane role assignments, Cosmos data-plane assignments
# do NOT reject duplicate (principal, role, scope) tuples, so a fresh apply will not
# 409. To keep state clean and avoid a redundant duplicate, adopt the existing one:
#   terraform -chdir=deploy/environments/dev import \
#     azurerm_cosmosdb_sql_role_assignment.ca_identity_data_contributor \
#     "<assignment id from: az cosmosdb sql role assignment list --account-name cortexa-dev-cosmos --resource-group cortexa-dev-rg>"
resource "azurerm_cosmosdb_sql_role_assignment" "ca_identity_data_contributor" {
  resource_group_name = azurerm_resource_group.rg.name
  account_name        = module.cosmos_db.account_name
  role_definition_id  = "${module.cosmos_db.account_id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
  principal_id        = module.container_apps.identity_principal_id
  scope               = module.cosmos_db.account_id
}

# Key Vault data-plane access for the shared Container Apps managed identity.
# Services resolve their secrets at startup via managed identity; without this
# they 403 (ForbiddenByRbac) on getSecret. BUG059 restored least privilege:
# the shared identity (10 services: ingestion, extraction, evidence, vector-router,
# scoring, seeding, harvesting, api-gateway, identity, model-router) holds read-only
# "Key Vault Secrets User", while job-orchestrator's dedicated identity holds
# "Key Vault Secrets Officer" (it deletes batch-git-pat-{batchId} secrets during
# batch delete cascade). Distinct from the deployer's Secrets Officer grant
# in secrets.tf (different principal).
resource "azurerm_role_assignment" "ca_identity_kv_secrets_user" {
  scope                = module.key_vault.vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = module.container_apps.identity_principal_id
  principal_type       = "ServicePrincipal"
}

# Data-plane RBAC for Service Bus, Blob Storage, and AI Search. The shared Container
# Apps managed identity needs these grants so services can authenticate via managed
# identity (no keys). Service Bus: send saga events + receive subscriptions. Blob:
# read raw files + corpus assets. AI Search: Search Index Data Contributor for
# document upsert/query + Search Service Contributor for data-plane index management
# (vector-router creates the cortexa-corpus index at startup under Entra auth). Without
# these the saga 401s on publish and the vector-router 403s on search/index creation.
# Mirrors the Cosmos + Key Vault grants above.
resource "azurerm_role_assignment" "ca_identity_sb_sender" {
  scope                = module.service_bus.namespace_id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = module.container_apps.identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "ca_identity_sb_receiver" {
  scope                = module.service_bus.namespace_id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = module.container_apps.identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "ca_identity_blob_contributor" {
  scope                = module.blob_storage.account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = module.container_apps.identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "ca_identity_search_index_contributor" {
  scope                = module.ai_search.search_service_id
  role_definition_name = "Search Index Data Contributor"
  principal_id         = module.container_apps.identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "ca_identity_search_service_contributor" {
  scope                = module.ai_search.search_service_id
  role_definition_name = "Search Service Contributor"
  principal_id         = module.container_apps.identity_principal_id
  principal_type       = "ServicePrincipal"
}

# Data-plane access to the consolidated AI resource for embeddings via managed
# identity. Prod disables local (key) auth, so this role is the ONLY way the
# vector-router can call the embeddings endpoint; dev uses it too so both
# environments share one auth path (BUG093). Mirrors the ca_identity_* grants above.
resource "azurerm_role_assignment" "ca_identity_openai_user" {
  scope                = module.ai_resource.account_id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = module.container_apps.identity_principal_id
  principal_type       = "ServicePrincipal"
}

# Dedicated orchestrator identity grants. job-orchestrator needs the same data-plane
# access as the shared identity (Service Bus, Blob, Cosmos, AI Search) plus
# vault-wide Key Vault Secrets Officer (to delete batch-git-pat-* secrets). The
# orchestrator identity is the ONLY identity on the job-orchestrator app (not both) —
# KEDA scaler and DefaultAzureCredential must resolve deterministically (BUG059).
resource "azurerm_role_assignment" "orchestrator_kv_secrets_officer" {
  scope                = module.key_vault.vault_id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = module.container_apps.orchestrator_identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "orchestrator_sb_sender" {
  scope                = module.service_bus.namespace_id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = module.container_apps.orchestrator_identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "orchestrator_sb_receiver" {
  scope                = module.service_bus.namespace_id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = module.container_apps.orchestrator_identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "orchestrator_blob_contributor" {
  scope                = module.blob_storage.account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = module.container_apps.orchestrator_identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "orchestrator_search_index_contributor" {
  scope                = module.ai_search.search_service_id
  role_definition_name = "Search Index Data Contributor"
  principal_id         = module.container_apps.orchestrator_identity_principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_cosmosdb_sql_role_assignment" "orchestrator_data_contributor" {
  resource_group_name = azurerm_resource_group.rg.name
  account_name        = module.cosmos_db.account_name
  role_definition_id  = "${module.cosmos_db.account_id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
  principal_id        = module.container_apps.orchestrator_identity_principal_id
  scope               = module.cosmos_db.account_id
}

# Codify EasyAuth as permanently disabled on api-gateway. EasyAuth intercepts
# all unauthenticated requests at the Azure infra layer before ASP.NET Core sees
# them — including .AllowAnonymous() endpoints. The gateway's own JWT middleware
# handles all auth; EasyAuth must never be enabled. This resource ensures drift
# (e.g. accidental portal enable) is caught on the next terraform plan.
resource "azapi_resource" "api_gateway_auth_config" {
  type      = "Microsoft.App/containerApps/authConfigs@2024-03-01"
  name      = "current"
  parent_id = module.container_apps.container_app_ids["api-gateway"]

  body = {
    properties = {
      platform = {
        enabled = false
      }
    }
  }

  schema_validation_enabled = false
}

module "budget_alert" {
  count  = var.enable_budget ? 1 : 0
  source = "../../modules/budget-alert"

  project             = var.project
  environment         = var.environment
  resource_group_name = azurerm_resource_group.rg.name
  resource_group_id   = azurerm_resource_group.rg.id
  tags                = local.common_tags

  monthly_amount    = var.budget_monthly_amount
  time_period_start = var.budget_time_period_start
  contact_emails    = var.budget_contact_emails
}
