using Collector.Application.Extraction;
using Collector.Domain.Enums;

namespace Collector.Tests.Extraction;

public sealed class DocumentStatusRulesTests
{
    private readonly DocumentStatusRules _rules = new();

    [Theory]
    [InlineData(SkipReason.TooLarge, DocumentStatus.Excluded)]
    [InlineData(SkipReason.BinaryContent, DocumentStatus.Excluded)]
    [InlineData(SkipReason.UnsupportedFormat, DocumentStatus.Excluded)]
    [InlineData(SkipReason.ParseFailure, DocumentStatus.Failed)]
    public void Maps_skip_reason_to_expected_status(SkipReason reason, DocumentStatus expected)
    {
        Assert.Equal(expected, _rules.ForSkip(reason));
    }

    [Theory]
    [InlineData(DocumentStatus.Pending, DocumentStatus.Extracting, true)]
    [InlineData(DocumentStatus.Extracting, DocumentStatus.Extracted, true)]
    [InlineData(DocumentStatus.Extracting, DocumentStatus.Failed, true)]
    [InlineData(DocumentStatus.Failed, DocumentStatus.Extracting, true)]
    [InlineData(DocumentStatus.Extracted, DocumentStatus.Extracting, true)]
    [InlineData(DocumentStatus.Excluded, DocumentStatus.Extracting, false)]
    [InlineData(DocumentStatus.Pending, DocumentStatus.Extracted, false)]
    public void Validates_lifecycle_transitions(DocumentStatus from, DocumentStatus to, bool expected)
    {
        Assert.Equal(expected, _rules.CanTransition(from, to));
    }
}
