# moved blocks preserve the 5 live orchestrator subscriptions on *.completed
# topics during the refactor from the old for_each resources to the explicit
# subscriptions map. Without these, Terraform would destroy and recreate them,
# dropping any in-flight saga messages.

moved {
  from = azurerm_servicebus_subscription.session["ingestion.completed"]
  to   = azurerm_servicebus_subscription.subscriptions["ingestion.completed__orchestrator"]
}

moved {
  from = azurerm_servicebus_subscription.session["extraction.completed"]
  to   = azurerm_servicebus_subscription.subscriptions["extraction.completed__orchestrator"]
}

moved {
  from = azurerm_servicebus_subscription.session["evidence.completed"]
  to   = azurerm_servicebus_subscription.subscriptions["evidence.completed__orchestrator"]
}

moved {
  from = azurerm_servicebus_subscription.session["scoring.completed"]
  to   = azurerm_servicebus_subscription.subscriptions["scoring.completed__orchestrator"]
}

moved {
  from = azurerm_servicebus_subscription.session["engine.completed"]
  to   = azurerm_servicebus_subscription.subscriptions["engine.completed__orchestrator"]
}
