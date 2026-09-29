output "vault_id" {
  value       = azurerm_key_vault.this.id
  description = "Resource ID of the Key Vault"
}

output "vault_uri" {
  value       = azurerm_key_vault.this.vault_uri
  description = "URI of the Key Vault"
}

output "vault_name" {
  value       = azurerm_key_vault.this.name
  description = "Name of the Key Vault"
}
