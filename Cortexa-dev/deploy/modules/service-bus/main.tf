locals {
  namespace_name = lower("${var.project}-${var.environment}-bus")

  # All pipeline topics — one topic per event type
  topics = [
    "ingestion.requested",
    "ingestion.completed",
    "extraction.requested",
    "extraction.completed",
    "extraction.failed",
    "evidence.requested",
    "evidence.completed",
    "evidence.failed",
    "scoring.requested",
    "scoring.completed",
    "scoring.failed",
    "harvesting.requested",
    "harvesting.failed",
    "seeding.requested",
    "seeding.failed",
    # US115 Deep Seeding: the whole asset is embedded into vector memory before
    # ideation. seeding publishes/consumes asset-embedding.requested; the
    # job-orchestrator saga consumes asset-embedding.completed.
    "asset-embedding.requested",
    "asset-embedding.completed",
    # US116 Deep Seeding: after the asset digest is built, seeding consumes
    # digest.requested and the job-orchestrator saga consumes digest.completed.
    "digest.requested",
    "digest.completed",
    # US117 Deep Seeding: after the digest, seeding builds the patent landscape.
    # seeding consumes landscape.requested and the job-orchestrator saga
    # consumes landscape.completed.
    "landscape.requested",
    "landscape.completed",
    # US119 Deep Seeding grounded ideation loop: seeding publishes ideation.completed
    # (one message per document) which the job-orchestrator saga consumes; the
    # orchestrator then publishes seeding-report.requested which seeding consumes to
    # assemble the final report.
    "ideation.completed",
    "seeding-report.requested",
    "engine.completed",
    # Saga fan-out trigger — job-orchestrator publishes batch.created on POST
    # /batches/start and its SagaEventConsumer listens on the orchestrator
    # subscription below. Missing entity throws MessagingEntityNotFound 404.
    "batch.created",
  ]

  # Explicit subscription map — keys use the form "<topic>__<subscription-name>"
  # for stable, readable resource addresses.
  #
  # Consumers and their required subscription names:
  #   job-orchestrator      → "orchestrator" on every *.completed topic (session-ordered)
  #   ingestion-service     → "ingestion"    on ingestion.requested     (session-ordered)
  #   extraction-service    → "extraction"   on extraction.requested    (session-ordered)
  #   evidence-service      → "evidence"     on evidence.requested      (session-ordered)
  #   scoring-service       → "scoring"      on scoring.requested       (session-ordered)
  #   harvesting-service    → "harvesting"   on harvesting.requested    (session-ordered)
  #   seeding-service       → "seeding"      on seeding.requested       (session-ordered)
  #   seeding-service       → "seeding"      on asset-embedding.requested   (session-ordered)
  #   job-orchestrator      → "orchestrator" on asset-embedding.completed   (session-ordered)
  #   seeding-service       → "seeding"      on digest.requested            (session-ordered)
  #   job-orchestrator      → "orchestrator" on digest.completed            (session-ordered)
  #   seeding-service       → "seeding"      on landscape.requested         (session-ordered)
  #   job-orchestrator      → "orchestrator" on landscape.completed         (session-ordered)
  #   job-orchestrator      → "orchestrator" on ideation.completed          (session-ordered)
  #   seeding-service       → "seeding"      on seeding-report.requested    (session-ordered)
  subscriptions = {
    "ingestion.requested__ingestion"     = { topic = "ingestion.requested", name = "ingestion", requires_session = true }
    "ingestion.completed__orchestrator"  = { topic = "ingestion.completed", name = "orchestrator", requires_session = true }
    "extraction.completed__orchestrator" = { topic = "extraction.completed", name = "orchestrator", requires_session = true }
    "extraction.failed__orchestrator"    = { topic = "extraction.failed", name = "orchestrator", requires_session = true }
    "evidence.completed__orchestrator"   = { topic = "evidence.completed", name = "orchestrator", requires_session = true }
    "evidence.failed__orchestrator"      = { topic = "evidence.failed", name = "orchestrator", requires_session = true }
    "scoring.completed__orchestrator"    = { topic = "scoring.completed", name = "orchestrator", requires_session = true }
    "scoring.failed__orchestrator"       = { topic = "scoring.failed", name = "orchestrator", requires_session = true }
    "harvesting.failed__orchestrator"    = { topic = "harvesting.failed", name = "orchestrator", requires_session = true }
    "seeding.failed__orchestrator"       = { topic = "seeding.failed", name = "orchestrator", requires_session = true }
    "engine.completed__orchestrator"     = { topic = "engine.completed", name = "orchestrator", requires_session = true }
    "extraction.requested__extraction"   = { topic = "extraction.requested", name = "extraction", requires_session = true }
    "evidence.requested__evidence"       = { topic = "evidence.requested", name = "evidence", requires_session = true }
    "scoring.requested__scoring"         = { topic = "scoring.requested", name = "scoring", requires_session = true }
    "harvesting.requested__harvesting"   = { topic = "harvesting.requested", name = "harvesting", requires_session = true }
    "seeding.requested__seeding"         = { topic = "seeding.requested", name = "seeding", requires_session = true }
    # US115 Deep Seeding embedding fan-out — seeding consumes the request, the
    # orchestrator saga consumes the completion (session-ordered like the other
    # *.completed orchestrator subscriptions).
    "asset-embedding.requested__seeding"      = { topic = "asset-embedding.requested", name = "seeding", requires_session = true }
    "asset-embedding.completed__orchestrator" = { topic = "asset-embedding.completed", name = "orchestrator", requires_session = true }
    # US116 Deep Seeding digest fan-out — seeding consumes the request, the
    # orchestrator saga consumes the completion (session-ordered like the other
    # *.completed orchestrator subscriptions).
    "digest.requested__seeding"      = { topic = "digest.requested", name = "seeding", requires_session = true }
    "digest.completed__orchestrator" = { topic = "digest.completed", name = "orchestrator", requires_session = true }
    # US117 Deep Seeding landscape fan-out — seeding consumes the request, the
    # orchestrator saga consumes the completion (session-ordered like the other
    # *.completed orchestrator subscriptions).
    "landscape.requested__seeding"      = { topic = "landscape.requested", name = "seeding", requires_session = true }
    "landscape.completed__orchestrator" = { topic = "landscape.completed", name = "orchestrator", requires_session = true }
    # US119 Deep Seeding grounded ideation loop — seeding publishes ideation.completed
    # (consumed by the orchestrator saga), and the orchestrator publishes
    # seeding-report.requested (consumed by seeding to assemble the report). Both are
    # session-ordered on batch_id like the rest of the pipeline.
    "ideation.completed__orchestrator"  = { topic = "ideation.completed", name = "orchestrator", requires_session = true }
    "seeding-report.requested__seeding" = { topic = "seeding-report.requested", name = "seeding", requires_session = true }
    # batch.created caps redelivery at 5 — a poisoned saga-fanout message is dead-lettered
    # sooner instead of looping. All other subscriptions inherit the default of 10.
    "batch.created__orchestrator" = { topic = "batch.created", name = "orchestrator", requires_session = true, max_delivery_count = 5 }
  }
}

