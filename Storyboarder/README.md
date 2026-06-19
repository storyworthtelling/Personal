# Storyboarder

An **Android app** that turns a Google Docs novel into a multi-volume **graphic novel**. It reads
the whole manuscript, builds a private design bible of characters and settings, storyboards the
book into volumes → pages → panels, then renders every panel and assembles **one PDF per volume**.
Because image generation is rate-limited, the work runs **in the background** and is fully
**resumable** — it keeps piecing the book together across pauses, app restarts, and reboots until
it's done.

- **Analysis** (design bible + storyboarding) uses **Claude** (Anthropic).
- **Image generation** uses **Google Gemini** (an image model that takes reference images, so
  characters/settings stay visually consistent).
- **Doc selection** uses native device-account **Google sign-in** (Android `AccountManager`) and a
  Drive doc picker; the chosen doc's text is fetched once via the Google Docs API at ingest.
- **Background work** is an AndroidX **WorkManager** periodic worker that drains the pipeline a
  cycle at a time. There is no server and no web UI — everything runs on the phone.

## Architecture

`Storyboarder.sln`:

- **Storyboarder.Domain** (`net10.0`) — pure logic, no external deps: chunking, design-bible merge,
  rate limiter, backoff, the pipeline state machine, page-layout math, entities, and collaborator
  interfaces.
- **Storyboarder.Infrastructure** (`net10.0;net10.0-android`) — EF Core + SQLite `DbContext`, the
  Claude/Gemini/Docs HTTP clients, SkiaSharp page composer, PdfSharp volume composer, the
  `GraphicNovelProcessor` (advances one novel by one unit of work per call; all state in the DB),
  and `PipelineRunner` (the resumable drain loop, bounded per WorkManager cycle).
- **Storyboarder.Android** (`net10.0-android`, MAUI) — the app head: `MauiProgram` DI, the
  `PipelineWorker` (WorkManager), device-account Google sign-in + Drive doc lister, and three pages
  (projects list, new project, settings). Modeled on the sibling `DailyLoader` app.
- **Storyboarder.Tests** (`net10.0`) — xUnit + Moq: pure-logic unit tests, in-memory-SQLite data
  tests, fake-HTTP client tests, the end-to-end pipeline test, and the `PipelineRunner` tests.

## Configuration

All settings are entered **in the app** (Settings page) and stored in MAUI `Preferences`:

- **Claude API key** — Anthropic key for analysis/storyboarding (`console.anthropic.com`).
- **Gemini API key** — Google **Generative Language API** key for image generation
  (`aistudio.google.com`).
- **Google account** — tap *Sign in with Google* to pick the device account; the app requests the
  `documents.readonly` and `drive.readonly` scopes via Android `AccountManager`. No OAuth client id,
  API key, or Play Services binding is needed, so there is **no `dl.google.com` build dependency**.

The SQLite database and generated images/PDFs live under the app's private storage
(`FileSystem.AppDataDirectory`).

## Build & test

Tests run on the desktop TFM with no device, emulator, or API keys (external services are faked):

```bash
dotnet test Storyboarder/Storyboarder.sln -p:StoryboarderTfms=net10.0
```

The `-p:StoryboarderTfms=net10.0` switch builds `Storyboarder.Infrastructure` for `net10.0` only so
the suite restores and runs without the MAUI/Android workload.

Building the Android head needs the **.NET MAUI / Android workload** and the **Android SDK** (open
`Storyboarder.sln` in Visual Studio, or `dotnet build Storyboarder/Storyboarder.Android`). In a
sandboxed environment a minimal SDK works, as with the FocusPhone heads:
`-p:AndroidSdkDirectory=<sdk> -p:AndroidUseApkSigner=false`. Debug builds sign with the default
debug keystore.

## Using the app

1. Install the app and open **Settings**; paste your Claude and Gemini API keys and sign in with
   Google.
2. Tap **New project**, pick a Google Doc, optionally tweak the art style, and **Create**.
3. The background worker advances the novel over time (WorkManager runs at least every 15 minutes,
   and re-runs sooner while work remains). The projects list shows live progress.
4. When a volume's PDF is ready, tap the project to **open/share** it.

## Notes & limits

- A full novel can be **hundreds–thousands of panels** — potentially days of wall-clock time on
  free tiers and real image-generation cost. Gemini's free tier is ~100 images/day, so the resumable
  design spreads the work across many background cycles. Test on a short doc first.
- Keep the app exempt from battery optimization (and on a charger for big runs) so WorkManager isn't
  throttled.
- Gemini character consistency is good but not perfect; the same reference sheet is supplied on
  every panel and the art-style directive is repeated to minimize drift.
- Dialogue/captions are typeset deterministically by the composer (the image model is told not to
  render text, which it does unreliably).
