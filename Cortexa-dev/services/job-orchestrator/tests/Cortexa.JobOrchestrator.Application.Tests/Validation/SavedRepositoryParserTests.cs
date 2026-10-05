using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Validation;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Validation;

public sealed class SavedRepositoryParserTests
{
    private const int MaxCount = 3;

    private static string Json(string provider, string owner, string repository, string branch) =>
        $$"""{"provider":"{{provider}}","owner":"{{owner}}","repository":"{{repository}}","branch":"{{branch}}"}""";

    private static string JsonWithFiles(string provider, string owner, string repository, string branch, string filesJson) =>
        $$"""{"provider":"{{provider}}","owner":"{{owner}}","repository":"{{repository}}","branch":"{{branch}}","selected_files":{{filesJson}}}""";

    [Fact]
    public void Parse_NoValues_ReturnsEmptyWithoutError()
    {
        var result = SavedRepositoryParser.Parse([], MaxCount);

        result.Error.Should().BeNull();
        result.Repositories.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ValidGitHubAndAzure_ReturnsBoth()
    {
        var result = SavedRepositoryParser.Parse(
            [Json("github", "acme", "api", "feature/x"), Json("azure-devops", "contoso", "Platform/api", "main")],
            MaxCount);

        result.Error.Should().BeNull();
        result.Repositories.Should().Equal(
            new SavedRepositoryRef("github", "acme", "api", "feature/x"),
            new SavedRepositoryRef("azure-devops", "contoso", "Platform/api", "main"));
    }

    [Fact]
    public void Parse_Duplicates_AreCollapsed()
    {
        var value = Json("github", "acme", "api", "main");

        var result = SavedRepositoryParser.Parse([value, value], MaxCount);

        result.Repositories.Should().ContainSingle();
    }

    [Fact]
    public void Parse_TooMany_Fails()
    {
        var value = Json("github", "acme", "api", "main");

        var result = SavedRepositoryParser.Parse([value, value, value, value], MaxCount);

        result.Error.Should().Contain("At most 3");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"provider":"github","owner":"acme"}""")]
    [InlineData("[]")]
    public void Parse_Malformed_Fails(string raw)
    {
        SavedRepositoryParser.Parse([raw], MaxCount).Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("gitlab", "acme", "api", "main")]
    [InlineData("github", "ac/me", "api", "main")]
    [InlineData("github", "acme", "a/b", "main")]
    [InlineData("azure-devops", "contoso", "api", "main")]
    [InlineData("azure-devops", "contoso", "P/a/b", "main")]
    [InlineData("github", "acme", "api", "../../etc")]
    [InlineData("github", "..", "api", "main")]
    [InlineData("github", "acme", "api", " main")]
    public void Parse_InvalidNames_Fail(string provider, string owner, string repository, string branch)
    {
        SavedRepositoryParser.Parse([Json(provider, owner, repository, branch)], MaxCount).Error.Should().NotBeNull();
    }

    [Fact]
    public void Parse_WithSelectedFiles_KeepsThemOnTheRef()
    {
        var result = SavedRepositoryParser.Parse(
            [JsonWithFiles("github", "acme", "api", "main", """["src/app.py","README.md"]""")],
            MaxCount);

        result.Error.Should().BeNull();
        result.Repositories.Should().ContainSingle()
            .Which.SelectedFiles.Should().Equal("src/app.py", "README.md");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""["../../etc/passwd"]""")]
    [InlineData("""["/etc/passwd"]""")]
    [InlineData("""[""]""")]
    [InlineData("""["src/./app.py"]""")]
    public void Parse_InvalidSelectedFiles_Fail(string filesJson)
    {
        var result = SavedRepositoryParser.Parse([JsonWithFiles("github", "acme", "api", "main", filesJson)], MaxCount);

        result.Error.Should().NotBeNull();
    }
}
