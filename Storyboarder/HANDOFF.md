# Storyboarder — session handoff

**Phases 1–3 are complete; Phase 4 is partly done; Phases 5–6 remain.** Phase 1 merged in PR #111. Phases 2,
3, and the safe half of 4 landed on branch `claude/storyboarder-handoff-phases-nbhko0` (Phase 2 backend
`bfa44bc`→`38574ad` + UI `a186b76`; Phase 3 `000fe63`→ reader/report/diff pages; Phase 4 `bf30279`→`8a9803c`
— shot types, weighted layout, dialogue trim, derived art style). The rest of Phase 4 needs a live vision
model or on-device visual iteration (see its section). Read this top to bottom before starting work —
several of the traps below cost a full CI round each to discover.

**Phase 2 is complete end to end.** The draft loop is built and tested in Domain/Infrastructure —
`ProjectService.CreateNextDraftAsync` (add a draft), `GetDraftDiffAsync` (findings resolved/new + changed
panels), and the storyboard-stage reuse that carries an unchanged window's renders forward — **and now
wired into the Android head**: each project card offers "New draft (re-pick doc)" (opens the Drive picker
in draft mode) and, once a draft has a predecessor, "What changed since last draft" (a diff summary). The
head compiles clean (see the build note below). The diff surface is a text summary today; the richer
side-by-side old-vs-new **art** view the owner asked for is a Phase 3 reader job — `GetDraftDiffAsync`
already returns the paired image paths for it.

**The Android head now COMPILES in a web/CI container.** Run `Storyboarder/scripts/setup-android-build.sh`
once (a few minutes: it installs the maui-android workload from nuget, pulls android.jar for API 36 from a
GitHub mirror since `dl.google.com` is blocked, and stitches together a minimal SDK at `/opt/android-sdk-min`
with the workload's bundled aapt2/r8 plus apt's zipalign). Then UI edits are no longer written blind:

```
Storyboarder/scripts/setup-android-build.sh
dotnet build Storyboarder/Storyboarder.Android/Storyboarder.Android.csproj \
  -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false
```

This gets `dotnet build` green through XAML + C# + resource linking; it does **not** run the app (no
emulator/display). The setup is ephemeral — the container is reclaimed — so re-run the script each session
you need to build the head. Everything it does is documented in the script's header.

---

## 1. Start here: environment

**There is no .NET SDK in a fresh container**, and the official installer cannot fetch one — the network
policy blocks `builds.dotnet.microsoft.com` and `dotnetcli.azureedge.net`. Ubuntu's archive has it.

A SessionStart hook already handles this: `.claude/hooks/session-start.sh` installs `dotnet-sdk-10.0` via
apt (running `apt-get update` first, because the image's package index is stale and installing without it
404s on every `.deb`). It is remote-only and idempotent.

> **Important:** the hook lives on this branch and is **not yet merged to `main`**. Start the next session
> **on this branch**, or merge PR #111 first — otherwise the session has no compiler and the whole
> write-blind/red-CI cycle described below starts over.

Verify the toolchain, then the suite:

```
dotnet --version     # expect 10.0.x
dotnet test Storyboarder/Storyboarder.Tests/Storyboarder.Tests.csproj -p:StoryboarderTfms=net10.0
```

**Baseline: 322 passing, 0 failing.** Run this before every commit. Never commit a red suite.

---

## 2. The product thesis (this shapes every decision)

Storyboarder is **not** a graphic-novel generator. It is a **manuscript revision instrument for an audience
of one, in which the illustration is a comprehension probe.** If the rendered art doesn't match what the
author pictured, that is evidence the prose under-specifies the scene.

Consequences, all confirmed directly by the owner:

- **The revision report is the primary deliverable.** The art exists to show whether the prose conveyed
  what was pictured.
- **The unit of work is the draft, not the panel.** The owner explicitly declined panel regeneration,
  variant history, reroll budgets, and panel↔prose linkage UI.
- **It must be hands-off.** They declined review gates, design-bible editing, run controls (pause/resume,
  queue ordering, stop-after-stage), and notifications entirely.
