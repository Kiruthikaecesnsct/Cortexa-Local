output "account_id" {
  value       = azurerm_cosmosdb_account.this.id
  description = "Resource ID of the Cosmos DB account"
}

output "endpoint" {
  value       = azurerm_cosmosdb_account.this.endpoint
  description = "Cosmos DB account endpoint URI"
}

output "primary_connection_string" {
  value       = azurerm_cosmosdb_account.this.primary_sql_connection_string
  sensitive   = true
  description = "Primary SQL API connection string"
}

output "database_name" {
  value       = azurerm_cosmosdb_sql_database.pipeline.name
  description = "Name of the pipeline SQL database"
}

output "account_name" {
  value       = azurerm_cosmosdb_account.this.name
  description = "Name of the Cosmos DB account"
}
