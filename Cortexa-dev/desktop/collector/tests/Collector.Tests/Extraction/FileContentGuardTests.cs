using System.Text;
using Collector.Application.Extraction;
using Collector.Tests.Support;

namespace Collector.Tests.Extraction;

public sealed class FileContentGuardTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-guard-{Guid.NewGuid():N}");
    private readonly FileContentGuard _guard = new();

    public FileContentGuardTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public async Task Accepts_small_utf8_text_file()
    {
        var path = WriteFile("small.txt", Encoding.UTF8.GetBytes("hello world"));

        var result = await _guard.EvaluateAsync(path, TestSupport.Ct);

        Assert.True(result.IsAccepted);
        Assert.Equal("hello world", result.Text);
    }

    [Fact]
    public async Task Rejects_file_larger_than_one_mebibyte()
    {
        var path = WriteFile("big.txt", new byte[FileContentGuard.MaxFileBytes + 1]);

        var result = await _guard.EvaluateAsync(path, TestSupport.Ct);

        Assert.Equal(FileGuardOutcome.TooLarge, result.Outcome);
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public async Task Rejects_file_with_nul_byte_in_first_8000_bytes()
    {
        var content = new byte[100];
        content[50] = 0x00;
        var path = WriteFile("binary.dat", content);

        var result = await _guard.EvaluateAsync(path, TestSupport.Ct);

        Assert.Equal(FileGuardOutcome.BinaryContent, result.Outcome);
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public async Task Ignores_nul_byte_beyond_the_check_window()
    {
        var content = new byte[FileContentGuard.BinaryCheckBytes + 100];
        Array.Fill(content, (byte)'a');
        content[^1] = 0x00;
        var path = WriteFile("late-nul.dat", content);

        var result = await _guard.EvaluateAsync(path, TestSupport.Ct);

        Assert.True(result.IsAccepted);
    }
}
