namespace Index.Domain.Connect;

/// <summary>
/// Every word on the connect page (mockup 5), in one place so a test can hold it against the drawing
/// instead of a loose "contains". Two rules the copy keeps on purpose:
///
/// <para><b>No timings.</b> How long Admin mode takes to open and how long it stays open are Flyleaf's
/// to change, so no Index screen repeats them (D133). <c>AdminModeUntimedTests</c> reads this file.</para>
///
/// <para><b>No instruction to confirm.</b> Coming back from Chrome is enough: the page checks for a
/// finished session when it reappears, so there is nothing to tap to say "I'm back".</para>
/// </summary>
public static class ConnectCopy
{
    // ---- frame 1: Admin mode needed ----
    public const string NeededHeading = "Admin mode needed";
    public const string NeededWhy =
        "Your bank's sign-in page opens in Chrome, and this phone only runs Chrome while Admin mode is open.";
    public const string NeededWhere = "Open Admin mode from Flyleaf's settings, then come back here.";
    public const string NeededNotRequired =
        "Balances don't need it. Once a bank is connected they refresh every day without a browser.";

    /// <summary>The old instruction, kept as the page's quiet line: the vault brings the bank here.</summary>
    public const string Fallback = "Connect on another phone instead";

    // ---- the third state: no Chrome to wait for ----
    public const string ChromeMissingHeading = "Chrome not found";
    public const string ChromeMissingWhy =
        "Your bank's sign-in page opens in Chrome, and Index can't find it on this phone.";

    // ---- frame 2: Admin mode is open ----
    public const string OpenHeading = "Admin mode is open";
    public static readonly IReadOnlyList<string> OpenSteps =
    [
        "Chrome opens Plaid's page.",
        "Choose your bank and sign in there.",
        "Come back to Index. It notices the new bank on its own.",
    ];
    public const string OpenPlaidButton = "Open Plaid in Chrome";

    // ---- frame 3: back from Chrome, waiting for Plaid ----
    public const string FinishingHeading = "Finishing up";
    public const string FinishingBody =
        "This usually takes a few seconds. If Chrome closed before its last page, open it again.";
    public const string OpenChromeAgain = "Open Chrome again";
    public const string CheckAgainButton = "Check again";

    /// <summary>
    /// The teal status sentence. Names the bank when it is known (a reconnect knows it up front; a fresh
    /// link learns it only when Plaid hands it over) and never prints an empty slot when it is not.
    /// </summary>
    public static string Waiting(string? institutionName) =>
        $"Waiting for Plaid to hand over {(string.IsNullOrWhiteSpace(institutionName) ? "your bank" : institutionName.Trim())}…";

    // ---- before the page is reached ----
    /// <summary>
    /// Shown by the entry points (the Add-an-account chooser, a reconnect) instead of a raw
    /// "Couldn't start Plaid Link" error when no Plaid keys are set. Settings says the same.
    /// </summary>
    public const string NotConfigured = "Enter your Plaid client ID and secret in Settings before connecting.";
}
