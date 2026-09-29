using Cortexa.JobOrchestrator.Application.Validation;
using Cortexa.JobOrchestrator.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Validation;

public sealed class PatentSourceValidatorTests
{
    private const string ValidKey = "abcdefgh12345678";

    [Theory]
    [InlineData("uspto", PatentSource.Uspto)]
    [InlineData("USPTO", PatentSource.Uspto)]
    [InlineData("epo", PatentSource.Epo)]
    [InlineData("Epo", PatentSource.Epo)]
    [InlineData("lens", PatentSource.Lens)]
    [InlineData("LENS", PatentSource.Lens)]
    public void TryParseSource_KnownSourceAnyCasing_ReturnsTrueWithParsedValue(string raw, PatentSource expected)
    {
        var result = PatentSourceValidator.TryParseSource(raw, out var source);

        result.Should().BeTrue();
        source.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("google-patents")]
    [InlineData("uspto-legacy")]
    public void TryParseSource_UnknownOrBlank_ReturnsFalse(string? raw)
    {
        var result = PatentSourceValidator.TryParseSource(raw, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSecretWrite_UsptoValidKey_ReturnsNull()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Uspto, ValidKey, null, null);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateSecretWrite_LensValidKey_ReturnsNull()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Lens, ValidKey, null, null);

        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSecretWrite_UsptoMissingKey_ReturnsError(string? apiKey)
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Uspto, apiKey, null, null);

        error.Should().Be("api_key is required.");
    }

    [Fact]
    public void ValidateSecretWrite_UsptoKeyTooShort_ReturnsShapeError()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Uspto, "short", null, null);

        error.Should().Contain("api_key").And.Contain("characters");
    }

    [Fact]
    public void ValidateSecretWrite_UsptoKeyContainsWhitespace_ReturnsShapeError()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Uspto, "abcd efgh 1234", null, null);

        error.Should().Contain("whitespace");
    }

    [Fact]
    public void ValidateSecretWrite_UsptoKeyTooLong_ReturnsShapeError()
    {
        var tooLong = new string('a', 513);

        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Uspto, tooLong, null, null);

        error.Should().Contain("characters");
    }

    [Fact]
    public void ValidateSecretWrite_EpoBothFieldsValid_ReturnsNull()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Epo, null, ValidKey, ValidKey);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateSecretWrite_EpoBothMissing_ReturnsRequiredError()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Epo, null, null, null);

        error.Should().Be("consumer_key and oauth_secret are required.");
    }

    [Fact]
    public void ValidateSecretWrite_EpoConsumerOnly_ReturnsPairError()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Epo, null, ValidKey, null);

        error.Should().Be("consumer_key and oauth_secret must be provided together.");
    }

    [Fact]
    public void ValidateSecretWrite_EpoOauthOnly_ReturnsPairError()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Epo, null, null, ValidKey);

        error.Should().Be("consumer_key and oauth_secret must be provided together.");
    }

    [Fact]
    public void ValidateSecretWrite_EpoConsumerShapeInvalid_ReturnsConsumerError()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Epo, null, "short", ValidKey);

        error.Should().Contain("consumer_key");
    }

    [Fact]
    public void ValidateSecretWrite_EpoOauthShapeInvalid_ReturnsOauthError()
    {
        var error = PatentSourceValidator.ValidateSecretWrite(PatentSource.Epo, null, ValidKey, "short");

        error.Should().Contain("oauth_secret");
    }
}
