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
}

/// <summary>The reading, and the exact text the code carried.</summary>
public readonly record struct QrReading(QrAction Action, string Value);

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
/// </summary>
public static class QrScan
{
    public static QrReading Read(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new QrReading(QrAction.Nothing, string.Empty);
        }

        return new QrReading(IsSafeWebAddress(raw) ? QrAction.OpenInBrowser : QrAction.ShowText, raw);
    }

    private static bool IsSafeWebAddress(string raw) =>
        Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        // "https://" parses as an absolute URI with an empty host, which is not somewhere
        // the browser can go.
        && !string.IsNullOrWhiteSpace(uri.Host);
}
