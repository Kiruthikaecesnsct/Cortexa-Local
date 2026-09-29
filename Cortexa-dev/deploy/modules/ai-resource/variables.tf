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
  description = "Azure region for the AI resource"
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

variable "gpt_deployments" {
  type = map(object({
    model    = string
    version  = string
    capacity = number
  }))
  default = {
    "gpt-5.5" = {
      model    = "gpt-5.5"
      version  = "2026-04-24"
      capacity = 5000
    }
    "gpt-5.4" = {
      model    = "gpt-5.4"
      version  = "2026-03-05"
      capacity = 15000
    }
  }
  description = "Map of GPT deployments keyed by deployment name. Each entry specifies the model, version, and quota capacity in thousands of tokens per minute (TPM). Deployment resource names match the map keys so live imports align. Capacity lives per-entry so distinct live quotas (gpt-5.5=5000, gpt-5.4=15000) survive terraform apply without drifting back to a shared default."
}

variable "gpt_deployment_sku_name" {
  type        = string
  default     = "GlobalStandard"
  description = "SKU name for GPT deployments: Standard or GlobalStandard"
}

variable "enable_embedding_deployment" {
  type        = bool
  default     = true
  description = "Deploy the text-embedding-3-large model. Set false if quota or availability blocks the first apply — services fall back gracefully."
}

variable "embedding_deployment_name" {
  type        = string
  default     = "text-embedding-3-large"
  description = "Name for the embedding deployment resource"
}

variable "embedding_model_name" {
  type        = string
  default     = "text-embedding-3-large"
  description = "Azure OpenAI embedding model name"
}

variable "embedding_model_version" {
  type        = string
  default     = "1"
  description = "Version of the embedding model to deploy"
}

variable "embedding_deployment_sku_name" {
  type        = string
  default     = "GlobalStandard"
  description = "SKU name for the embedding deployment. The Standard tier's regional quota ceiling was saturated (350/350 TPM, verified via az cognitiveservices usage list) causing 429 throttling (BUG104). GlobalStandard for the same model has confirmed headroom (0/15000 TPM)."
}

variable "embedding_deployment_capacity" {
  type        = number
  default     = 5000
  description = "Quota capacity in thousands of tokens per minute (TPM) for embedding. 5000 = 5000K TPM. Sits within the verified 15000K GlobalStandard headroom for this model and matches the order of magnitude of the gpt-5.5 deployment."
}

variable "enable_claude_deployment" {
  type        = bool
  default     = false
  description = "Deploy Claude models. Requires Anthropic terms acceptance in Azure portal first. Default false — config-ready but disabled until quota granted."
}

variable "claude_deployment_name" {
  type        = string
  default     = "claude-opus-4-8"
  description = "Name for the Claude deployment resource"
}

variable "claude_model_name" {
  type        = string
  default     = "claude-opus-4-8"
  description = "Anthropic Claude model name available in Azure AI Foundry"
}

variable "claude_model_version" {
  type        = string
  default     = "1"
  description = "Version of the Claude model to deploy"
}

variable "claude_deployment_capacity" {
  type        = number
  default     = 10
  description = "Claude deployment capacity in thousands of tokens per minute (TPM). 10 = 10K TPM."
}

variable "model_provider_industry" {
  type        = string
  default     = "Technology"
  description = "Industry for Anthropic model provider data (required by Azure; passed via azapi ARM call)"
}

variable "model_provider_org_name" {
  type        = string
  description = "Organisation name for Anthropic model provider data (required by Azure)"
}

variable "model_provider_country_code" {
  type        = string
  default     = "IN"
  description = "ISO 3166-1 alpha-2 country code for Anthropic model provider data"
}

variable "enable_grok_deployment" {
  type        = bool
  default     = true
  description = "Deploy the grok-4.3 model. Already live in the dev AI Foundry portal — default true so Terraform adopts it without disruption."
}

variable "grok_deployment_name" {
  type        = string
  default     = "grok-4.3"
  description = "Name for the grok-4.3 deployment resource"
}

variable "grok_model_name" {
  type        = string
  default     = "grok-4.3"
  description = "xAI grok model name available in Azure AI Foundry"
}

variable "grok_model_version" {
  type        = string
  default     = "1"
  description = "Version of the grok model to deploy"
}

variable "grok_deployment_capacity" {
  type        = number
  default     = 500
  description = "grok-4.3 deployment capacity in thousands of tokens per minute (TPM). 500 = 500K TPM, matching live dev quota."
}

variable "enable_deepseek_deployment" {
  type        = bool
  default     = true
  description = "Deploy the DeepSeek-V4-Pro model. Already live in the dev AI Foundry portal — default true so Terraform adopts it without disruption."
}

variable "deepseek_deployment_name" {
  type        = string
  default     = "DeepSeek-V4-Pro"
  description = "Name for the DeepSeek-V4-Pro deployment resource"
}

variable "deepseek_model_name" {
  type        = string
  default     = "DeepSeek-V4-Pro"
  description = "DeepSeek model name available in Azure AI Foundry"
}

variable "deepseek_model_version" {
  type        = string
  default     = "2026-04-23"
  description = "Version of the DeepSeek model to deploy"
}

variable "deepseek_deployment_capacity" {
  type        = number
  default     = 500
  description = "DeepSeek-V4-Pro deployment capacity in thousands of tokens per minute (TPM). 500 = 500K TPM, matching live dev quota."
}

variable "public_network_access_enabled" {
  type        = bool
  default     = true
  description = "Allow public network access to the Cognitive Services account"
}

variable "local_authentication_enabled" {
  type        = bool
  default     = false
  description = "Enable API key authentication. Dev: set true; prod must remain false (managed identity only)."
}
