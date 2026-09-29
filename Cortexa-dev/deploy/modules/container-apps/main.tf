resource "azurerm_user_assigned_identity" "ca_identity" {
  name                = "${var.project}-${var.environment}-ca-identity"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_user_assigned_identity" "orchestrator_identity" {
  name                = "${var.project}-${var.environment}-orchestrator-identity"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_container_app_environment" "env" {
  name                       = "${var.project}-${var.environment}-ca-env"
  location                   = var.location
  resource_group_name        = var.resource_group_name
  log_analytics_workspace_id = var.log_analytics_workspace_id
  tags                       = var.tags
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.ca_identity.principal_id
}

resource "azurerm_role_assignment" "acr_pull_orchestrator" {
  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.orchestrator_identity.principal_id
}

locals {
  # Internal-ingress apps are reachable inside the environment at
  # <app-name>.internal.<env-default-domain>. The model-router is internal-only, so
  # callers must target this FQDN — a bare "http://model-router" does not resolve
  # (DNS Errno -2). Built here rather than passed in because default_domain is only
  # known after the environment is created inside this module.
  model_router_url = "http://${var.project}-${var.environment}-model-router.internal.${azurerm_container_app_environment.env.default_domain}"

  # Same internal-ingress FQDN shape as model_router_url. vector-router is
  # internal-only, so evidence (and any other caller) must target this FQDN — a
  # bare "http://vector-router" does not resolve (DNS Errno -2).
  vector_router_url = "http://${var.project}-${var.environment}-vector-router.internal.${azurerm_container_app_environment.env.default_domain}"

  # Same internal-ingress FQDN shape. seeding calls evidence's patent-search REST
  # endpoint (US117 prior-art landscape) over internal ingress.
  evidence_url = "http://${var.project}-${var.environment}-evidence.internal.${azurerm_container_app_environment.env.default_domain}"

  # api-gateway's UserStatusMiddleware calls the identity service directly (not via YARP), so it
  # needs its own internal base URL. Scheme/host shape must match
  # DefaultClusterConfigFilter.ComputeInternalAddress's https://{namePrefix}-{clusterId}.internal.{internalDomain}.
  identity_internal_url = "https://${var.project}-${var.environment}-identity.internal.${azurerm_container_app_environment.env.default_domain}"

  # One entry per microservice. Port and stack come from each service's Dockerfile;
  # api-gateway is the only externally-exposed app — everything else is internal-only.
  services = {
    ingestion          = { port = 8000, external = false, stack = "python" }
    extraction         = { port = 8000, external = false, stack = "python" }
    evidence           = { port = 8000, external = false, stack = "python" }
    "vector-router"    = { port = 8000, external = false, stack = "python" }
    scoring            = { port = 8000, external = false, stack = "python" }
    seeding            = { port = 8000, external = false, stack = "python" }
    harvesting         = { port = 8000, external = false, stack = "python" }
    "api-gateway"      = { port = 5000, external = true, stack = "dotnet" }
    identity           = { port = 5000, external = false, stack = "dotnet" }
    "model-router"     = { port = 5000, external = false, stack = "dotnet" }
    "job-orchestrator" = { port = 5000, external = false, stack = "dotnet" }
  }

  # Map each service to its dedicated UAMI. job-orchestrator gets the orchestrator-specific
  # identity with vault-wide Secrets Officer; all others get the shared identity with Secrets User.
  app_identity_id = {
    for k in keys(local.services) : k => k == "job-orchestrator" ? azurerm_user_assigned_identity.orchestrator_identity.id : azurerm_user_assigned_identity.ca_identity.id
  }
  app_client_id = {
    for k in keys(local.services) : k => k == "job-orchestrator" ? azurerm_user_assigned_identity.orchestrator_identity.client_id : azurerm_user_assigned_identity.ca_identity.client_id
  }
}

