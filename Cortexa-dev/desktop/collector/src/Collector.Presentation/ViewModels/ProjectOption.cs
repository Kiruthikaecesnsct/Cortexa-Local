namespace Collector.Presentation.ViewModels;

public sealed record ProjectOption(string Label, string? Value)
{
    public override string ToString() => Label;
}
