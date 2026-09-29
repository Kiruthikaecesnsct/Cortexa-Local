output "vault_uri" {
  value       = module.key_vault.vault_uri
  description = "URI of the Key Vault"
}

output "login_server" {
  value       = module.container_registry.login_server
  description = "ACR login server URL"
}

output "law_id" {
  value       = module.monitoring.law_id
  description = "Resource ID of the Log Analytics workspace"
}

output "storage_account_name" {
  value       = module.blob_storage.account_name
  description = "Name of the blob storage account"
}

output "storage_primary_connection_string" {
  value       = module.blob_storage.primary_connection_string
  sensitive   = true
  description = "Primary connection string for the blob storage account"
}

output "cosmos_endpoint" {
  value       = module.cosmos_db.endpoint
  description = "Cosmos DB account endpoint URI"
}

output "cosmos_primary_connection_string" {
  value       = module.cosmos_db.primary_connection_string
  sensitive   = true
  description = "Primary SQL API connection string for Cosmos DB"
}

output "cosmos_database_name" {
  value       = module.cosmos_db.database_name
  description = "Name of the Cosmos pipeline database"
}

output "postgresql_fqdn" {
  value       = module.postgresql.fqdn
  description = "Fully-qualified domain name of the PostgreSQL server"
}

output "postgresql_connection_string" {
  value       = module.postgresql.connection_string
  sensitive   = true
  description = "ADO.NET connection string for the identity PostgreSQL database"
}

output "service_bus_namespace_name" {
  value       = module.service_bus.namespace_name
  description = "Name of the Service Bus namespace"
}

output "service_bus_primary_connection_string" {
  value       = module.service_bus.primary_connection_string
  sensitive   = true
  description = "Primary connection string for the Service Bus namespace"
}

output "container_apps_environment_id" {
  value       = module.container_apps.environment_id
  description = "Resource ID of the Container Apps environment"
}

output "container_apps_environment_default_domain" {
  value       = module.container_apps.environment_default_domain
  description = "Default domain of the Container Apps environment"
}

output "container_apps_identity_id" {
  value       = module.container_apps.identity_id
  description = "Resource ID of the Container Apps user-assigned managed identity"
}

output "container_apps_identity_client_id" {
  value       = module.container_apps.identity_client_id
  description = "Client ID of the Container Apps user-assigned managed identity"
}

output "static_web_app_default_host_name" {
  value       = module.static_web_app.default_host_name
  description = "Default hostname of the Static Web App"
}

output "static_web_app_api_key" {
  value       = module.static_web_app.api_key
  sensitive   = true
  description = "Deployment API key for the Static Web App"
}

output "ai_search_endpoint" {
  value       = module.ai_search.endpoint
  description = "Endpoint URL of the Azure AI Search service"
}

output "ai_search_primary_key" {
  value       = module.ai_search.primary_key
  sensitive   = true
  description = "Primary admin key for the Azure AI Search service"
}

output "ai_resource_openai_endpoint" {
  value       = module.ai_resource.openai_endpoint
  description = "Azure OpenAI endpoint of the consolidated AI resource — used for GPT and embedding"
}

output "ai_resource_project_endpoint" {
  value       = module.ai_resource.project_endpoint
  description = "AI Foundry project endpoint of the consolidated AI resource"
}

output "ai_resource_gpt_deployment_names" {
  value       = module.ai_resource.gpt_deployment_names
  description = "List of GPT deployment names (used as deployment IDs in model-router config)"
}

output "ai_resource_embedding_endpoint" {
  value       = module.ai_resource.embedding_endpoint
  description = "Embedding endpoint (null when deployment disabled)"
}

output "ai_resource_claude_endpoint" {
  value       = module.ai_resource.claude_endpoint
  description = "Claude endpoint — Azure AI Foundry Anthropic passthrough base"
}

output "key_vault_secret_names" {
  value       = keys(azurerm_key_vault_secret.platform)
  description = "Names of secrets written to Key Vault — services resolve these at runtime"
}

output "budget_alert_id" {
  value       = length(module.budget_alert) > 0 ? module.budget_alert[0].budget_id : null
  description = "Resource ID of the dev subscription budget alert (null when enable_budget=false)"
}
