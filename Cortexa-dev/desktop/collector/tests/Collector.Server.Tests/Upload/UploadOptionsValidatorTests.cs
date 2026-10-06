using Collector.Server.Application.Upload;

namespace Collector.Server.Tests.Upload;

public class UploadOptionsValidatorTests
{
    private readonly UploadOptionsValidator _validator = new();

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var result = _validator.Validate(null, UploadTestOptions.Create());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("harvesting")]
    [InlineData("Seeding")]
    [InlineData("DUAL")]
    public void Validate_KnownEngineAnyCase_Succeeds(string engine)
    {
        var result = _validator.Validate(null, UploadTestOptions.Create(options => options.Engine = engine));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("both")]
    public void Validate_UnknownEngine_FailsNamingTheEngineSetting(string engine)
    {
        var result = _validator.Validate(null, UploadTestOptions.Create(options => options.Engine = engine));

        Assert.Contains("Upload:Engine", Assert.Single(result.Failures!));
    }

    [Theory]
    [MemberData(nameof(NonPositiveCaps))]
    public void Validate_NonPositiveCap_FailsNamingTheCap(string name, Action<UploadOptions> zeroOut)
    {
        var result = _validator.Validate(null, UploadTestOptions.Create(zeroOut));

        Assert.Contains($"Upload:{name}", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Validate_BlankModelDefault_FailsNamingTheDefault()
    {
        var result = _validator.Validate(
            null,
            UploadTestOptions.Create(options => options.ModelDefaults.ScoringModel = " "));

        Assert.Contains("Upload:ModelDefaults:ScoringModel", Assert.Single(result.Failures!));
    }

    public static TheoryData<string, Action<UploadOptions>> NonPositiveCaps() => new()
    {
        { nameof(UploadOptions.MaxRequestBytes), o => o.MaxRequestBytes = 0 },
        { nameof(UploadOptions.MaxDocuments), o => o.MaxDocuments = 0 },
        { nameof(UploadOptions.MaxItemsPerDocument), o => o.MaxItemsPerDocument = 0 },
        { nameof(UploadOptions.MaxBatchNameLength), o => o.MaxBatchNameLength = 0 },
        { nameof(UploadOptions.MaxTitleLength), o => o.MaxTitleLength = 0 },
        { nameof(UploadOptions.MaxSummaryLength), o => o.MaxSummaryLength = 0 },
        { nameof(UploadOptions.MaxDetailsLength), o => o.MaxDetailsLength = 0 },
        { nameof(UploadOptions.MaxIdempotencyKeyLength), o => o.MaxIdempotencyKeyLength = -1 }
    };
}
