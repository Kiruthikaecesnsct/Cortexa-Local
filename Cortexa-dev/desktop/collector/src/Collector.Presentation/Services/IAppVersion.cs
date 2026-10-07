using System.Reflection;

namespace Collector.Presentation.Services;

public interface IAppVersion
{
    string Current { get; }
}

public sealed class AssemblyAppVersion : IAppVersion
{
    private const char BuildMetadataSeparator = '+';
    private const string FallbackVersion = "0.0.0";

    public string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(AssemblyAppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString();
        if (string.IsNullOrWhiteSpace(version))
        {
            return FallbackVersion;
        }

        var separator = version.IndexOf(BuildMetadataSeparator);
        return separator > 0 ? version[..separator] : version;
    }
}
