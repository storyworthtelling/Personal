# FocusPhone Audio — Phases 6 & 7 Plan

Listening Genius (Phase 6) and Discovery (Phase 7). This is the detailed follow-on to
`PHASE5-HANDOFF.md`; the original session plan file (`take-up-the-persona-virtual-zebra.md`)
did not survive, so the roadmap for 6–7 is reconstructed here from the locked decisions plus
the two patterns these phases are modeled on. Phases 8 and 9 are sketched at the end from the
owner's notes — they're device-heavy and planned in detail closer to the time.

## The pillar (locked decisions, from the handoff)

**Taste Profile** — a Claude-powered profile of the owner's music taste, modeled on Storyboarder's
`DesignBibleMerger` (map-reduce evidence → a durable, propagatable profile). Locked:

- **Drive-only source** — no on-device library, no in-app upload; the "how to add music" guide (built
  in Phase 5) is the only onboarding.
- **Claude-powered engine** — owner's API key; needs a **kill switch** and an **honest data note**.
- **Passive-only learning** — no ratings/thumbs. Signals: completions, skips, replays,
  album-vs-shuffle-per-genre, time-of-day.
- **Explicit-content guardrail** — an embedded advisory tag plus Claude inference.
- **Discovery is its own tab, but playlists generate only when prompted.**
- **Friday picks** — a quiet in-app "New for you" shelf; no push, no badge.

## Patterns these phases reuse (already in the repo)

- **Map-reduce → durable profile:** `Storyboarder.Domain.Analysis.DesignBibleMerger` reduces per-chunk
  fragments into one profile, counting **distinct sources** (chunks), not raw mentions, so one noisy
  source can't dominate. Listening Genius reduces per-signal evidence the same way — count distinct
  **sessions**, not raw plays, so one repeat-all afternoon doesn't crown a genre.
- **Drive-JSON profile with graceful degradation:** `Storyworthtelling.Voice.VoiceProfile` +
  `DriveVoiceProfileStore` (one JSON file in Drive, absent/garbled ⇒ `Empty`, never throws into the UI)
  + `IVoiceClient`/`VoiceClient` (raw-HTTP Claude, no SDK) + `VoicedMessenger` (loads profile, composes,
  degrades to neutral). The Taste Profile mirrors this shape exactly.
- **Shared Claude key, entered once:** `VoiceConfig`/`DriveVoiceConfigStore`/`DriveVoiceOptions` — the key
  lives in one Drive file, written by Storyboarder's settings, read by every app. Listening Genius
  **reuses this same key** (no new key screen). Codex.Ui.Android already references
  `Storyworthtelling.Voice`.
- **Drive text-file seam:** `IDriveTextFile`/`DriveTextFile` — the narrow Google glue the profile stores
  sit on. New stores sit on the same seam.
- **Signal sources already present:** `PlaybackProgressBook`/`PlaybackProgress` (position + `IsComplete`),
  `PlaybackService`, and `Track`/`MusicCatalog` (genre, artist, album artist). No listening *history* is
  captured yet — Phase 6 adds it.

---

## Phase 6 — Listening Genius (domain + infrastructure, TDD)