resource "azurerm_servicebus_namespace" "this" {
  name                = local.namespace_name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = var.sku

  tags = var.tags
}

resource "azurerm_servicebus_topic" "this" {
  for_each = toset(local.topics)

  name         = replace(each.key, ".", "-")
  namespace_id = azurerm_servicebus_namespace.this.id
}

resource "azurerm_servicebus_subscription" "subscriptions" {
  for_each = local.subscriptions

  name               = each.value.name
  topic_id           = azurerm_servicebus_topic.this[each.value.topic].id
  max_delivery_count = try(each.value.max_delivery_count, 10)
  requires_session   = each.value.requires_session

  dead_lettering_on_message_expiration      = false
  dead_lettering_on_filter_evaluation_error = false
}

# Scoped authorization rule — Send+Listen only; no Manage rights
# Services must use this connection string, not the root namespace key
resource "azurerm_servicebus_namespace_authorization_rule" "services" {
  name         = "services"
  namespace_id = azurerm_servicebus_namespace.this.id

  listen = true
  send   = true
  manage = false
}

locals {
  # One dead-letter alert per *-requested consumer subscription. Every pipeline stage
  # can dead-letter, so all seven are covered — a subset would leave a stage's failures
  # unmonitored (seeding was the gap that let a seeding hard-fail go unnoticed).
  dlq_alert_topics = {
    ingestion       = "ingestion-requested/Subscriptions/ingestion"
    extraction      = "extraction-requested/Subscriptions/extraction"
    evidence        = "evidence-requested/Subscriptions/evidence"
    scoring         = "scoring-requested/Subscriptions/scoring"
    harvesting      = "harvesting-requested/Subscriptions/harvesting"
    seeding         = "seeding-requested/Subscriptions/seeding"
    asset-embedding = "asset-embedding-requested/Subscriptions/seeding"
    digest          = "digest-requested/Subscriptions/seeding"
    landscape       = "landscape-requested/Subscriptions/seeding"
    # US119 grounded ideation loop — the orchestrator dispatches seeding-report.requested
    # and seeding consumes it to assemble the report; a report-assembly failure dead-letters
    # here. ideation.completed rides the orchestrator subscription (a *.completed leg, not a
    # *-requested consumer), so it inherits the standing orchestrator DLQ monitoring.
    seeding-report = "seeding-report-requested/Subscriptions/seeding"
  }
}

resource "azurerm_monitor_action_group" "dlq_alerts" {
  count = var.dlq_alert_enabled && length(var.dlq_alert_email_receivers) > 0 ? 1 : 0

  name                = "${local.namespace_name}-dlq-ag"
  resource_group_name = var.resource_group_name
  short_name          = "dlq-alerts"

  dynamic "email_receiver" {
    for_each = { for i, v in var.dlq_alert_email_receivers : tostring(i) => v }
    content {
      name          = "receiver-${email_receiver.key}"
      email_address = email_receiver.value
    }
  }

  tags = var.tags
}

resource "azurerm_monitor_metric_alert" "dlq" {
  for_each = var.dlq_alert_enabled ? local.dlq_alert_topics : {}

  name                = "${local.namespace_name}-${each.key}-dlq"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_servicebus_namespace.this.id]
  description         = "Dead-letter messages exceeded threshold on topic ${each.value}"
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"
  auto_mitigate       = true

  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "DeadletteredMessages"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = var.dlq_alert_threshold

    dimension {
      name     = "EntityName"
      operator = "Include"
      values   = [each.value]
    }
  }

  dynamic "action" {
    for_each = length(azurerm_monitor_action_group.dlq_alerts) > 0 ? [1] : []
    content {
      action_group_id = azurerm_monitor_action_group.dlq_alerts[0].id
    }
  }

  tags = var.tags
}
