variable "project" {
  type        = string
  description = "Project short name"
}

variable "environment" {
  type        = string
  description = "Environment name"
}

variable "resource_group_name" {
  type        = string
  description = "Name of the resource group (used for resource naming only)"
}

variable "resource_group_id" {
  type        = string
  description = "Full resource ID of the resource group to scope the budget to"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}

variable "monthly_amount" {
  type        = number
  description = "Monthly budget cap in USD"
}

variable "time_period_start" {
  type        = string
  description = "RFC3339 date when the budget period begins — must be the first day of a month (e.g. 2026-06-01T00:00:00Z)"

  validation {
    condition     = can(regex("^\\d{4}-\\d{2}-01T00:00:00Z$", var.time_period_start))
    error_message = "time_period_start must be the first day of a month in RFC3339 format, e.g. 2026-06-01T00:00:00Z."
  }
}

variable "contact_emails" {
  type        = list(string)
  description = "Email addresses that receive budget alert notifications"

  validation {
    condition     = length(var.contact_emails) > 0
    error_message = "At least one contact email is required."
  }
}

variable "threshold_percentages" {
  type        = list(number)
  default     = [80, 100]
  description = "Percentage thresholds of monthly_amount that trigger notifications"
}
