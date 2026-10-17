using System.Reflection;
using System.Text.Json;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// The vault's JSON keys are its wire format, and they live on the owner's Drive.
///
/// <para>There is no naming policy, and <c>[JsonPropertyName]</c> appears only where a section's
/// wire name had to be pinned (Foreword's, Pulp's and Constitution's), so a C# property name <b>is</b> the key written to Drive. That makes every rename of a section a
/// silent data loss: deserialisation looks for the new name, does not find it, and falls back to
/// the record's default — empty strings — so the app comes up as though it had never been
/// configured, on every phone at once. Nothing throws.</para>
///
/// <para>P9 renames DailyLoader to Foreword, which is exactly that hazard. The section is renamed
/// in code and pinned to its old key on the wire, and this is the test that says the wire key did
/// not move. The calendar mapping JSON is the reason it matters most: the vault holds it because
/// it is, in its own comment, "a magic string that is tedious to reconstruct by hand".</para>
/// </summary>
public class VaultWireNamesTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void ForewordsSectionIsStillWrittenUnderItsOriginalKey()
    {
        var vault = new SecretsVault
        {
            Foreword = new ForewordSecrets("anthropic-key", "{\"CalendarColumns\":{}}"),
        };

        var json = JsonSerializer.Serialize(vault, Options);

        Assert.Contains("\"DailyLoader\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Foreword\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AVaultWrittenBeforeTheRenameStillLoads()
    {
        // Exactly what an existing vault on Drive looks like: the section is called DailyLoader,
        // because that is what the app was called when it was written.
        const string onDrive = """
            {
              "DailyLoader": {
                "AnthropicApiKey": "sk-ant-existing",
                "CalendarMappingJson": "{\"FamilyCalendarId\":\"family@group.calendar.google.com\"}"
              }
            }
            """;

        var vault = JsonSerializer.Deserialize<SecretsVault>(onDrive, Options);

        Assert.NotNull(vault);
        Assert.Equal("sk-ant-existing", vault!.Foreword.AnthropicApiKey);
        Assert.Contains("family@group.calendar.google.com", vault.Foreword.CalendarMappingJson);
    }

    [Fact]
    public void ARoundTripThroughDriveKeepsBothValues()
    {
        var before = new SecretsVault { Foreword = new ForewordSecrets("key", "mapping") };

        var after = JsonSerializer.Deserialize<SecretsVault>(
            JsonSerializer.Serialize(before, Options), Options);

        Assert.Equal("key", after!.Foreword.AnthropicApiKey);
        Assert.Equal("mapping", after.Foreword.CalendarMappingJson);
    }

    // Constitution's section (P13, C-8.2.1). Written as the file actually on Drive, not as a round
    // trip: a round trip passes happily with the property renamed at both ends, which is the bug.
    // The vault's own serializer settings are used, and every value is invented.
    private static readonly string PlaceholderPassword = "not-a-" + "real-password";

    [Fact]
    public void ConstitutionSection()
    {
        var onDrive = $$"""
            {
              "Claude": { "ApiKey": "example-claude" },
              "Constitution": {
                "SleepNumberLogin": "sleeper@example.com",
                "SleepNumberPassword": "{{PlaceholderPassword}}"
              }
            }
            """;

        var vault = JsonSerializer.Deserialize<SecretsVault>(onDrive, VaultEnvelope.SerializerOptions);

        Assert.NotNull(vault);
        Assert.Equal("sleeper@example.com", vault!.Constitution.SleepNumberLogin);
        Assert.Equal(PlaceholderPassword, vault.Constitution.SleepNumberPassword);
        Assert.Equal("example-claude", vault.Claude.ApiKey);
    }

    [Fact]
    public void ConstitutionIsWrittenUnderExactlyItsFrozenNames()
    {
        var vault = new SecretsVault
        {
            Constitution = new ConstitutionSecrets("sleeper@example.com", PlaceholderPassword),
        };

        using var written = JsonDocument.Parse(JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions));

        // Ordinal, case-sensitive: the wire name is exactly these spellings.
        Assert.True(written.RootElement.TryGetProperty("Constitution", out var section), "no \"Constitution\" key on the wire");
        Assert.Equal(["SleepNumberLogin", "SleepNumberPassword"], section.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void AVaultWrittenBeforeConstitutionExistedLoadsWithAnEmptySection()
    {
        const string older = """
            {
              "Claude": { "ApiKey": "example-claude" },
              "DailyLoader": { "AnthropicApiKey": "example-foreword", "CalendarMappingJson": "" },
              "Pulp": { "OmdbKey": "example-key-2" },
              "OwnerAccount": "owner@example.com"
            }
            """;

        var vault = JsonSerializer.Deserialize<SecretsVault>(older, VaultEnvelope.SerializerOptions);

        Assert.NotNull(vault);
        Assert.NotNull(vault!.Constitution);
        Assert.Equal(string.Empty, vault.Constitution.SleepNumberLogin);
        Assert.Equal(string.Empty, vault.Constitution.SleepNumberPassword);
        Assert.Equal("example-foreword", vault.Foreword.AnthropicApiKey);
        Assert.Equal("example-key-2", vault.Pulp.OmdbKey);
    }

    [Fact]
    public void ConstitutionsSectionHoldsOnlyTheHouseholdLoginAndNothingPerInstall()
    {
        // The section is exactly the Sleep Number login: a decision to add a field has to edit this
        // list. Per-install facts (Health Connect choices, the work schedule) belong in PersonalVault
        // — restored from the first phone onto the second they would be Foreword Release I's bug.
        var fields = typeof(ConstitutionSecrets)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["SleepNumberLogin", "SleepNumberPassword"], fields);

        var personal = typeof(PersonalVault)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();
        Assert.DoesNotContain(personal, n => n.Contains("Constitution", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(personal, n => n.Contains("SleepNumber", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, n =>
            n.Contains("Health", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Work", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConstitutionIsASharedSectionAndSurvivesAnotherAppsPublish()
    {
        var before = SecretsVault.Empty with
        {
            Constitution = new ConstitutionSecrets("sleeper@example.com", PlaceholderPassword),
        };

        var merged = before with { Pulp = new PulpSecrets("example-key-1") };

        Assert.Contains("Constitution", typeof(SecretsVault).GetProperties().Select(p => p.Name));
        Assert.Equal("sleeper@example.com", merged.Constitution.SleepNumberLogin);
        Assert.Equal(PlaceholderPassword, merged.Constitution.SleepNumberPassword);
    }
}
