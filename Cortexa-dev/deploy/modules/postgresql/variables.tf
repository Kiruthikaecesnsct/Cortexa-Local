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

variable "admin_username" {
  type        = string
  default     = "cortexaadmin"
  description = "Administrator username for the PostgreSQL flexible server"
}

variable "admin_password" {
  type        = string
  sensitive   = true
  description = "Administrator password for the PostgreSQL flexible server — store in Key Vault, never in tfvars"
}

variable "sku_name" {
  type        = string
  default     = "B_Standard_B1ms"
  description = "SKU for the flexible server (dev: B_Standard_B1ms, prod: GP_Standard_D2s_v3)"
}

variable "storage_mb" {
  type        = number
  default     = 32768
  description = "Storage in MB for the flexible server (dev: 32768, prod: 131072)"
}

variable "postgres_version" {
  type        = string
  default     = "16"
  description = "PostgreSQL major version"
}

variable "allowed_ip_ranges" {
  type = list(object({
    name     = string
    start_ip = string
    end_ip   = string
  }))
  default     = []
  description = "Firewall rules granting access to the PostgreSQL server (at least one required)"

  validation {
    condition     = length(var.allowed_ip_ranges) > 0
    error_message = "At least one allowed_ip_ranges entry is required so the server is reachable."
  }
}

variable "public_network_access_enabled" {
  type        = bool
  default     = false
  description = "Allow public internet access to the PostgreSQL server. Keep false in prod; set true only when VNet/private-endpoint integration is not yet provisioned."
}

variable "name_suffix" {
  type        = string
  default     = ""
  description = "Optional suffix appended to the server name to avoid Azure name reservations after region moves"
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}
