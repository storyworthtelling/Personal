# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Personal projects repository — primarily background automation apps, with occasional MAUI mobile apps and Angular frontends. Projects are added incremenledger; update this file with project-specific build/test commands when a new one lands.

## Design

**`DESIGN.md` at the repo root is the Paper & Ink design bible** — the single source of truth for palette, typography, motion, materiality, spacing, components, PDF page specs, icon specs, voice, and accessibility across every project. Rules that matter when writing code:

- **No color hex or type-size literals outside the registered token files** (DESIGN.md §16). Leak-guard unit tests in every project enforce this; consume tokens instead (`FocusPhone.Domain.Design.DesignTokens` + `FocusPhone.Ui.Android` `PaperUi`/`PaperTheme` for FocusPhone, `Storyboarder.Domain.Design.DesignTokens` + `Resources/Styles/DesignTokens.xaml` styles for the MAUI apps).
- The six shared fonts live in `/Fonts` (Source Serif 4, Source Sans 3, Cascadia Code — static TTFs, OFL). Native heads package them as assets via `FocusPhone.Ui.Android`; MAUI heads via `<MauiFont>`; the Skia renderers via embedded resources (`EmbeddedTypefaces`).
- **Interaction rules are load-bearing** (DESIGN.md §5–§11): success is an inline state change (no success toasts/alerts), destructive acts are undo-first (an `UndoBar` window, not a confirm dialog), loading is a status sentence not a spinner, primary actions live in a pinned bottom bar, screens open with a Serif in-page header, time is 12-hour and list timestamps are relative (`RelativeTime`/`TimeFormats`, duplicated per Domain and kept identical by a parity test).
- **Generated artifacts** (icons, the paper-grain tile, the owner branding medallion) are all produced by the committed **`Tools/IconStudio`** console: `dotnet run --project Tools/IconStudio -- --repo-root .` (tests: `dotnet test Tools/IconStudio.sln`). Never hand-edit generated resources. **Owner photo swap:** drop a square image at `/Branding/owner.png` and rerun IconStudio — it circle-masks the photo into the medallion for every consumer (PDF covers + settings screens) and never clobbers an existing `owner.png`.
- **Owner's voice is pending** (DESIGN.md §11): the intent is to learn the owner's wit from their self-authored Google Drive docs, which needs the Google Drive connector authorized in claude.ai. Until then copy stays quiet-neutral.
- New projects start on-system: see DESIGN.md §15 for the MAUI/Angular defaults.

## Projects

### FocusPhone

Distraction-free Pixel 8a: a device-owner launcher/DPC plus companion apps (see `FocusPhone/`).

- Tests: `dotnet test FocusPhone/FocusPhone.Tests/FocusPhone.Tests.csproj` (Domain + Infrastructure + design-token guards; no device needed — target the Tests csproj so the build skips the Android heads)
- Shared UI: `FocusPhone.Ui.Android` (referenced by all four heads) carries the Paper & Ink theme (`Theme.FocusPhone.Paper`), `PaperUi` view factories, `PaperListAdapter`, and the font assets; design constants live in `FocusPhone.Domain/Design/DesignTokens.cs`
- Android heads target `net10.0-android` and need the Android SDK. In environments without one (e.g. sandboxed CI), a minimal SDK works: `android.jar` under `platforms/android-36/` plus stub `platform-tools/adb`, then build with `-p:AndroidSdkDirectory=<sdk> -p:AndroidUseApkSigner=false` (workload bundles aapt2/r8; jarsigner signs debug builds)
- App IDs are `com.storyworthtelling.focusphone.*` and must match `PolicyContext` constants in Domain
- APKs build in CI via `.github/workflows/build-focusphone.yml` (test job → signed build of all four heads → upload). Signs with the shared repo keystore secrets (`KEYSTORE_BASE64`/`KEY_ALIAS`/`KEY_PASSWORD`/`KEYSTORE_PASSWORD`, same as DailyLoader/Storyboarder) so all four heads share one signature for the device-owner update channel; download the **FocusPhone-Android** artifact from the run
- The Camera app's ML Kit / Play-services bindings download .aar payloads from dl.google.com at build time (Xamarin.Build.Download). CI builds it fine (the runner has network); only a network-restricted sandbox can't — build Camera in CI or on a normal dev machine, not the sandbox

