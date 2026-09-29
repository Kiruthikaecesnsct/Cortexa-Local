output "environment_id" {
  value       = azurerm_container_app_environment.env.id
  description = "Resource ID of the Container Apps environment"
}

output "environment_default_domain" {
  value       = azurerm_container_app_environment.env.default_domain
  description = "Default domain of the Container Apps environment"
}

output "identity_id" {
  value       = azurerm_user_assigned_identity.ca_identity.id
  description = "Resource ID of the user-assigned managed identity"
}

output "identity_principal_id" {
  value       = azurerm_user_assigned_identity.ca_identity.principal_id
  description = "Principal ID of the user-assigned managed identity"
}

output "identity_client_id" {
  value       = azurerm_user_assigned_identity.ca_identity.client_id
  description = "Client ID of the user-assigned managed identity"
}

output "orchestrator_identity_id" {
  value       = azurerm_user_assigned_identity.orchestrator_identity.id
  description = "Resource ID of the job-orchestrator dedicated managed identity"
}

output "orchestrator_identity_principal_id" {
  value       = azurerm_user_assigned_identity.orchestrator_identity.principal_id
  description = "Principal ID of the job-orchestrator dedicated managed identity"
}

output "orchestrator_identity_client_id" {
  value       = azurerm_user_assigned_identity.orchestrator_identity.client_id
  description = "Client ID of the job-orchestrator dedicated managed identity"
}

output "acr_login_server" {
  value       = var.acr_login_server
  description = "Login server hostname of the Azure Container Registry"
}

output "container_app_names" {
  value       = { for k, v in azurerm_container_app.service : k => v.name }
  description = "Map of service name to its Container App resource name"
}

output "container_app_fqdns" {
  value       = { for k, v in azurerm_container_app.service : k => v.latest_revision_fqdn }
  description = "Map of service name to its Container App FQDN — used by CD for health checks"
}

output "container_app_ids" {
  value       = { for k, v in azurerm_container_app.service : k => v.id }
  description = "Map of service name to its Container App ARM resource ID"
}
