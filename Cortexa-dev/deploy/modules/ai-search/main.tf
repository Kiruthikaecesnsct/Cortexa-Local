resource "azurerm_search_service" "search" {
  name                         = "${var.project}-${var.environment}-search"
  location                     = var.location
  resource_group_name          = var.resource_group_name
  sku                          = var.sku
  replica_count                = var.replica_count
  partition_count              = var.partition_count
  local_authentication_enabled = var.local_authentication_enabled
  tags                         = var.tags
}
