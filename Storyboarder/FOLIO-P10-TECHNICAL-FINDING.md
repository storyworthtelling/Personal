# Folio (P10) — Technical Finding

**Status:** awaiting approval. No code is written until this document is approved.

**Gate 1** settled 101 decisions across 20 rounds, with seven recorded reversals; its record is the
design book at
`https://claude.ai/code/artifact/f540076c-08d0-489b-8183-877f828214d2`.

This document is the technical half: what was verified in this session and **how**, what the risks
are, which Gate 1 decisions the evidence changed, what gets guarded, how the work breaks into
releases, what is deliberately not built, and — separately and explicitly — what is **not** verified.

---

## 0. Executive summary

Folio is the second user-facing rename of the same app. Canonizer deliberately froze every internal
name; **P10 deliberately unfreezes them** (D1), which makes this phase's riskiest work identical in
shape to P9's: a set of load-bearing strings that fail *invisibly*. Nothing breaks at build time,
nothing throws at run time — the app comes up with empty settings, or quietly stops running the
pipeline.

Four things found by reading the code changed what this phase has to do:

1. **The pipeline restart the owner asked to prevent is real, and it is not in the state machine.**
   `ProjectStage.Completed` is already terminal. `VerifyAndHealOutputsAsync` writes the stage
   backwards *directly*, bypassing `NextStage`, for any novel that is not `Cancelled` or `Failed` —
   which includes `Completed`. §1 and §3 (R-1).
2. **Prompt caching already exists in the Claude client.** `SendMessage` takes a `cachedContext` and
   marks it `cache_control: {type:"ephemeral"}`. D51's grouped, cached passes need the *grouping*
   built, not the caching.
3. **The vector switch is not a drop-in.** Folio's page geometry is 150-DPI pixels; `VectorPdfSink`
   is in points, and `PaperTypeScale` is the registered points ladder. Type changes size, pages
   reflow, and the token file holding the current geometry is the one D65 deletes.
4. **Dropping the in-app report makes the report unreachable.** The head contains no PDF-opening code
   of any kind. D85 is a deletion that removes the only path to the product unless something is built
   to replace it.

Two Gate 1 decisions are reversed on this evidence (§3), and two Gate 1 *figures* are corrected.

---

## 1. Facts — verified in this session

Every fact below is marked with how it was established. "Read" means the source file was read in this
session. "Ran" means a command was executed and its output observed.

### The build and the suite

| Fact | How |
|---|---|
| The Android head **compiles green** — 0 errors, 50 warnings | Ran `dotnet build Storyboarder.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false` |
| The suite is **298 passing, 0 failing** | Ran `dotnet test Storyboarder.Tests.csproj -p:StoryboarderTfms=net10.0` |
| The head carries a **high-severity advisory**: `SQLitePCLRaw.lib.e_sqlite3(.android)` 2.1.11, GHSA-2m69-gcr7-jv3q | Observed in the build output |

This is the **first compile since the pivot session**, which the handoff flagged as unverified. It
passes. HANDOFF.md's "build it first thing next session" is discharged.

### The frozen surface

Twelve preference keys, all `nameof()`-derived, read from `SettingsStore.cs`:

```
ClaudeApiKey · GeminiApiKey · VaultPassphrase · ArchiveToDrive
FreeLocalCopiesAfterArchive · DeliverToRemarkable · RemarkableDeviceToken
GoogleAccountEmail · LastRunOutcome · LastRunAtUtc · LastMigrationError
CanonizerSchemaReset
```

> **Correction to Gate 1.** Gate 1 recorded eleven. There are **twelve** —
> `FreeLocalCopiesAfterArchive` was missed. This is exactly why the guard asserts literals.

`CredentialHealthStore` holds a thirteenth key under its own `Key` constant, outside `SettingsStore`.

| Frozen thing | Value | What breaks, silently | How |
|---|---|---|---|
| Application id | `com.storyworthtelling.storyboarder` | The GitHub release channel installs by package id; a changed id installs a **second app** | Read csproj |
| Worker Java name | `[Register("com.storyworthtelling.storyboarder.PipelineWorker")]` | WorkManager instantiates by class name from a persisted row; a moved class is never instantiated and **the pipeline stops** | Read `PipelineWorker.cs` |
| Unique work names | `storyboarder-pipeline`, `storyboarder-pipeline-now` | `EnqueueUniquePeriodicWork` with `Keep`: a changed name leaves the old schedule running **and** adds a new one | Read `PipelineWorker.cs` |
| `MainActivity` Java name | **none declared** | Generated from the namespace. A namespace rename moves it, breaking home-screen shortcuts | Read `MainActivity.cs` |
| Database | `storyboarder.db` under `FileSystem.AppDataDirectory` | Holds every book, draft, finding and scene | Read `MauiProgram.cs` |
| Crash log tag | `"Storyboarder"` | Cosmetic only — logcat filter | Read `MainApplication.cs` |

