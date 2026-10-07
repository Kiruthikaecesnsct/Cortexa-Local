namespace Collector.Application.Extraction;

public enum CodeSplitStrategy
{
    Brace,
    Indentation,
    None,
}

public sealed record CodeLanguageInfo
{
    public required string Name { get; init; }

    public required CodeSplitStrategy Strategy { get; init; }
}

public static class CodeLanguage
{
    private static readonly Dictionary<string, CodeLanguageInfo> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = new CodeLanguageInfo { Name = "csharp", Strategy = CodeSplitStrategy.Brace },
        [".java"] = new CodeLanguageInfo { Name = "java", Strategy = CodeSplitStrategy.Brace },
        [".js"] = new CodeLanguageInfo { Name = "javascript", Strategy = CodeSplitStrategy.Brace },
        [".jsx"] = new CodeLanguageInfo { Name = "javascript", Strategy = CodeSplitStrategy.Brace },
        [".ts"] = new CodeLanguageInfo { Name = "typescript", Strategy = CodeSplitStrategy.Brace },
        [".tsx"] = new CodeLanguageInfo { Name = "typescript", Strategy = CodeSplitStrategy.Brace },
        [".go"] = new CodeLanguageInfo { Name = "go", Strategy = CodeSplitStrategy.Brace },
        [".rs"] = new CodeLanguageInfo { Name = "rust", Strategy = CodeSplitStrategy.Brace },
        [".c"] = new CodeLanguageInfo { Name = "c", Strategy = CodeSplitStrategy.Brace },
        [".h"] = new CodeLanguageInfo { Name = "c", Strategy = CodeSplitStrategy.Brace },
        [".cpp"] = new CodeLanguageInfo { Name = "cpp", Strategy = CodeSplitStrategy.Brace },
        [".cc"] = new CodeLanguageInfo { Name = "cpp", Strategy = CodeSplitStrategy.Brace },
        [".hpp"] = new CodeLanguageInfo { Name = "cpp", Strategy = CodeSplitStrategy.Brace },
        [".py"] = new CodeLanguageInfo { Name = "python", Strategy = CodeSplitStrategy.Indentation },
    };

    public static CodeLanguageInfo? ForExtension(string extension) =>
        ByExtension.TryGetValue(extension, out var info) ? info : null;

    public static bool IsKnownCodeExtension(string extension) => ByExtension.ContainsKey(extension);
}
