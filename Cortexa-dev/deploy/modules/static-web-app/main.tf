resource "azurerm_static_web_app" "swa" {
  name                = "${var.project}-${var.environment}-swa"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku_tier            = var.sku_tier
  sku_size            = var.sku_size
  tags                = var.tags
}

# Links a Container App (or other ARM resource) as the SWA backend so /api/* requests
# are proxied server-side. Requires Standard SKU; no-op when linked_backend_resource_id is empty.
resource "azapi_resource" "linked_backend" {
  count     = var.linked_backend_resource_id != "" ? 1 : 0
  type      = "Microsoft.Web/staticSites/linkedBackends@2023-01-01"
  name      = "api-gateway"
  parent_id = azurerm_static_web_app.swa.id

  body = {
    properties = {
      backendResourceId = var.linked_backend_resource_id
      region            = var.linked_backend_region
    }
  }

  lifecycle {
    precondition {
      condition     = var.linked_backend_region != ""
      error_message = "linked_backend_region must be provided when linked_backend_resource_id is set."
    }
  }
}
