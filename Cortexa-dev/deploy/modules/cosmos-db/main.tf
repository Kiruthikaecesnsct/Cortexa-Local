locals {
  account_name  = substr(lower("${var.project}-${var.environment}-cosmos"), 0, 44)
  database_name = "cortexa-pipeline"

  # All pipeline containers with their partition keys
  containers = {
    documents        = "/batch_id"
    chunks           = "/batch_id"
    provenance_maps  = "/batch_id"
    candidates       = "/batch_id"
    evidence_bundles = "/batch_id"
    verdicts         = "/batch_id"
    reports          = "/batch_id"
    harvesting       = "/batch_id"
    # Seeding REST generate-lattice write path (SeedingRepository) targets this
    # container; without it POST /seeding/generate-lattice 500s. The batch
    # pipeline persists seeding results to `reports` (engine="seeding"), not here.
    seeding = "/batch_id"
    # Saga state store for job-orchestrator (CosmosSagaRepository). Lifecycle
    # endpoints 500 without it — create/start/list/results all target this container.
    batches = "/batch_id"
    # System-wide configuration store (batch-level config, feature flags, scoring
    # thresholds). Uses synthetic partition value "__global__" for uniformity.
    config = "/batch_id"
  }
}

resource "azurerm_cosmosdb_account" "this" {
  name                = local.account_name
  location            = var.location
  resource_group_name = var.resource_group_name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"

  consistency_policy {
    consistency_level       = "Session"
    max_interval_in_seconds = 5
    max_staleness_prefix    = 100
  }

  geo_location {
    location          = var.location
    failover_priority = 0
    zone_redundant    = false
  }

  # Public network access is gated by var.public_network_access_enabled (secure
  # default: false). When enabled, var.allowed_ip_ranges scopes the firewall.
  # Authentication is always via managed identity (no account keys).
  public_network_access_enabled = var.public_network_access_enabled

  # Cosmos IP firewall. The special value "0.0.0.0" is Azure's documented marker
  # for "Accept connections from within public Azure datacenters" — it lets
  # Azure-hosted services with dynamic egress IPs (e.g. Container Apps without
  # VNet integration) reach the account without pinning a rotating IP.
  ip_range_filter = var.allowed_ip_ranges

  tags = var.tags
}

resource "azurerm_cosmosdb_sql_database" "pipeline" {
  name                = local.database_name
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
}

resource "azurerm_cosmosdb_sql_container" "this" {
  for_each = local.containers

  name                = each.key
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.pipeline.name
  partition_key_paths = [each.value]

  autoscale_settings {
    max_throughput = var.throughput
  }
}
