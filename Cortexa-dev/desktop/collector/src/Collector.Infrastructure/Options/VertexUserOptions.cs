namespace Collector.Infrastructure.Options;

public sealed class VertexUserOptions
{
    public const string SectionName = "Ai:VertexUser";

    public string ProjectId { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;
}
