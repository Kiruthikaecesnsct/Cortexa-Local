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

variable "name_suffix" {
  type        = string
  default     = "storage"
  description = "Short suffix appended to the storage account name, e.g. raw or state"
}

variable "containers" {
  type        = list(string)
  default     = []
  description = "Names of blob containers to create inside the storage account"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}
