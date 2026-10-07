using Collector.Application.Extraction;
using Collector.Application.Ports;

namespace Collector.Tests.Extraction;

public sealed class CodeSplitterTests
{
    private static readonly CodeLanguageInfo CSharp = new() { Name = "csharp", Strategy = CodeSplitStrategy.Brace };
    private static readonly CodeLanguageInfo Python = new() { Name = "python", Strategy = CodeSplitStrategy.Indentation };

    [Fact]
    public void Splits_csharp_file_with_three_top_level_functions_into_three_units()
    {
        const string source =
            "public static int Add(int a, int b)\n" +
            "{\n" +
            "    return a + b;\n" +
            "}\n" +
            "\n" +
            "public static int Subtract(int a, int b)\n" +
            "{\n" +
            "    return a - b;\n" +
            "}\n" +
            "\n" +
            "public static int Multiply(int a, int b)\n" +
            "{\n" +
            "    return a * b;\n" +
            "}\n";
        var splitter = new CodeSplitter(new CharTokenCounter());

        var units = splitter.Split(source, CSharp);

        Assert.Equal(3, units.Count);
        Assert.StartsWith("public static int Add", units[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("public static int Subtract", units[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("public static int Multiply", units[2].Text, StringComparison.Ordinal);
        Assert.Equal(1, units[0].StartLine);
        Assert.Equal(6, units[1].StartLine);
    }

    [Fact]
    public void Returns_whole_file_as_a_single_unit_when_no_boundaries_are_found()
    {
        const string source = "Console.WriteLine(\"Hello\");\nConsole.WriteLine(\"World\");\n";
        var splitter = new CodeSplitter(new CharTokenCounter());

        var units = splitter.Split(source, CSharp);

        Assert.Single(units);
        Assert.Equal(source, units[0].Text);
    }

    [Fact]
    public void Falls_back_to_a_token_window_when_a_single_function_exceeds_the_limit()
    {
        var body = string.Concat(Enumerable.Repeat("x", 2500));
        var source = $"public static void Big()\n{{\n    var value = \"{body}\";\n}}\n";
        var splitter = new CodeSplitter(new CharTokenCounter());

        var units = splitter.Split(source, CSharp);

        Assert.Equal(2, units.Count);
        Assert.Equal(source, string.Concat(units.Select(u => u.Text)));
    }

    [Fact]
    public void Splits_python_file_by_indentation_based_def_boundaries()
    {
        const string source =
            "def first():\n" +
            "    return 1\n" +
            "\n" +
            "def second():\n" +
            "    return 2\n";
        var splitter = new CodeSplitter(new CharTokenCounter());

        var units = splitter.Split(source, Python);

        Assert.Equal(2, units.Count);
        Assert.StartsWith("def first()", units[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("def second()", units[1].Text, StringComparison.Ordinal);
    }

    private sealed class CharTokenCounter : ITokenCounter
    {
        public IReadOnlyList<int> Encode(string text) => text.Select(c => (int)c).ToList();

        public string Decode(IReadOnlyList<int> tokens) => new([.. tokens.Select(t => (char)t)]);

        public int Count(string text) => text.Length;
    }
}
