variable "project" {
  type        = string
  description = "Project short name, e.g. cortexa"
}

variable "environment" {
  type        = string
  description = "Environment name, e.g. dev or prod"
}

variable "location" {
  type        = string
  description = "Azure region"
}

variable "resource_group_name" {
  type        = string
  description = "Name of the resource group to deploy into"
}

variable "sku" {
  type        = string
  default     = "Standard"
  description = "Service Bus namespace SKU (dev: Standard, prod: Premium)"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.sku)
    error_message = "sku must be one of: Basic, Standard, Premium."
  }
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}

variable "dlq_alert_enabled" {
  type        = bool
  default     = true
  description = "Create Azure Monitor metric alerts for dead-letter queue depth on the three dispatch topics"
}

variable "dlq_alert_threshold" {
  type        = number
  default     = 10
  description = "Dead-letter message count above which the alert fires (GreaterThan operator)"
}

variable "dlq_alert_email_receivers" {
  type        = list(string)
  default     = []
  description = "Email addresses that receive DLQ alert notifications; action group is omitted when empty"
}
