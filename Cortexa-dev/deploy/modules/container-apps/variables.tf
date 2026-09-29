variable "project" {
  type        = string
  description = "Project short name, e.g. cortexa"
}

variable "environment" {
  type        = string
  description = "Environment name, e.g. dev or prod"
}

variable "location" {
  type        = string
  description = "Azure region"
}

variable "resource_group_name" {
  type        = string
  description = "Name of the resource group to deploy into"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}

variable "log_analytics_workspace_id" {
  type        = string
  description = "Resource ID of the Log Analytics workspace to link to the Container Apps environment"
}

variable "acr_id" {
  type        = string
  description = "Resource ID of the Azure Container Registry for AcrPull role assignment scope"
}

variable "acr_login_server" {
  type        = string
  description = "Login server hostname of the Azure Container Registry"
}

variable "key_vault_uri" {
  type        = string
  description = "URI of the Key Vault — passed to every Container App so services can resolve secrets at runtime"
}

variable "service_bus_namespace_fqdn" {
  type        = string
  description = "Fully-qualified domain name of the Service Bus namespace — passed to every Container App"
}

variable "cosmos_endpoint" {
  type        = string
  description = "Cosmos DB account endpoint URI — passed to every Container App so services authenticate to Cosmos via managed identity (no key)"
}

variable "image_tag" {
  type        = string
  default     = "latest"
  description = "Initial image tag used for first Terraform apply; CD updates the running image via az containerapp update after this"
}

variable "min_replicas" {
  type        = number
  default     = 0
  description = "Minimum replica count for every Container App (dev: 0 = scale-to-zero, prod: 1 = always-on)"
}

variable "warm_services" {
  type        = set(string)
  default     = []
  description = "Service names forced to a minimum of 1 replica even when min_replicas is 0. Keeps slow-cold-start services (e.g. identity runs EF migrate/seed at startup) always-on so health probes do not time out. Empty by default; prod already runs all services warm via min_replicas = 1."
}

variable "container_cpu" {
  type        = number
  default     = 1.0
  description = "vCPU allocation per Container App replica. Azure Container Apps CPU:memory pairs must be valid combinations: 0.25 vCPU pairs with 0.5 GiB; 0.5 vCPU with 1.0 GiB; 0.75 vCPU with 1.5 GiB; 1.0 vCPU with 2.0 GiB, etc. Pairs step in 0.25 vCPU / 0.5 GiB increments; invalid pairs fail at apply time."
}

variable "container_memory" {
  type        = string
  default     = "2Gi"
  description = "Memory allocation per Container App replica. Must be a valid pairing with container_cpu (see container_cpu description for valid CPU:memory combinations)."
}

variable "blob_account_url" {
  type        = string
  description = "Primary blob service endpoint URL — injected so the ingestion service can reach Blob Storage via managed identity"
}

variable "ai_search_endpoint" {
  type        = string
  default     = ""
  description = "Azure AI Search endpoint URL — injected for the vector-router service"
}

variable "embedding_endpoint" {
  type        = string
  default     = ""
  description = "Embedding model endpoint URL (AI Foundry) — injected for the vector-router service"
}

variable "embedding_deployment" {
  type        = string
  default     = ""
  description = "Embedding deployment name (used as the deployment ID in API calls) — injected for the vector-router service"
}

variable "embedding_dimensions" {
  type        = number
  default     = 1536
  description = "Embedding vector dimensions (1536 for text-embedding-3-large) — injected for the vector-router service"
}

variable "gateway_cors_allowed_origins" {
  type        = list(string)
  default     = []
  description = "Origins permitted by the api-gateway CORS policy (BUG026). Each entry maps to Cors__AllowedOrigins__N env var consumed by the ASP.NET Core config binder. Empty list denies all cross-origin requests. Supply the SWA default hostname after provisioning, or any custom domain bound to the SWA."
}

variable "max_replicas" {
  type        = number
  default     = 10
  description = "Maximum replica count for every Container App"
}

variable "http_concurrent_requests" {
  type        = number
  default     = 50
  description = "HTTP scaler concurrency threshold for HTTP-driven services"
}

variable "sentry_dsn_secret_ids" {
  type        = map(string)
  default     = {}
  description = "Sentry DSN Key Vault secret ID (versionless), keyed by service name (US106, Phase 1 — dev only). Only services with an entry get a SENTRY_DSN/Sentry__Dsn env var and secret block; others are unaffected."
}

