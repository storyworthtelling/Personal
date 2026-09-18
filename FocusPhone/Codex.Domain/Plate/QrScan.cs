namespace Codex.Domain.Plate;

/// <summary>What Plate does with a code it has just read.</summary>
public enum QrAction
{
    /// <summary>Nothing was found in the frame.</summary>
    Nothing,

    /// <summary>Show the value on the page. Never in a dialog — §7 bans those.</summary>
    ShowText,

    /// <summary>
    /// Hand it to the browser as a plain VIEW intent, which the device-owner policy routes
    /// into Flyleaf's restricted browser.
    /// </summary>
    OpenInBrowser,

    /// <summary>
    /// Offer to join the network the code encodes, via Android's
    /// <c>ACTION_WIFI_ADD_NETWORKS</c> network-suggestion flow.
    /// </summary>
    JoinWifi,
}

/// <summary>
/// How a Wi-Fi QR code's network is secured — decides which passphrase setter the head
/// calls, and whether a join can be offered at all.
/// </summary>
public enum WifiSecurity
{
    /// <summary>No password (<c>nopass</c>, or a type this parser does not recognise).</summary>
    Open,

    /// <summary>
    /// WPA or WPA2 (the code's plain <c>WPA</c> means this — the spec predates WPA3), set
    /// through <c>WifiNetworkSuggestion.Builder.SetWpa2Passphrase</c>.
    /// </summary>
    Wpa2,

    /// <summary>
    /// WPA3, set through <c>WifiNetworkSuggestion.Builder.SetWpa3Passphrase</c> — kept apart
    /// from <see cref="Wpa2"/> since the two use different builder calls.
    /// </summary>
    Wpa3,

    /// <summary>
    /// WEP. Real, rare, and not expressible through <c>WifiNetworkSuggestion</c> at all —
    /// a join can never be offered for one, so this must stay distinct from every other
    /// value rather than folded into one of them.
    /// </summary>
    Wep,
}

/// <summary>A Wi-Fi network parsed out of a <c>WIFI:</c> QR code.</summary>
public readonly record struct WifiNetwork(string Ssid, string? Password, WifiSecurity Security, bool Hidden);

/// <summary>
/// The reading, and the exact text the code carried. <see cref="Network"/> is populated
/// alongside <see cref="Value"/> for a recognised Wi-Fi code, whatever its action ends up
/// being.
/// </summary>
public readonly record struct QrReading(QrAction Action, string Value, WifiNetwork? Network = null);

/// <summary>
/// Reading a scanned code.
///
/// <para>This was decided inside the head's ML Kit success listener, where it could not be
/// tested without a camera, and it ended in an <c>AlertDialog</c> for anything that was not
/// a link — which §7 bans outright: failures and results surface inline, never in a dialog.</para>
///
/// <para><b>Why the scheme check is narrow.</b> A QR code is untrusted input off a sticker
/// anyone can print. The head fires a plain <c>ACTION_VIEW</c> for whatever this says is a
/// link, and the device-owner policy routes http(s) into the restricted browser — but
/// <c>javascript:</c>, <c>file:</c>, <c>intent:</c> and <c>content:</c> are not the
/// browser's to receive, and an intent: URI in particular is a way to reach components the
/// lockdown exists to keep shut. Only http and https are ever handed out; everything else
/// is shown to the owner and goes no further.</para>
///
/// <para><b>Why the Wi-Fi parser is loose.</b> This is a personal single-user app, not a
/// defence against a hostile QR code, so the common unescaped
/// <c>WIFI:T:&lt;type&gt;;S:&lt;ssid&gt;;P:&lt;password&gt;;H:&lt;true|false&gt;;;</c> shape is
/// handled and a backslash-escaped <c>;</c> inside the SSID/password is not.</para>
/// </summary>
public static class QrScan
{
    public static QrReading Read(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new QrReading(QrAction.Nothing, string.Empty);
        }

        if (IsSafeWebAddress(raw))
        {
            return new QrReading(QrAction.OpenInBrowser, raw);
        }

        if (ParseWifi(raw) is { } network)
        {
            // WEP is real but WifiNetworkSuggestion cannot express it at all, so offering a
            // join here would silently fail. Showing the parsed SSID with an honest note is
            // the one case where the value on the page is not the raw code.
            return network.Security == WifiSecurity.Wep
                ? new QrReading(QrAction.ShowText, WepUnsupportedText(network), network)
                : new QrReading(QrAction.JoinWifi, raw, network);
        }

        return new QrReading(QrAction.ShowText, raw);
    }

    private static bool IsSafeWebAddress(string raw) =>
        Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        // "https://" parses as an absolute URI with an empty host, which is not somewhere
        // the browser can go.
        && !string.IsNullOrWhiteSpace(uri.Host);

    private static WifiNetwork? ParseWifi(string raw)
    {
        const string prefix = "WIFI:";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? ssid = null;
        string? password = null;
        var security = WifiSecurity.Open;
        var hidden = false;

        foreach (var field in raw[prefix.Length..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = field.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var value = field[(separator + 1)..];
            switch (field[..separator])
            {
                case "S":
                    ssid = value;
                    break;
                case "P":
                    password = value;
                    break;
                case "T":
                    security = ParseSecurity(value);
                    break;
                case "H":
                    hidden = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        // No SSID means this was not really a usable Wi-Fi code — shown as plain text like
        // any other code this parser does not recognise.
        return string.IsNullOrEmpty(ssid) ? null : new WifiNetwork(ssid, password, security, hidden);
    }

    private static WifiSecurity ParseSecurity(string value) => value.ToUpperInvariant() switch
    {
        "WPA" or "WPA2" => WifiSecurity.Wpa2,
        "WPA3" => WifiSecurity.Wpa3,
        "WEP" => WifiSecurity.Wep,
        // "nopass", empty, or anything unrecognised: treated as open rather than refused.
        _ => WifiSecurity.Open,
    };

    private static string WepUnsupportedText(WifiNetwork network) =>
        $"Wi-Fi network \"{network.Ssid}\" (WEP). WEP can't be joined directly from here — "
        + "connect to it from the phone's Wi-Fi settings instead.";
}
