using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// What a scanned code means. The shipped head decides this inside an ML Kit success
/// listener and then shows anything non-web in an AlertDialog, which §7 bans outright —
/// and being inside the listener means the rule cannot be tested without a camera.
/// </summary>
public class QrScanTests
{
    [Theory]
    [InlineData("https://harbourline.com/warranty/8841-B")]
    [InlineData("http://example.org")]
    [InlineData("HTTPS://SHOUTING.EXAMPLE")]
    public void AWebAddressIsHandedToTheBrowser(string raw)
    {
        var reading = QrScan.Read(raw);

        Assert.Equal(QrAction.OpenInBrowser, reading.Action);
        Assert.Equal(raw, reading.Value);
    }

    [Theory]
    [InlineData("BEGIN:VCARD\nFN:Marguerite Okonjo\nEND:VCARD")]
    [InlineData("just some text")]
    [InlineData("8841-B")]
    public void AnythingElseIsShownRatherThanActedOn(string raw)
    {
        var reading = QrScan.Read(raw);

        Assert.Equal(QrAction.ShowText, reading.Action);
        Assert.Equal(raw, reading.Value);
    }

    [Theory]
    [InlineData("WIFI:S:Home;T:WPA;P:hunter2;;", "Home", "hunter2", WifiSecurity.Wpa2)]
    [InlineData("WIFI:T:WPA2;S:Upstairs;P:reallysecret;;", "Upstairs", "reallysecret", WifiSecurity.Wpa2)]
    [InlineData("WIFI:T:WPA3;S:NewRouter;P:letmein123;;", "NewRouter", "letmein123", WifiSecurity.Wpa3)]
    public void AWpaWifiCodeOffersToJoin(string raw, string ssid, string password, WifiSecurity security)
    {
        var reading = QrScan.Read(raw);

        Assert.Equal(QrAction.JoinWifi, reading.Action);
        // Kept exactly as scanned, like every other reading — this is the one case the code
        // deserves an action for, not the one case it deserves special handling of the text.
        Assert.Equal(raw, reading.Value);
        Assert.Equal(new WifiNetwork(ssid, password, security, Hidden: false), reading.Network);
    }

    [Theory]
    [InlineData("WIFI:T:nopass;S:Guest Wifi;;")]
    [InlineData("WIFI:S:Guest Wifi;;")]
    public void AnOpenWifiCodeOffersToJoinWithNoPassword(string raw)
    {
        var reading = QrScan.Read(raw);

        Assert.Equal(QrAction.JoinWifi, reading.Action);
        Assert.Equal(new WifiNetwork("Guest Wifi", null, WifiSecurity.Open, Hidden: false), reading.Network);
    }

    [Fact]
    public void AHiddenWifiNetworkIsCarriedThrough()
    {
        var reading = QrScan.Read("WIFI:T:WPA;S:Backroom;P:hunter2;H:true;;");

        Assert.Equal(QrAction.JoinWifi, reading.Action);
        Assert.Equal(new WifiNetwork("Backroom", "hunter2", WifiSecurity.Wpa2, Hidden: true), reading.Network);
    }

    [Fact]
    public void AWepWifiCodeIsShownRatherThanOfferedAsAJoin()
    {
        // WifiNetworkSuggestion cannot express WEP at all, so pretending to offer a join
        // would silently fail. It falls back to text — but the parsed SSID with an honest
        // note, not the raw code, since the owner needs to know why there is no button.
        var reading = QrScan.Read("WIFI:T:WEP;S:OldRouter;P:oldpass;;");

        Assert.Equal(QrAction.ShowText, reading.Action);
        Assert.Contains("OldRouter", reading.Value);
        Assert.Contains("WEP", reading.Value);
        Assert.Equal(new WifiNetwork("OldRouter", "oldpass", WifiSecurity.Wep, Hidden: false), reading.Network);
    }

    [Fact]
    public void AWifiCodeWithNoSsidIsShownAsPlainText()
    {
        // Not really a usable Wi-Fi code — treated like any other text this parser does not
        // recognise, raw value carried through untouched.
        const string raw = "WIFI:T:WPA;P:hunter2;;";
        var reading = QrScan.Read(raw);

        Assert.Equal(QrAction.ShowText, reading.Action);
        Assert.Equal(raw, reading.Value);
        Assert.Null(reading.Network);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingScannedIsItsOwnAnswer(string? raw)
    {
        // Distinct from ShowText: there is nothing to show, and the screen should say the
        // camera found no code rather than display an empty box.
        Assert.Equal(QrAction.Nothing, QrScan.Read(raw).Action);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("intent://scan/#Intent;scheme=x;end")]
    [InlineData("content://com.android.providers.media/external")]
    public void OnlyHttpAndHttpsAreEverHandedOut(string raw)
    {
        // A QR code is untrusted input from a sticker anyone can print. The head fires a
        // plain VIEW intent for whatever it is told is a link, and the device-owner policy
        // routes http(s) into the restricted browser — but javascript:, file:, intent: and
        // content: are not the browser's to receive and must never leave this app as one.
        Assert.Equal(QrAction.ShowText, QrScan.Read(raw).Action);
    }

    [Fact]
    public void AMalformedUriIsShownRatherThanOpened()
    {
        Assert.Equal(QrAction.ShowText, QrScan.Read("https://").Action);
        Assert.Equal(QrAction.ShowText, QrScan.Read("http://  ").Action);
    }

    [Fact]
    public void TheValueIsCarriedThroughUntouched()
    {
        // Whatever is shown must be exactly what the code said; trimming or re-casing it
        // would misreport a password or a serial number.
        const string raw = "  8841-B  ";
        Assert.Equal(raw, QrScan.Read(raw).Value);
    }
}
