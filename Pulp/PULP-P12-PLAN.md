# Pulp (P12) — Gate 4 implementation plan

**Status:** approved (D274). Builds what `PULP-P12-DISCOVERY.md` (D1–D273) decided,
`PULP-P12-TECHNICAL-FINDING.md` (approved, D211) measured, and the fifteen approved mockups in
`Pulp/design/` drew. Nothing here re-opens a decision; where the plan had to choose something the
decisions left open, it says so under **Plan choices** and the choice is the owner's to overturn.

---

## 0. The mockups are the specification

Every UI task is built **1:1** from its approved mockup, and anything that cannot be built as drawn is
**reported back, not substituted**. The published book is
https://claude.ai/artifact/M5cdj33HaqxnXsdvv2bu4N; the committed copies are what briefs embed.

| # | Mockup | File | Approved | Release |
|---|---|---|---|---|
| 01 | Continue watching | `design/mockup-01.html` + `.png` | D212 | C |
| 02 | A title page | `mockup-02` | D218 | C |
| 03 | Discover | `mockup-03` | D222 | C |
| 04 | A playlist | `mockup-04` | D225 | C |
| 05 | Recently watched | `mockup-05` | D229 | C |
| 06 | Favorites (a child's view) | `mockup-06` | D234 | D |
| 07 | Sports | `mockup-07` | D243 | E |
| 08 | Calendar | `mockup-08` | D247 | E |
| 09 | Settings | `mockup-09` | D250 | D |
| 10 | The action card | `mockup-10` | D256 | C |
| 11 | Add to… (the picker) | `mockup-11` | D259 | C |
| 12 | Notices | `mockup-12` | D262 | C |
| 13 | Empty states | `mockup-13` | D265 | C |
| 14 | The book: cover and icon | `mockup-14` | D268 | A |
| 15 | Index: the usage line | `mockup-15` | D271 | F |

**Built without mockups, by the owner's decision (D272):** a service's own page, the Household group,
search results and mood search, standings / bracket / league schedule, the prompt-or-seed playlist
flow, a film's title page, Vault and first run. Each is built from the decisions it cites, in the
frame and components of the approved screens beside it, and **reviewed by the owner in the running
app** — the plan's definition of done says so for each.

### Plan choices (decisions the record left open)

- **P1 · Project layout.** `Pulp/` at the root (D189): `Pulp.Domain` (`net10.0`, pure, no Android),
  `Pulp.Infrastructure` (`net10.0`: HTTP clients, the household file, codecs — every client faked in
  tests), `Pulp.Android` (`net10.0-android` native head, references `Codex.Ui.Android`, `Codex.Domain`,
  `Storyworthtelling.Voice`, `Storyworthtelling.Updates`), `Pulp.Tests`. `Pulp.sln` holds all four;
  CI targets the test project so it never needs the workload — the FocusPhone pattern.
- **P2 · The trailer player.** `PaperWebView`'s two postures both forbid it (Reading turns script off;
  Browsing navigates in place). A third posture, **`Player`**, is added to the shared component:
  script on, **no navigation at all**, one fixed https `baseUrl` so YouTube's Referer check passes
  (finding §2.1). It is the only place Pulp plays anything, and a guard says so.
- **P3 · The kids' time limits, together-watching and every TV surface** are not in this plan
  (D187, D190, D194). The household file's schema carries room for them (versioned records) so the
  TV phase adds fields rather than migrating.
- **P4 · One PR for the phase**, as Index ran: releases land as labelled commits on
  `claude/marquee-p12`; the PR opens only when CI is green and the owner asks.

---

## 1. Addenda the gates added to the finding

- **The data stack is final** (D203–D210): Streaming Availability (availability, deep links, expiry,
  metadata, posters), TVmaze (episode air dates), OMDb (the rating chip only — **no reasons**, D207),
  YouTube Data API (trailers, league highlights), MLB's own feed and ESPN's JSON for sport (D208).
  **No TMDB anywhere** (D203); a guard scans for its host.