- **Audience of one, screen only, phone only.** Therefore no print/PoD, no CMYK or bleed, no sharing links,
  no collaboration, no tablet or desktop build, no EPUB/CBZ.

When a feature seems obviously useful but contradicts the above, it was almost certainly already declined.

## 3. Confirmed product decisions

Gathered from 39 answered questions. Treat as settled unless the owner reopens them.

| Area | Decision |
|---|---|
| Art's purpose | Faithfulness to the text over visual appeal — a literal read-back is the honest signal |
| Art style | **Claude derives it** from the manuscript's prose and genre (not yet built — Phase 4) |
| Colour | Colour, not black-and-white |
| Page craft | Varied panel sizes, full-page splashes, deliberate camera progression. **No** two-page spreads |
| Character looks | One canonical sheet unless the text describes a change; then a new sheet at that point |
| Consistency tools | Off-model detection and fix-a-character-then-re-render. Automatic, not a review queue |
| Reference sheets | Recurring cast only (built) |
| Design bible | Not viewable or editable — keep it internal |
| Dialogue | Trimmed to fit balloons (readability wins over verbatim here, unlike the art) |
| Lettering | Balloon tails, thought-vs-speech, SFX. **No** speaker name labels |
| Volumes | Let Claude decide the breaks |
| Reading | In-app reader with chapter jump and panel zoom |
| Report | Wants line edits, prose/voice, dialogue critique, scene breakdown (all built); chapter references, not quoted passages; in-app **and** PDF; report-only mode as an option |
| Prose-signal | "The illustrator misread this" gets **its own report section** (Phase 4 — needs a vision pass) |
| Drafts | Versions **within one project**; pick a doc each time; re-render **only what changed** |
| Draft diff | Findings resolved/new, plus changed panels |
| Blocked content | Auto-retry, then ask. Never cancel the book (built) |
| Unrenderable panel | Placeholder only |
| Cost | Estimate before running, cheap tier for grunt work (both built). No spend cap, no live counter |
| Providers | Stay Claude + Gemini |
| Notifications | **None** |
| Sources | Add `.docx`. Nothing else |
| Data safety | Drive sync, verify-after-reboot, and archive completed drafts beside the manuscript |
| Extras | Fact-checking (built), owner's voice **propagated repo-wide** |

---

## 4. What Phase 1 delivered

All committed and pushed. Chronological, with the commit worth reading for each:

1. `52cd733` — the original five diagnostics fixes: 6-panel cap, reserved caption/dialogue bands,
   volume-binding retry, action-not-posed prompts, reference-sheet scale.
2. `7c75164` → `9b9131c` — schema for chapters/report/archive, plus a **snapshot-drift fix** (see traps).
3. `7f71f72`–`5bbbd66` — chapter detection (`ChapterDetector`, deterministic, no Claude call) and the
   report expansion; the reduce is **two Claude calls** (story half, craft half) because one reply cannot
   carry both without truncating or outlasting the background worker.
4. `344b8de` — advance past an already-rendered report before any preparatory work.
5. `fa581d5` — the SessionStart hook.
6. `580039b` — a blocked panel no longer cancels the book.
7. `babf6b5` — reference sheets for recurring cast only.
8. `db1dc96` — cheap model tier for per-chunk calls; run estimate driven by rate limits.
9. `f6895c9` — fact-checking via Anthropic's hosted web search.
10. `4f711c9` — `BATCHING.md`, the Phase 6 investigation.
11. `bb61137` — Drive archive.

---

## 5. Traps that have already bitten

**EF migrations are hand-written.** There is no `dotnet ef` run here. Each migration needs the `.cs`
(Up/Down), a `.Designer.cs` (a copy of `StoryboarderDbContextModelSnapshot.cs` with `[Migration("...")]`,
`partial class <Name>`, and `BuildTargetModel` instead of `BuildModel`, plus a `Migrations` using), and an
update to the snapshot.

