using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Collector.Application.History;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.Services;

public sealed class ShellFileLauncher(IOptions<HistoryOptions> options) : ILocalFileLauncher
{
    private static readonly string[] ShellExtensions = [".pdf", ".docx"];
    private static readonly string[] RemotePrefixes = [@"\\", "//"];

    public bool TryLaunch(LocalSourceTarget target)
    {
        var startInfo = BuildStartInfo(target, options.Value.TextEditorPath);
        if (startInfo is null)
        {
            return false;
        }

        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    public static ProcessStartInfo? BuildStartInfo(LocalSourceTarget target, string editorPath)
    {
        var path = SafePath(target.Path);
        if (path is null)
        {
            return null;
        }

        return UsesShell(target.SourceKind, path) ? ShellInfo(path) : EditorInfo(editorPath, path);
    }

    private static string? SafePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || IsRemote(raw) || !Path.IsPathFullyQualified(raw))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(raw);
            return !IsRemote(full) && File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsRemote(string path) =>
        RemotePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));

    private static bool UsesShell(SourceKind kind, string path) =>
        kind == SourceKind.Paper
        && ShellExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ProcessStartInfo ShellInfo(string path) => new(path) { UseShellExecute = true };

    private static ProcessStartInfo EditorInfo(string editorPath, string path)
    {
        var info = new ProcessStartInfo(editorPath) { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return info;
    }
}