### Storyboarder

An **Android (MAUI) app** that turns a Google Docs novel into a multi-volume graphic novel: Claude builds a design bible and storyboards the book; Gemini renders the panels; a resumable pipeline assembles one PDF per volume. After the volumes, a final `ComposingAnalysis` stage produces one additional **book-analysis (revision) PDF** per novel — Claude map-reduces the manuscript into a report (character map + arcs, plot holes, inconsistencies/inaccuracies, multi-perspective reviews, a prioritized revision plan, plus line-level edits, prose style & voice, dialogue critique and a scene-by-scene breakdown) that is rendered to raster pages via SkiaSharp and wrapped by the existing image-based PDF composer (no PDF-text/font machinery). Chapters are detected at ingestion (`ChapterDetector`) so every finding cites a chapter; the reduce is **two** Claude calls (story half + craft half, persisted separately) because one reply can't carry both without truncating or outlasting the background worker. `Novel.ReportOnly` skips every art stage and runs the design bible straight into the report. Modeled on DailyLoader: an AndroidX **WorkManager** periodic worker drains the pipeline on-device (no web host, no SignalR). See `Storyboarder/`.

- Tests: `dotnet test Storyboarder/Storyboarder.sln -p:StoryboarderTfms=net10.0` (Domain + Infrastructure + Tests; no device, no API keys — external services are faked). The `-p:StoryboarderTfms=net10.0` switch builds Infrastructure for the desktop TFM only so the suite runs without the MAUI/Android workload.
- Android head (`Storyboarder.Android`, `net10.0-android`, app id `com.storyworthtelling.storyboarder`): build with the MAUI/Android workload + Android SDK (same minimal-SDK note as FocusPhone — `platforms/android-34/`, `-p:AndroidSdkDirectory=<sdk> -p:AndroidUseApkSigner=false`); signs with the default debug keystore. Background work uses `Xamarin.AndroidX.Work.Runtime`.
- Secrets are entered in-app on the Settings page (stored in MAUI `Preferences`): Claude + Gemini API keys. Google Docs access uses native device-account sign-in via Android `AccountManager` (no Play Services binding, so **no `dl.google.com` build dependency** — unlike the FocusPhone Camera app); a Drive doc picker lists the user's Google Docs. Sign-in requires the app to be registered as an **Android OAuth client** (package `com.storyworthtelling.storyboarder` + signing-cert SHA-1) in a Google Cloud project with the Docs+Drive APIs enabled and the user added as a test user, else it fails with `UnregisteredOnApiConsole`; the Settings page surfaces the exact package + SHA-1 to register (see `Storyboarder/README.md`).
- The launcher icon is **native** Android adaptive-icon resources under `Storyboarder.Android/Resources/mipmap-*` (`mipmap-anydpi-v26/appicon.xml` + per-density `appicon`/`appicon_foreground` PNGs + `values/colors.xml`). MAUI's `MauiIcon`/resizetizer pipeline is **bypassed**. Two load-bearing details make it actually ship (both apps had the *default* icon until fixed — DailyLoader too, with `ic_launcher` naming): (1) the resources live in the plain **`Resources/`** folder, **not** `Platforms/Android/Resources`. These heads don't set `<SingleProject>true</SingleProject>`, so MAUI never repoints the Android resource prefix — it stays the .NET-for-Android default `Resources`, and the active glob is only `Resources/*/*` (one level, specific extensions; that's also why `Resources/Styles/DesignTokens.xaml` is left alone). Anything under `Platforms/Android/Resources` is silently dropped. (2) `android:icon` is set via `[Application(Icon="@mipmap/appicon", RoundIcon="@mipmap/appicon_round")]` on `MainApplication` — **not** the XML `AndroidManifest.xml`, which (same reason) isn't wired into these heads at all (that's why permissions live in `[assembly: UsesPermission]` attributes too). The CI `build` job runs an **ICON CHECK** (`aapt2 dump badging` + `unzip`) that reads the resolved `android:icon` back out of the APK and **fails the build if it's empty** — a green build now proves the custom icon is present. (Launchers cache icons across same-signature updates — uninstall/reinstall or reboot to see a new icon.) All icon resources are (re)generated by the committed `Tools/IconStudio` console — see the Design section above.
- `Storyboarder.Infrastructure` multi-targets `net10.0;net10.0-android`; `Storyboarder.Domain` is `net10.0`. Data layer is EF Core + SQLite (the prescribed data-access choice); SkiaSharp composes pages, PdfSharp assembles volume PDFs. On device the DB and image/PDF output live under `FileSystem.AppDataDirectory` (volume PDFs at `<novelId>/volumes/volume-N.pdf`, the analysis report at `<novelId>/analysis/book-analysis.pdf`).
- EF migrations run against the desktop TFM: `StoryboarderTfms=net10.0 dotnet ef migrations add <Name> --project Storyboarder/Storyboarder.Infrastructure --framework net10.0` (a `DesignTimeDbContextFactory` supplies the context; the env var keeps the Android TFM out of the design-time build). In practice the migrations here are **hand-written** (no `dotnet ef` run in the sandbox): each needs the `.cs`, a `.Designer.cs` mirroring the current `StoryboarderDbContextModelSnapshot`, and a snapshot update — snapshot drift fails `MigrationSafetyTests` at runtime, not at compile time.
- **Drafts (Phase 2):** a `Novel` is one *draft*; a *project* is the book being revised, grouping drafts by `ProjectId` (first draft's ProjectId = its own Id). Each `Panel` records the source-text span `[SourceStartOffset, SourceEndOffset)` of the storyboard window it came from (window granularity — all panels of a window share its span). `ProjectService.CreateNextDraftAsync` adds a draft from a re-picked doc; `GetDraftDiffAsync` diffs a draft against its `PreviousDraftId` (findings resolved/new + changed panels); and storyboarding reuses a prior draft's panels+renders for any window whose text is byte-identical, so a revision re-illustrates only what moved. `PanelSourceMatching` is the single definition of "same source span" shared by the diff and the reuse. The Android UI invokes both from a project card ("New draft (re-pick doc)" and "What changed since last draft"), and an in-app reader (`ReaderPage` with chapter jump + `PageZoomPage` pinch-zoom, `ReportPage`, and a before/after `DraftDiffPage`) reads finished books without opening the PDF. `scripts/setup-android-build.sh` makes the head compile in a web/CI container (the official `android.jar` host is network-blocked, so it pulls API 36 from a GitHub mirror and stitches a minimal SDK at `/opt/android-sdk-min`); build with `-p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false` (it does not run the app — no emulator/display).
- **Art & layout (Phase 4, partial):** each `Panel` carries a `ShotType` (Standard/Wide/Splash) the storyboard model tags; `PageLayoutCalculator`'s shot-aware `Layout` keeps an all-Standard page identical to the old uniform grid while a Wide shot becomes a full-width band and a Splash gets its own page (`SplitForPages`). `DialogueFit` abridges dialogue to a balloon budget, and `IClaudeAnalysisClient.DeriveArtStyle` derives one book-wide style from the prose+cast at design-bible time unless the user picked one. Deferred (need a live vision model or on-device eyes): balloon-tail/thought/SFX lettering, off-model auto-detection, and the prose-divergence vision-pass report section.
- **Sources & data safety (Phase 5, partial):** sources are Google Docs **and `.docx`** — `DocxTextExtractor` (pure, in `Storyboarder.Domain/Text`) reads a .docx's `word/document.xml`, and `GoogleDocsClient` detects a picked file's Drive mime type to route (Docs API vs Drive byte-download + extract), so nothing above it changed (`Google.Apis.Drive.v3` is now an Infrastructure dep). `ProjectService.VerifyAndHealOutputsAsync` runs at app start (`MainActivity`) to heal any "done" unit whose file went missing after a reboot, regressing the novel so the pipeline rebuilds it. Owner's-voice propagation is still pending the owner's Drive docs.
- **Gemini throughput (Phase 6, partial):** `RateLimitOptions.GeminiRequestsPerDay` is a safety ceiling (60/min, 5,000/day) sized under the owner's real tier (2,000/min, 50,000/day), not at it. An **opt-in batch renderer** exists behind `GeminiOptions.UseBatchRendering` (default off): `IGeminiBatchClient`/`GeminiBatchImageClient`, in-flight state (`Novel.BatchJobId`, `Panel.BatchAttempted`, `WorkStatus.SubmittedToBatch`), and `GraphicNovelProcessor.RunBatchStep` — the first render pass goes through one batch job; any panel it fails or blocks falls back to the synchronous render + `HandleBlockedPanel` ladder (first attempt batched, escalations synchronous). The real HTTP client is **not** verified against the live Batch API — see `BATCHING.md`'s pre-enable checklist (verify the REST envelope; move reference sheets to the Files API) before switching it on.
- **Owner's voice (cross-cutting):** a shared **`Storyworthtelling.Voice`** library (net10.0, pure .NET, at the repo root — its own `.csproj` + `.Tests`) learns the owner's writing voice from the documents they upload and writes situational app messages in it; every com.storyworthtelling app references it (a `<ProjectReference>`, so the DLL ships in each APK — Storyboarder.Infrastructure references it today). Pieces: `VoiceProfile` (stored as JSON in Drive), `IVoiceClient`/`VoiceClient` (Claude, raw non-streaming HTTP — `RefineAsync` distils a profile from a document, `ComposeAsync` writes one message; reads its key live via `IOptions<VoiceOptions>`, default models `claude-sonnet-4-6` / `claude-haiku-4-5-20251001`), `IVoiceProfileStore`/`DriveVoiceProfileStore` over a narrow `IDriveTextFile` seam (`DriveTextFile` is the Google glue), and **`VoicedMessenger`** — the entry point apps call: it loads the profile, composes fresh, and **degrades to the neutral fallback (never throws)** when there's no profile/key or a call fails, so static labels stay minimalist and only situational messaging is voiced. In Storyboarder the pipeline refines the voice from each manuscript at **ingestion** (best-effort, guarded by `Novel.VoiceAnalyzed` so it runs once per draft), and `MainPage` shows a voiced welcome that stays hidden until a profile exists. Tests: `dotnet test Storyworthtelling.Voice.Tests/Storyworthtelling.Voice.Tests.csproj` (fake HTTP + in-memory store; no key). **Not yet done:** the profile file is per-app under the `drive.file` scope, so a shared folder/broader scope is needed before other apps read one profile (flagged in `DriveTextFile`); rolling the reference out to Ledger + FocusPhone; live-API verification of `VoiceClient`.

### Ledger

An **Android (MAUI) app** that shows balances across your banks, credit cards, and loans in one place by connecting to the **Plaid** aggregator (no US consumer bank offers a per-user balance API directly). Modeled on Storyboarder/DailyLoader: an AndroidX **WorkManager** periodic worker refreshes each connected institution on-device (no web host, no backend), EF Core + SQLite stores the latest balances/liabilities, and a Paper & Ink list displays them. See `Ledger/`.

- Tests: `dotnet test Ledger/Ledger.Tests/Ledger.Tests.csproj -p:LedgerTfms=net10.0` (Domain + Infrastructure + Tests; no device, no keys — Plaid is faked via `FakeHttpMessageHandler`/Moq). The `-p:LedgerTfms=net10.0` switch builds Infrastructure for the desktop TFM only so the suite runs without the MAUI/Android workload.
- Android head (`Ledger.Android`, `net10.0-android`, app id `com.storyworthtelling.ledger`): build with the MAUI/Android workload + Android SDK (same minimal-SDK note as Storyboarder); signs with the shared repo keystore secrets. Background work uses `Xamarin.AndroidX.Work.Runtime` (daily `PeriodicWorkRequest` + a one-shot for "Refresh now").
- **Secrets & data model:** the Plaid **client id/secret + environment** are entered on the Settings page (stored in MAUI `Preferences`, read live by the worker via `LiveOptions<PlaidOptions>`); per-institution **access tokens** live in the SQLite DB (`Ledger.Domain.Model.Institution`). The DB is at `FileSystem.AppDataDirectory/ledger.db`. There is no backend by design — for a personal single-user app the credentials-in-Preferences tradeoff is accepted (see `Ledger/README.md`).
- **Connecting an account (net-new vs the other apps):** a "Connect an account" button opens **Plaid Hosted Link** in a `WebView` (`PlaidLinkPage`); the app creates the `link_token` and, on Done, reads the completed session's `public_token` via `/link/token/get`, exchanges it for an `access_token`, and records the `Institution`. This connect seam is the one piece to finalize against Plaid's current Hosted Link docs (redirect/session handling); everything after it (refresh → SQLite → display) is fully covered.
- **Data pulled:** `/accounts/balance/get` for every account, plus `/liabilities/get` (next payment due/amount + APR) for credit cards and mortgages. Auto/home-improvement loans and some credit unions may be unsupported by Plaid → fall back to manual awareness. The account list also shows the credit limit where Plaid reports one, and each account row **expands to recent transactions** loaded on demand from `/transactions/get` (last 30 days) — this is a UI-side fetch, not part of the background refresh, so no extra tables. Transactions is a Plaid **subscription product** (billed per Item in production; the link token requests it via `required_if_supported_products`).
- The launcher icon is **native** Android adaptive-icon resources under `Ledger.Android/Resources/mipmap-*`, generated by `Tools/IconStudio` (glyph `Glyphs.Ledger` — the four-plus-one ledger marks). Unlike the other apps, Ledger's generated icon binaries are **not committed**; the CI `build` job regenerates them with `dotnet run --project Tools/IconStudio` before building, and the same fatal **ICON CHECK** (`aapt2 dump badging`) proves the custom icon is in the APK. Design tokens are registered in DESIGN.md §16 with the usual leak-guard + parity tests.
- `Ledger.Infrastructure` multi-targets `net10.0;net10.0-android`; `Ledger.Domain` is `net10.0`. EF migrations run against the desktop TFM: `LedgerTfms=net10.0 dotnet ef migrations add <Name> --project Ledger/Ledger.Infrastructure --framework net10.0` (a `DesignTimeDbContextFactory` supplies the context). APK builds in CI via `.github/workflows/build-ledger.yml`.

## Languages & Frameworks

- **Primary**: C# / .NET
- **Frontend** (when needed): Angular
- **Mobile** (when needed): .NET MAUI
- **IDE**: Visual Studio

## Project Structure

Each project lives in a single `.sln` with multiple `.csproj` files — typically split by concern (e.g. `Domain`, `Infrastructure`, `API`, `Tests`). Keep projects coarse-grained until complexity actually demands splitting.

## Architecture Preferences

- Object-oriented; avoid functional-style abstractions that don't fit naturally in C#.
- Controllers handle HTTP; services handle logic; EF Core `DbContext` is the data access layer. Do not add a repository layer on top of `DbContext`.
- Do not access `DbContext` directly from controllers or above the service layer.
- Prefer fewer layers over more. Avoid redundant typecasts and pass-through classes that add no value.
- Use SignalR for pushing real-time updates to clients.
- Expose APIs via controllers with Swagger documentation.

## Code Style

Follow standard Microsoft C# naming conventions throughout. Do not use underscores in any identifier — this includes private fields (use `camelCase`, not `_camelCase`).

## Async

Use `async`/`await` only where I/O actually warrants it or where it will meaningfully improve expected performance. Do not make everything async by default.

## Error Handling

Handle errors where they occur. Propagate failures with a descriptive, specific message and preserve the stack trace for debugging. Do not swallow exceptions or replace them with vague messages.

## Testing

- Unit tests for all logic; integration tests at the API boundary.
- TDD strictly: tests are written before implementation.
- Do not skip or defer tests — untested code is not considered done.

## Dependencies

Only add a dependency if one of these is true:
1. It is already present in the project.
2. It is from Microsoft or another well-established, trusted publisher.
3. It is required to integrate with the specific tool or service being built.

When in doubt, implement it directly in C#.

## Git Workflow

- **Never push to `main`**. All changes go on a feature branch for review first.
- Open a pull request and wait for approval before merging.