- **Refresh shape** (D204): weekly per tracked title, a daily `/changes` pass for the household's
  services, and a fresh look on open. The month's lookup count goes to **Diagnostics only** (D261).
- **Search** is title, mood and team (D209). **Rating chip** in four steps (D215).
- **Children** may start titles only on attested services (D210, D249: "Adults only" badge).
- **Identity:** Marigold, OKLCH 63°, cloth `#F6A24E` (D202); icon full bleed (D267).

---

## 2. How the work is run

The orchestrator (this session) owns the gates, the owner's questions, every push and the PR.
Subagents do bounded tasks — **at most four at a time, never two on one file** — and report back.
Levels, as Index used them:

| Level | For |
|---|---|
| **L1 — mechanical** | Fully specified here plus a repo precedent (a workflow, a constant, an IconStudio spec). |
| **L2 — standard** | One project, an existing pattern; UI built 1:1 from a mockup. |
| **L3 — judgement** | New algorithms; cross-app contracts; anything whose failure is **silent** (frozen names, merge, encryption, the children's filter, spoilers). |

**An L3 agent reviews every L1/L2 diff in its release before the orchestrator pushes.** The
orchestrator then runs the suites and compiles the head itself (§5) — an agent's report is not
verification.

### What every brief carries

The task, release, level and the files it may touch; **the guards to write first** (§4), with the
instruction to watch each fail and record the red run in the commit message; the verification
commands (§5); the standing rules (§2.1) pasted in; the graphify rule (orient with
`graphify query "<real type names>"`). **Every UI brief embeds its mockup**: the section pasted
verbatim from `design/mockup-NN.html`, the PNG opened with Read before any code, and "build 1:1;
report what can't be built as drawn". A subagent never pushes, never opens a PR, never asks the owner.

### 2.1 Standing rules

- Tests first, watched failing, red run recorded. Untested code is not done.
- No hex or type-size literal outside registered token files; no underscores in identifiers; no empty
  `catch`; no model identifier in any committed file.
- **Invented data only** in tests, fixtures, docs and screenshots. No real service list, title,
  team, name or history — ever.
- Keys and tokens live in the vault and Actions secrets; never in code, config, logs or test output.
- Read binding/API member names from the packages' own metadata before coding against them.
- **Compile the Android head locally** every round; CI is not the first compiler.
- WIP checkpoints are labelled `WIP checkpoint (unreviewed snapshot)` and squashed before the PR
  (`--force-with-lease`, own branch only).
- Say plainly what is not verified. "Passes against fakes" is not "works".

---

## 3. Releases

### Release A — the app, its frozen set, the book

- **[L3]** `PulpIdentity.ApplicationId = "com.storyworthtelling.pulp"`; registered Java names for
  `MainActivity`, `FridayStackWorker`, `WeeklyRefreshWorker`, `DailyChangesWorker` and their unique
  work names; every preference key a literal. **G1.**
- **[L3]** `SecretsVault.Pulp` (`StreamingAvailabilityKey`, `OmdbKey`, `YouTubeKey`) with
  `[JsonPropertyName("Pulp")]`; the wire-name test written as the file on Drive, not a round trip
  (Foreword's lesson). **G2.**
- **[L1]** `build-pulp.yml` from `build-plate.yml`: test job on `main` + `claude/**`; build job on
  `main` and Run workflow; JDK 21; **fatal APP ID CHECK**, **ICON CHECK**, **FONT CHECK over all eight
  fonts by name** (native head: `assets/fonts/`); versionCode from the shared action at **band 9**; a
  `SuiteVersionBands` entry; the rolling-release step; artifact **Pulp-Android**. Plus the existing
  guard that every workflow names a real folder. **G3.**
- **[L2]** Identity: `Codex.Domain` `DesignTokens.Identity.Pulp*`, `Tools/IconStudio/Tokens.cs`,
  `Storyworthtelling.Paper/AppIdentityTokens.cs`, DESIGN.md §3 table and §19 — and **Pulp added to
  `IdentityTiersStayClearOfEverySemanticRole`** and to Folio's sibling-distance guard. **G4.**
- **[L1]** IconStudio `Pulp` spec from `Branding/pulp-cover.jpg` (the approved den painting):
  **full-bleed** launcher icon (D267), the shelf cover in marigold cloth with ink rule, mirrored
  flourishes, keyline window and title band (§12), `cover_pulp` for Flyleaf's shelf. → *Embed
  mockup 14.*
- **[L1]** Leak guard for `Pulp/` (no hex / type-size literal outside token files). **G5.**
- **[L3]** The two architectural guards, written now so every later release runs under them:
  **G6 — Pulp plays nothing itself** (no Media3/ExoPlayer/`MediaPlayer` reference, no
  `RESOURCE_PROTECTED_MEDIA_ID` grant, the only player is the `Player` posture loaded from one class);
  **G7 — no provider key or TMDB in source** (a key-shaped-literal scan across `Pulp/`, and
  `themoviedb.org` / `api.tmdb.org` absent).

### Release B — the rules (Domain, pure, every one unit-tested)

- **[L3]** `ServiceCatalogue`: service id ↔ Android package(s) ↔ availability-source id ↔ merchant
  names (for Index), public data only. `HandOff.For(title, service)` → explicit-package intent spec
  (finding §2.4). **G8.**
- **[L3]** `ChildFilter` — ceiling as an age band mapped to MPA and US TV (D127), unrated hidden
  (D128), block beats allow beats ceiling (D12, D129), a parent's favorite allow-lists (D255),
  attested-service gate (D210), strictest child wins (D175 kept for the TV phase). **G9.**
- **[L2]** `BestHome` (quality, then no-ads, D28) with the per-title override (D216); `RentLine`
  (cheapest at HD or better, D53); `RatingStep` (four steps, D215).
- **[L2]** `ContinueWatching` (most recent first, new-episode jump, caught-up leaves, finished
  leaves for good, D96, D150–D153); `RecentlyWatched` (one row per show, by last-watched day,
  forever, films and shows only, D226–D228); `Playlists` (hand order, struck-not-removed,
  caught-up dims, D101, D103, D224); `Favorites` (three sources, D230).
- **[L2]** `FridayStack` (two piles, sizes from settings, Friday 6 am, permanent vetoes, Bring it
  back, D106–D111, D219–D221) — taste learned passively plus vetoes (D24), never from a child (D23).
- **[L3]** `SpoilerHold` (D70, D237, D242): a league is held while a followed team has an unwatched
  recording; held = standings, highlights, revealing fixtures absent; one peek. **G10** — a held
  league yields **no score-bearing field** anywhere in the view model (structural, like Folio's
  `Mark`).
- **[L2]** `LeagueOrder` (closest to season's end; preseason/off-season last, D235), `LeagueFold`
  (what starts open, D241), championships always followed (D239–D240).
- **[L2]** `CalendarItems` (D146–D149, D244–D245): what, whose, timed vs all-day, two weeks.
- **[L3]** `Watched` (D10, D30, D182): session evidence first, hand-off fallback, manual override.
- **[L3]** `HouseholdDocument` + `HouseholdMerge`: per-record hybrid-logical-clock LWW with
  tombstones, commutative and idempotent; read-merge-write-reread (finding §2.7). **G11** — property
  tests over shuffled interleavings converge.

### Release C — the phone, core screens

**Wave 1 (foundations, before any screen):** the data clients, the shell, the voice list and the
hand-off executor, in disjoint folders. *Added at Release C:* the plan had no step for the four
data clients the screens read, so they land here.

- **[L3]** `Pulp.Infrastructure` clients — Streaming Availability (titles, offers, deep links,
  expiry, posters, `/changes`), TVmaze (air dates), OMDb (the rating code only), YouTube Data
  (trailer ids) — each over `HttpClient`, keys read live from the vault section, faked in tests,
  reporting credential health; mapped into a `Pulp.Domain/Catalogue` model. **No TMDB** (G7).
- **[L2]** The shell: `PulpActivity` over `BinderFrame` with the six areas of mockup 01 (Watching,
  Playlists, Discover, Sports, Calendar, Settings), area hues per DESIGN.md §9 with an area-hue
  guard, and the pinned bottom bar.

**Wave 2:** the screens below, each from its mockup.


All UI **[L2]**, each with its mockup embedded; components from `Codex.Ui.Android` (BinderFrame,
ArtworkWindow, ActionCard, AccordionPicker, Checkbox, DogEar, FilterField, WaitingActivity).

- Continue watching (01), with the voiced greeting (D118) and the bar (D213, D263).
- Title page (02), incl. the film page (D272, no mockup) and the trailer through `Player` (P2).
- Discover (03): the two stacks, the search field with Title/Mood (D170–D171), search results
  (D272, no mockup), mood search through the shared Claude key.
- A playlist (04), Recently watched (05), the action card (10), the picker (11), notices (12),
  empty states (13), and the model-made playlist flow (D104, D272, no mockup).
- **[L3]** `VoicedOccasions` closed list (D38, D99-deferred, D232, D264) with a guard that errors are
  never voiced. **G12.**
- **[L3]** Hand-off executor: explicit package, the attested-service gate, the mis-landing check via
  sessions (D122), the usage count for Index. **G13** — a child's hand-off to an unattested service
  is refused before any intent is built.

### Release D — Settings, Favorites, the household

- Settings (09) and its groups; a service's own page and Household (D272, no mockups).
- Favorites (06) and the child's view: three tabs (D231), "Watching as …" (D179), Back to me
  (D213), the age-moderated voice (D232–D233).
- **[L3]** First run and Vault (D142, D272): passphrase restore, credential health for the three keys.
- **[L3]** `DriveHouseholdStore` over `IDriveTextFile` + `VaultEnvelope`; the owner's-account file
  shared to the other adult (owner action). **G14** — refuses to write over a file it couldn't
  decrypt (the prayer-archive rule).

### Release E — Sports and Calendar

- **[L3]** `ISportsSource` with `MlbStatsSource` and `EspnSource` adapters (D208), faked in tests;
  league → source table named in About (D140). **G15** — no adapter surfaces a score into the
  Domain.
- **[L2]** Sports (07) incl. standings / bracket / schedule (D272, no mockups); Calendar (08).
- **[L3]** The household Pulp calendar: created in the owner's account, shared, found by id
  (D145); writes and silent moves (D149).

### Release F — the three cross-app contracts

- **[L3]** Pulp → Index usage (D90, D269–D271): `com.storyworthtelling.ledger.UsageReceiver` (Index kept the
  Ledger package id so it upgrades in place — `IndexHandoff.Package`), signature permission
  `…ledger.RECEIVE_USAGE`, Index's line under a matching charge (mockup 15).
  Contract tests on both sides (`IndexHandoffContractTests` pattern). **G16.**
- **[L3]** Pulp → Flyleaf hidden apps (D8, D92, D93): Flyleaf's receiver persists the set and its
  tray excludes it; `PolicyContext.PulpPackage`; Pulp's cover-out book on the sibling shelf (D91).
  **G17.**
- **[L3]** Flyleaf → Pulp playback sessions (finding §2.3): Flyleaf's existing listener forwards
  sessions for the packages Pulp named, signature permission `…pulp.RECEIVE_SESSIONS`. **G18.**

---

## 4. The guard ledger

| Id | Fails when | Where |
|---|---|---|
| G1 | the app id, a registered Java name, a work name or a preference key moves | `Pulp.Tests/Guard/PulpInvariantsTests` |
| G2 | the vault section's wire name changes | `Storyworthtelling.Voice.Tests/Secrets` |
| G3 | the workflow loses its APP ID / ICON / FONT checks or the band | `Storyworthtelling.Updates.Tests` |
| G4 | a Pulp tier crowds a role or a sibling below 20° | `Codex.Tests/Design`, `Folio.Tests/Design` |
| G5 | a hex or type-size literal appears outside the token files | `Pulp.Tests/Design` |
| G6 | Pulp gains any playback path of its own | `Pulp.Tests/Guard/PlaysNothingTests` |
| G7 | a key-shaped literal or a TMDB host appears in source | `Pulp.Tests/Guard/NoKeysTests` |
| G8 | a hand-off is built without an explicit package | `Pulp.Tests/HandOff` |
| G9 | the filter order or the unrated rule changes | `Pulp.Tests/Children` |
| G10 | a held league's view model can carry a score | `Pulp.Tests/Sports` |
| G11 | two writers' interleavings fail to converge | `Pulp.Tests/Household` |
| G12 | an error is voiced, or a seat is added off the list | `Pulp.Tests/Voice` |
| G13 | a child can reach an unattested service | `Pulp.Tests/HandOff` |
| G14 | the store writes over an undecryptable file | `Pulp.Tests/Household` |
| G15 | a sports adapter returns a score | `Pulp.Tests/Sports` |
| G16–G18 | a cross-app action, permission or payload drifts | both sides' contract tests |

Each guard's first commit records its red run and names the bug it reintroduced. **A guard that
passes against an empty input is a vacuous guard** — each asserts it found something to check.

## 5. Verification, every round

```
dotnet test Pulp/Pulp.Tests/Pulp.Tests.csproj
dotnet test FocusPhone/Codex.Tests/Codex.Tests.csproj
dotnet test Storyworthtelling.Voice.Tests/Storyworthtelling.Voice.Tests.csproj
dotnet test Storyworthtelling.Updates.Tests/Storyworthtelling.Updates.Tests.csproj
dotnet test Ledger/Ledger.Tests/Ledger.Tests.csproj -p:LedgerTfms=net10.0      # Release F
FocusPhone/scripts/setup-android-build.sh                                    # once per session
dotnet build Pulp/Pulp.Android/Pulp.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false
```

Then the branch's Actions runs are checked after every push and red runs fixed here; a local pass is
not CI.

## 6. Definition of done

Per release: guards red-then-green; suites and the head compile locally; CI green on the branch;
an L3 review of every L1/L2 diff; the mockup's screen matched 1:1 or the gap reported. For the
D272 pages: built, and listed in the PR as **for the owner's review in the running app**.

**Phase:** an independent read-only review of the whole diff (correctness, vacuous guards, CLAUDE.md
hygiene, privacy by file and line, doc accuracy); its blockers fixed. Then CLAUDE.md (a Pulp section:
commands, frozen set, what's unverified), the roadmap artifact, and DESIGN.md §3/§19 updated. The PR
follows `.github/pull_request_template.md` and opens only when the owner asks.

## 7. What is not verified until a device

Each service's deep link landing on the title; each service publishing title and position into its
media session; the Flyleaf → Pulp session relay under the lockdown; the profile-PIN configuration
actually stopping a child; the household file visible and writable from her account; the Pulp
calendar share; live-channel links; the free lookup budget against a real month; the trailer
player's Referer under a real WebView; the in-place install from the rolling channel at band 9.

## 8. Owner actions before the first device test

Create the three keys (Streaming Availability, OMDb, YouTube Data) and enter them once in Pulp's
Vault; share the household file with her account; PIN-lock adult profiles on the services the
children use and attest them in Settings; grant her phone's notification access to Pulp.
