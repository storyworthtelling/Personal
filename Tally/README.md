# Tally

A distraction-free Android app that shows the balances of your banks, credit cards, and loans in one
place. Tally connects to your institutions through the **Plaid** aggregator, refreshes balances once a
day on-device (AndroidX WorkManager), stores them in SQLite, and lists them in the repo's Paper & Ink
style. There is **no backend** — everything runs on your phone, mirroring DailyLoader/Storyboarder.

## Why Plaid

US consumer banks don't offer a per-user API you can call to read your own balances. An aggregator
(Plaid, MX, Finicity, Yodlee) is the realistic path. Plaid is used here because it's the only major
aggregator with a free personal Trial plan (real production data, capped at 10 connected "Items" =
10 institution logins — enough for a personal set of accounts).

## Setup

1. **Create a Plaid account** at [dashboard.plaid.com](https://dashboard.plaid.com) and copy your
   **client ID** and a **secret** (start with the Sandbox secret).
2. **Register the Android app** as an Android OAuth/Link client for package
   `com.storyworthtelling.tally`, and enable the **Balance** and **Liabilities** products.
3. Install the APK (build it in CI — see below), open **Settings**, paste the client ID + secret,
   choose the **sandbox** environment, and **Save**.
4. Tap **Connect an account** → finish Plaid Hosted Link in the WebView → **Done**. The account list
   populates after the first refresh.
5. When you're happy, switch the environment to **production** and connect your real institutions.

Expect the four bank/card accounts plus a mortgage to connect cleanly; auto loans (e.g. Driveway),
home-improvement loans (e.g. GreenSky), and some credit unions may not be supported by Plaid.

## Security note (single-user, personal)

Plaid recommends the `secret` stay on a server. Tally has no server by design, and the APK only ever
runs on your own phone, so the Plaid **client id/secret** live in MAUI `Preferences` and the
per-institution **access tokens** live in the app-private SQLite database — the same protection the
repo's other apps use for their API keys. This tradeoff is acceptable for a personal single-user tool;
don't ship this build to anyone else.

## Build & test

```bash
# Unit tests (no device, no keys — Plaid is faked)
dotnet test Tally/Tally.Tests/Tally.Tests.csproj -p:TallyTfms=net10.0

# Signed APK: pushed to the branch, built by .github/workflows/build-tally.yml
#   → download the "Tally-Android" artifact from the run.
```

The CI build regenerates the launcher icon with `Tools/IconStudio` and fails if the custom icon isn't
packaged (the ICON CHECK). Launchers cache icons across same-signature updates — uninstall/reinstall or
reboot to see a new icon.

## Architecture

- `Tally.Domain` (`net10.0`) — POCOs (`Institution`, `Account`), enums, `IPlaidClient` contract, the
  shared `IClock`/`RelativeTime`/`TimeFormats` helpers, and pure pipeline helpers.
- `Tally.Infrastructure` (`net10.0;net10.0-android`) — `TallyDbContext` (EF Core + SQLite),
  `PlaidClient`, the resumable `PipelineRunner` + `BalanceRefreshProcessor`, and `AccountService`.
- `Tally.Android` (`net10.0-android`, MAUI) — pages, `MauiProgram` DI, `RefreshWorker` (WorkManager),
  `SettingsStore`, and the `PlaidLinkPage` WebView connect flow.
- `Tally.Tests` (`net10.0`) — xUnit + Moq; Plaid over a fake HTTP handler; design leak-guard/parity
  and time tests.

The connect flow (Hosted Link → `public_token` → exchange) is the one seam to finalize against Plaid's
current Hosted Link documentation; the refresh/storage/display path is fully covered by tests.