**Snapshot drift is the #1 failure mode.** It compiles fine and fails at runtime as
`PendingModelChangesWarning` (caught by `MigrationSafetyTests`). It cost two CI rounds. When adding an
entity you must add **all three**: every property, the relationship block (`HasOne`/`WithMany`), and the
parent's `b.Navigation("...")`. Property *order* in the snapshot is cosmetic; presence and type are not.
A quick check that beats reading it by eye — diff entity properties against the snapshot with a script;
just make sure the script actually matches something, since a regex that silently matches zero classes
reports a cheerful false pass.

**`ProjectStage` persists as an integer.** Append new stages to the end of the enum, never insert in
pipeline order — inserting renumbers `Completed`/`Failed`/`Cancelled` and silently re-stages every existing
novel. Pipeline order lives in `ProjectStateMachine`, not in enum ordering.

**A new non-nullable `bool` column lands as `false` on existing rows.** If `false` changes behaviour,
backfill it in the migration (`DesignRef.NeedsSheet` does this, or every queued sheet would have silently
vanished for an in-flight conversion).

**Check the feature is reachable.** Two near-misses this session: the rework flow was gated on
`Stage == Cancelled` and would have become dead code once novels stopped cancelling; and the archive would
have had no toggle. A stage or service method with no path to it from the UI is not a feature.

**Session limits killed two subagents mid-task**, one of which lost everything uncommitted and left a
migration without its `.Designer.cs`. Commit per feature, and prefer working inline over delegating large
briefs.

**A stage must not be able to strand a finished book.** Both the volume-binding retry and the archive stage
are built so failure still lets the book complete.

---

## 6. Remaining work

### Phase 2 — drafts + incremental re-run ✅ backend done (UI pending)
The loop the owner actually cares about: revise → re-render only what changed → compare against the last
draft. All of the following is built, tested, and on the branch — only the Android UI to invoke it remains
(see the note at the top).

- **Panel→source offsets** (`bfa44bc`): every `Panel` records the absolute `[SourceStartOffset,
  SourceEndOffset)` of the storyboard window it came from. This was the phase's real cost — "what changed"
  is computable now. Window granularity: all panels of one window share its span (the model doesn't
  sub-divide a window per panel).
- **Draft versions inside one project** (`ae11d43`): `Novel` carries `ProjectId` / `DraftNumber` /
  `PreviousDraftId`. First draft is a project of one (ProjectId = its own Id); the migration backfills
  existing novels the same way. `ProjectService.CreateNextDraftAsync` adds the next draft from a re-picked
  doc, inheriting title/style/report-only.
- **Draft diff** (`8498bef`): `GetDraftDiffAsync` returns findings resolved/new (diffed by detail text
  across the four finding sections) and changed panels (paired with the previous render at the same reading
  position for a side-by-side). Reusable-vs-changed uses the shared `PanelSourceMatching` helper.
- **Incremental reuse** (`38574ad`): at storyboarding, a window whose text is byte-identical to one the
  previous draft storyboarded copies its panels — prompts and renders — forward, spending no Claude or
  Gemini call. All-or-nothing per window; re-storyboards if a render has gone missing.

**Known limitation (inherent, documented):** reuse is window-granular, and windows re-pack under edits, so
an edit early in a chunk shifts later window boundaries and those windows re-render even if their content is
unchanged. Finer (per-paragraph) anchoring would need the model to map panels to sub-spans, which it doesn't
provide. The diff is honest about this — it reports what actually carried forward.