Everything model-agnostic and pure lives in Domain/Infrastructure and is fully tested; the Android head
is a thin recorder + settings adapter. No UI surface of its own (that's Phase 7) beyond a kill switch and
the data note.

### Data flow

```
PlaybackService position updates ──▶ SignalDeriver ──▶ ListeningEvent (append)
                                                             │
ListeningEventStore (rolling window, Drive or on-device) ────┘
                                                             │
                                    TasteEvidenceBuilder (pure, map-reduce) ──▶ TasteEvidence
                                                             │
                    ITasteProfileClient.RefineAsync(existing, evidence)  [Claude, shared key]
                                                             │
                                     TasteProfile (Drive JSON) ──▶ Phase 7 consumers
```

### Domain (`Codex.Domain/Audio` or a new `.../Taste`)

- **`ListeningSignal`** (enum): `Completed`, `Skipped`, `Replayed`, `PlayedFromAlbum`, `PlayedFromShuffle`.
- **`ListeningEvent`** (record): `TrackId`, `Genre`, `ArtistName`, `AlbumArtist`, `ListeningSignal`,
  `LocalHour` (0–23, for time-of-day), `SessionId`, `AtUtc`. Kept flat so it serializes cheaply and the
  reduce needs no catalog lookup.
- **`SignalDeriver`** (pure): given a track, its prior `PlaybackProgress`, the new position/duration, and
  how it was launched (album list vs shuffle — the call site already knows: `PlayTracks` vs
  `PlayShuffled`), derive zero or more `ListeningSignal`s. Completion = crossed `IsComplete`; skip = left
  well before the end after a short dwell; replay = restart of an already-complete track. Encodes the
  thresholds in one place.
- **`TasteEvidence`** (record): aggregates keyed by genre / artist / album-artist, each carrying
  distinct-**session** counts of completions vs skips, an album-vs-shuffle lean, and a time-of-day
  histogram. Plus a global explicit-content advisory flag.
- **`TasteEvidenceBuilder`** (pure, the reduce — the `DesignBibleMerger` analogue): folds a list of
  `ListeningEvent`s into `TasteEvidence`, counting distinct sessions per key (a `HashSet<SessionId>`, like
  the merger's `HashSet<int> Chunks`) so one long session can't inflate affinity.
- **`TasteProfile`** (record, the durable artifact — `VoiceProfile` analogue): `Summary` (a short Claude
  narrative of the taste), `Loves`/`Leans`/`Avoids` (short descriptors), `TopGenres`/`TopArtists`,
  `ListensBy` (time-of-day tendencies), `ExplicitAdvisory`, `SessionsAnalyzed` (provenance).
  `Empty` + `IsEmpty` for graceful degradation.
- **`ListeningKillSwitch`** / a guardrail helper: when off, `SignalDeriver` emits nothing and no profile is
  refined — passive learning fully stops. Pure, testable.

### Infrastructure (`Codex.Infrastructure`)

- **`ITasteProfileClient`** + **`TasteProfileClient`** (raw-HTTP Claude, mirroring `VoiceClient`; no SDK):
  `RefineAsync(TasteProfile existing, TasteEvidence evidence, CancellationToken)` → updated profile.
  Reads its key/model from the shared `VoiceConfig` via `DriveVoiceOptions`. Faked in tests.
- **`DriveTasteProfileStore`** over `IDriveTextFile` (reuse the Voice seam), one file
  `storyworthtelling-taste.json`; absent/garbled ⇒ `TasteProfile.Empty` (never throws).
- **`ListeningEventStore`** + a codec: **on-device only** (SQLite/preferences) — the raw per-play history
  never leaves the phone; only the summarized `TasteProfile` syncs to Drive. Append-only with a **rolling
  cap of the last ~200 sessions** so it can't grow unbounded. Codec unit-tested like `SubscriptionListCodec`.
- **`TasteProfileRefresher`** (the `VoicedMessenger` analogue): loads events → builds evidence → refines
  → saves. Best-effort; degrades to a no-op when there's no key/profile/consent or on any error.

### Android (`FocusPhone.Audio.Android`) — thin

- Hook `PlaybackService`/`AudioActivity` playback callbacks to `SignalDeriver` → `ListeningEventStore`
  (respecting the kill switch). The launch mode (album vs shuffle) is already distinguishable at the call
  sites (`PlayTracks` / `PlayShuffled` / `PlaySmartMix`).
- A **kill switch** setting and a one-line **honest data note** ("Your taste profile is built on device
  from what you play and sent to Claude with your key to summarize; turn it off any time."). Reuse
  `MusicSettingsStore`-style preferences; no new tokens.
- Refresh the profile opportunistically (e.g. on app open, throttled), not on a background worker in
  Phase 6.

### TDD test list (Domain/Infra)

`SignalDeriverTests` (completion/skip/replay thresholds, album vs shuffle, kill switch silences),
`TasteEvidenceBuilderTests` (distinct-session counting, per-key aggregation, time-of-day histogram,
explicit advisory), `TasteProfileTests` (Empty/IsEmpty), `DriveTasteProfileStoreTests` (round-trip,
absent, garbled), `ListeningEventStore`/codec round-trip + cap, `TasteProfileRefresherTests` (degrades to
no-op without key/consent; happy path with a fake client).

---

## Phase 7 — Discovery tab + Claude pass

Consumes the Phase 6 `TasteProfile`. Playlists are **prompted, never automatic**; Friday picks are a
**quiet shelf**.

### Surfacing

- **Discovery entry.** The top toggle is 2-way (`Podcasts | Music`). Rather than a third top segment
  (crowds a distraction-free UI), add **Discovery** as an entry in the iPod-style Music menu (which already
  lists Playlists/Artists/Albums/Songs/Genres) — *decision to confirm* (see below).
- **"Make me a playlist"** — the only generation trigger. A prompt UI (seed = a mood/genre/free-text line)
  → `IDiscoveryClient.GeneratePlaylistAsync(profile, catalog, request)` → an ordered set of catalog track
  ids → play/queue via the existing `PlayTracks`. Nothing generates on its own.
- **Friday picks** — a quiet "New for you" shelf, no push/badge. A **weekly WorkManager `PeriodicWorkRequest`**
  (like the MAUI apps' on-device workers) generates the picks in the background so they're ready before the
  app is opened; the pure `FridayPicksSchedule` decides whether picks are due (worker stays a thin trigger).
  The shelf is quiet and dismissable — the worker never pushes or badges.

### Domain

- **`PlaylistRequest`** (record): `Seed`, `MaxTracks`.
- **`DiscoveryPlaylist`** (record): title + ordered `TrackId`s.
- **`PlaylistPlan`** (pure): maps a Claude reply to real catalog tracks — validates/normalizes ids,
  drops unknowns, dedupes, caps length. (Like Storyboarder's correlation-gap logging: a mis-referenced
  id degrades safely, never crashes.)
- **`FridayPicksSchedule`** (pure date logic): given "now" (owner-local) and the last-generated date,
  decide whether picks are due this week. No timers in the pure layer.

### Infrastructure

- **`IDiscoveryClient`** + **`DiscoveryClient`** (Claude, shared key, faked in tests):
  `GeneratePlaylistAsync(TasteProfile, MusicCatalog, PlaylistRequest)` and a `FridayPicksAsync(...)`
  variant. Prompts carry the compact `TasteProfile` + a catalog digest; replies are track-id lists that
  `PlaylistPlan` resolves.

### Android — thin

- A Discovery view (prompt field + "Make a playlist") reached from the **Music menu**, a "New for you"
  shelf on the Music surface, and wiring to `PlayTracks`/queue. Inline, quiet, dismissable; no success
  toasts; loading is a sentence.
- A weekly **`PeriodicWorkRequest`** (`Xamarin.AndroidX.Work.Runtime`) that runs `FridayPicksSchedule` +
  `DiscoveryClient.FridayPicksAsync` and stashes the result for the shelf to read on open.

### TDD test list

`PlaylistPlanTests` (unknown/duplicate/over-cap ids resolve safely), `FridayPicksScheduleTests` (due
Friday, not-yet-Friday, already-generated-this-week, week rollover), `DiscoveryClient` behavior via a fake
(request shape, empty-profile ⇒ neutral/empty result).

---

---

## Phases 8 & 9 — later, device-heavy (owner's notes, for reference)

Sketched here so the roadmap is complete; both are planned in detail closer to the time, and both need a
real device (and Phase 9 a car), so they come after the on-device Phase 6/7 logic is proven.

- **Phase 8 — Music playback polish / eyes-free.** Resume + auto-resume after phone calls, an
  iPod-faithful now-playing screen, EQ / Sound Check, per-device memory, gapless playback, artwork
  override, a car mode, and headset-gesture controls.
- **Phase 9 — Android Auto browse tree + Cast spike (device-gated, riskiest, last).** Populate the
  `MediaLibraryService` browse tree via the Guava `ListenableFuture` binding (the tree is structurally
  ready from Phase 4 but unpopulated), validate Android Auto in the car, then spike Cast → Google Home.

## Cross-cutting constraints

- **TDD strictly** — Domain/Infrastructure tests before implementation; Android heads are thin, untested
  adapters. Target `Codex.Tests/Codex.Tests.csproj`.
- **No new dependencies** — reuse the Voice library's raw-HTTP Claude approach (no SDK) and its Drive
  seam; no new packages.
- **Design system** — no hex/type-size literals outside `DesignTokens.cs`; reuse `PaperUi` factories and
  existing dialog patterns; success is inline (no toasts), loading is a sentence, copy stays
  quiet-neutral, destructive acts are undo-first.
- **Privacy/consent** — kill switch honored everywhere signals are derived; the honest data note ships
  with it; passive-only (no ratings UI ever).
- **Validation is CI-only** in the sandbox (`build-focusphone.yml`); device testing batched to end of
  phase per the owner's standing instruction.
- **Git** — same branch/PR as Phase 5 per the owner's request; commit per logical group; no model id in
  any pushed artifact.

## Decisions (resolved with the owner)

1. **Event storage** — **on-device only** (SQLite/preferences); raw listening history never leaves the
   phone. Only the summarized `TasteProfile` syncs to Drive as the propagatable artifact.
2. **Discovery placement** — a **Music-menu entry** (alongside Playlists/Artists/Albums/Songs/Genres);
   the top toggle stays 2-way (`Podcasts | Music`).
3. **Friday-picks trigger** — a **weekly WorkManager `PeriodicWorkRequest`** generates picks in the
   background; the shelf reads them on open. Quiet and dismissable — no push, no badge.
4. **Claude models** — **reuse the shared `VoiceConfig` tiers** (sonnet for refine/generate, haiku where
   cheap); no separate model config.
5. **Rolling window** — retain the **last ~200 sessions** of events on device.
