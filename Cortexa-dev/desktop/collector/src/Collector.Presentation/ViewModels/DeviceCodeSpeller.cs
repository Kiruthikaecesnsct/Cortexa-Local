namespace Collector.Presentation.ViewModels;

public static class DeviceCodeSpeller
{
    public static string Spell(string code) =>
        string.Join(", dash, ", code.Split('-').Select(part => string.Join(' ', part.AsEnumerable())));
}
