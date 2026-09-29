output "namespace_id" {
  value       = azurerm_servicebus_namespace.this.id
  description = "Resource ID of the Service Bus namespace"
}

output "namespace_name" {
  value       = azurerm_servicebus_namespace.this.name
  description = "Name of the Service Bus namespace"
}

output "primary_connection_string" {
  value       = azurerm_servicebus_namespace_authorization_rule.services.primary_connection_string
  sensitive   = true
  description = "Send+Listen-only connection string for services (no Manage rights)"
}

output "topic_ids" {
  value       = { for k, v in azurerm_servicebus_topic.this : k => v.id }
  description = "Map of topic name to resource ID"
}

output "dlq_action_group_id" {
  value       = length(azurerm_monitor_action_group.dlq_alerts) > 0 ? azurerm_monitor_action_group.dlq_alerts[0].id : null
  description = "Resource ID of the DLQ monitor action group; null when no email receivers are configured"
}

output "dlq_alert_ids" {
  value       = { for k, v in azurerm_monitor_metric_alert.dlq : k => v.id }
  description = "Map of alert key (evidence, scoring, extraction) to metric alert resource ID"
}
