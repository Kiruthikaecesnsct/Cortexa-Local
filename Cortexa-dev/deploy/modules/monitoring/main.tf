resource "azurerm_log_analytics_workspace" "this" {
  name                = "${var.project}-${var.environment}-law"
  location            = var.location
  resource_group_name = var.resource_group_name
  retention_in_days   = var.retention_in_days
  tags                = var.tags
}

resource "azurerm_application_insights" "this" {
  name                = "${var.project}-${var.environment}-ai"
  location            = var.location
  resource_group_name = var.resource_group_name
  application_type    = "web"
  workspace_id        = azurerm_log_analytics_workspace.this.id
  tags                = var.tags
}
