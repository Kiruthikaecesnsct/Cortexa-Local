using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Services;

public sealed class ModelConfigValidatorTests
{
    private readonly IModelCatalogClient _catalogClient;
    private readonly ModelConfigValidator _validator;

    private const string ValidExtractionModel = "gpt-5.5";
    private const string ValidEvidenceModel = "gpt-5.4";
    private const string ValidScoringModel = "gpt-5.5";
    private const string ValidSeedingModel = "gpt-5.5";
    private const string InvalidModel = "invalid-model";

    public ModelConfigValidatorTests()
    {
        _catalogClient = Substitute.For<IModelCatalogClient>();
        _validator = new ModelConfigValidator(_catalogClient);
    }

    private static IReadOnlyList<CatalogModel> DefaultCatalog() =>
    [
        new CatalogModel("gpt-5.5", true, ["extraction", "scoring", "seeding"]),
        new CatalogModel("gpt-5.4", true, ["evidence", "scoring", "seeding"]),
        new CatalogModel("grok-4.3", true, ["extraction", "evidence", "scoring", "seeding"]),
        new CatalogModel("DeepSeek-V4-Pro", true, ["extraction", "evidence", "scoring", "seeding"]),
        new CatalogModel("claude-opus-4-8", false, ["extraction", "evidence", "scoring", "seeding"]),
        new CatalogModel("claude-sonnet-4-6", false, ["extraction", "evidence", "scoring", "seeding"])
    ];

    [Fact]
    public async Task ValidateAsync_AllModelsValidForTheirStage_ReturnsValidResult()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, ValidEvidenceModel, ValidScoringModel, ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.IsCatalogUnavailable.Should().BeFalse();
        result.Errors.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_Gpt55SubmittedForEvidence_RejectedAsNotAllowedForStage()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, "gpt-5.5", ValidScoringModel, ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.IsCatalogUnavailable.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Stage == ModelStage.Evidence && e.Model == "gpt-5.5");
    }

    [Fact]
    public async Task ValidateAsync_Gpt54SubmittedForExtraction_RejectedAsNotAllowedForStage()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection("gpt-5.4", ValidEvidenceModel, ValidScoringModel, ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Stage == ModelStage.Extraction && e.Model == "gpt-5.4");
    }

    [Fact]
    public async Task ValidateAsync_DisabledModelSubmitted_RejectedEvenThoughStageAllowsIt()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, "claude-opus-4-8", ValidScoringModel, ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Stage == ModelStage.Evidence && e.Model == "claude-opus-4-8");
    }

    [Fact]
    public async Task ValidateAsync_MultipleInvalidSlots_ReturnsOneErrorPerSlot()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection(InvalidModel, "gpt-5.5", ValidScoringModel, ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Stage == ModelStage.Extraction && e.Model == InvalidModel);
        result.Errors.Should().Contain(e => e.Stage == ModelStage.Evidence && e.Model == "gpt-5.5");
    }

    [Fact]
    public async Task ValidateAsync_ScoringAcceptsAnyEnabledModel_NoNarrowingBeyondEnabled()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, ValidEvidenceModel, "grok-4.3", ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CatalogThrowsException_ReturnsCatalogUnavailable()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogModel>>(_ => throw new HttpRequestException("Service unavailable"));

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, ValidEvidenceModel, ValidScoringModel, ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.IsCatalogUnavailable.Should().BeTrue();
        result.Errors.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_CatalogTimesOut_ReturnsCatalogUnavailable()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogModel>>(_ => throw new TaskCanceledException("Timeout"));

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, ValidEvidenceModel, ValidScoringModel, ValidSeedingModel),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.IsCatalogUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_InvalidSeedingModel_RejectedWithSeedingError()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, ValidEvidenceModel, ValidScoringModel, InvalidModel),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Stage == ModelStage.Seeding && e.Model == InvalidModel);
    }

    [Fact]
    public async Task ValidateAsync_DisabledModelForSeeding_RejectedEvenThoughStageAllowsIt()
    {
        _catalogClient.GetModelsAsync(Arg.Any<CancellationToken>()).Returns(DefaultCatalog());

        var result = await _validator.ValidateAsync(
            new StageModelSelection(ValidExtractionModel, ValidEvidenceModel, ValidScoringModel, "claude-opus-4-8"),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Stage == ModelStage.Seeding && e.Model == "claude-opus-4-8");
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("deep")]
    [InlineData("DEEP")]
    public void ValidateSeedingMode_AllowedValue_ReturnsNull(string mode)
    {
        var error = ModelConfigValidator.ValidateSeedingMode(mode);

        error.Should().BeNull();
    }

    [Theory]
    [InlineData("shallow")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateSeedingMode_InvalidValue_ReturnsError(string? mode)
    {
        var error = ModelConfigValidator.ValidateSeedingMode(mode);

        error.Should().NotBeNull();
        error.Should().Contain("legacy");
        error.Should().Contain("deep");
    }

    [Fact]
    public void BuildErrorMessage_CatalogUnavailable_ReturnsCorrectMessage()
    {
        var result = ValidationResult.Unavailable();

        var message = result.BuildErrorMessage();

        message.Should().Be("Model catalog is unavailable; configuration was not saved");
    }

    [Fact]
    public void BuildErrorMessage_SingleStageError_MentionsStageAndAllowedModels()
    {
        var errors = new List<StageValidationError>
        {
            new(ModelStage.Evidence, "gpt-5.5", ["gpt-5.4", "grok-4.3"])
        };
        var result = ValidationResult.Invalid(errors);

        var message = result.BuildErrorMessage();

        message.Should().Contain("gpt-5.5");
        message.Should().Contain("evidence");
        message.Should().Contain("gpt-5.4, grok-4.3");
    }

    [Fact]
    public void BuildErrorMessage_MultipleStageErrors_MentionsEachStage()
    {
        var errors = new List<StageValidationError>
        {
            new(ModelStage.Extraction, "gpt-5.4", ["gpt-5.5"]),
            new(ModelStage.Evidence, "gpt-5.5", ["gpt-5.4"])
        };
        var result = ValidationResult.Invalid(errors);

        var message = result.BuildErrorMessage();

        message.Should().Contain("extraction");
        message.Should().Contain("evidence");
    }
}
