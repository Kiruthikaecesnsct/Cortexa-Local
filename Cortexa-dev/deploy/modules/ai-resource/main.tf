resource "azurerm_cognitive_account" "this" {
  name                = "${var.project}-${var.environment}-ai-resource"
  location            = var.location
  resource_group_name = var.resource_group_name
  kind                = "AIServices"
  sku_name            = "S0"

  custom_subdomain_name         = "${var.project}-${var.environment}-ai-resource"
  public_network_access_enabled = var.public_network_access_enabled
  local_auth_enabled            = var.local_authentication_enabled

  # Azure attaches a system-assigned identity when project_management_enabled is set.
  # The identity block is required by the azurerm provider when that flag is true.
  identity {
    type = "SystemAssigned"
  }

  tags = var.tags

  # Azure sets project_management_enabled internally when AI Foundry projects are linked.
  # Ignoring it prevents forced replacement on every plan.
  lifecycle {
    ignore_changes = [project_management_enabled]
  }
}

# GPT deployments — default map includes gpt-5.5 and gpt-5.4 matching live dev state
resource "azurerm_cognitive_deployment" "gpt" {
  for_each = var.gpt_deployments

  name                 = each.key
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = "OpenAI"
    name    = each.value.model
    version = each.value.version
  }

  sku {
    name     = var.gpt_deployment_sku_name
    capacity = each.value.capacity
  }
}

# Embedding deployment — gated behind toggle for graceful fallback if quota/availability blocks
#
# Azure's Cognitive Services ARM API serializes deployment writes per parent account —
# concurrent PUTs against sibling deployments 409 with "Another operation is being
# performed on the parent resource" (hit in CD after BUG087 added grok/deepseek
# alongside this one). depends_on chains every deployment resource in this module onto
# a single sequential order so Terraform never fires two deployment PUTs at once.
resource "azurerm_cognitive_deployment" "embedding" {
  count = var.enable_embedding_deployment ? 1 : 0

  name                 = var.embedding_deployment_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = "OpenAI"
    name    = var.embedding_model_name
    version = var.embedding_model_version
  }

  sku {
    name     = var.embedding_deployment_sku_name
    capacity = var.embedding_deployment_capacity
  }

  depends_on = [azurerm_cognitive_deployment.gpt]
}

# Anthropic models on Azure require prior terms-of-use acceptance in the Azure portal.
# Set var.enable_claude_deployment = true only AFTER accepting terms in AI Foundry portal.
resource "azapi_resource" "claude_deployment" {
  count     = var.enable_claude_deployment ? 1 : 0
  type      = "Microsoft.CognitiveServices/accounts/deployments@2024-10-01"
  name      = var.claude_deployment_name
  parent_id = azurerm_cognitive_account.this.id

  # modelProviderData is required by Azure for Anthropic models but not yet in the
  # provider's embedded schema — disable validation so the field reaches the ARM API.
  schema_validation_enabled = false

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = var.claude_deployment_capacity
    }
    properties = {
      model = {
        format  = "Anthropic"
        name    = var.claude_model_name
        version = var.claude_model_version
      }
      modelProviderData = {
        industry         = var.model_provider_industry
        organizationName = var.model_provider_org_name
        countryCode      = var.model_provider_country_code
      }
    }
  }

  response_export_values = ["*"]

  # See the depends_on note on azurerm_cognitive_deployment.embedding above: this
  # chain prevents concurrent ARM PUTs on sibling deployments under this account.
  depends_on = [azurerm_cognitive_deployment.embedding]
}

# grok-4.3 (xAI) — same azapi escape hatch as Claude above because
# azurerm_cognitive_deployment only supports format="OpenAI" (provider issue #29194).
# No modelProviderData block: that's Anthropic-specific terms-of-use metadata, not
# part of the ARM schema for any other model provider.
resource "azapi_resource" "grok_deployment" {
  count     = var.enable_grok_deployment ? 1 : 0
  type      = "Microsoft.CognitiveServices/accounts/deployments@2024-10-01"
  name      = var.grok_deployment_name
  parent_id = azurerm_cognitive_account.this.id

  # The provider's embedded schema does not recognize non-OpenAI format enum values.
  schema_validation_enabled = false

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = var.grok_deployment_capacity
    }
    properties = {
      model = {
        format  = "xAI"
        name    = var.grok_model_name
        version = var.grok_model_version
      }
    }
  }

  response_export_values = ["*"]

  # See the depends_on note on azurerm_cognitive_deployment.embedding above.
  depends_on = [azapi_resource.claude_deployment]
}

# DeepSeek-V4-Pro — same azapi escape hatch as grok-4.3 above.
resource "azapi_resource" "deepseek_deployment" {
  count     = var.enable_deepseek_deployment ? 1 : 0
  type      = "Microsoft.CognitiveServices/accounts/deployments@2024-10-01"
  name      = var.deepseek_deployment_name
  parent_id = azurerm_cognitive_account.this.id

  schema_validation_enabled = false

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = var.deepseek_deployment_capacity
    }
    properties = {
      model = {
        format  = "DeepSeek"
        name    = var.deepseek_model_name
        version = var.deepseek_model_version
      }
    }
  }

  response_export_values = ["*"]

  # See the depends_on note on azurerm_cognitive_deployment.embedding above. This is
  # the pair that actually 409'd in CD (both fired in parallel with no ordering).
  depends_on = [azapi_resource.grok_deployment]
}
