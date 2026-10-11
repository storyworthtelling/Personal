namespace Pulp.Tests.Calendar;

/// <summary>
/// The Vault page is where the owner gives the calendar consent. The Android head cannot be loaded here, so
/// the wiring is checked by reading it.
/// </summary>
public class VaultCalendarConsentGuardTests
{
    [Fact]
    public void TheVaultPageOffersTheCalendarConsentAndSaysSoInline()
    {
        // Bug kept out: no way to give the consent (the nightly writer waits forever); a button drawn on the
        // other phone; a cancelled chooser shown as a failure; a success toast instead of a status change.
        var path = Path.Combine(PulpSource.Root(), "Pulp.Android/Vault/VaultActivity.cs");
        Assert.True(File.Exists(path), "VaultActivity.cs does not exist.");
        var text = File.ReadAllText(path);

        Assert.Contains("CalendarConnection.Status(services.HouseholdFileRole)", text, StringComparison.Ordinal);
        Assert.Contains("CalendarConnection.OffersConnect(services.HouseholdFileRole)", text, StringComparison.Ordinal);
        Assert.Contains("services.CalendarConnection.ConnectAsync(this)", text, StringComparison.Ordinal);
        Assert.Contains("CancellationVerdict.IsUserCancel(", text, StringComparison.Ordinal);
        Assert.Contains("VoiceGoogleAccount.HandleChooserResult(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Toast", text, StringComparison.Ordinal);
    }
}
