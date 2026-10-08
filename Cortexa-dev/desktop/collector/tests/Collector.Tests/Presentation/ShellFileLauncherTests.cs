using Collector.Application.History;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Collector.Presentation.Services;
using Microsoft.Extensions.Options;

namespace Collector.Tests.Presentation;

public sealed class ShellFileLauncherTests : IDisposable
{
    private const string Editor = "editor.exe";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "collector-launcher-" + Guid.NewGuid().ToString("N"));

    public ShellFileLauncherTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, "x");
        return path;
    }

    private static System.Diagnostics.ProcessStartInfo? Build(string path, SourceKind kind) =>
        ShellFileLauncher.BuildStartInfo(new LocalSourceTarget(path, kind), Editor);

    [Theory]
    [InlineData("paper.pdf")]
    [InlineData("paper.docx")]
    [InlineData("PAPER.PDF")]
    public void BuildStartInfo_PaperPdfOrDocx_UsesShell(string name)
    {
        var path = File(name);

        var info = Build(path, SourceKind.Paper);

        Assert.NotNull(info);
        Assert.True(info.UseShellExecute);
        Assert.Equal(path, info.FileName);
        Assert.Empty(info.ArgumentList);
    }

    [Theory]
    [InlineData("notes.txt", SourceKind.Paper)]
    [InlineData("Program.cs", SourceKind.Code)]
    [InlineData("scan.pdf", SourceKind.Code)]
    [InlineData("script.bat", SourceKind.Code)]
    [InlineData("noextension", SourceKind.Paper)]
    public void BuildStartInfo_Everything_ElseUsesConfiguredEditor(string name, SourceKind kind)
    {
        var path = File(name);

        var info = Build(path, kind);

        Assert.NotNull(info);
        Assert.False(info.UseShellExecute);
        Assert.Equal(Editor, info.FileName);
        Assert.Equal([path], info.ArgumentList);
    }

    [Fact]
    public void BuildStartInfo_PathWithSpacesAndMetacharacters_StaysOneArgument()
    {
        var path = File("a b & c; d.cs");

        var info = Build(path, SourceKind.Code);

        Assert.NotNull(info);
        Assert.Single(info.ArgumentList);
        Assert.Equal(path, info.ArgumentList[0]);
    }

    [Fact]
    public void BuildStartInfo_MissingFile_ReturnsNull()
    {
        Assert.Null(Build(Path.Combine(_directory, "missing.pdf"), SourceKind.Paper));
    }

    [Fact]
    public void BuildStartInfo_Directory_ReturnsNull()
    {
        var folder = Path.Combine(_directory, "folder.pdf");
        Directory.CreateDirectory(folder);

        Assert.Null(Build(folder, SourceKind.Paper));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/file.pdf")]
    [InlineData("file.pdf")]
    [InlineData(@"..\file.pdf")]
    public void BuildStartInfo_NotFullyQualified_ReturnsNull(string path)
    {
        Assert.Null(Build(path, SourceKind.Paper));
    }

    [Theory]
    [InlineData(@"\\server\share\file.pdf")]
    [InlineData(@"\\?\C:\Windows\notepad.exe")]
    [InlineData(@"\\.\C:\file.pdf")]
    [InlineData("//server/share/file.pdf")]
    public void BuildStartInfo_UncOrDevicePath_ReturnsNull(string path)
    {
        Assert.Null(Build(path, SourceKind.Paper));
    }

    [Fact]
    public void BuildStartInfo_InvalidCharacters_ReturnsNull()
    {
        Assert.Null(Build(_directory + Path.DirectorySeparatorChar + "bad\0name.pdf", SourceKind.Paper));
    }

    [Fact]
    public void TryLaunch_MissingFile_ReturnsFalseWithoutThrowing()
    {
        var launcher = new ShellFileLauncher(Options.Create(new HistoryOptions { TextEditorPath = Editor }));

        var launched = launcher.TryLaunch(new LocalSourceTarget(Path.Combine(_directory, "gone.pdf"), SourceKind.Paper));

        Assert.False(launched);
    }

    [Fact]
    public void TryLaunch_EditorCannotStart_ReturnsFalseWithoutThrowing()
    {
        var path = File("code.cs");
        var launcher = new ShellFileLauncher(Options.Create(new HistoryOptions { TextEditorPath = Path.Combine(_directory, "no-such-editor.exe") }));

        var launched = launcher.TryLaunch(new LocalSourceTarget(path, SourceKind.Code));

        Assert.False(launched);
    }
}
