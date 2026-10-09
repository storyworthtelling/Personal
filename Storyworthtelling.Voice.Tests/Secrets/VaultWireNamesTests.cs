using System.Text.Json;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// The vault's JSON keys are its wire format, and they live on the owner's Drive.
///
/// <para>There is no naming policy, and <c>[JsonPropertyName]</c> appears only where a section's
/// wire name had to be pinned (Foreword's and Pulp's), so a C# property name <b>is</b> the key written to Drive. That makes every rename of a section a
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
}
