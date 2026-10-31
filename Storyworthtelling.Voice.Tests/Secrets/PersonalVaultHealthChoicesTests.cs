using System.Text.Json;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// Constitution's Health Connect choices (P13, plan choice PC3) live in each person's own vault.
///
/// <para>They are a per-install fact: which signals this person agreed to share with the app. In the
/// shared vault they would be restored from the first phone onto the second, and one adult's choices
/// would become the other's — Foreword Release I's bug again. So the field lives on
/// <see cref="PersonalVault"/>, whose key on the wire is the property name (the vault has no naming
/// policy), which makes the spelling below as permanent as <c>WorkScheduleJson</c>'s. Written as the
/// file actually on Drive rather than as a round trip, which passes happily with the property renamed
/// at both ends.</para>
/// </summary>
public class PersonalVaultHealthChoicesTests
{
    [Fact]
    public void HealthConnectChoicesAreWrittenUnderTheirFrozenName()
    {
        var vault = PersonalVault.Empty with { HealthConnectChoicesJson = "{\"signals\":[\"steps\"]}" };

        using var written = JsonDocument.Parse(JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions));

        Assert.True(
            written.RootElement.TryGetProperty("HealthConnectChoicesJson", out var choices),
            "no \"HealthConnectChoicesJson\" key on the wire");
        Assert.Equal("{\"signals\":[\"steps\"]}", choices.GetString());
    }

    [Fact]
    public void ChoicesWrittenOnDriveLoadIntoTheField()
    {
        const string onDrive = """
            {
              "WorkScheduleJson": "{\"blocks\":[]}",
              "HealthConnectChoicesJson": "{\"signals\":[\"sleep\"]}"
            }
            """;

        var vault = JsonSerializer.Deserialize<PersonalVault>(onDrive, VaultEnvelope.SerializerOptions);

        Assert.NotNull(vault);
        Assert.Equal("{\"signals\":[\"sleep\"]}", vault!.HealthConnectChoicesJson);
        Assert.Equal("{\"blocks\":[]}", vault.WorkScheduleJson);
    }

    [Fact]
    public void APersonalVaultWrittenBeforeHealthChoicesExistedLoadsWithAnEmptyField()
    {
        // Exactly what Foreword and Score have already put on Drive: no such key at all.
        const string older = """
            {
              "TidalAccount": { "UserId": "listener-1", "RefreshToken": "example-refresh" },
              "OwnerColumn": "Sam",
              "CalendarMappingJson": "{}",
              "WorkScheduleJson": "{}"
            }
            """;

        var vault = JsonSerializer.Deserialize<PersonalVault>(older, VaultEnvelope.SerializerOptions);

        Assert.NotNull(vault);
        Assert.Equal(string.Empty, vault!.HealthConnectChoicesJson);
        Assert.Equal("listener-1", vault.TidalAccount.UserId);
        Assert.Equal("Sam", vault.OwnerColumn);
    }

    [Fact]
    public void TheSharedVaultHasNoPlaceForHealthChoices()
    {
        var shared = JsonSerializer.Serialize(SecretsVault.Empty, VaultEnvelope.SerializerOptions);

        Assert.DoesNotContain("HealthConnect", shared, StringComparison.Ordinal);
    }
}
