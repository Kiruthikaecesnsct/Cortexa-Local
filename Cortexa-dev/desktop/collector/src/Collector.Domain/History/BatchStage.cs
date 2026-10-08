namespace Collector.Domain.History;

public enum BatchStage
{
    Ingested,
    Extracted,
    Scored,
    Harvested,
    Seeded,
}
