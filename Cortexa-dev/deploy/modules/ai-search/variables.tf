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

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}

variable "sku" {
  type        = string
  default     = "basic"
  description = "SKU tier for the Azure AI Search service, e.g. basic or standard"
}

variable "replica_count" {
  type        = number
  default     = 1
  description = "Number of replicas for the search service"
}

variable "partition_count" {
  type        = number
  default     = 1
  description = "Number of partitions for the search service"
}

variable "local_authentication_enabled" {
  type        = bool
  description = "Enable local API key authentication. Set false to enforce managed identity only."
  default     = false
}
