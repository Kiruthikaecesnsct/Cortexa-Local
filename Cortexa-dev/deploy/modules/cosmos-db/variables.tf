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

variable "throughput" {
  type        = number
  default     = 400
  description = "Autoscale max throughput RU/s for each container (dev: 400, prod: 4000)"

  validation {
    condition     = var.throughput >= 400 && var.throughput <= 100000 && var.throughput % 100 == 0
    error_message = "throughput must be between 400 and 100000 and a multiple of 100."
  }
}

variable "public_network_access_enabled" {
  type        = bool
  default     = false
  description = "Whether the Cosmos account accepts public network traffic. Secure default false; dev overrides to true until VNet/private-endpoint integration exists."
}

variable "allowed_ip_ranges" {
  type        = set(string)
  default     = []
  description = "Cosmos IP firewall entries (CIDRs or IPs). The special value \"0.0.0.0\" accepts connections from within public Azure datacenters. Only applied when public_network_access_enabled is true."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Tags to apply to all resources"
}
