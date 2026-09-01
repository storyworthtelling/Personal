# Canonizer

An **Android app** that reads a novel manuscript and produces a **revision report** — a deep,
chapter-cited editorial read aimed at helping the author revise toward publication. Alongside the
report it renders **one wordless image per scene** as *supporting evidence*: a fidelity check that
asks whether each beat lands visually as written. **The report is the star; the image is evidence.**

The manuscript can be a **Google Doc**, `.docx`, **RTF**, **ePub**, or **PDF**. The app reads the
whole book, builds a private design bible of characters and settings (so the scene images stay
visually consistent), detects scenes, renders one image per scene, and consolidates a full report.
Because the work is rate-limited and long-running, it runs **in the background** and is fully
**resumable** — it keeps going across pauses, app restarts, and reboots until it's done, then
delivers the finished report to Google Drive beside the manuscript.

Four principles are load-bearing:

- **Nudge, never write.** The report diagnoses and points; it never ghost-writes the author's prose
  or lines.
- **Both ends of the pendulum.** It flags under-writing *and* over-writing.
- **The report is the star.** The per-scene image is a comprehension probe, not the product.
- **Quiet by default.** The report is delivered silently to Drive with one in-app note — no push,
  no toast.

- **Analysis** (design bible + report) uses **Claude** (Anthropic).
- **Image generation** uses **Google Gemini** (an image model that takes reference images, so
  characters/settings stay visually consistent scene to scene).
- **Doc selection** uses native device-account **Google sign-in** (Android `AccountManager`) and a
  Drive picker; a Google Doc's text is fetched via the Docs API, and every other format is
  byte-downloaded from Drive and its text extracted on-device.
- **Background work** is an AndroidX **WorkManager** periodic worker that drains the pipeline a
  cycle at a time. There is no server and no web UI — everything runs on the phone.

> **Internal identity.** The project keeps its original **`Storyboarder.*`** namespaces, solution,
> `StoryboarderDbContext`, `storyboarder.db`, and app id `com.storyworthtelling.storyboarder` so an
> installed build upgrades in place. Only the user-facing name (**Canonizer**), the icon (a red pen
> nib), and a few domain concepts changed in the pivot from graphic-novel factory to revision
> instrument.

## Architecture

`Storyboarder.sln`:

- **Storyboarder.Domain** (`net10.0`) — pure logic, no external deps: chunking, chapter/scene
  detection, design-bible merge, rate limiter, backoff, the pipeline state machine, the report data
  schema, entities, and collaborator interfaces.
- **Storyboarder.Infrastructure** (`net10.0;net10.0-android`) — EF Core + SQLite `DbContext`, the
  Claude/Gemini/Docs HTTP clients, the source-format extractors (docx/RTF/ePub/PDF), the SkiaSharp
  report renderer + PdfSharp PDF assembler, the `ReviewProcessor` (advances one manuscript by one
  unit of work per call; all state in the DB), and `PipelineRunner` (the resumable drain loop,
  bounded per WorkManager cycle).
- **Storyboarder.Android** (`net10.0-android`, MAUI) — the app head: `MauiProgram` DI, the
  `PipelineWorker` (WorkManager), device-account Google sign-in + Drive doc lister, and the pages
  (manuscript list, new project, report reader, draft diff, settings). Modeled on the sibling
  `DailyLoader` app.
- **Storyboarder.Tests** (`net10.0`) — xUnit + Moq: pure-logic unit tests, in-memory-SQLite data
  tests, fake-HTTP client tests, the end-to-end pipeline test, and the `PipelineRunner` tests.

### The pipeline

One manuscript advances through stages, one unit of work per worker cycle, all state persisted so
any stage resumes after an interruption:

`Ingesting` → `BuildingDesignBible` (per-chunk analysis + reference sheets) →
`RenderingSceneImages` (one wordless portrait image per detected scene) →
`ComposingAnalysis` (map-reduce the manuscript into the report, then render it to a PDF) →
`Archiving` (deliver to Drive) → `Completed`.

## The report

The report is a chapter-cited editorial read, consolidated by Claude from per-chunk notes:

- **Frame:** genre, word count vs the genre norm, logline, synopsis.
- **Thematic sections:** story integrity (plot holes, inconsistencies, inaccuracies), structure,
  setup/payoff, theme hierarchy, **pacing with a tension-over-chapters chart**, show-vs-tell, prose
  & voice, dialogue, POV/tense, and authorial-voice consistency (where the manuscript's own prose
  drifts from its dominant voice).
- **Scene by scene:** for each scene, the evidence image on top, then a **Fidelity gap / Craft /
  Purpose** block, cited `Ch. N · Scene M`. The *fidelity gap* is a prose-specification read (does
  the prose under-specify the beat?), not a vision comparison of the generated image.
- **Series continuity** *(sequels only):* when a book is linked as book N of a series, a read that flags
  where it contradicts the prior book's established canon (characters, world, timeline) and tracks how it
  handles the threads the prior book left open. Silent for a standalone book or a clean sequel.
