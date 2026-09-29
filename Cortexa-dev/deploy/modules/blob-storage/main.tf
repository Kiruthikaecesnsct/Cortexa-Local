resource "azurerm_storage_account" "this" {
  name                       = substr(lower(replace("${var.project}${var.environment}${var.name_suffix}", "-", "")), 0, 24)
  location                   = var.location
  resource_group_name        = var.resource_group_name
  account_tier               = "Standard"
  account_replication_type   = "LRS"
  min_tls_version            = "TLS1_2"
  https_traffic_only_enabled = true

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 7
    }

    container_delete_retention_policy {
      days = 7
    }
  }

  tags = var.tags
}

resource "azurerm_storage_container" "this" {
  count                 = length(var.containers)
  name                  = var.containers[count.index]
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}
