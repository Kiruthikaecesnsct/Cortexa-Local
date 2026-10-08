using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record AxisScoreRow
{
    [JsonPropertyName("score")]
    public double Score { get; init; }
}
