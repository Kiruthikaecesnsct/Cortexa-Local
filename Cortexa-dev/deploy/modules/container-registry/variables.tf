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
  description = "ACR SKU: Basic, Standard, or Premium"
}

variable "admin_enabled" {
  type        = bool
  default     = false
  description = "Enable ACR admin user — disabled by default; enable only when RBAC-based pull is unavailable"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}
