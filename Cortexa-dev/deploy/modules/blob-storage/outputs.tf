output "account_name" {
  value       = azurerm_storage_account.this.name
  description = "Name of the storage account"
}

output "account_id" {
  value       = azurerm_storage_account.this.id
  description = "Resource ID of the storage account"
}

output "primary_connection_string" {
  value       = azurerm_storage_account.this.primary_connection_string
  description = "Primary connection string for the storage account"
  sensitive   = true
}

output "primary_blob_endpoint" {
  value       = azurerm_storage_account.this.primary_blob_endpoint
  description = "Primary blob service endpoint URL"
}

output "container_names" {
  value       = [for c in azurerm_storage_container.this : c.name]
  description = "Names of the created blob containers"
}
