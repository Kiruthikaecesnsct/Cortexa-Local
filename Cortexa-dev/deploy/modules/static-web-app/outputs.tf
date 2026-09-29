output "default_host_name" {
  value       = azurerm_static_web_app.swa.default_host_name
  description = "Default hostname of the Static Web App"
}

output "api_key" {
  value       = azurerm_static_web_app.swa.api_key
  description = "Deployment API key for the Static Web App"
  sensitive   = true
}

output "id" {
  value       = azurerm_static_web_app.swa.id
  description = "Resource ID of the Static Web App"
}

output "linked_backend_id" {
  value       = try(azapi_resource.linked_backend[0].id, null)
  description = "ARM resource ID of the linked backend, or null when no backend is linked"
}
