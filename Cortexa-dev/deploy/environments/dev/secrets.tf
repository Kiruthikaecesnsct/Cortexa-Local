# Symmetric JWT signing key shared by identity (issuer) and api-gateway (validator).
# random_password keeps it stable across applies; stored only in Key Vault + state.
resource "random_password" "jwt_signing_key" {
  length  = 64
  special = false
}

# Symmetric HMAC secret shared by identity (issuer/verifier of X-Internal-Key) and
# api-gateway (sender). random_password keeps it stable across applies.
resource "random_password" "internal_api_shared_secret" {
  length  = 64
  special = false
}

# Sole owner of the deployer's Key Vault Secrets Officer grant.
# Terraform must track this assignment in state; if state is ever lost while the
# assignment still exists in Azure, re-adopt it before applying:
#   terraform -chdir=deploy/environments/dev import \
#     azurerm_role_assignment.deployer_secrets <assignment-resource-id>
# (find the id via: az role assignment list --scope <vault-id> --query "[].id" -o tsv)
resource "azurerm_role_assignment" "deployer_secrets" {
  scope                = module.key_vault.vault_id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = var.deployer_object_id
  principal_type       = "ServicePrincipal"
}

# Azure AD RBAC propagation can take several minutes.
# The sleep ensures secret writes don't 403 on a fresh apply.
resource "time_sleep" "rbac_propagation" {
  create_duration = "120s"

  depends_on = [azurerm_role_assignment.deployer_secrets]
}

locals {
  kv_secrets = {
    "blob-primary-connection-string"        = module.blob_storage.primary_connection_string
    "cosmos-primary-connection-string"      = module.cosmos_db.primary_connection_string
    "postgresql-connection-string"          = module.postgresql.connection_string
    "service-bus-primary-connection-string" = module.service_bus.primary_connection_string
    # Converged AI resource secrets — both model-router keys carry the same API key value
    "model-router-foundry-api-key"   = var.ai_resource_api_key
    "model-router-anthropic-api-key" = var.ai_resource_api_key
    # Gemini — model-router's default provider (Router:Mode=single-gemini)
    "gemini-api-key" = var.gemini_api_key
    # Query key only — least-privilege for service consumers; admin key is not distributed
    "ai-search-query-key" = module.ai_search.query_key
    # Connection string preferred over instrumentation key (ikey is deprecated by Microsoft)
    "app-insights-connection-string" = module.monitoring.connection_string
    # Patent API credentials — fetched by evidence service at startup via managed identity
    "uspto-api-key"    = var.patent_uspto_api_key
    "epo-consumer-key" = var.patent_epo_consumer_key
    "epo-oauth-secret" = var.patent_epo_oauth_secret
    "lens-api-key"     = var.patent_lens_api_key
    # JWT signing key — shared secret for identity (issuer) and api-gateway (validator)
    "jwt-signing-key" = random_password.jwt_signing_key.result
    # Internal API HMAC secret — shared between identity (issuer/verifier of X-Internal-Key)
    # and api-gateway (sender), same pattern as jwt-signing-key above
    "identity-internal-api-shared-secret" = random_password.internal_api_shared_secret.result
    "internal-api-key"                    = random_password.internal_api_shared_secret.result
    # Cosmos account endpoint URI — job-orchestrator reads this at startup for its
    # managed-identity CosmosClient (Cosmos:Uri). Distinct from the connection string.
    "job-orchestrator-cosmos-uri" = module.cosmos_db.endpoint
  }
}

resource "azurerm_key_vault_secret" "platform" {
  for_each = local.kv_secrets

  name         = each.key
  value        = each.value
  key_vault_id = module.key_vault.vault_id

  depends_on = [time_sleep.rbac_propagation]

  tags = local.common_tags

  lifecycle {
    # Prevent terraform apply from reverting secrets rotated out-of-band in Key Vault
    ignore_changes = [value]
  }
}

# Sentry DSNs (US106, Phase 1 — dev only), one secret per backend service. Read by
# each Container App at startup via a KV-secret-referenced env var (SENTRY_DSN /
# Sentry__Dsn) — see container-apps module. Kept separate from local.kv_secrets
# because the key naming ("sentry-dsn-<service>") is derived from the map keys
# rather than being a 1:1 static mapping.
resource "azurerm_key_vault_secret" "sentry" {
  # Terraform forbids a sensitive value as a for_each key (it could leak into the
  # resource instance address). The service names are not secret — only the DSN
  # values are — so iterate over nonsensitive(keys(...)) and look the DSN back up
  # by key for "value", which stays sensitive.
  for_each = nonsensitive(toset(keys(var.sentry_dsns)))

  name         = "sentry-dsn-${each.value}"
  value        = var.sentry_dsns[each.value]
  key_vault_id = module.key_vault.vault_id

  depends_on = [time_sleep.rbac_propagation]

  tags = local.common_tags

  lifecycle {
    # Prevent terraform apply from reverting secrets rotated out-of-band in Key Vault
    ignore_changes = [value]
  }
}
