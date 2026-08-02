# FocusPhone Audio — Phases 8 & 9 Plan

Music playback polish / eyes-free (Phase 8) and the Android Auto browse tree + Cast spike (Phase 9).
The detailed follow-on to `AUDIO-PHASES-6-7-PLAN.md`, expanded from the owner's notes and grounded in the
current playback code. These phases are **device-heavy** — most of the work is Android glue validated in CI
by compilation and on a real device/car at the end — so the plan calls out the thin slices of pure,
unit-testable logic explicitly and leaves the rest device-gated, matching the repo's standing posture.

## Where the code already is (so the plan is concrete)

- **`PlaybackService`** is a Media3 `MediaLibraryService` hosting an `ExoPlayer` via `ExoPlayerBuilder`, with
  a `MediaLibrarySession`, a resume book (`PlaybackProgressBook`/`PlaybackProgressStore`, polled every 10 s),
  and skip-silence toggling. The player is built with a `ResolvingDataSource` that turns `drive://{fileId}`
  into an authed download URL per open; podcast http(s) URIs pass through.
- **Audio focus is NOT configured yet** — `ExoPlayerBuilder` sets no `AudioAttributes` and no
  `handleAudioFocus`/`handleAudioBecomingNoisy`. So "auto-resume after calls" and "pause on headphone unplug"
  are genuinely missing, not just unpolished.
- **`MediaBrowseTree`** already builds the whole browsable tree (root → Albums / Artists / Songs → playable
  `drive://` leaves) from the cached `MusicCatalog`. The `MediaLibrarySession`'s `LibraryCallback` holds the
  tree but **does not implement `onGetLibraryRoot`/`onGetChildren`/`onGetItem`** — the Guava
  `ListenableFuture` binding is the noted follow-up. So Phase 9's browse work is *wiring*, not *building*.
- **Now-playing is only a bar** (`NowPlayingBar`), driven by a 1 s ticker over a `MediaController`. There is
  no full now-playing screen yet.

---

## Phase 8 — Music playback polish / eyes-free

Eight items from the owner's notes. Each names what exists, what to add, and (where any) the pure logic to
TDD before the Android glue.

### 1. Resume + auto-resume after calls (audio focus)
- **Add:** on `ExoPlayerBuilder`, `SetAudioAttributes(new AudioAttributes.Builder().SetUsage(Media).SetContentType(Music).Build(), handleAudioFocus: true)` and `SetHandleAudioBecomingNoisy(true)`. ExoPlayer then ducks/pauses on a transient focus loss (a call, a nav prompt) and resumes after, and pauses when headphones unplug.
- Resume-to-position already works (`ResumeWithRewind`); this covers the *interruption* case the notes call out.
- **Pure/TDD:** none new — configuration on the builder. Device-gated verification.

### 2. iPod-faithful now-playing screen
- **Add:** a full-screen `NowPlayingActivity` (Paper & Ink) — large artwork, Serif title / artist / album, a **scrubber** (seek via `MediaController.SeekTo`), transport, shuffle/repeat toggles, per-show/track speed entry. The now-playing bar opens it on tap.
- **Pure/TDD:** a `PlaybackTimeline` helper (elapsed/remaining/scrubber fraction formatting, 12-hour/relative rules) and a `RepeatShuffleMode` model (off/all/one, shuffle on/off, next-state transitions). Both pure, unit-tested; the Activity is a thin adapter over `MediaController`.

### 3. EQ / Sound Check
- **Add:** attach Android `Equalizer` + `LoudnessEnhancer` to the player's `AudioSessionId` (Media3 exposes it). "Sound Check" = per-track loudness normalization toward a reference LUFS via `LoudnessEnhancer` (or ReplayGain tags read by `TagLibSharp` at scan time, folded into the catalog).
- **Pure/TDD:** an `EqualizerPreset` model (named presets + custom band gains) and its persistence codec; a `SoundCheckGain` calc (track loudness → gain toward reference, clamped). Pure, tested; the effect attach is device-gated.
- **Decision:** LoudnessEnhancer at playback vs precomputed ReplayGain at scan (below).

### 4. Per-device memory
- **Add:** remember volume / EQ preset / speed per **output device** (car Bluetooth vs headphones vs speaker). The Android side reads the current route (`AudioManager`/`MediaRouter`) and applies the saved settings on route change.
- **Pure/TDD:** a `PlaybackDeviceSettings` map (device key → settings) with resolve/update + codec — pure, tested. Only the route-change hook is device-gated.

### 5. Gapless
- **Verify/expose:** ExoPlayer plays gapless-encoded content gaplessly when consecutive items share one timeline (they already do via `SetMediaItems`). Confirm no silence is inserted and expose a setting only if needed. Mostly device verification; no pure logic.

### 6. Artwork override
- **Add:** let the owner set a custom image per album (or track) when the embedded art is missing/ugly; surfaced in the now-playing screen and browse rows.
- **Pure/TDD:** an `ArtworkOverride` store (albumKey → local image path) + codec, and resolution precedence (override → embedded `Track.ArtRef` → default). Pure, tested; the image picker + render are device-gated.

