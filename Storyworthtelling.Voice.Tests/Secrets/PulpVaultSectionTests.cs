using System.Reflection;
using System.Text.Json;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// Pulp's section of the shared vault, and the guard that its wire name never moves.
///
/// <para>The vault has no naming policy, so a property name is the key written to the owner's
/// Drive. A round trip proves nothing about that — it passes just as happily with the section
/// renamed at both ends — so the load-bearing case here is written as the file actually on Drive,
/// the lesson Foreword's rename taught (see <see cref="VaultWireNamesTests"/>). All values are
/// obviously invented placeholders, never anything shaped like a real key.</para>
/// </summary>
public class PulpVaultSectionTests
{
    private static readonly JsonSerializerOptions Options = VaultEnvelope.SerializerOptions;

    [Fact]
    public void ThePulpSectionAsWrittenOnDriveLoads()
    {
        const string onDrive = """
            {
              "Claude": { "ApiKey": "example-claude" },
              "Pulp": {
                "StreamingAvailabilityKey": "example-key-1",
                "OmdbKey": "example-key-2",
                "YouTubeKey": "example-key-3"
              }
            }
            """;

        var vault = JsonSerializer.Deserialize<SecretsVault>(onDrive, Options);

        Assert.NotNull(vault);
        Assert.Equal("example-key-1", vault!.Pulp.StreamingAvailabilityKey);
        Assert.Equal("example-key-2", vault.Pulp.OmdbKey);
        Assert.Equal("example-key-3", vault.Pulp.YouTubeKey);
        Assert.Equal("example-claude", vault.Claude.ApiKey);
    }

    [Fact]
    public void ThePulpSectionIsWrittenUnderExactlyItsFrozenNames()
    {
        var vault = new SecretsVault
        {
            Pulp = new PulpSecrets("example-key-1", "example-key-2", "example-key-3"),
        };

        using var written = JsonDocument.Parse(JsonSerializer.Serialize(vault, Options));

        // Ordinal, case-sensitive property lookups: the wire name is exactly these spellings.
        Assert.True(written.RootElement.TryGetProperty("Pulp", out var section), "no \"Pulp\" key on the wire");
        var names = section.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["StreamingAvailabilityKey", "OmdbKey", "YouTubeKey"], names);
        Assert.Equal("example-key-1", section.GetProperty("StreamingAvailabilityKey").GetString());
        Assert.Equal("example-key-2", section.GetProperty("OmdbKey").GetString());
        Assert.Equal("example-key-3", section.GetProperty("YouTubeKey").GetString());
    }

    [Fact]
    public void AVaultWrittenBeforePulpExistedLoadsWithAnEmptySection()
    {
        // What every vault on Drive looks like today: no Pulp key at all.
        const string onDrive = """
            {
              "Claude": { "ApiKey": "example-claude" },
              "DailyLoader": { "AnthropicApiKey": "example-foreword", "CalendarMappingJson": "" },
              "Tidal": { "ClientId": "example-client", "ClientSecret": "example-secret" },
              "OwnerAccount": "owner@example.com"
            }
            """;

        var vault = JsonSerializer.Deserialize<SecretsVault>(onDrive, Options);

        Assert.NotNull(vault);
        Assert.NotNull(vault!.Pulp);
        Assert.Equal(string.Empty, vault.Pulp.StreamingAvailabilityKey);
        Assert.Equal(string.Empty, vault.Pulp.OmdbKey);
        Assert.Equal(string.Empty, vault.Pulp.YouTubeKey);
        Assert.Equal("example-claude", vault.Claude.ApiKey);
        Assert.Equal("example-foreword", vault.Foreword.AnthropicApiKey);
        Assert.Equal("example-client", vault.Tidal.ClientId);
        Assert.Equal("owner@example.com", vault.OwnerAccount);
    }

    [Fact]
    public void PulpIsAHouseholdFactSoItLivesOnTheSharedVaultOnly()
    {
        // All three are household API keys — one subscription to each service, used by every
        // install. The personal vault is for what must differ between installs; a copy there would
        // be a second place to paste the same key and a second copy to go stale.
        var sharedNames = typeof(SecretsVault)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();
        var personalMembers = typeof(PersonalVault)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m is PropertyInfo or FieldInfo)
            .ToArray();

        Assert.Contains("Pulp", sharedNames);
        Assert.NotEmpty(personalMembers);
        Assert.DoesNotContain(personalMembers, m => m.Name.Contains("Pulp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(personalMembers, m =>
            (m as PropertyInfo)?.PropertyType == typeof(PulpSecrets)
            || (m as FieldInfo)?.FieldType == typeof(PulpSecrets));
    }
}