### Phase 3 — in-app reader ✅ done
Built and compiling (via `scripts/setup-android-build.sh`). Backend read models are unit-tested; the MAUI
pages are compile-verified (there's no emulator here to run them):

- **ReaderPage** — a book's composed pages in reading order with a chapter-jump picker; tap a page for
  **PageZoomPage** (pinch-zoom, pan, double-tap reset). Fed by `ProjectService.GetReaderPagesAsync`.
- **ReportPage** — the folded revision report as browsable scrollable sections. Fed by `GetReportAsync`.
- **DraftDiffPage** — the Phase 2 "What changed" action now opens a real view: findings resolved/new and
  each changed panel paired **before/after**, the side-by-side old-vs-new art the owner asked for.
- Reachable from the project card action sheet: "Read in app", "Read report in app", "What changed since
  last draft".

Not yet done (deferred UI polish, all runtime-only concerns): the reader loads full-page rasters eagerly in
its `CollectionView` — fine for a phone-sized book, but if a volume is long, switch the list to `ThumbPath`
and load the full image only in `PageZoomPage`. Worth checking on a real device.

### Phase 4 — art fidelity + layout ⏳ partially done
The testable, safe-to-ship-blind half is built and on the branch; the parts that need a live vision model
or on-device visual iteration are deferred (this environment can compile the head but not run it).

**Done (tested):**
- **Shot types** (`bf30279`): `Panel.ShotType` (Standard/Wide/Splash), asked of Claude in the storyboard
  prompt, parsed case-insensitively (unknown/absent → Standard). Migration defaults existing panels to
  Standard.
- **Weighted layout** (`cf8be4b`): `PageLayoutCalculator` gains a shot-aware `Layout`. A page of all
  Standard shots lays out *identically* to the old uniform grid (unit-tested), so untagged pages are
  unchanged; a Wide shot is a full-width cinematic band and a Splash gets a page to itself (SplitForPages
  isolates it). The composer, `PanelArt`, and the render-time aspect-ratio calc all thread the shot through.
- **Dialogue trimmed to fit** (`cf8be4b`): `DialogueFit` abridges dialogue to a balloon budget at a word
  boundary when panels are created.
- **Claude-derived art style** (`8a9803c`): `DeriveArtStyle` picks one cohesive style from the opening prose
  + cast at design-bible time, unless the user chose one. Best-effort — failure keeps the default.

**Deferred (need a live model or eyes on a device — don't ship blind):**
- **Lettering: balloon tails, thought-vs-speech, SFX.** A Skia-composer change; correctness is visual, so it
  needs the app running to iterate. The bands/typesetting are in `SkiaPageComposer` + `PanelSlotLayout`.
- **Off-model auto-detection + re-render.** Needs a vision compare of each rendered panel against the
  character's reference sheet; heuristic, unverifiable without the image models.
- **Prose-divergence report section.** The owner won't hand-flag mismatches, so the only automatic design is
  a **vision pass**: feed each rendered panel + its source passage (the `[SourceStartOffset,SourceEndOffset)`
  span Phase 2 added makes this exact) back to the model and ask what's missing/added/wrong. Advisory, false
  positives expected; render it as its own report section. Needs Claude vision — plumb + verify with the real
  API, not blind.
- **Literal-to-text panel prompts** is largely the existing prompt already; further tuning is visual.

The weighted layout is verified as *math* (no overlap, fills the page, all-Standard == old grid) but its
*look* wasn't — worth a glance on a device once a Wide/Splash-tagged book renders.

### Phase 5 — odds
`.docx` import · verify-after-reboot · **owner's voice propagated repo-wide** (DESIGN.md §11 plus FocusPhone,
Ledger and Storyboarder copy — a design-bible change, probably its own PR).

### Phase 6 — Gemini batching
See `Storyboarder/BATCHING.md` for the full investigation. **Do the cheap thing first:**
`RateLimitOptions.GeminiRequestsPerDay` defaults to **100**, which is *this app's own safety ceiling, not
Google's limit*. Raising it to match the account's real tier may deliver most of the wall-clock win for one
line. Only then batch panel renders (50% discount and a separate quota pool). The owner has asked to be
walked through finding their real Gemini limit when we get there.

---

## 7. Waiting on the owner

- **Register the `drive.file` scope** on the OAuth client in the Google Cloud project, then sign in again on
  the device. Until then archiving stays off (which is the default, so nothing breaks).
- **Check the real Gemini tier limit** and raise `GeminiRequestsPerDay` — do this before any batching work.
- **The stuck "Heart and Mind" project recovers on its own** once this ships: tap **Retry now** and volume 1
  re-binds, which unblocks the revision report. No manual data fix needed.
