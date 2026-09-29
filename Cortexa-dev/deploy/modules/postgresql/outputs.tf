output "server_id" {
  value       = azurerm_postgresql_flexible_server.this.id
  description = "Resource ID of the PostgreSQL flexible server"
}

output "server_name" {
  value       = azurerm_postgresql_flexible_server.this.name
  description = "Name of the PostgreSQL flexible server"
}

output "fqdn" {
  value       = azurerm_postgresql_flexible_server.this.fqdn
  description = "Fully-qualified domain name of the PostgreSQL server"
}

output "database_name" {
  value       = azurerm_postgresql_flexible_server_database.identity.name
  description = "Name of the identity database"
}

output "connection_string" {
  value       = "Host=${azurerm_postgresql_flexible_server.this.fqdn};Database=identity;Username=${var.admin_username};Password=${var.admin_password};Ssl Mode=Require;"
  sensitive   = true
  description = "ADO.NET-style connection string for the identity database"
}