**Folio has no vault section.** It writes the *shared* `Claude`, `Gemini` and `Remarkable` sections
via `VaultSync`. So P9's `[JsonPropertyName]` trap — a renamed section silently deserialising to
empty on every device — **does not apply here**. Verified by reading `VaultSync.cs` and
`SecretsVault.cs`; the only `[JsonPropertyName]` in the vault is Foreword's `"DailyLoader"`.

### The restart path (R-1)

Read from `ProjectStateMachine.cs` and `ProjectService.cs`:

- `ProjectStateMachine.NextStage` already has `ProjectStage.Completed => ProjectStage.Completed`.
  **Completed is terminal today.** A guard asserting that would pass without changing anything.
- `VerifyAndHealOutputsAsync` runs at app start (`MainActivity`), selects
  `Novels.Where(n => n.Stage != Cancelled && n.Stage != Failed)` — **`Completed` is included** — and
  for any missing scene image or missing report PDF sets the unit to `Pending` and calls
  `Regress(stage)`, writing `novel.Stage` backwards **without going through `NextStage`**.

So a finished book whose files have vanished — a reboot mid-flush, a storage sweep, an interrupted
write — is pulled out of `Completed` and re-run. Every remaining stage is re-billed. This is the
behaviour D69 was aimed at, and the state machine is not where it lives.

The heal path is *correct* for an unfinished book; the bug is only that `Completed` is in its
selection.

### The Claude client

Read from `ClaudeAnalysisClient.cs`:

| Fact | Consequence |
|---|---|
| Raw HTTP; payload is `{system, messages, max_tokens}` with an `anthropic-version` header | No SDK; every new API feature is hand-rolled |
| `SendMessage(..., string? cachedContext = null, string? model = null)` | The per-call model override D52 needs **already exists** |
| Both the instruction and `cachedContext` carry `cache_control = new { type = "ephemeral" }` | **Prompt caching is already implemented.** D51 needs the grouping, not the caching |
| **No `output_config`**, no structured outputs, no `thinking` | D54's machine-readable output must be added; today replies are JSON-in-prose parsed by `JsonResponse` |
| Models: `Model = "claude-sonnet-4-6"` (strong), `FastModel = "claude-haiku-4-5-20251001"` (cheap) | D52 moves section passes to Opus 5 — a real change, see R-4 |

### The report's geometry

Read from `Storyboarder.Domain/Design/DesignTokens.cs` and `Storyworthtelling.Paper/PaperTypeScale.cs`:

- Folio renders at **1240×1754 px, declared 150 DPI** (true A4), body **30px**.
- `VectorPdfSink.BeginPage(widthPoints, heightPoints)` takes **points**; a PDF's user space is
  already 1/72 inch.
- `PaperTypeScale` is the registered points ladder: `Body = 16 × 0.75 = 12pt`.
- 30px at 150 DPI is **14.4pt**. The registered ladder is **12pt**.

So the vector switch changes the type size, and **pages reflow**. Foreword's finding recorded exactly
this: re-fitting moved one page, the calendar timeline, whose strip heights were chosen for an 8pt
face.

> **Correction to Gate 1.** Gate 1 quoted the marked-up manuscript at **~203 pages** for a
> 90,000-word draft. That was computed from the geometry this phase replaces. Under the registered
> points ladder the same draft is **~140 pages** — 45 lines at 12pt/1.35 in a 489×732pt content box,
> ~645 words per page. Computed in this session from both geometries.

`DesignTokens.Pdf` is inside the token file **D65 deletes**, so the page spec has to move regardless.

### The MAUI kit

Read from `Storyworthtelling.Maui`:

- `Paper.xaml` declares `x:Class="Storyworthtelling.Maui.PaperKit"` — so
  `Resources.MergedDictionaries.Add(new PaperKit())` is the documented merge, and it works because a
  XAML `Source=` resolves only inside its own assembly.
- `PaperFonts.Register(IFontCollection fonts)` — passed straight to `ConfigureFonts`.
- The kit is `net10.0-android`, so `build-shared-libs` can only run its tests; its compilation is
  proven by the heads that reference it.