### 7. Car mode
- **Add:** a simplified, large-target in-app variant of the now-playing/browse screens for at-a-glance use (distinct from Android Auto, which is Phase 9). A toggle; bigger hit targets, fewer controls, high contrast within the Paper & Ink tokens.
- **Pure/TDD:** none beyond reusing existing view-models. Device-gated.

### 8. Headset gestures
- **Add:** the `MediaSession` already routes single media-button events (play/pause, next/prev). Map multi-press (double-tap → next, triple → previous) if desired via the session's media-button handling.
- **Pure/TDD:** a `MediaButtonGesture` interpreter (press-count + timing → action) — pure, tested. The session hook is device-gated.

---

## Phase 9 — Android Auto browse tree + Cast spike (device-gated, riskiest, last)

### Populate the browse tree (the Guava ListenableFuture binding)
- **Add:** implement on `LibraryCallback` (the `MediaLibrarySession.ICallback`):
  - `OnGetLibraryRoot(session, browser, params)` → `Futures.immediateFuture(LibraryResult.OfItem(tree.Root(), params))`.
  - `OnGetChildren(session, browser, parentId, page, pageSize, params)` → `LibraryResult.OfItemList(tree.Children(parentId), params)` (with paging).
  - `OnGetItem(session, browser, mediaId)` → the single item.
  The **tree content already exists** (`MediaBrowseTree`); this is purely returning it through the Guava `ListenableFuture<LibraryResult<…>>` types the Java API expects — the reconstruction wrinkle is the Xamarin binding for `com.google.common.util.concurrent.ListenableFuture` (use `Futures.immediateFuture`, or `Google.Guava` binding helpers).
- **Pure/TDD:** **extract the tree logic to a pure, testable shape** first. `MediaBrowseTree` currently lives in the Android head and depends on `AndroidX.Media3.Common.MediaItem`, so it isn't unit-tested. Split it: a pure Domain `BrowseNode` model (id, title, browsable-vs-playable, child ids/track ids) built from `MusicCatalog` — **TDD this** (root children, album/artist/song expansion, id round-tripping, the `` separator encoding) — and keep a thin Android mapper `BrowseNode → MediaItem`. That converts today's untested Android class into tested Domain logic + a trivial adapter.
- **Validate in the car:** Desktop Head Unit (DHU) then a real car — browse, playback, voice ("play …"), and that `drive://` leaves stream through the resolving data source under Auto.

### Cast → Google Home spike
- **Spike:** add the Media3 Cast extension (`androidx.media3:media3-cast`), a `CastPlayer`, and a `MediaRouteButton`; evaluate handing off to a Google Home / Chromecast.
- **The load-bearing risk:** Cast plays from a URL the **receiver** fetches, not the phone. The in-app `drive://` → authed-download resolution happens on-device via `ResolvingDataSource`; a Cast receiver can't use that. So casting the owner's Drive music needs either a short-lived publicly-fetchable URL per track or an on-device proxy — a real design question. Treat this as an **exploratory spike** with a go/no-go outcome, not a committed feature.
- **Pure/TDD:** thin — a route/device selection view-model at most. This phase is mostly device experimentation.

---

## Cross-cutting constraints (unchanged)

- **TDD** the pure slices called out above (timeline/scrubber, repeat-shuffle, EQ preset + codec, sound-check
  gain, per-device settings, artwork-override resolution, media-button gesture, and the extracted
  `BrowseNode`); Android heads stay thin adapters, compile-checked in CI.
- **No new design tokens** — reuse `PaperUi`; the now-playing screen and car mode compose existing tokens.
- **Dependencies:** `androidx.media3:media3-cast` (Xamarin.AndroidX.Media3.Cast) is the one new package,
  and only for the Phase 9 spike — within the "required to integrate with the service" bar.
- **Interaction rules** hold: status sentences not spinners, 12-hour/relative time, quiet-neutral copy,
  no success toasts.
- **Validation is CI-only** in the sandbox; on-device (and in-car) verification is the end-of-phase batch.

## Open decisions to confirm before building

1. **Sound Check** — `LoudnessEnhancer` at playback (simple, approximate) vs reading **ReplayGain tags** at
   scan time via TagLibSharp and normalizing from those (more accurate, needs tagged files). Recommend
   LoudnessEnhancer first, ReplayGain if the owner's files carry the tags.
2. **EQ** — a few fixed presets (Flat / Bass / Treble / Vocal) vs a full custom band editor. Recommend fixed
   presets first; custom bands only if wanted.
3. **Now-playing** — a dedicated `NowPlayingActivity` (recommended, room for artwork + scrubber) vs expanding
   the existing bar into a sheet.
4. **Car mode** — a standalone in-app large-target mode (recommended, works without a car head unit) vs
   leaning entirely on Android Auto (Phase 9).
5. **Cast** — pursue beyond the spike only if the `drive://`-to-receiver resolution has a clean answer;
   otherwise stop at the go/no-go and leave casting out.
6. **Browse-tree refactor** — confirm splitting `MediaBrowseTree` into a pure Domain `BrowseNode` + Android
   mapper (recommended: turns untested Android code into tested Domain logic before Auto depends on it).
