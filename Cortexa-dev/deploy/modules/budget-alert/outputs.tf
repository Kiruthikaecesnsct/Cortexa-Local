output "budget_id" {
  value       = azurerm_consumption_budget_resource_group.this.id
  description = "Resource ID of the consumption budget"
}

output "budget_name" {
  value       = azurerm_consumption_budget_resource_group.this.name
  description = "Name of the consumption budget"
}