Folio currently references **neither**. It carries `Storyboarder.Domain/Design/DesignTokens.cs` and
`Resources/Styles/DesignTokens.xaml`.

`HexLiteralLeakTests` allowlists exactly **three** files (read from its source):

```
Storyboarder.Domain/Design/DesignTokens.cs
Storyboarder.Android/Resources/Styles/DesignTokens.xaml
Storyboarder.Tests/Design/DesignTokensTests.cs
```

The third is the anti-pattern DESIGN.md §19 names outright: a leak-guard test holding **a second
hand-typed copy** of the palette to check the first against, so both can be wrong together — and on
the MAUI side, were. Adopting the kit means deleting all three and **emptying the allowlist**, so the
guard then scans everything under `Storyboarder/` with no exceptions. That is what makes D65's
adoption real rather than declared.

### The report is unreachable without the in-app view (R-2)

Searched the whole head for `Launcher`, `OpenAsync`, `FileProvider`, `OpenFileRequest`: **no
matches**. The head has never opened a file with an external viewer.

D85 drops `ReportPage`. Unless a PDF-opening path is built, the product becomes unreachable from the
app — HANDOFF.md §4's "check the feature is reachable" trap, live. And it is not a one-liner: Android
7+ throws `FileUriExposedException` for a `file://` URI crossing an app boundary, so this needs a
`FileProvider` with an `android:authorities`, an XML paths resource, and `FLAG_GRANT_READ_URI_PERMISSION`.

### What is already true and needs no work

- `Completed` terminal in `NextStage` — already true.
- Prompt caching plumbing — already true.
- Per-call model override — already true.
- `VoiceProfile.IsEmpty` already means "fall back to neutral", honoured by every consumer — so D101's
  relearn window needs no new code.
- The reMarkable **root** upload already exists (`Upload` root overload); D3 deletes the folder path
  rather than building anything.

---

## 2. Risks

**R-1 — A finished book restarts and re-bills.** *Verified, live today.* The heal path regresses
`Completed` novels. With D50's ~20 Opus 5 passes, a spurious regression is no longer a nuisance; it is
a repeat of the most expensive stage in the pipeline. **Mitigation:** exclude `Completed` from the
heal selection, and guard it.

**R-2 — The report becomes unreachable.** *Verified.* D85 deletes the only in-app path to the report
and the head cannot open a PDF. **Mitigation:** the release that drops `ReportPage` must ship the
`FileProvider` path in the same release, with a guard that the book page exposes an open action.

**R-3 — The rename's invisible set.** *Verified, 12 keys + 4 names.* P9's precedent. **Mitigation:**
one `FolioIdentity` constant, frozen literal key constants, and `FolioInvariantsTests` asserting
hard-coded expected values (D61–D63).

**R-4 — Cost.** *Computed.* ~20 whole-manuscript passes on a 90k-word draft (~120k tokens) is
~2.4M input tokens per draft. Published rates: Opus 5 $5/MTok input, Sonnet 5 $2, Sonnet 4.6 $3
(what is configured today). Uncached on Opus 5 that is **~$12/draft**; grouped and cached (write
1.25×, reads ~0.1×) it is **~$2.30**. Worth noting the repo is currently on
**`claude-sonnet-4-6` at $3/MTok — more expensive than current-generation Sonnet 5 at $2**.
**Mitigation:** D51's grouping; and the model line is a decision to take deliberately, not a default
to inherit (§3, reversal 2).

**R-5 — Caching depends on grouping that fights resumability.** *Verified.* The worker runs one unit
of work per cycle with a 9-minute budget; cycles are 15 minutes apart. A 5-minute cache TTL cannot
span that. **Mitigation:** D51 groups the passes into one cycle; resumability moves to the group
boundary. The 9-minute budget is the real constraint — see U-3.

**R-6 — The markup's offsets.** Marks resolve to character offsets into the stored manuscript
(D79). Scene spans are `[StartOffset, EndOffset)`. If the stored text and the offsets ever disagree,
every mark lands in the wrong place — silently, because a misplaced mark still renders.
**Mitigation:** offsets are computed from and applied to the same stored string; a pure test asserts
placement without rendering.

**R-7 — Plate's lesson: nothing here has met a device.** Every screen, the whole report, the shelf and
the markup are new or rebuilt, and the head cannot be run in this container (no emulator, no display).
**Mitigation:** the on-device test plan, and honest gating.

---

## 3. Decisions changed by this evidence

