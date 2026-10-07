using System.Text;

namespace Collector.Application.Extraction;

public enum FileGuardOutcome
{
    Accepted,
    TooLarge,
    BinaryContent,
}

public sealed record FileGuardResult
{
    public required FileGuardOutcome Outcome { get; init; }

    public string? Text { get; init; }

    public bool IsAccepted => Outcome == FileGuardOutcome.Accepted;

    public static FileGuardResult Accepted(string text) => new() { Outcome = FileGuardOutcome.Accepted, Text = text };

    public static FileGuardResult Rejected(FileGuardOutcome outcome) => new() { Outcome = outcome };
}

public sealed class FileContentGuard
{
    public const long MaxFileBytes = 1 * 1024 * 1024;
    public const int BinaryCheckBytes = 8000;

    public async Task<FileGuardResult> EvaluateAsync(string filePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        if (info.Length > MaxFileBytes)
        {
            return FileGuardResult.Rejected(FileGuardOutcome.TooLarge);
        }

        var raw = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var checkLength = Math.Min(raw.Length, BinaryCheckBytes);
        if (Array.IndexOf(raw, (byte)0x00, 0, checkLength) >= 0)
        {
            return FileGuardResult.Rejected(FileGuardOutcome.BinaryContent);
        }

        var text = Encoding.UTF8.GetString(raw);
        return FileGuardResult.Accepted(text);
    }
}