variable "sentry_traces_sample_rate" {
  type        = number
  default     = 0.1
  description = "Sentry performance-tracing sample rate (0.0-1.0), injected as SENTRY_TRACES_SAMPLE_RATE (Python) or Sentry__TracesSampleRate (.NET) for services present in sentry_dsn_secret_ids"
}

variable "appinsights_connection_string_secret_ids" {
  type        = map(string)
  default     = {}
  description = "Application Insights connection-string Key Vault secret ID (versionless), keyed by service name (US121 per-stage LLM telemetry). Only services with an entry get an APPLICATIONINSIGHTS_CONNECTION_STRING env var and secret block; others are unaffected."
}

variable "servicebus_scaler_services" {
  type = map(object({
    topic         = string
    subscription  = string
    message_count = number
  }))
  default     = {}
  description = "Services configured to scale on Service Bus queue depth (US108 session consumers). Key = service name. Each object specifies the topic, subscription, and KEDA messageCount threshold. Any service present in this map gets an azure-servicebus custom_scale_rule instead of the HTTP scaler."
}

variable "servicebus_extra_scaler_rules" {
  type = list(object({
    service       = string
    rule_name     = string
    topic         = string
    subscription  = string
    message_count = number
  }))
  default     = []
  description = "Additional Service Bus scale rules for services that consume more than one subscription (US115 Deep Seeding: seeding drains both seeding-requested and asset-embedding-requested). The servicebus_scaler_services map allows only one rule per app, but a Container App supports multiple KEDA rules — each entry here adds a second azure-servicebus custom_scale_rule on the named service. service must already be a key in servicebus_scaler_services; rule_name must be unique within that app."
}

variable "service_max_replicas" {
  type        = map(number)
  default     = {}
  description = "Per-service maximum replica count override (US109 capacity plan). Key = service name, value = max_replicas. Any service not present falls back to the module-wide max_replicas variable."
}

variable "llm_research_model" {
  type        = string
  default     = ""
  description = "BUG133: GPT deployment name (must match a key in the ai-resource module's gpt_deployments map) used to override the evidence service's Source-3 LLM deep-research call so it runs against a faster model than the batch's primary evidence model, avoiding request timeouts. Injected as LLM_RESEARCH_MODEL for the evidence service only. Empty string (default) omits the env var entirely, so evidence falls back to its primary model config."
}

variable "epo_search_ceiling_rps" {
  type        = number
  default     = 5.0
  description = "BUG147: EPO OPS per-tenant search throttling ceiling (req/s), advertised via the x-throttling-control response header (search=green:5). Injected as EPO_SEARCH_CEILING_RPS for the evidence service only; reference value for the throttle logging/alerting path, not enforced directly by the token bucket."
}

variable "epo_search_target_rps" {
  type        = number
  default     = 4.0
  description = "BUG147: Fleet-wide EPO search rate target (req/s) the per-replica token bucket collectively enforces, kept under epo_search_ceiling_rps for headroom. Injected as EPO_SEARCH_TARGET_RPS for the evidence service only."
}

variable "evidence_max_replicas" {
  type        = number
  default     = 10
  description = "BUG147: MUST be kept equal to the evidence Container App's actual max_replicas (lookup(service_max_replicas, \"evidence\", max_replicas) below) — it is the denominator the evidence service's per-replica EPO token bucket divides epo_search_target_rps by. Injected as EVIDENCE_MAX_REPLICAS for the evidence service only. Changing evidence's effective replica count without updating this value desyncs the rate limiter from the real fleet size."
}

variable "epo_search_max_rps_per_replica" {
  type        = number
  default     = 0.4
  description = "BUG147: Explicit, overridable per-replica EPO search rate cap (default = epo_search_target_rps / evidence_max_replicas = 4.0 / 10 = 0.4). Injected as EPO_SEARCH_MAX_RPS_PER_REPLICA for the evidence service only."
}

variable "epo_throttle_retry_budget_seconds" {
  type        = number
  default     = 8.0
  description = "BUG147: Total retry-backoff budget in seconds for a throttled (HTTP 403 throttle-indicator or HTTP 500 busy) EPO OPS search call; must stay strictly less than evidence_patent_acquire_timeout_seconds (20s, see services/evidence settings.py) so a throttled search can never itself exhaust the patent-branch acquire timeout. Injected as EPO_THROTTLE_RETRY_BUDGET_SECONDS for the evidence service only."
}

variable "epo_throttle_max_retries" {
  type        = number
  default     = 2
  description = "BUG147: Maximum retry attempts for a throttled (HTTP 403 throttle-indicator or HTTP 500 busy) EPO OPS search call. Injected as EPO_THROTTLE_MAX_RETRIES for the evidence service only."
}