P9 reversed two Gate 1 decisions at this gate. This gate reverses two and corrects two figures.

### Reversal 1 — D69 was right and insufficient

**Gate 1 said:** `Completed` is terminal in the state machine, with a guard proving no stage
transitions out of it.

**Evidence:** that is already true, and the restart comes from `VerifyAndHealOutputsAsync` writing
the stage backwards directly.

**Now:** the guard on `NextStage` is kept (it is cheap and it pins existing behaviour), **and** the
heal path excludes `Completed`. The real guard is on the heal path: a completed novel with a missing
PDF must heal **nothing** and stay `Completed`. Written first, watched to fail by reintroducing
`Completed` into the selection.

### Reversal 2 — D52's "Opus 5 for every section pass" is re-scoped

**Gate 1 said:** Opus 5 for every section pass.

**Evidence:** the repo runs `claude-sonnet-4-6` for its strong tier — a *previous-generation* model
at $3/MTok, which current-generation Sonnet 5 beats on price at $2/MTok. The choice at Gate 1 was
framed as "Opus 5 versus tiering down", but the real baseline was never current.

**Now:** **Opus 5 for the seven reader passes and the two synthesis passes** — the judgement calls
where quality is the product. **Sonnet 5 for the eight structural section passes**, which replaces
Sonnet 4.6 and is both newer and cheaper than what ships today. This is not the tiering that failed
for entity extraction (that was Haiku doing extraction); it is current-generation Sonnet doing
structured reads. The mix is a decision to revisit with measurement, not a permanent setting.

### Correction 1 — twelve preference keys, not eleven

### Correction 2 — the markup is ~140 pages, not ~203

Both above, in §1.

### Unchanged but now qualified

- **D37 (vector this phase)** stands, but is **not cheap**: the page spec must move from 150-DPI
  pixels to points and the type re-fits to `PaperTypeScale`. It is correctly bundled with D65, which
  deletes the file the current geometry lives in.
- **D51 (grouped, cached)** stands and is cheaper to build than assumed — the caching exists.
- **D54 (machine-readable output)** stands but needs the request shape extended; the client has no
  `output_config` today.

---

## 4. Guards

Every guard is written **first** and watched to fail by reintroducing the bug; the commit message
names what was reintroduced (standing rule).

| Guard | Asserts | Reintroduce to watch it fail |
|---|---|---|
| `FolioInvariantsTests` | App id, both work names, the worker's `Register` value, `MainActivity`'s declared `Name`, and all twelve key strings — against **hard-coded literals**, not recomputed constants | Rename any property; change any constant |
| `CompletedBooksDoNotHealTests` | A `Completed` novel with a missing PDF heals nothing and keeps its stage | Put `Completed` back in the heal selection |
| `TerminalStageTests` | `NextStage` returns `Completed` for `Completed` for every progress permutation | Add a transition out of `Completed` |
| `ReportIsReachableTests` | The book page exposes an open-the-report action; the `FileProvider` authority is declared | Delete the open action after dropping `ReportPage` |
| `KitAdoptionTests` | No file under `Storyboarder/` declares a colour hex or type-size literal; the head-local token files are **gone**; `PaperKit` is merged and `PaperFonts.Register` is called | Re-add `DesignTokens.xaml` |
| `MarkPlacementTests` | Marks resolve to the right span for a known text, without rendering | Off-by-one a scene span |
| `SchemaResetGenerationTests` | The reset key is a literal string, not `nameof()`, and carries no brand name | Rewrite it as `nameof(FolioSchemaReset)` |
| `PersonaCoverageTests` | Every section and every one of the seven readers has a persona, and no two share one | Drop a persona |
| `SeveritySpansFourTests` | Exactly four severities, `Great` among them, each mapped to a role hue | Remove `Great` |
| `IdentityLadderTests` | Folio's five tiers read back through `Oklch.FromHex` ≥ 20° from every chromatic role **and** every committed app identity | Move a tier toward caution |
| `VoiceRecordShapeTests` | `Register`/`Cadence`/`Punctuation` exist; examples carry a project id; no `Summary` | Re-add `Summary` |

---

## 5. Release breakdown

One PR per release. **CI green before merge** — no exceptions.

**Release A — the rename and the frozen set.** `FolioIdentity`, frozen key constants, explicit Java
names, `SchemaResetGeneration`, namespace/DB/context/solution rename, fresh start. Guards first.
**This release ships before any other**, because D60 drops the database and must do so while the
shelf holds nothing (Gate 1 ordering rule).

