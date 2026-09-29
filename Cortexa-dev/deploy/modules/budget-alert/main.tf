resource "azurerm_consumption_budget_resource_group" "this" {
  name              = "${var.project}-${var.environment}-budget"
  resource_group_id = var.resource_group_id

  amount     = var.monthly_amount
  time_grain = "Monthly"

  time_period {
    start_date = var.time_period_start
  }

  dynamic "notification" {
    for_each = var.threshold_percentages

    content {
      enabled        = true
      operator       = "GreaterThanOrEqualTo"
      threshold      = notification.value
      threshold_type = "Actual"
      contact_emails = var.contact_emails
      contact_roles  = []
    }
  }
}
