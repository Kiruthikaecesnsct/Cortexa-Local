locals {
  server_name = lower("${var.project}-${var.environment}-pg${var.name_suffix}")
}

resource "azurerm_postgresql_flexible_server" "this" {
  name                   = local.server_name
  resource_group_name    = var.resource_group_name
  location               = var.location
  version                = var.postgres_version
  administrator_login    = var.admin_username
  administrator_password = var.admin_password
  sku_name               = var.sku_name
  storage_mb             = var.storage_mb
  zone                   = "1"

  backup_retention_days         = 7
  geo_redundant_backup_enabled  = false
  public_network_access_enabled = var.public_network_access_enabled

  tags = var.tags
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "this" {
  count = length(var.allowed_ip_ranges)

  name             = var.allowed_ip_ranges[count.index].name
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = var.allowed_ip_ranges[count.index].start_ip
  end_ip_address   = var.allowed_ip_ranges[count.index].end_ip
}

resource "azurerm_postgresql_flexible_server_database" "identity" {
  name      = "identity"
  server_id = azurerm_postgresql_flexible_server.this.id
  collation = "en_US.utf8"
  charset   = "utf8"
}
