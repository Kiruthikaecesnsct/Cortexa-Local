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
  description = "Azure region for the Static Web App (must be a region that supports SWA, e.g. eastus2)"
}

variable "resource_group_name" {
  type        = string
  description = "Name of the resource group to deploy into"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}

variable "sku_tier" {
  type        = string
  default     = "Free"
  description = "SKU tier for the Static Web App: Free (dev) or Standard (prod)"
}

variable "sku_size" {
  type        = string
  default     = "Free"
  description = "SKU size for the Static Web App, must match sku_tier"
}

variable "linked_backend_resource_id" {
  type        = string
  default     = ""
  description = "ARM resource ID of the backend to link (e.g. a Container App). Empty string disables linking (required for Free SKU)."
}

variable "linked_backend_region" {
  type        = string
  default     = ""
  description = "Azure region of the linked backend resource. Required when linked_backend_resource_id is set."
}