- **Reviews:** five fair perspectives, including an *Agent & acquiring editor* commercial/readiness
  read.
- **What to do:** a revision plan grouped **Must / Should / Consider**.
- **Verdict:** the candid state of the draft — biggest strengths, biggest problems, and the single
  thing this draft most needs next.

Scoring is **qualitative only** — the report speaks of "improving/regressed", never numbers.

## Configuration

All settings are entered **in the app** (Settings page) and stored in MAUI `Preferences`:

- **Claude API key** — Anthropic key for analysis and the report (`console.anthropic.com`).
- **Gemini API key** — Google **Generative Language API** key for image generation
  (`aistudio.google.com`).
- **Google account** — tap *Sign in with Google* to pick the device account. The app requests the
  `documents.readonly` scope (to read a native Google Doc) and the full `drive` scope (to
  byte-download other formats and to write the finished report back beside the manuscript). No Play
  Services binding is needed, so there is **no `dl.google.com` build dependency** — but Google still
  identifies the app by its **package name + signing-certificate SHA-1**, so that pair must be
  registered as an **Android OAuth client** (see *Google Cloud setup* below) or sign-in fails with
  `UnregisteredOnApiConsole`.

The SQLite database and generated images/PDFs live under the app's private storage
(`FileSystem.AppDataDirectory`).

## Google Cloud setup (one-time)

Sign-in uses the device's Google account via `AccountManager`, which requires the app to be a
registered **Android OAuth client** — Google identifies it by package name + signing-certificate
SHA-1 (no client id is sent from code). Without this, sign-in throws
`AuthenticatorException: UnregisteredOnApiConsole`.

1. In the [Google Cloud Console](https://console.cloud.google.com) pick or create a project.
2. **APIs & Services → Library** — enable the **Google Docs API** and **Google Drive API**.
3. **APIs & Services → OAuth consent screen** — User type *External*; add the scopes
   `.../auth/documents.readonly` and `.../auth/drive`; add your Google account under **Test users**;
   keep publishing status *Testing* (the restricted Drive scope then works for test users, past an
   "unverified app" warning).
4. **APIs & Services → Credentials → Create credentials → OAuth client ID → Android** — set *Package
   name* `com.storyworthtelling.storyboarder` and the signing-certificate **SHA-1** shown on the
   app's **Settings** page (tap *Copy package + SHA-1*). Debug and CI/release builds have different
   SHA-1s — register whichever you install (or add both as separate clients on the same package).

Wait a few minutes for the client to propagate, then sign in again.

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
`-p:AndroidSdkDirectory=<sdk> -p:AndroidUseApkSigner=false` (see `scripts/setup-android-build.sh`).
Debug builds sign with the default debug keystore.

## Using the app

1. Install the app and open **Settings**; paste your Claude and Gemini API keys and sign in with
   Google.
2. Tap **New project**, pick a manuscript (Google Doc, `.docx`, RTF, ePub, or PDF), optionally tweak
   the art style, and **Create**.
3. The background worker advances the review over time (WorkManager runs at least every 15 minutes,
   and re-runs sooner while work remains). The manuscript list shows live progress.
4. When the report is ready, it is delivered to Google Drive beside the manuscript as
   `<Title> — Draft N — Analysis.pdf`, with a quiet in-app note. Tap the manuscript to open/share or
   read the report in the app.
5. **Revising or continuing a book:** tap a manuscript card. *New draft (re-pick doc)* starts the next
   draft of the same book — the report then diffs it against the previous draft (resolved / new / regressed
   findings, before/after scene images). *Start next book (new manuscript)* begins book N+1 of a series,
   linked to this one: the sequel reuses this book's cast for visual consistency and its canon for the
   **Series continuity** read, and its card wears a *Book N* badge.
6. **Optionally, to a reMarkable tablet:** register the tablet once in Settings (a one-time code from
   `my.remarkable.com/device/mobile/connect`) and turn on *Deliver to reMarkable* — the report then
   also lands in a **"Canonizer" folder** on the tablet to read and annotate by hand. Best-effort and
   independent of Drive; the pairing is shared with the other Storyworthtelling apps via the secrets
   vault, so pairing once anywhere is enough.

## Notes & limits

- A full novel is **~50–150 scenes** — an image apiece, plus the analysis calls — so a review can
  take real wall-clock time on free tiers and some image-generation cost. The resumable design
  spreads the work across many background cycles; test on a short manuscript first.
- Keep the app exempt from battery optimization (and on a charger for big runs) so WorkManager isn't
  throttled.
- Gemini character consistency is good but not perfect; the same reference sheets are supplied on
  every scene and the art-style directive is repeated to minimize drift.
- A scene image the content filter refuses is retried on a different moment up to five times, then
  marked unavailable — an unavailable image **never blocks the report**.
- The Gemini image calls, the Drive writes, and the on-device look are reconstructed and **not yet
  live-verified** on a device — the first real run is the verification.