resource "azurerm_container_app" "service" {
  for_each = local.services

  name                         = "${var.project}-${var.environment}-${each.key}"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [local.app_identity_id[each.key]]
  }

  registry {
    server   = var.acr_login_server
    identity = local.app_identity_id[each.key]
  }

  # Sentry DSN (US106, Phase 1 — dev only). Only services present in
  # sentry_dsn_secret_ids get this secret block. The identity here MUST be the
  # per-service assigned UAMI (local.app_identity_id), not the system identity —
  # this app has no system identity, only the UserAssigned one, and that is the
  # identity Azure uses to resolve the Key Vault reference at revision start.
  dynamic "secret" {
    for_each = contains(keys(var.sentry_dsn_secret_ids), each.key) ? [1] : []
    content {
      name                = "sentry-dsn"
      key_vault_secret_id = var.sentry_dsn_secret_ids[each.key]
      identity            = local.app_identity_id[each.key]
    }
  }

  # Application Insights connection string (US121 per-stage LLM telemetry). Only
  # services present in appinsights_connection_string_secret_ids get this secret block.
  # Same identity constraint as the sentry-dsn block above: the KV reference is resolved
  # by the per-service UserAssigned identity at revision start.
  dynamic "secret" {
    for_each = contains(keys(var.appinsights_connection_string_secret_ids), each.key) ? [1] : []
    content {
      name                = "app-insights-connection-string"
      key_vault_secret_id = var.appinsights_connection_string_secret_ids[each.key]
      identity            = local.app_identity_id[each.key]
    }
  }

  ingress {
    external_enabled = each.value.external
    target_port      = each.value.port
    transport        = "auto"

    # Internal-only apps serve plain HTTP on the mesh: with the default (false) the
    # ingress 301-redirects HTTP→HTTPS, and callers use http:// (see model_router_url)
    # to avoid TLS-verification against the untrusted internal cert. The external
    # api-gateway keeps this false so public traffic is still forced to HTTPS.
    allow_insecure_connections = !each.value.external

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # A service listed in warm_services keeps at least one replica even when the
    # environment default is scale-to-zero, so its first request never pays a cold
    # start. Otherwise it follows the environment-wide min_replicas.
    min_replicas = contains(var.warm_services, each.key) ? max(var.min_replicas, 1) : var.min_replicas
    # Per-service max_replicas (US109 capacity plan) overrides the module-wide default.
    # BUG147: for evidence specifically, this resolved value MUST be kept equal to the
    # var.evidence_max_replicas env var injected below — it is the denominator the
    # evidence service's per-replica EPO search token bucket divides
    # epo_search_target_rps by. If evidence ever gets a service_max_replicas override
    # (or the module-wide max_replicas default changes), update var.evidence_max_replicas
    # in the same change, or the rate limiter silently desyncs from the real fleet size.
    max_replicas = lookup(var.service_max_replicas, each.key, var.max_replicas)

    container {
      name = each.key
      # Initial image only — CD updates the running revision via `az containerapp update`
      # after this is applied. The lifecycle block below makes Terraform ignore image
      # drift, so subsequent applies keep CD's deployed github.sha image instead of
      # reverting every app to this create-time default tag.
      image  = "${var.acr_login_server}/${each.key}:${var.image_tag}"
      cpu    = var.container_cpu
      memory = var.container_memory

      env {
        name  = each.value.stack == "python" ? "KEYVAULT_URI" : "KeyVault__Uri"
        value = var.key_vault_uri
      }

      env {
        name  = each.value.stack == "python" ? "SERVICEBUS_NAMESPACE_FQDN" : "ServiceBus__NamespaceFqdn"
        value = var.service_bus_namespace_fqdn
      }

      env {
        name  = each.value.stack == "python" ? "COSMOS_URI" : "Cosmos__Uri"
        value = var.cosmos_endpoint
      }

      env {
        name  = each.value.stack == "python" ? "BLOB_ACCOUNT_URL" : "Blob__AccountUrl"
        value = var.blob_account_url
      }

      env {
        name  = each.value.stack == "python" ? "MODEL_ROUTER_URL" : "ModelRouter__Url"
        value = local.model_router_url
      }

      env {
        name  = each.value.stack == "python" ? "VECTOR_ROUTER_URL" : "VectorRouter__Url"
        value = local.vector_router_url
      }

      env {
        name  = each.value.stack == "python" ? "EVIDENCE_URL" : "Evidence__Url"
        value = local.evidence_url
      }

      env {
        name  = each.value.stack == "python" ? "AI_SEARCH_ENDPOINT" : "AiSearch__Endpoint"
        value = var.ai_search_endpoint
      }

      env {
        name  = each.value.stack == "python" ? "EMBEDDING_ENDPOINT" : "Embedding__Endpoint"
        value = var.embedding_endpoint
      }

      env {
        name  = each.value.stack == "python" ? "EMBEDDING_DEPLOYMENT" : "Embedding__Deployment"
        value = var.embedding_deployment
      }

      env {
        name  = each.value.stack == "python" ? "EMBEDDING_DIMENSIONS" : "Embedding__Dimensions"
        value = tostring(var.embedding_dimensions)
      }

      env {
        name  = "AZURE_CLIENT_ID"
        value = local.app_client_id[each.key]
      }

      # Gateway-specific config: api-gateway uses these to construct internal backend
      # URLs as https://<NamePrefix>-<svc>.internal.<InternalDomain>. Injected into
      # all apps uniformly; other services ignore them.
      env {
        name  = "Gateway__NamePrefix"
        value = "${var.project}-${var.environment}"
      }

      env {
        name  = "Gateway__InternalDomain"
        value = azurerm_container_app_environment.env.default_domain
      }

      # UserStatusMiddleware (BUG075): api-gateway resolves the identity service's UserStatus
      # endpoint via this env var rather than the YARP cluster config, so it needs its own
      # explicit base URL. Only api-gateway consumes it; other services ignore it.
      dynamic "env" {
        for_each = each.key == "api-gateway" ? [1] : []
        content {
          name  = "UserStatus__IdentityInternalBaseUrl"
          value = local.identity_internal_url
        }
      }

      # US110 ModelCatalogClient: job-orchestrator validates per-stage model configs by
      # calling model-router GET /models. Only job-orchestrator consumes it; other
      # services ignore it.
      dynamic "env" {
        for_each = each.key == "job-orchestrator" ? [1] : []
        content {
          name  = "ModelRouter__BaseUrl"
          value = local.model_router_url
        }
      }

      # BUG133: override the evidence service's Source-3 LLM deep-research model with a
      # faster deployment than its primary evidence model, fixing a timeout. Evidence is
      # always the python stack, so the name is not stack-ternaried like the shared vars
      # above. Only wired when llm_research_model is set; empty (module default) omits
      # the env var so evidence falls back to its primary model config.
      dynamic "env" {
        for_each = each.key == "evidence" && var.llm_research_model != "" ? [1] : []
        content {
          name  = "LLM_RESEARCH_MODEL"
          value = var.llm_research_model
        }
      }

      # BUG147: EPO OPS per-replica search rate limiting config. Evidence is always the
      # python stack, so names are not stack-ternaried like the shared vars above. Only
      # wired for evidence; other services ignore EPO search config. See the
      # var.evidence_max_replicas description and the max_replicas comment above for the
      # sync requirement with this app's resolved max_replicas.
      dynamic "env" {
        for_each = each.key == "evidence" ? [1] : []
        content {
          name  = "EPO_SEARCH_CEILING_RPS"
          value = tostring(var.epo_search_ceiling_rps)
        }
      }

      dynamic "env" {
        for_each = each.key == "evidence" ? [1] : []
        content {
          name  = "EPO_SEARCH_TARGET_RPS"
          value = tostring(var.epo_search_target_rps)
        }
      }

      dynamic "env" {
        for_each = each.key == "evidence" ? [1] : []
        content {
          name  = "EVIDENCE_MAX_REPLICAS"
          value = tostring(var.evidence_max_replicas)
        }
      }

      dynamic "env" {
        for_each = each.key == "evidence" ? [1] : []
        content {
          name  = "EPO_SEARCH_MAX_RPS_PER_REPLICA"
          value = tostring(var.epo_search_max_rps_per_replica)
        }
      }

      dynamic "env" {
        for_each = each.key == "evidence" ? [1] : []
        content {
          name  = "EPO_THROTTLE_RETRY_BUDGET_SECONDS"
          value = tostring(var.epo_throttle_retry_budget_seconds)
        }
      }

      dynamic "env" {
        for_each = each.key == "evidence" ? [1] : []
        content {
          name  = "EPO_THROTTLE_MAX_RETRIES"
          value = tostring(var.epo_throttle_max_retries)
        }
      }

      # Sentry (US106, Phase 1 — dev only). Only services present in
      # sentry_dsn_secret_ids receive these; the secret block above already guards
      # existence, so a plain env referencing "sentry-dsn" is safe within this dynamic.
      dynamic "env" {
        for_each = contains(keys(var.sentry_dsn_secret_ids), each.key) ? [1] : []
        content {
          name        = "SENTRY_DSN"
          secret_name = "sentry-dsn"
        }
      }

      dynamic "env" {
        for_each = contains(keys(var.sentry_dsn_secret_ids), each.key) ? [1] : []
        content {
          name  = each.value.stack == "python" ? "SENTRY_ENVIRONMENT" : "Sentry__Environment"
          value = "dev"
        }
      }

      dynamic "env" {
        for_each = contains(keys(var.sentry_dsn_secret_ids), each.key) ? [1] : []
        content {
          name  = each.value.stack == "python" ? "SENTRY_TRACES_SAMPLE_RATE" : "Sentry__TracesSampleRate"
          value = tostring(var.sentry_traces_sample_rate)
        }
      }

      # Application Insights connection string (US121 per-stage LLM telemetry). Only
      # services present in appinsights_connection_string_secret_ids receive this; the
      # secret block above guards existence, so the plain secret_name reference is safe.
      dynamic "env" {
        for_each = contains(keys(var.appinsights_connection_string_secret_ids), each.key) ? [1] : []
        content {
          name        = "APPLICATIONINSIGHTS_CONNECTION_STRING"
          secret_name = "app-insights-connection-string"
        }
      }

      # CORS allowed origins (BUG026). api-gateway reads Cors:AllowedOrigins at startup;
      # ASP.NET Core's double-underscore array binder maps Cors__AllowedOrigins__0,
      # Cors__AllowedOrigins__1, … to the list. Empty by default — supply origins via
      # gateway_cors_allowed_origins. Other services ignore these keys.
      dynamic "env" {
        for_each = { for idx, origin in var.gateway_cors_allowed_origins : tostring(idx) => origin }
        iterator = cors_entry
        content {
          name  = "Cors__AllowedOrigins__${cors_entry.key}"
          value = cors_entry.value
        }
      }

      # Health probes: all services expose /health on their native port. Liveness +
      # readiness for all services; startup probe with longer delay for identity (which
      # runs EF Core migrate + admin seed at cold start) to prevent pod kill during init.
      liveness_probe {
        transport               = "HTTP"
        path                    = "/health"
        port                    = each.value.port
        interval_seconds        = 30
        timeout                 = 5
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        path                    = "/health"
        port                    = each.value.port
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
        success_count_threshold = 1
      }

      startup_probe {
        transport               = "HTTP"
        path                    = "/health"
        port                    = each.value.port
        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = each.key == "identity" ? 30 : 10
      }
    }

    # KEDA autoscaling. Services consuming Service Bus subscriptions (US108 session
    # consumers: evidence, scoring, extraction, seeding, ingestion, harvesting,
    # job-orchestrator) scale on queue depth. HTTP-driven services (api-gateway,
    # identity, model-router, vector-router) scale on concurrent HTTP requests.
    #
    # Service Bus scaler uses workload identity (no connection string secret).
    # Each app is assigned exactly one UAMI: the shared ca_identity (10 services)
    # or orchestrator_identity (job-orchestrator only). Both hold Azure Service Bus
    # Data Receiver on the namespace (granted in deploy/environments/{dev,prod}/main.tf),
    # so the scaler authenticates via the service's assigned UAMI. The scale rule's
    # identity_id explicitly binds that UAMI for workload-identity auth — Container
    # Apps does not infer it from the app's assigned identities.
    dynamic "custom_scale_rule" {
      for_each = contains(keys(var.servicebus_scaler_services), each.key) ? [1] : []
      content {
        name             = "servicebus-queue-depth"
        custom_rule_type = "azure-servicebus"
        identity_id      = local.app_identity_id[each.key]
        metadata = {
          topicName        = var.servicebus_scaler_services[each.key].topic
          subscriptionName = var.servicebus_scaler_services[each.key].subscription
          messageCount     = tostring(var.servicebus_scaler_services[each.key].message_count)
          namespace        = replace(var.service_bus_namespace_fqdn, ".servicebus.windows.net", "")
        }
      }
    }

    # US115 Deep Seeding: a service that consumes a second subscription (seeding also
    # drains asset-embedding-requested) needs a second azure-servicebus rule. The
    # app-keyed servicebus_scaler_services map allows only one rule per app, so extra
    # rules are supplied as a flat list and matched to this app by service name. Same
    # workload-identity auth and metadata shape as the primary rule above.
    dynamic "custom_scale_rule" {
      for_each = { for r in var.servicebus_extra_scaler_rules : r.rule_name => r if r.service == each.key }
      content {
        name             = custom_scale_rule.value.rule_name
        custom_rule_type = "azure-servicebus"
        identity_id      = local.app_identity_id[each.key]
        metadata = {
          topicName        = custom_scale_rule.value.topic
          subscriptionName = custom_scale_rule.value.subscription
          messageCount     = tostring(custom_scale_rule.value.message_count)
          namespace        = replace(var.service_bus_namespace_fqdn, ".servicebus.windows.net", "")
        }
      }
    }

    dynamic "http_scale_rule" {
      for_each = !contains(keys(var.servicebus_scaler_services), each.key) ? [1] : []
      content {
        name                = "http-concurrency"
        concurrent_requests = tostring(var.http_concurrent_requests)
      }
    }
  }

  # CD owns the running image (deployed by `az containerapp update` with a github.sha
  # tag). Ignore image drift here so `terraform apply` does not reset apps to the
  # create-time default tag and clobber images for services not rebuilt in that run.
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }

  depends_on = [
    azurerm_role_assignment.acr_pull,
    azurerm_role_assignment.acr_pull_orchestrator
  ]
}
