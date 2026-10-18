using System.Text.Json;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// Constitution's bed side lives in each person's own vault: the household shares one Sleep Number login, so which
/// side of the bed is mine is the one thing that must differ between the two installs. In the shared vault it would
/// be restored from the first phone onto the second, and one adult's night would be read as the other's. The key on
/// the wire is the property name (no naming policy), so its spelling is as permanent as <c>WorkScheduleJson</c>'s;
/// the tests write the file as Drive holds it, not as a round trip.
/// </summary>
public class PersonalVaultSleeperSideTests
{
    [Fact]
    public void ASideIsWrittenUnderItsFrozenName()
    {
        var vault = PersonalVault.Empty with { SleeperSide = "left" };

        using var written = JsonDocument.Parse(JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions));

        Assert.True(written.RootElement.TryGetProperty("SleeperSide", out var side), "no \"SleeperSide\" key on the wire");
        Assert.Equal("left", side.GetString());
    }

    [Fact]
    public void ASideWrittenOnDriveLoadsIntoTheField()
    {
        const string onDrive = """
            {
              "HealthConnectChoicesJson": "{}",
              "SleeperSide": "right"
            }
            """;

        var vault = JsonSerializer.Deserialize<PersonalVault>(onDrive, VaultEnvelope.SerializerOptions);

        Assert.NotNull(vault);
        Assert.Equal("right", vault!.SleeperSide);
        Assert.Equal("{}", vault.HealthConnectChoicesJson);
    }

    [Fact]
    public void APersonalVaultWrittenBeforeTheSideExistedLoadsEmpty()
    {
        const string older = """
            {
              "OwnerColumn": "Sam",
              "HealthConnectChoicesJson": "{}"
            }
            """;

        var vault = JsonSerializer.Deserialize<PersonalVault>(older, VaultEnvelope.SerializerOptions);

        Assert.NotNull(vault);
        Assert.Equal(string.Empty, vault!.SleeperSide);
        Assert.Equal("Sam", vault.OwnerColumn);
    }

    [Fact]
    public void TheSharedVaultHasNoPlaceForASide()
    {
        var shared = JsonSerializer.Serialize(SecretsVault.Empty, VaultEnvelope.SerializerOptions);

        Assert.DoesNotContain("SleeperSide", shared, StringComparison.Ordinal);
    }
}
