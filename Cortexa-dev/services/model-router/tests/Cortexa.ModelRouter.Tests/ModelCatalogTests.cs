using Cortexa.ModelRouter.Application.Configuration;
using Cortexa.ModelRouter.Application.Services;
using FluentAssertions;

namespace Cortexa.ModelRouter.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public void Get_AllModelsEnabled_ReturnsDualModeAvailable()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("gpt-5.4", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", true),
                CreateEntry("claude-sonnet-4-6", "secondary", true)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        response.DualModeAvailable.Should().BeTrue();
        response.Defaults.Single.Should().Be("gpt-5.5");
        response.Defaults.Dual.Primary.Should().Be("gpt-5.5");
        response.Defaults.Dual.Secondary.Should().Be("claude-opus-4-8");
        response.Models.Should().HaveCount(4);
    }

    [Fact]
    public void Get_DevState_NoDualMode()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("gpt-5.4", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false),
                CreateEntry("claude-sonnet-4-6", "secondary", false)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        response.DualModeAvailable.Should().BeFalse();
        response.Defaults.Single.Should().Be("gpt-5.5");
        response.Defaults.Dual.Primary.Should().Be("gpt-5.5");
        response.Defaults.Dual.Secondary.Should().BeNull();
    }

    [Fact]
    public void Get_SingleDefaultDisabled_FallsBackToFirstEnabled()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", false),
                CreateEntry("gpt-5.4", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        response.Defaults.Single.Should().Be("gpt-5.4");
    }

    [Fact]
    public void Get_ZeroEnabledModels_NoCrash()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", false),
                CreateEntry("claude-opus-4-8", "secondary", false)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        response.DualModeAvailable.Should().BeFalse();
        response.Defaults.Single.Should().Be("gpt-5.5");
        response.Defaults.Dual.Secondary.Should().BeNull();
        response.Models.Should().HaveCount(2);
    }

    [Fact]
    public void Get_ResponseCamelCase_SerializesCorrectly()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        var json = System.Text.Json.JsonSerializer.Serialize(
            response,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

        json.Should().Contain("dualModeAvailable");
        json.Should().Contain("capabilities");
        json.Should().Contain("allowedStages");
        json.Should().NotContain("DualModeAvailable");
        json.Should().NotContain("Capabilities");
        json.Should().NotContain("AllowedStages");
    }

    [Fact]
    public void Get_Gpt55_AllowedStagesExcludeEvidence()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntryWithStages(
                    "gpt-5.5",
                    "primary",
                    true,
                    new List<string> { "extraction", "scoring" })
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();
        var gpt55 = response.Models.Single(m => m.Id == "gpt-5.5");

        gpt55.AllowedStages.Should().Contain("extraction");
        gpt55.AllowedStages.Should().Contain("scoring");
        gpt55.AllowedStages.Should().NotContain("evidence");
    }

    [Fact]
    public void Get_Gpt54_AllowedStagesExcludeExtraction()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntryWithStages(
                    "gpt-5.4",
                    "primary",
                    true,
                    new List<string> { "evidence", "scoring" })
            },
            SingleDefault = "gpt-5.4"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();
        var gpt54 = response.Models.Single(m => m.Id == "gpt-5.4");

        gpt54.AllowedStages.Should().Contain("evidence");
        gpt54.AllowedStages.Should().Contain("scoring");
        gpt54.AllowedStages.Should().NotContain("extraction");
    }

    [Fact]
    public void Get_SingleDefaultNotPrimary_FallsBackToPrimary()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", true)
            },
            SingleDefault = "claude-opus-4-8"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        response.Defaults.Dual.Primary.Should().Be("gpt-5.5");
    }

    [Fact]
    public void Get_OnlyPrimaryEnabled_SecondaryIsNull()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        response.DualModeAvailable.Should().BeFalse();
        response.Defaults.Dual.Secondary.Should().BeNull();
    }

    [Fact]
    public void Resolve_KnownEnabledId_ReturnsMatchingResolution()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.4", "primary", true)
            },
            SingleDefault = "gpt-5.4"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("gpt-5.4");

        resolution.Should().NotBeNull();
        resolution!.Provider.Should().Be("azure-foundry");
        resolution.Deployment.Should().Be("gpt-5.4");
        resolution.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsNull()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("unknown-model");

        resolution.Should().BeNull();
    }

    [Fact]
    public void Resolve_EntryWithoutDeploymentOverride_FallsBackToId()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("gpt-5.5");

        resolution!.Deployment.Should().Be("gpt-5.5");
    }

    [Fact]
    public void Resolve_EntryWithDeploymentOverride_UsesDeploymentValue()
    {
        const string DeploymentOverride = "gpt-5-4-eastus-deployment";
        var entry = CreateEntry("gpt-5.4", "primary", true);
        entry.Deployment = DeploymentOverride;
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry> { entry },
            SingleDefault = "gpt-5.4"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("gpt-5.4");

        resolution!.Deployment.Should().Be(DeploymentOverride);
    }

    [Fact]
    public void Resolve_DisabledEntry_ReturnsResolutionWithEnabledFalse()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("claude-opus-4-8", "secondary", false)
            },
            SingleDefault = "claude-opus-4-8"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("claude-opus-4-8");

        resolution.Should().NotBeNull();
        resolution!.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Grok43_ReturnsAzureFoundryEnabledResolution()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("grok-4.3", "primary", true)
            },
            SingleDefault = "grok-4.3"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("grok-4.3");

        resolution.Should().NotBeNull();
        resolution!.Provider.Should().Be("azure-foundry");
        resolution.Deployment.Should().Be("grok-4.3");
        resolution.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Resolve_DeepSeekV4Pro_ReturnsAzureFoundryEnabledResolution()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateAzureFoundryEntry("DeepSeek-V4-Pro", "secondary", true)
            },
            SingleDefault = "DeepSeek-V4-Pro"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("DeepSeek-V4-Pro");

        resolution.Should().NotBeNull();
        resolution!.Provider.Should().Be("azure-foundry");
        resolution.Deployment.Should().Be("DeepSeek-V4-Pro");
        resolution.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Get_DeepSeekV4ProSecondaryWithClaudeDisabled_DualModeAvailable()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("grok-4.3", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false),
                CreateEntry("claude-sonnet-4-6", "secondary", false),
                CreateAzureFoundryEntry("DeepSeek-V4-Pro", "secondary", true)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var response = catalog.Get();

        response.DualModeAvailable.Should().BeTrue();
        response.Defaults.Dual.Primary.Should().Be("gpt-5.5");
        response.Defaults.Dual.Secondary.Should().Be("DeepSeek-V4-Pro");
    }

    [Fact]
    public void Resolve_GptAlias_ResolvesToGpt55Enabled()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false)
            },
            SingleDefault = "gpt-5.5",
            Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt"] = "gpt-5.5",
                ["claude"] = "claude-opus-4-8"
            }
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("gpt");

        resolution.Should().NotBeNull();
        resolution!.Provider.Should().Be("azure-foundry");
        resolution.Deployment.Should().Be("gpt-5.5");
        resolution.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ClaudeAlias_ResolvesToClaudeOpusDisabled()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false)
            },
            SingleDefault = "gpt-5.5",
            Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt"] = "gpt-5.5",
                ["claude"] = "claude-opus-4-8"
            }
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("claude");

        resolution.Should().NotBeNull();
        resolution!.Provider.Should().Be("anthropic");
        resolution.Deployment.Should().Be("claude-opus-4-8");
        resolution.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Resolve_UnknownAlias_ReturnsNull()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true)
            },
            SingleDefault = "gpt-5.5",
            Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt"] = "gpt-5.5"
            }
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("nonexistent");

        resolution.Should().BeNull();
    }

    [Fact]
    public void Resolve_AliasIsCaseInsensitive()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true)
            },
            SingleDefault = "gpt-5.5",
            Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt"] = "gpt-5.5"
            }
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("GPT");

        resolution.Should().NotBeNull();
        resolution!.Deployment.Should().Be("gpt-5.5");
    }

    [Fact]
    public void Resolve_ExactIdWithAliasesConfigured_StillResolvesUnchanged()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true)
            },
            SingleDefault = "gpt-5.5",
            Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt"] = "gpt-5.5"
            }
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.Resolve("gpt-5.5");

        resolution.Should().NotBeNull();
        resolution!.Deployment.Should().Be("gpt-5.5");
        resolution.Enabled.Should().BeTrue();
    }

    [Fact]
    public void HasEnabledProvider_AnthropicDisabled_ReturnsFalse()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false)
            }
        };
        var catalog = CreateCatalog(settings);

        catalog.HasEnabledProvider("anthropic").Should().BeFalse();
        catalog.HasEnabledProvider("azure-foundry").Should().BeTrue();
    }

    [Fact]
    public void HasEnabledProvider_AnthropicEnabled_ReturnsTrue()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", true)
            }
        };
        var catalog = CreateCatalog(settings);

        catalog.HasEnabledProvider("anthropic").Should().BeTrue();
    }

    [Fact]
    public void ResolveEnabledSecondary_DeepSeekV4ProEnabledClaudeDisabled_ReturnsDeepSeekResolution()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false),
                CreateEntry("claude-sonnet-4-6", "secondary", false),
                CreateAzureFoundryEntry("DeepSeek-V4-Pro", "secondary", true)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.ResolveEnabledSecondary();

        resolution.Should().NotBeNull();
        resolution!.Provider.Should().Be("azure-foundry");
        resolution.Deployment.Should().Be("DeepSeek-V4-Pro");
        resolution.Enabled.Should().BeTrue();
    }

    [Fact]
    public void ResolveEnabledSecondary_NoSecondaryEnabled_ReturnsNull()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                CreateEntry("gpt-5.5", "primary", true),
                CreateEntry("claude-opus-4-8", "secondary", false),
                CreateEntry("claude-sonnet-4-6", "secondary", false)
            },
            SingleDefault = "gpt-5.5"
        };
        var catalog = CreateCatalog(settings);

        var resolution = catalog.ResolveEnabledSecondary();

        resolution.Should().BeNull();
    }

    private ModelCatalog CreateCatalog(ModelCatalogSettings settings)
    {
        return new ModelCatalog(settings);
    }

    private ModelCatalogEntry CreateEntry(
        string id,
        string role,
        bool enabled)
    {
        return new ModelCatalogEntry
        {
            Id = id,
            Label = $"{id} label",
            Provider = role == "primary" ? "azure-foundry" : "anthropic",
            Role = role,
            Enabled = enabled,
            Capabilities = new List<string> { "reasoning", "grounding" },
            AllowedStages = new List<string> { "extraction", "evidence", "scoring" }
        };
    }

    private ModelCatalogEntry CreateEntryWithStages(
        string id,
        string role,
        bool enabled,
        List<string> allowedStages)
    {
        var entry = CreateEntry(id, role, enabled);
        entry.AllowedStages = allowedStages;
        return entry;
    }

    private ModelCatalogEntry CreateAzureFoundryEntry(
        string id,
        string role,
        bool enabled)
    {
        return new ModelCatalogEntry
        {
            Id = id,
            Label = $"{id} label",
            Provider = "azure-foundry",
            Role = role,
            Enabled = enabled,
            Capabilities = new List<string> { "reasoning", "grounding" },
            AllowedStages = new List<string> { "extraction", "evidence", "scoring" }
        };
    }
}
