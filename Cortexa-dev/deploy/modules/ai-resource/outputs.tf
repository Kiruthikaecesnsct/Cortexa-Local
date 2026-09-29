output "account_id" {
  value       = azurerm_cognitive_account.this.id
  description = "Resource ID of the consolidated AI Services account"
}

output "openai_endpoint" {
  value       = "https://${azurerm_cognitive_account.this.name}.openai.azure.com"
  description = "Azure OpenAI endpoint for GPT and embedding models — used by model-router for OpenAI SDK clients"
}

output "cognitive_endpoint" {
  value       = azurerm_cognitive_account.this.endpoint
  description = "Cognitive Services endpoint (Azure-managed format) — alternative base URI for Azure SDK clients"
}

output "project_endpoint" {
  value       = "https://${azurerm_cognitive_account.this.name}.services.ai.azure.com/api/projects/${var.project}-${var.environment}-ai"
  description = "AI Foundry project endpoint — used for multi-service AI orchestration"
}

output "primary_key" {
  value       = azurerm_cognitive_account.this.primary_access_key
  sensitive   = true
  description = "Primary API key — dev only (key auth disabled in prod). Sensitive but kept for state parity; actual KV secrets sourced from var not this output."
}

output "gpt_deployment_names" {
  value       = keys(azurerm_cognitive_deployment.gpt)
  description = "List of GPT deployment names (used as deployment IDs in API calls). Returns map keys, e.g. [\"gpt-5.5\", \"gpt-5.4\"]."
}

output "embedding_endpoint" {
  value       = var.enable_embedding_deployment ? "https://${azurerm_cognitive_account.this.name}.openai.azure.com" : ""
  description = "Embedding endpoint (same as openai_endpoint when deployment enabled; empty string when disabled). Used by services for text-embedding-3-large."
}

output "embedding_deployment_name" {
  value       = var.enable_embedding_deployment ? var.embedding_deployment_name : ""
  description = "Embedding deployment name (used as the deployment ID in API calls); empty string when disabled."
}

output "claude_endpoint" {
  value       = "https://${azurerm_cognitive_account.this.name}.services.ai.azure.com/anthropic"
  description = "Claude endpoint — Azure AI Foundry Anthropic passthrough base (append /v1/messages to get full invocation endpoint)"
}

output "grok_deployment_name" {
  value       = var.enable_grok_deployment ? var.grok_deployment_name : ""
  description = "grok-4.3 deployment name (used as the deployment ID in API calls); empty string when disabled."
}

output "deepseek_deployment_name" {
  value       = var.enable_deepseek_deployment ? var.deepseek_deployment_name : ""
  description = "DeepSeek-V4-Pro deployment name (used as the deployment ID in API calls); empty string when disabled."
}
