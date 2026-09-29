output "search_service_id" {
  value       = azurerm_search_service.search.id
  description = "Resource ID of the Azure AI Search service"
}

output "endpoint" {
  value       = "https://${azurerm_search_service.search.name}.search.windows.net"
  description = "Endpoint URL of the Azure AI Search service"
}

output "primary_key" {
  value       = azurerm_search_service.search.primary_key
  sensitive   = true
  description = "Primary admin key for the Azure AI Search service — use only for index management, not for service queries"
}

output "query_key" {
  value       = azurerm_search_service.search.query_keys[0].key
  sensitive   = true
  description = "Read-only query key for the Azure AI Search service — use this for all service consumers"
}
