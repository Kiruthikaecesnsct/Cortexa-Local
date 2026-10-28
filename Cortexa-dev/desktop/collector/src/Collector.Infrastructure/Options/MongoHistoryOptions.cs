namespace Collector.Infrastructure.Options;

public sealed class MongoHistoryOptions
{
    public const string SectionName = "History:Mongo";

    public string Database { get; set; } = "cortexa_collector";

    public string Collection { get; set; } = "processed_documents";

    public int ServerSelectionTimeoutSeconds { get; set; } = 5;

    public int OperationTimeoutSeconds { get; set; } = 10;
}
