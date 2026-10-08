namespace Collector.Application.Remote;

public sealed class RemoteFetchOptions
{
    public const string SectionName = "RemoteSources:Fetch";

    public long MaxRepositoryBytes { get; set; } = 500L * 1024 * 1024;

    public int MaxFilesPerFetch { get; set; } = 2000;

    public int MaxParallelDownloads { get; set; } = 4;

    public string[] TextExtensions { get; set; } =
    [
        ".md", ".markdown", ".mdx", ".txt", ".text", ".rst", ".adoc", ".asciidoc", ".org",
    ];

    public string[] ExcludedDirectories { get; set; } =
    [
        ".git", "node_modules", "bin", "obj", "dist", "vendor", ".venv",
    ];
}
