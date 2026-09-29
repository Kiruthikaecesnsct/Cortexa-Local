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
#   terraform -chdir=deploy/environments/prod import \
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
    # Prod: managed identity RBAC only — no AI key secrets written. Services authenticate
    # via Container Apps managed identity with Cognitive Services OpenAI User role.
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
