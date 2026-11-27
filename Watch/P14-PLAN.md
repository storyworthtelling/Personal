# P14 · Watch — implementation plan

**Status:** approved (A75–A77).

This plan builds what four approved records already decided, and reopens none of them:

- the **owner's answers**, [`P14-OWNER-ANSWERS.md`](P14-OWNER-ANSWERS.md), cited **A#** (A1–A74);
- the **watch design language**, [`WATCH-DESIGN.md`](WATCH-DESIGN.md) (approved, A58), cited **WD §…**;
- the **screen mockups**, approved round 2 (A72–A74), cited **mock: <screen>**, at
  `https://claude.ai/artifact/6Md1cBXMJNCHkQ9yHS3eUc`, with the face studies (round 2, study 1b,
  A46) and glyph studies (family A, A50);
- the **technical finding**, [`P14-TECHNICAL-FINDING.md`](P14-TECHNICAL-FINDING.md), cited **TF §…**
  (its approval is asked for alongside this plan).

Where the plan had to choose something those records left open, it says so under **Plan choices (PC)**,
and each is the owner's to overturn. **Everything in this plan is invented**: Sam, Lee, Robin and
Juniper, the songs, shows and workouts. No real person, number, message or reading appears here.

---

## 0. How to read this plan

### 0.1 The mockups are the specification

Every screen is built **1:1** from its approved mockup, and anything that cannot be built as drawn is
**reported back, not substituted**. The face is study 1b; the glyphs are family A; colours, type, touch
and words follow WD.

### 0.2 Identifiers, sizes and flags

The same conventions as Constitution's plan (`Constitution/CONSTITUTION-P13-PLAN.md` §0.2):

- **PBI ids** are `W-<epic>.<feature>.<n>`; features `F-<epic>.<n>`; epics `E<n>`.
- **Sizes:** XS (under an hour) · S (a day) · M (two or three days) · L (a week) · XL (split it first).
- **Verify:** **Tests** (green suites are enough) · **Owner** (seen in the running app) · **Device**
  (only the real watch or phone can prove it; it goes on the device test plan).