**Release B — the restart fix.** R-1. Small, independently verifiable, and it protects every
subsequent release's billing. Deliberately not folded into A.

**Release C — the kit and the screens.** Delete both head-local token files, merge `PaperKit`,
register the fonts, redraw Work desk · Shelf · Settings and the book page from the kit, binder tabs.
`KitAdoptionTests` in the same release — P9's lesson, made a guard.

**Release D — the shelf.** Spines from word count, bookends, book page, archive as a manual action,
covers via Gemini with the plain-cloth fallback, replace/repaint.

**Release E — the vector page and the report frame.** `VectorPdfSink`, points geometry,
`PaperTypeScale`, the cover carrying the book's art, the pacing chart redrawn. No content change —
this release is about the page, so a reflow regression is isolated.

**Release F — the reading model.** Persona passes, grouped and cached, structured output, the model
mix, the status sentence. The pipeline's largest change, on a page that already works.

**Release G — the marked-up manuscript.** Offsets, proofreading marks, severity hues, inline reasons,
scene blocks, images placed in the markup. The biggest new surface, last, on top of a proven page and
a proven reading model.

**Release H — the voice.** The refitted record, the discard-and-relearn, the per-project line
persisted on state change, voiced empty states and the error rule.

**Release I — reachability and delivery.** `FileProvider`, drop `ReportPage`, Drive + reMarkable
delivery of the new document, diagnostics.

---

## 6. Deliberately not built

- **Per-destination colour.** D35/D47: one Screen render for every destination. The P10 roadmap card
  lists this as carried; it is **dropped**, and the card is corrected at the phase boundary rather
  than left claiming it.
- **An rM2 profile / device setting.** D36 — Paper Pro only.
- **Database migration.** D60 — fresh start, no copy-forward.
- **A Settings voice preview.** D96 — the Work desk's per-project line is the preview.
- **Reference-sheet images.** D47 — prose only. A whole class of billed Gemini calls deleted.
- **Build-time image replacement as new work.** D58 — IconStudio's `Branding/` convention already
  provides it.
- **Spine-out usage sizing.** Folio has no usage signal; width is word count (D30).
- **An in-app report reader.** D85.

---

## 7. What is **not** verified

Stated plainly, once.

- **Nothing in this phase has run on a device.** No screen has been drawn, no report rendered, no
  book shelved. The head compiles; that is all the container can prove.
- **The Gemini cover call has never been made.** Prompt, refusal behaviour and the plain-cloth
  fallback are designed, not observed.
- **The 48dp icon is known bad and unresolved.** Five objects in a window 35% of the icon. Open.
- **No persona prompt has been written or run.** The claim that per-persona reads remove the
  repetition is a hypothesis; it is the phase's central bet and it has no evidence yet.
- **The caching saving is computed, not measured.** `cache_read_input_tokens` must be asserted on a
  real run before the ~$2.30 figure is believed. The standing rule is that a caching regression is
  silent — requests keep succeeding, the bill is just higher.
- **U-3: the 9-minute cycle budget almost certainly does not fit ~20 passes.** Verified:
  `PipelineWorker` constructs `new RunBudget(..., MaxIterations: 200, MaxDuration:
  TimeSpan.FromMinutes(9), MaxInCycleWait: 60s)` — 9 minutes to stay under WorkManager's ~10-minute
  cap. Twenty whole-manuscript reads on Opus 5 at high effort will exceed that; a single pass over
  120k tokens is plausibly 30–90 seconds, which puts the group at 10–30 minutes. **This is the
  likeliest thing to force a design change during Release F**, and it is unmeasured. The honest
  options are a foreground service (no 10-minute cap), splitting the group into two or three cached
  sub-groups within the 5-minute TTL, or the Batch API at 50% cost with asynchronous collection.
  None is in the plan; Release F opens with measuring one real pass.
- **The reMarkable path remains reconstructed-not-verified**, as CLAUDE.md has said since P4. A
  ~140-page upload has never been attempted.
- **`storyboarder.db` fresh-start behaviour on a real device** — the drop happens once, gated by a
  flag, and has only ever been exercised in a test.
- **The SQLitePCLRaw advisory is untriaged.**

---

## 8. What this phase must not break

- The app id, the shared keystore, and the `apps-latest` update channel.
- Any other app's vault section — Folio writes three shared ones.
- The suite's 298 passing tests.
- `MigrationSafetyTests`, which migrates a fresh DB and asserts no pending model changes.
