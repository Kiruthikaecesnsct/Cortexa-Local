using System.Text.Json;
using Cortexa.ModelRouter.Application.DTOs;
using FluentAssertions;

namespace Cortexa.ModelRouter.Tests;

public sealed class CompleteRequestBindingTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_EvidenceSnakeCaseBody_BindsTaskKind()
    {
        const string json = """{"task_kind":"patent_research","prompt":"Analyze X","evidence_refs":null,"options":null}""";

        var request = JsonSerializer.Deserialize<CompleteRequest>(json, WebOptions);

        request.Should().NotBeNull();
        request!.TaskKind.Should().Be("patent_research");
        request.Prompt.Should().Be("Analyze X");
    }

    [Fact]
    public void Deserialize_EvidenceSnakeCaseBodyWithModel_BindsAllFields()
    {
        const string json = """{"task_kind":"patent_research","prompt":"Research Y","evidence_refs":null,"options":null,"model":"gpt-5.4"}""";

        var request = JsonSerializer.Deserialize<CompleteRequest>(json, WebOptions);

        request.Should().NotBeNull();
        request!.TaskKind.Should().Be("patent_research");
        request.Model.Should().Be("gpt-5.4");
    }

    [Fact]
    public void Deserialize_SnakeCaseOptions_BindsModelOptions()
    {
        const string json = """{"task_kind":"extraction","prompt":"Analyze Z","evidence_refs":null,"options":{"max_tokens":8192,"temperature":1.0,"stream":false,"force_json_output":true}}""";

        var request = JsonSerializer.Deserialize<CompleteRequest>(json, WebOptions);

        request.Should().NotBeNull();
        request!.TaskKind.Should().Be("extraction");
        request.Options.Should().NotBeNull();
        request.Options!.MaxTokens.Should().Be(8192);
        request.Options.ForceJsonOutput.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_NullTaskKind_BindsToNull()
    {
        const string json = """{"task_kind":null,"prompt":"Analyze W","evidence_refs":null,"options":null}""";

        var request = JsonSerializer.Deserialize<CompleteRequest>(json, WebOptions);

        request.Should().NotBeNull();
        request!.TaskKind.Should().BeNull();
    }
}
