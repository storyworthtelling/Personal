namespace Index.Infrastructure.Configuration;

// Strongly-typed configuration. On device these are read live from MAUI Preferences (see the
// LiveOptions wrappers in MauiProgram); secrets (the Plaid client id/secret) are entered on the
// Settings page and never committed.
public sealed class PlaidOptions
{
    public const string SectionName = "Plaid";

    public string ClientId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;

    // Index uses Plaid's production environment only (D144) — there is no environment setting in
    // the app. This still selects the API host (a developer testing against Plaid's sandbox during
    // development edits this one default), but nothing wired into the shipped app ever sets it to
    // anything else: see MauiProgram.cs, which deliberately does not read a stored value into this.
    public string Environment { get; set; } = "production";

    // The base API host for the selected environment.
    public string BaseUrl => $"https://{Environment}.plaid.com";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Secret);
}

// Pacing and retry policy for the balance-refresh worker.
public sealed class RefreshOptions
{
    public const string SectionName = "Refresh";

    // How long after a successful refresh an institution becomes due again. Balances change slowly, so
    // once a day is plenty and keeps Plaid calls minimal.
    public int RefreshIntervalHours { get; set; } = 24;

    // Terminal-failure ceiling: after this many consecutive failed attempts an institution is marked
    // Failed and left alone until the user reconnects.
    public int MaxAttempts { get; set; } = 5;

    public int BaseDelaySeconds { get; set; } = 30;
    public int MaxDelaySeconds { get; set; } = 3_600;
}