- **Agent level and effort:** L1 mechanical (low) · L2 standard (medium) · L3 judgement (high, or
  xhigh where a wrong answer is silent and costly: the Data Layer contracts, the installer, the
  update channel's watch rule, exertion).
- **Strict TDD:** every PBI names the tests written first. Each red run is recorded in
  `Watch/P14-PROGRESS.md` before the code that turns it green. An L3 agent reviews every release
  read-only, then a fix round; the orchestrator runs the suites and compiles every head itself.

### 0.3 Standing rules for every build brief

No colour hex or type size outside `WatchTokens` (WD §13) · Microsoft C# naming with **no underscores**
in any identifier, short test names · no empty `catch` · no model identifier in any committed file ·
**never ask for, store or log the vault passphrase**, and the watch never holds it (owner's ruling, 27 Nov, retiring A38) ·
frozen identifiers are string literals under guards, never `nameof` · success is a state change, a
destructive act is undo-first (WD §9), loading is a sentence · dependencies only per CLAUDE.md, versions
in `Directory.Packages.props` · say plainly what is not verified: "passes against fakes" is not
"works", and a device item is "built, device check pending".

---

## 1. Overview

### 1.1 Goal

**Leave the phone at home and keep the world in front of you** (A1). The owner wears the watch and
stays reachable (Dash), listens to music and podcasts (Wave), records workouts and the day (Beat), and
sees the time on a face that shows nothing else (Point).

Success is three things together: **a whole outing with the phone at home where nothing was missed,
music never stopped, and the watch was hardly looked at.**

### 1.2 Scope (P14)

- **Four watch apps** on stock Pixel Watch software (A68): Point (a Watch Face Format face and a
  hidden installer), Dash, Wave and Beat, each with its tiles (Wave's Now playing, Dash's People
  waiting).
- **Two watches:** the owner's Pixel Watch 2 (LTE, Fi, shared number) and the partner's newer Pixel
  Watch (A4). Hers runs Wave and Beat only (A39).
- **Phone-side partners:** Post relays texts and mail lines (A67), Score shares the queue and TIDAL
  (A18, A62), Constitution sends the day's workout and receives sessions (A26, A29), and Flyleaf sends
  watch updates (A70).
- **The phone stays on.** On Fi the phone carries calls and texts to the watch, so every route here
  assumes the phone is on and online at home (A67).

### 1.3 Non-goals

- A **lockdown** on the watch: no device owner, no allowlist, no blocked apps or notifications (A68,
  A69).
- **Texts, calls or mail with the phone off** (A67): not possible on Fi by any design.
- **Downloads** to the watch: everything streams (A17).
- **TIDAL playback on the watch**: TIDAL plays through the phone only (A62, A73).
- **Discover** and **lyrics** on the watch (A17, A19).
- **Voice seats** on the watch: every line is neutral (A37).
- **Counts, badges, unread marks, streaks, goals, scores**, or anything on the face beyond time and
  date (A34, A36).
- **Planning** on the watch: Beat records; the plan stays on the phone and the reMarkable (A25).
- **Body values** on the watch other than live heart rate during a session (A29).
- **A calendar** on the watch (A31's allowlist is gone; Google's own apps are as they are).
- An **emulator** check: real watch only (A40).
- **Wear OS on iPhone**, other watch makers, and Play Store publishing (A70).

### 1.4 Releases and what each gives the owner

The owner chose **one PR per release** (A41) and the order **Point first, then Dash** (A41, A68).

| Release | Delivers to the owner | Epics |
|---|---|---|
| **A — Point** | The polygon face on both watches. The Point installer on the watch, and Flyleaf sending watch updates to it from the phone: install once by hand, then every update arrives from the phone. The watch workflow, tokens and guards. | E1, E2, E3 |
| **B — Dash** | People waiting, conversations, replies by voice or keyboard, group texts and photos, filing and deleting with Undo, the call note, mail lines, the People waiting tile, and the vault passphrase entry, all relayed through Post on the phone. | E4, E5 |
| **C — Wave** | The wave player: Drive music and podcasts streamed over LTE, one queue with Score, either device as the remote, TIDAL through the phone, the Now playing tile, and listening counted for the taste profile. | E6 |
| **D — Beat: workouts** | Today's workout from Constitution, sets and rests, runs with cadence, heart rate and distance, Ease off, cadence driving Wave's tempo, and the session back to the phone, into Constitution and Health Connect. | E7, E8 |
| **E — Beat: the day, and her watch** | All-day steps and resting heart rate, sleep via Fitbit as the pipe, the eating-out card, the ink-and-watch discrepancy in the digest, and the partner's watch with Wave and Beat. | E9 |
| **F — On the watches** | The device test plan run with the owner on both watches and phones; each failure reopens its PBI. | E10 |

Each release is usable on its own.

---

## 2. Architecture sketch

### 2.1 Projects (A40: .NET only)

| Project | What | TFM |
|---|---|---|
| `Watch/Watch.Domain` | Pure logic: `WatchTokens`, `DayColour`, `HourShape`, `WaveGeometry`, the Data Layer message contracts and codecs, `ExertionWatch`, `CadenceTempo`, `QueueMerge`, `WatchUpdatePlan` | net10.0 |
| `Watch/Watch.Ui.Wear` | The watch kit: canvas views (the wave, the dash, rings), Plex Mono, `WatchText`, the undo ring, the Data Layer seam | net10.0-android |
| `Watch/Dash.Wear`, `Watch/Wave.Wear`, `Watch/Beat.Wear` | The three watch apps | net10.0-android |
| `Watch/Point.Installer` | The hidden installer (no launcher icon) | net10.0-android |
| `Watch/Point.Face` | The face: Watch Face Format XML and resources only, built into a code-free APK by `Tools/FaceStudio` | (resources) |
| `Tools/FaceStudio` | C# console: generates the face XML from `HourShape`, validates it, packages the resource-only APK with aapt2 | net10.0 |
| `Watch/Watch.Tests` | Domain, contracts, guards; no device | net10.0 |

The watch UI is **Android views and canvas**, not Compose (Compose cannot be written in C#), the same
way Flyleaf's bookcase is a canvas view. Phone-side pieces live in their own apps (§2.5).

### 2.2 Identity: the watch apps carry their phone app's id (PC1)

The Data Layer delivers only between apps with **the same package name and the same signing
certificate** on both devices (Google's Data Layer overview, "Security of communications"; checked
20 Nov 2026). So:

| Watch app | Package (frozen, shared with its phone app) | Phone counterpart |
|---|---|---|
| Dash | `com.storyworthtelling.focusphone.communication` | Post |
| Wave | `com.storyworthtelling.focusphone.audio` | Score |
| Beat | `com.storyworthtelling.constitution` | Constitution |
| Point installer | `com.storyworthtelling.focusphone.launcher` | Flyleaf |
| Point face | `com.storyworthtelling.watch.point` (its own; a face needs no phone partner) | none |

All are signed with the suite's one keystore. **Consequence for the update channel:** a watch APK and
its phone app share a package name, so Flyleaf must never install a watch APK on the phone, and the
partner's Score must never self-update from Wave's APK. Each APK's sidecar gains a `form` field
(`phone` or `watch`), read with aapt2 from `android.hardware.type.watch`, and `SiblingUpdatePlan`
and `UpdateScope` filter on it (W-3.1.1). This is the phase's most silent failure, so it is L3 xhigh
with a guard on both ends.

### 2.3 CI

`.github/workflows/build-watch.yml`: branch pushes run `Watch.Tests` only; `main` and **Run workflow**
build and sign every watch APK and the face, with fatal **APP ID**, **FORM** (each watch APK says
`watch`), **ICON**, **FONT** (Plex Mono) and **DEPENDENCY** checks, and publish to `apps-latest` with
`form: watch` sidecars. versionCode from the shared `version-code` action at **band 11** (PC4).
`CiHygieneTests` and the path filters cover the new closure.

### 2.4 Keys on the watch (A38, A61)

- **The vault passphrase never reaches the watch** (owner's ruling, 27 Nov 2026, retiring A38): one
  passphrase opens every key in both vaults, and nothing on the watch needs it — tokens are handed over
  (A61), TIDAL plays through the phone, Dash goes through Post. `TheWatchHoldsNoVaultTests` holds it.
- **Google tokens** are handed over from the phone over the Data Layer (A61), as B3 built it: the watch
  asks, Score answers with a read-only Drive token (no Gmail token: Post relays mail lines). A token is
  held in memory only, never written down or logged. With the phone off the token runs out within the hour,
  which matches A67: the phone must be on anyway.

### 2.5 Contracts (each a Data Layer path pair, a versioned message, a codec in Watch.Domain)

| Contract | From → to | Carries |
|---|---|---|
| `dash/people` | Post → Dash | the waiting correspondences: name, one line, relative time, unread, group flag; photos on request |
| `dash/thread` | Post → Dash | the last messages of one correspondence |
| `dash/send` | Dash → Post | a reply or new text (person or group), dictated or typed |
| `dash/file`, `dash/delete`, `dash/undo` | Dash → Post | the act, by source id; Post keeps its tombstone rule |
| `dash/read` | Dash → Post | read on the watch is read on the phone (A14) |
| `dash/call-note` | Dash → Post | the dictated note for a call (A13) |
| `dash/mail` | Post → Dash | people-only mail lines (subject, sender, time; A11) |
| `wave/queue`, `wave/position` | Score ↔ Wave | one queue, resume points, who owns playback (A18, A20) |
| `wave/remote` | Wave ↔ Score | play, pause, skip, volume, move here (A20, A62) |
| `wave/signals` | Wave → Score | listening signals for the taste profile (A18) |
| `wave/settings` | Score → Wave | per-show speed, skip-silence, the equalizer (A18) |
| `beat/today` | Constitution → Beat | today's session: exercises, sets, reps, rests, tempo bands, duration, zone limits as numbers (A26) |
| `beat/session` | Beat → Constitution | the finished session (HR series, cadence, distance, sets), deleted from the watch once confirmed (A29) |
| `beat/day` | Beat → Constitution | all-day steps and resting HR (E9) |
| `update/apk`, `update/installed` | Flyleaf ↔ Point installer | APKs over `ChannelClient`; the installed versions back |
| `keys/google` | Post, Score → watch | the handed-over tokens (§2.4) |

Every message carries an integer `v`; a receiver refuses a newer one it cannot read and logs the
contract and outcome only, never the payload (the suite's contract rule). Body values cross only on
`beat/session` and `beat/day`, and both are on `BodyStaysHomeTests`' allowlist as named paths (TF §7).

### 2.6 Plan choices (the owner's to overturn)

| # | Choice | Why |
|---|---|---|
| PC1 | The watch apps share their phone app's package id; the face has its own | Google's Data Layer rule (§2.2) |
| PC2 | The face is built by a C# tool into a code-free APK | Watch Face Format refuses code; keeps ".NET only" (A40) |
| PC3 | Beat tells Wave the tempo on the watch through an explicit broadcast under a signature permission | the suite's contract pattern, on one device |
| PC4 | Every watch APK takes versionCode band 11 | clear of every phone band; same-package codes are per device |
| PC5 | Screens are Android views and canvas, not Compose | Compose is Kotlin-only |
| PC6 | Dash's Rolodex comes from Post over the Data Layer, not the watch's contacts | one address book, Post's |
| PC7 | Google tokens hourly from the phone; nothing signs in on the watch | A61; nothing to type on the wrist |
| PC8 | The Point installer is Flyleaf's package on the watch | the Data Layer needs Flyleaf's id at both ends |

---

## 3. Epics

### E1 — The watch kit, tokens and guards

- **Goal:** one place for the watch language (WD) and the pure logic every app draws from.
- **Exit criteria:** `WatchTokens` is the only file with a colour or type size; the day colour and hour
  shape are tested; a watch head compiles here.

**W-1.1.1 · WatchTokens and the leak guard**
- *Story:* As the owner, I want the approved language in one file, so that no screen drifts from it.
- *Acceptance:* the four day ranges in OKLCH, the three type sizes, the four haptic shapes, the motion
  durations, Plex Mono; a guard fails on a hex or a type size anywhere else in `Watch/`, and on any
  Paper & Ink token or font named in a watch project (A33).
- *Tests first:* `WatchTokensTests`, `WatchLeakGuardTests.NoHexOutsideTokens`, `NoPaperAndInkInTheWatch`.
- **S · Tests · L2 · medium**

**W-1.1.2 · DayColour**
- *Story:* As the owner, I want each app's colour to drift across its range once a day (A51), so that
  the watch follows the day.
- *Acceptance:* midnight is the night end, noon the day end, easing between, never jumping; the hue
  takes the shorter way round; dark ends stay dark (A54); Beat runs forest green to bright red (A53).
- *Tests first:* `DayColourTests.MidnightIsTheNightEnd`, `NoonIsTheDayEnd`, `NeverJumps`,
  `ShorterWayRound`, `DarkEndsStayDark`.
- **S · Tests · L2 · medium**

**W-1.1.3 · HourShape**
- *Acceptance:* sides from the 12-hour hour (1 a dot, 2 a line), hue from the minute round the full
  wheel, the after-noon ring from 12:00, standing on a point at twelve (A46).
- *Tests first:* `HourShapeTests.OneIsADot`, `TwoIsALine`, `FourIsADiamondOnItsPoint`,
  `AfternoonHasASecondRing`, `MinuteSweepsTheWheel`.
- **S · Tests · L2 · medium**

**W-1.2.1 · The watch kit's views**
- *Acceptance:* `WaveView` (progress bright along the wave, height as volume, flat when paused,
  mock: Now playing / Paused / Turning the crown), `DashGlyphView`, `RingView`, `UndoRing` (six
  seconds or Android's time to take action, whichever is longer), `WatchText` (one sentence, wraps
  once, never shrinks; WD §5), every view describing itself to TalkBack.
- *Tests first:* `WaveGeometryTests` (pure geometry in Domain), `UndoWindowTests`, `WatchA11yTests`.
- **M · Owner · L2 · medium**

**W-1.3.1 · Haptics**
- *Acceptance:* four patterns with one meaning each (A35, O4); text buzzes held during a set and
  released in rest (A26); email never buzzes (A11).
- *Tests first:* `HapticPlanTests.FourPatternsOneMeaningEach`, `TextsWaitForRest`, `MailIsSilent`.
- **S · Tests + Device · L2 · medium**

### E2 — Point: the face

**W-2.1.1 · FaceStudio generates the face**
- *Story:* As the owner, I want the polygon face exactly as approved (A46), so that the time reads as a
  shape and a colour.
- *Acceptance:* the Watch Face Format XML is generated from `HourShape` for every hour and minute
  colour, with the date in the shape's colour at low saturation and always-on as outline only with no
  date (WD §10); validated with Google's validator; packaged with aapt2 into an APK with
  `hasCode="false"`; nothing on the face but time and date (A34).
- *Tests first:* `FaceStudioTests.EveryHourHasItsShape`, `AlwaysOnIsOutlineWithoutDate`,
  `TheApkHasNoCode`, `NothingButTimeAndDate`.
- **L · Owner + Device · L3 · high**

**W-2.1.2 · Gestures, if Wear OS allows them**
- *Acceptance:* the face offers no gestures of its own; tiles and the crown are the way in (A35's
  fallback, TF §3.4).
- *Tests first:* `FaceStudioTests.NoTapTargetsOnTheFace`. **XS · Tests · L1 · low**

### E3 — Point: updates from the phone (A70)

**W-3.1.1 · The update channel tells a watch APK from a phone APK**
- *Story:* As the owner, I want Flyleaf never to install a watch app on my phone, so that Post, Score and
  Constitution are never replaced by their watch halves.
- *Acceptance:* `publish-rolling-release` writes `form` into each sidecar; `SiblingUpdatePlan` and
  `UpdateScope` skip a `watch` APK on a phone and a `phone` APK on the watch; a sidecar without `form`
  is read as `phone` (every APK published before P14).
- *Tests first:* `WatchFormTests.APhoneNeverInstallsAWatchApk`, `TheWatchNeverInstallsAPhoneApk`,
  `NoFormIsPhone`, `CiHygieneTests.EveryWatchApkSaysWatch`.
- **M · Tests · L3 · xhigh**

**W-3.1.2 · The Point installer**
- *Acceptance:* receives an APK over `ChannelClient`, installs it with `PackageInstaller`; the first
  install of each app asks once on the wrist; updates set `USER_ACTION_NOT_REQUIRED` as installer of
  record with `UPDATE_PACKAGES_WITHOUT_USER_ACTION`; reports installed versions back; logs outcomes,
  never contents.
- *Tests first:* `WatchUpdatePlanTests` (pure: what to send, what to ask), `InstallOutcomeTests`.
- **L · Tests + Device · L3 · xhigh**

**W-3.1.3 · Flyleaf sends watch updates**
- *Acceptance:* the six-hourly run also reads `form: watch` APKs, asks the watch what it has, and sends
  what is newer over the Data Layer while the watch is reachable; Settings' quiet update line says how
  the watch's last run went.
- *Tests first:* `FlyleafWatchUpdateTests.SendsOnlyNewer`, `WaitsForTheWatch`, `SaysHowItWent`.
- **M · Tests + Device · L3 · high**

**W-3.2.1 · The watch workflow** (§2.3). *Tests first:* `CiHygieneTests` cases for `build-watch.yml`.
**M · Tests · L2 · medium**

### E4 — Dash: the relay (Post's side)

**W-4.1.1 · The Dash contracts in Post**
- *Story:* As the owner, I want Post to stay the one place my texts live (A9), so that the watch never
  keeps a second, different copy.
- *Acceptance:* Post answers `dash/people`, `dash/thread`, `dash/mail` from its timeline, and carries out
  `dash/send` (through the bridge, SMS or group MMS as on the phone), `dash/file`, `dash/delete`,
  `dash/undo`, `dash/read` and `dash/call-note` with its existing rules (tombstones, the undo window,
  group threading); mail lines are people only (A11); nothing goes to the watch while the watch is
  unreachable.
- *Tests first:* `DashRelayTests.PeopleWaitingNewestFirst`, `ReplyGoesThroughPostsSendPath`,
  `FileKeepsTheTombstoneRule`, `ReadOnTheWatchIsReadOnThePhone`, `MailIsPeopleOnly`.
- **L · Tests + Device · L3 · xhigh**

**W-4.1.2 · Tokens and the vault on the watch** (§2.4, A38, A61). *Tests first:*
`KeyHandoffTests.TokenNeverLogged` (`PassphraseKeptInKeystoreOnly` retired with A38, replaced by `TheWatchHoldsNoVaultTests`). **M · Tests · L3 · high**

### E5 — Dash: the watch app (mock: Dash)

**W-5.1.1 · People waiting and a conversation** (A14). **M · Owner · L2 · medium**
**W-5.1.2 · Replying** by dictation or keyboard through the system's input; new texts from the Rolodex
or by voice (A10). **M · Owner + Device · L2 · medium**
**W-5.1.3 · Group texts and photos**, reply to all (A10). **S · Owner · L2 · medium**
**W-5.1.4 · Filing and deleting, undo-first** (A14, O5). *Tests first:* `DashUndoTests`. **S · Tests · L2 · medium**
**W-5.1.5 · The call note** after a system call (A13). **S · Owner + Device · L2 · medium**
**W-5.1.6 · Mail lines**, silent (A11). **S · Owner · L2 · medium**
**W-5.1.7 · The People waiting tile**: the dash glyph, newest name in full, no count (A72). **S · Owner · L2 · medium**
~~**W-5.1.8 · First run: the vault passphrase**~~ — retired by the owner's ruling (27 Nov 2026): the watch never holds the passphrase.
**W-5.1.9 · Overnight and unknown callers**: favourites (starred in the Rolodex) and repeat callers only
at night; unknown numbers ring on a second call within minutes (A12). Dash sets the watch's own
Do Not Disturb rules where Wear OS lets an app; where it does not, it is a setup step in the device test
plan. **S · Device · L2 · medium**

### E6 — Wave (mock: Wave)

**W-6.1.1 · The wave player**: progress along the wave, height as volume, flat when paused, tap to pause,
edge taps to skip (A72). **M · Owner · L2 · medium**
**W-6.1.2 · Streaming** Drive music over LTE with the next track prefetched (A17), with the handed-over
token (§2.4). **L · Device · L3 · high**
**W-6.1.3 · Podcasts**, the curated list synced with Score, the phone's speed and skip-silence (A18).
**M · Tests + Device · L2 · medium**
**W-6.1.4 · One queue and who plays**: whoever starts owns it, one line to switch, move with a tap (A18,
A20). *Tests first:* `QueueMergeTests`, `PlaybackOwnerTests`. **L · Tests + Device · L3 · high**
**W-6.1.5 · TIDAL through the phone, indistinguishable** from the library; "Needs your phone nearby"
when away (A62, A73). *Tests first:* `MergedLibraryTests.NoServiceNamedOnTheWatch`. **M · Tests · L3 · high**
**W-6.1.6 · Browse**: artists, albums, playlists, genres, songs, voice search (A19). **M · Owner · L2 · medium**
**W-6.1.7 · The Now playing tile** (A72). **S · Owner · L2 · medium**
**W-6.1.8 · Listening counted** for the taste profile (A18). **S · Tests · L2 · medium**
**W-6.1.9 · Cadence tempo**: steps per minute choose the tempo, the plan's band for strength, cadence
wins over the plan, crossfade 5–6 s (A21). *Tests first:* `CadenceTempoTests`. **M · Tests + Device · L3 · high**

### E7 — Beat: the session (mock: Beat)

**W-7.1.1 · The Health Services binding, pinned and guarded** (A64): `Xamarin.AndroidX.Health.ServicesClient`
at its exact prerelease version in `Directory.Packages.props`, with a guard that names the version.
**S · Tests · L2 · medium**
**W-7.1.2 · Today's workout from Constitution**, with the morning's swap; Start (A25, A26).
**M · Tests + Device · L3 · high**
**W-7.1.3 · Sets and rests**: tap anywhere when done, the rest ring and its buzz, texts in rests (A23, A26).
**M · Owner + Device · L2 · medium**
**W-7.1.4 · Runs and walks**: cadence, live heart rate and distance, no route kept (A23).
**M · Device · L2 · medium**
**W-7.1.5 · Ease off** (A24): above the maximum zone for a set time, or the heart not recovering in a
rest; zone limits arrive as numbers, never a birthdate. *Tests first:* `ExertionWatchTests.AboveMaxForLong`,
`NotRecoveringInRest`, `NeverFromOneReading`, `LimitsNotBirthdate`. **M · Tests + Device · L3 · xhigh**
**W-7.1.6 · Beat tells Wave the tempo** on the watch (PC3). **S · Tests · L3 · high**

### E8 — Beat: back to the phone (Constitution's side)

**W-8.1.1 · Sessions into Constitution and Health Connect** (A29, A66): stored in Constitution's own
store and written to Health Connect as exercise sessions, deleted from the watch once confirmed;
Constitution gains Health Connect **write** permissions for exactly those records, and the permission
check grows by them. *Tests first:* `BeatSessionTests.DeletedOnlyWhenConfirmed`,
`WritesOnlyBeatsOwnRecords`, `BodyStaysHomeTests` with the new named path. **L · Tests + Device · L3 · xhigh**
**W-8.1.2 · Discrepancies in the digest** (A25): ink and watch disagreeing is a calm line on the Saturday
digest, effort outside the plan feeds the next week's plan. **M · Tests · L3 · high**
**W-8.1.3 · Zone limits from history and age** (A24): computed on the phone from history and the
birthdate in the personal vault; only the limits travel. **M · Tests · L3 · high**

### E9 — Beat: the day, and her watch

**W-9.1.1 · All-day steps and resting heart rate** through Health Services passive monitoring, to the
phone in batches (A27). **M · Device · L3 · high**
**W-9.1.2 · Sleep stays Fitbit's** (A65): Fitbit keeps writing sleep to Health Connect; Body compares it
with the bed as P13 already does. No new code beyond the device check. **XS · Device · L1 · low**
**W-9.1.3 · The eating-out card**, silent, at the dinner hour when away (A28). **S · Tests + Device · L2 · medium**
**W-9.2.1 · Her watch**: Wave and Beat paired to her phone's Score and Constitution, her own TIDAL login
(A22), her sessions to her phone only (A29); no Dash, no face required (A39). **M · Device · L2 · medium**

### E10 — On the watches

**W-10.1.1 · The device test plan**, `Watch/P14-DEVICE-TEST-PLAN.md`, invented data only, results kept in
the owner's own copy; a guard fails if a PBI marked Device has no check. **M · Tests · L2 · medium**
**W-10.1.2 · The run**, with the owner on both watches and phones. **Device · owner**

---

## 4. Frozen identifiers and guards

The package ids in §2.2; the face's id; every Data Layer path in §2.5; the installer's registered names;
the sidecar's `form` field and its two values. Each is a string
literal in one constants class, held by `WatchInvariantsTests`, never `nameof`.

## 5. Data and privacy

- Body values on the watch: live heart rate during a session only (A29). The session and day data cross
  to the phone and are deleted once confirmed; both paths are named in `BodyStaysHomeTests`.
- The birthdate stays in the personal vault on the phone; the watch receives zone limits (A24).
- Messages are on the watch only as the relay brings them, and are dropped when a correspondence is
  filed or after seven days.
- Logs carry outcomes and contract names, never a message, a number, a reading or a token.

## 6. Risk register

| # | Risk | Mitigation | PBI |
|---|---|---|---|
| R1 | A watch APK installed on the phone, replacing Post, Score or Constitution | the `form` field, guards on both ends, a FORM CHECK in CI | W-3.1.1 |
| R2 | Silent updates do not work on Wear OS | each update asks once on the wrist; found on the first update | W-3.1.2 |
| R3 | The Data Layer relay is slow or drops with the phone at home | Bluetooth first, cloud relay second; a quiet line in Dash, no buzz | W-4.1.1 |
| R4 | The alpha Health Services binding breaks | pinned and guarded; a binding drift fails the build | W-7.1.1 |
| R5 | Ease off fires wrongly | never on one reading; limits from history; device check | W-7.1.5 |
| R6 | Battery under LTE streaming | the owner's call (A7); measured on the device | E10 |
| R7 | Watch Face Format refuses a generated face | validator in the tool; device check | W-2.1.1 |

## 7. The owner's outstanding actions

- Install the Point installer on the watch once, from a PC with adb (TF §5).
- Delete the old `watch-spike` pre-release and tag on GitHub.
- Confirm the household's TIDAL plan and give the partner her own login (A22).
- Turn off RCS for the number (A16), already recorded.

## 8. Not verified until a device

All of it: the face on both watches, the installer and silent updates, the relay with the phone at home,
dictation, the call note after a real call, Drive streaming over LTE, the queue and remote across two
devices, TIDAL through the phone, Health Services cadence and heart rate, Ease off, the Health Connect
writes, the partner's watch, the four buzzes, and a real day's battery.

## 9. Open questions

| # | Question | State |
|---|---|---|
| OQ1 | Does the owner approve the technical finding? | **Approved (A77)** |
| OQ2 | Plan choices PC1–PC8 | **Approved (A75, A77)** |
