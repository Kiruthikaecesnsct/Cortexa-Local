resource "azurerm_key_vault" "this" {
  name                = substr(lower("${var.project}-${var.environment}-kv"), 0, 24)
  location            = var.location
  resource_group_name = var.resource_group_name
  tenant_id           = var.tenant_id
  sku_name            = "standard"

  rbac_authorization_enabled = true
  purge_protection_enabled   = true
  soft_delete_retention_days = 7

  network_acls {
    default_action             = "Deny"
    bypass                     = "AzureServices"
    ip_rules                   = var.allowed_ip_rules
    virtual_network_subnet_ids = var.allowed_subnet_ids
  }

  tags = var.tags

  lifecycle {
    precondition {
      condition     = length(var.allowed_ip_rules) > 0 || length(var.allowed_subnet_ids) > 0
      error_message = "Key Vault network_acls default_action is Deny: supply at least one allowed_ip_rules or allowed_subnet_ids entry so the vault is reachable."
    }
    # CI jobs add/remove runner IPs via az CLI out-of-band; ignoring ip_rules only
    # prevents targeted applies from reverting those ephemeral firewall entries
    # while keeping security-critical attributes (default_action, bypass) tracked.
    ignore_changes = [network_acls[0].ip_rules]
  }
}
