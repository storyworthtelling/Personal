# Ledger

A distraction-free Android app that shows the balances of your banks, credit cards, and loans in one
place. Ledger connects to your institutions through the **Plaid** aggregator, refreshes balances once a
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
2. **Enable the products** on your Plaid account: **Balance**, **Liabilities**, and **Transactions**.
   (Transactions is a Plaid subscription product — billed per connected Item in production — and powers
   the expandable recent-transactions panel; the link token requests it as the required `product` because
   Plaid rejects a link token with no product. **Liabilities** is requested via
   `required_if_supported_products`, so deposit-only institutions still connect; institutions that don't
   support Transactions, e.g. some standalone loan/mortgage servicers, may not connect and fall back to
   manual awareness.) No Android package/app-ID registration is needed: Ledger connects through **Hosted
   Link** (opened in the phone's browser), not the native Android SDK, so the link token omits
   `android_package_name` — sending it makes Plaid reject the token (`INVALID_FIELD`) until the package is
   registered, which a hosted flow never uses.
3. Install the APK (build it in CI — see below), open **Settings**, paste the client ID + secret,
   choose the **sandbox** environment, and **Save**.
4. Tap **Connect an account** → the Plaid Hosted Link page opens in your **browser** → finish signing in,
   return to Ledger, and tap **Done**. The account list populates after the first refresh. (Link opens in
   an external browser, not an in-app WebView, because OAuth banks like **Chase**, Bank of America, and
   Wells Fargo block embedded WebViews and error out — a real browser is the only surface they complete in.)
5. When you're happy, switch the environment to **production** and connect your real institutions.

Expect the four bank/card accounts plus a mortgage to connect cleanly; auto loans (e.g. Driveway),
home-improvement loans (e.g. GreenSky), and some credit unions may not be supported by Plaid.

## Security note (single-user, personal)

Plaid recommends the `secret` stay on a server. Ledger has no server by design, and the APK only ever
runs on your own phone, so the Plaid **client id/secret** live in MAUI `Preferences` and the
per-institution **access tokens** live in the app-private SQLite database — the same protection the
repo's other apps use for their API keys. This tradeoff is acceptable for a personal single-user tool;
don't ship this build to anyone else.

## Build & test

```bash
# Unit tests (no device, no keys — Plaid is faked)
dotnet test Ledger/Ledger.Tests/Ledger.Tests.csproj -p:LedgerTfms=net10.0

# Signed APK: pushed to the branch, built by .github/workflows/build-ledger.yml
#   → download the "Ledger-Android" artifact from the run.
```

The CI build regenerates the launcher icon with `Tools/IconStudio` and fails if the custom icon isn't
packaged (the ICON CHECK). Launchers cache icons across same-signature updates — uninstall/reinstall or
reboot to see a new icon.

## Architecture

- `Ledger.Domain` (`net10.0`) — POCOs (`Institution`, `Account`), enums, `IPlaidClient` contract, the
  shared `IClock`/`RelativeTime`/`TimeFormats` helpers, and pure pipeline helpers.
- `Ledger.Infrastructure` (`net10.0;net10.0-android`) — `LedgerDbContext` (EF Core + SQLite),
  `PlaidClient`, the resumable `PipelineRunner` + `BalanceRefreshProcessor`, and `AccountService`.
- `Ledger.Android` (`net10.0-android`, MAUI) — pages, `MauiProgram` DI, `RefreshWorker` (WorkManager),
  `SettingsStore`, and the `PlaidLinkPage` browser-based connect flow.
- `Ledger.Tests` (`net10.0`) — xUnit + Moq; Plaid over a fake HTTP handler; design leak-guard/parity
  and time tests.

The connect flow (Hosted Link → `public_token` → exchange) is the one seam to finalize against Plaid's
current Hosted Link documentation; the refresh/storage/display path is fully covered by tests. The hosted
Link URL is opened in an **external browser** (an Android Custom Tab via `Browser.Default.OpenAsync`), not
an embedded WebView: OAuth institutions such as **Chase**, Bank of America, and Wells Fargo fingerprint the
browser environment and refuse to render their login inside a WebView (a generic "this part of our site
isn't working" error), so a real browser is the only surface that completes. When the user returns and
taps **Done**, the app polls `/link/token/get` for the completed session — the same finish step regardless
of which browser completed it.

On the owner's **FocusPhone** the device-owner policy suspends every real browser and pins FocusPhone's own
single-domain WebView as the default http/https handler, so opening Plaid Link there would dead-end (a
WebView Chase blocks, confined to one domain so the OAuth redirect is refused). `PlaidLinkPage` detects this
up front (`ConnectBrowserPolicy.IsRestrictedViewer`, keyed off the resolved default-browser package) and,
instead of minting an unusable link token, tells the owner to connect banks on a browser-capable phone — the
encrypted vault backup restores the access token onto the FocusPhone, where the daily refresh needs no
browser. The detection query needs the `<queries>` web-intent entry in the manifest for Android 11+ package
visibility; the decision itself is pure and unit-tested.
