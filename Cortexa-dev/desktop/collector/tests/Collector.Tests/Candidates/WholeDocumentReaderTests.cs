using Collector.Application.Candidates;
using Collector.Application.Extraction;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Tests.Extraction;
using Collector.Tests.Support;

namespace Collector.Tests.Candidates;

public sealed class WholeDocumentReaderTests : IDisposable
{
    private const string RawText = "  Alpha   beta\r\n\r\n\r\n\r\ngamma  ";
    private const string NormalizedText = "Alpha beta\n\ngamma";
    private const string CodeText = "def f():\n    return    1\n";
    private const string DocumentId = "doc-1";

    private readonly string _directory = Directory.CreateTempSubdirectory("whole-doc").FullName;
    private readonly FakePdfTextExtractor _pdf = new();
    private readonly FakeDocxTextExtractor _docx = new();

    public void Dispose() => Directory.Delete(_directory, true);

    private WholeDocumentReader Reader() => new(_pdf, _docx, new FileContentGuard(), new TextNormalizer());

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static CollectorDocument Document(string path, long size = 1) => new()
    {
        Id = DocumentId,
        SourceType = SourceType.Local,
        SourceKind = SourceKind.Code,
        SourcePath = path,
        Filename = Path.GetFileName(path),
        ContentHash = "hash",
        SizeBytes = size,
        Status = DocumentStatus.Pending,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task ReadAsync_Pdf_NormalizesWholeText()
    {
        _pdf.Result = new ParsedDocument { Text = RawText };

        var read = await Reader().ReadAsync(Document(Path.Combine(_directory, "paper.pdf")), TestSupport.Ct);

        Assert.True(read.IsRead);
        Assert.Equal(NormalizedText, read.Document!.Text);
        Assert.False(read.Document.IsCode);
        Assert.Equal(DocumentId, read.Document.DocumentId);
    }

    [Fact]
    public async Task ReadAsync_Docx_NormalizesText()
    {
        _docx.Result = new ParsedDocument { Text = RawText };

        var read = await Reader().ReadAsync(Document(Path.Combine(_directory, "thesis.docx")), TestSupport.Ct);

        Assert.Equal(NormalizedText, read.Document!.Text);
    }

    [Fact]
    public async Task ReadAsync_Code_PreservesIndentation()
    {
        var path = Write("sample.py", CodeText);

        var read = await Reader().ReadAsync(Document(path), TestSupport.Ct);

        Assert.True(read.Document!.IsCode);
        Assert.Equal(CodeText, read.Document.Text);
    }

    [Fact]
    public async Task ReadAsync_PlainText_Normalizes()
    {
        var path = Write("notes.txt", RawText);

        var read = await Reader().ReadAsync(Document(path), TestSupport.Ct);

        Assert.False(read.Document!.IsCode);
        Assert.Equal(NormalizedText, read.Document.Text);
    }

    [Fact]
    public async Task ReadAsync_OverCap_RejectedTooLargeWithSizes()
    {
        var path = Write("big.txt", new string('a', (int)FileContentGuard.MaxFileBytes + 1));
        var size = new FileInfo(path).Length;

        var read = await Reader().ReadAsync(Document(path, size), TestSupport.Ct);

        Assert.False(read.IsRead);
        Assert.Equal(CandidateOutcome.TooLarge, read.Outcome);
        Assert.Equal(size, read.SizeBytes);
        Assert.Equal(FileContentGuard.MaxFileBytes, read.CapBytes);
    }

    [Fact]
    public async Task ReadAsync_BinaryContent_RejectedFailed()
    {
        var path = Path.Combine(_directory, "blob.txt");
        File.WriteAllBytes(path, [0x41, 0x00, 0x42]);

        var read = await Reader().ReadAsync(Document(path), TestSupport.Ct);

        Assert.Equal(CandidateOutcome.Failed, read.Outcome);
        Assert.Equal("binary content", read.Reason);
    }

    [Fact]
    public async Task ReadAsync_ExtractorThrows_RejectedFailedWithMessage()
    {
        _pdf.Failure = new InvalidDataException("corrupt pdf");

        var read = await Reader().ReadAsync(Document(Path.Combine(_directory, "bad.pdf")), TestSupport.Ct);

        Assert.Equal(CandidateOutcome.Failed, read.Outcome);
        Assert.Equal("corrupt pdf", read.Reason);
    }

    [Fact]
    public async Task ReadAsync_ExtractorCanceled_Propagates()
    {
        _pdf.Failure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Reader().ReadAsync(Document(Path.Combine(_directory, "x.pdf")), TestSupport.Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public async Task ReadAsync_NoTextAfterNormalizing_RejectedFailed(string text)
    {
        _pdf.Result = new ParsedDocument { Text = text };

        var read = await Reader().ReadAsync(Document(Path.Combine(_directory, "scan.pdf")), TestSupport.Ct);

        Assert.Equal(CandidateOutcome.Failed, read.Outcome);
        Assert.Equal("no extractable text", read.Reason);
        Assert.Null(read.Document);
    }

    [Fact]
    public async Task ReadAsync_EmptyTextFile_RejectedFailed()
    {
        var read = await Reader().ReadAsync(Document(Write("empty.txt", string.Empty)), TestSupport.Ct);

        Assert.Equal(CandidateOutcome.Failed, read.Outcome);
    }
}
