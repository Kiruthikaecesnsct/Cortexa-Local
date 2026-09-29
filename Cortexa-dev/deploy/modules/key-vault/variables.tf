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

variable "tenant_id" {
  type        = string
  description = "Azure AD tenant ID"
}

variable "allowed_subnet_ids" {
  type        = list(string)
  default     = []
  description = "Subnet IDs permitted by Key Vault network ACLs"
}

variable "allowed_ip_rules" {
  type        = list(string)
  default     = []
  description = "IP address ranges permitted by Key Vault network ACLs"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}
