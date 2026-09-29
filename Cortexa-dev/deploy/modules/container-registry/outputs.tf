output "login_server" {
  value       = azurerm_container_registry.this.login_server
  description = "The login server URL for the container registry"
}

output "registry_id" {
  value       = azurerm_container_registry.this.id
  description = "Resource ID of the container registry"
}

output "admin_username" {
  value       = azurerm_container_registry.this.admin_username
  description = "Admin username for the container registry"
  sensitive   = true
}

output "admin_password" {
  value       = azurerm_container_registry.this.admin_password
  description = "Admin password for the container registry"
  sensitive   = true
}
