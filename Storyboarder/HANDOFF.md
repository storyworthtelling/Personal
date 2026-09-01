# Canonizer — session handoff

**Phase 1 of the Storyboarder → Canonizer pivot is complete.** The app is no longer a graphic-novel
factory; it is a **manuscript revision instrument** whose star product is a chapter-cited revision
report, with **one wordless image per scene as supporting evidence** (a fidelity check — does the
beat land visually as written?). This handoff replaces the old graphic-novel handoff; the phases it
described are superseded by the pivot.

The work landed on branch `claude/plan-kickoff-nczkfr` as an ordered set of commits: rebrand →
strip the graphic-novel machinery → Scene model + schema baseline → per-scene images → new report
anatomy → new source formats → Drive delivery → the Android head → docs. Read this top to bottom
before starting work.

**Internal identity is unchanged on purpose.** The namespaces (`Storyboarder.*`), solution,
`StoryboarderDbContext`, `storyboarder.db`, and app id `com.storyworthtelling.storyboarder` all
stay, so an installed build upgrades in place. Only the user-facing name (**Canonizer**), the icon
(a red pen nib), and a few domain concepts changed.

---

## 1. Start here: environment

The toolchain is present in this environment — `dotnet 10.0.x` is installed by the SessionStart
hook, and `dotnet ef` (10.0.x global tool) **works here** (unlike the old handoff's assumption).

Verify the toolchain, then the suite:

```
dotnet --version     # expect 10.0.x
dotnet test Storyboarder/Storyboarder.Tests/Storyboarder.Tests.csproj -p:StoryboarderTfms=net10.0
```

**Baseline: 287 passing, 0 failing** (Phase 5 landed) — plus `Storyworthtelling.Remarkable.Tests` (12) and
DailyLoader's suite. Run these before every commit. Never commit a red suite. `dotnet ef` is installed but
not on PATH in a fresh container — `export PATH="$PATH:$HOME/.dotnet/tools"` before `dotnet ef migrations
add …`. DailyLoader is now on .NET 10 like the rest of the repo, so its tests build and run here directly
(`-p:DailyLoaderTfms=net10.0` to skip the Android head).

The Android head needs the MAUI/Android workload + an Android SDK, neither present in a fresh
container. Run `Storyboarder/scripts/setup-android-build.sh` once (installs the workload from nuget,
pulls android.jar for API 36 from a GitHub mirror since `dl.google.com` is blocked, stitches a
minimal SDK at `/opt/android-sdk-min`), then:

```
dotnet build Storyboarder/Storyboarder.Android/Storyboarder.Android.csproj \
  -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false
```

This gets `dotnet build` green through XAML + C# + resource linking; it does **not** run the app (no
emulator/display). The setup is ephemeral — re-run the script each session you need the head. The
head could not be compiled in the pivot session (no network for the script), so its changes are
careful manual edits, not compile-verified — **build it first thing next session.**

## 2. The product thesis (this shapes every decision)

Canonizer is a **manuscript revision instrument for an audience of one, in which the image is a
comprehension probe.** Four principles override everything:

- **Nudge, never write.** Diagnose and point; never ghost-write the author's prose or lines.
- **Both ends of the pendulum.** Flag under-writing *and* over-writing.
- **The report is the star.** The per-scene image is evidence, not the product.
- **Quiet by default.** Silent Drive delivery, one in-app note, no push/toast.

When a feature seems obviously useful but contradicts these, it was almost certainly already
declined.

## 3. What Phase 1 delivered

- **Rebrand** — `Glyphs.PenNib` (ink nib + terracotta tip), regenerated mipmaps, display name +
  copy + CI workflow renamed to Canonizer. Internal `Storyboarder.*` identity kept.
- **Stripped the graphic-novel machinery** — entities `Volume`/`Page`/`Panel`, enums
  `ShotType`/`DialogueKind`, the batch renderer, `SkiaPageComposer`, `VolumeDecomposer`,
  `PageLayoutCalculator`, fact-checks, the in-app page reader, and the storyboard/cover/panel Claude
  methods are all gone. `GraphicNovelProcessor` became `ReviewProcessor`.
- **Scene model + schema baseline** — a first-class `Scene` entity (`IRetryable`, re-homing the
  source-offset anchor `Panel` used to carry) + a pure `SceneDetector` (authorial breaks + a length
  cap, ~50–150 scenes/novel). The migration history was **collapsed to one `InitialCreate`
  baseline** (`dotnet ef` works here); the head drops the legacy `storyboarder.db` once before the
  first migrate.
- **Per-scene images** — a `RenderingSceneImages` stage renders one **wordless portrait** image per
  scene via `DescribeSceneImage` (Claude) → `GenerateSceneImage` (Gemini) with the reference sheets
  supplied for consistency. Refusals retry a different moment up to 5×, then mark the image
  unavailable — **never blocking the report**.
- **New report anatomy** — genre, word count vs genre norm, a tension-over-chapters **pacing
  chart**, per-scene **Fidelity gap / Craft / Purpose** blocks with the evidence image, a five-lens
  review set (adding *Agent & acquiring editor*), a **Must / Should / Consider** revision plan, and
  a closing **Verdict**. Scoring is qualitative only. New renderer primitives: `SkiaImageFit`,
  `PageWriter.Image`, `PageWriter.PacingChart`.
- **New source formats** — RTF + ePub as pure-BCL `Domain/Text` extractors; PDF via **PdfPig**
  (`Infrastructure/External`). Routed by Drive mime type / extension in `GoogleDocsClient`.
- **Drive delivery** — the report lands beside the manuscript as `<Title> — Draft N — Analysis.pdf`
  by default (`ArchiveOptions.Enabled = true`), with one quiet in-app note.

## 3b. What Phase 2 delivered (the revision loop)

Phase 2 (§7 of the brief) makes Canonizer a revision partner across drafts:

- **First-class `Finding` rows** with a stable `ThreadId` (the same issue over time), a first-class
  `Category`, and a per-occurrence `Status` (New/Carried/Regressed — **Fixed is derived** from a thread's
  absence in a draft). Persisted by a resumable `ComposeAnalysis` sub-step gated by
  `BookAnalysis.FindingsReconciled`.
- **Finding reconciliation** — a new `ReconcileFindings` Claude call matches a draft's findings to prior
  threads even after the wording moves; the pure `FindingReconciler` (`Domain/Analysis`) applies the
  decisions deterministically. A first draft opens every thread fresh (no call).
- **Scene image reuse + before/after pairs** — the pure `SceneMatcher` (`Domain/Text`) pairs scenes across
  drafts by normalized text; `RenderSceneImages` reuses an unchanged scene's image and re-renders only
  changed/new scenes. `Scene.PreviousSceneId`/`Changed` carry the linkage.
- **Running story-state** threaded chapter-to-chapter through the map call (`AnalyzeChapter`'s
  `priorRunningState` + `ChapterNotes.RunningState` → `Novel.ContinuitySummary`).
- **Diff surface** — `GetDraftDiffAsync` reports resolved/new/regressed by `ThreadId` + before/after scene
  pairs; the report PDF adds a "Since draft N−1" summary + inline `[new]`/`[regressed]` tags (a `DraftReview`
  the composer takes); `DraftDiffPage` renders the scene pairs.

Schema changes landed via two follow-on migrations (`Phase2FindingsAndSceneHistory`,
`Phase2FindingsReconciledFlag`). Pure logic is unit-tested; the Android head (DraftDiffPage) is
compile-checked in CI, not built in the Phase-2 session (build it first thing, same as Phase 1).

## 3c. What Phase 3 delivered (the expansion engine)

Phase 3 (§6 of the brief) makes the report **additive** — the owner's drafts run short, so it surfaces
what to *add*, never drafted prose (nudge-never-write):

- **`ExpansionReport`** (`Domain/Analysis/BookAnalysisModels.cs`) folded into the report via
  `WithExpansion`: ranked `SceneAddition`s (`AdditionKind`, Must/Should/Consider), two-sided
  `ShowTellNote`s (`ShowTellDirection` Dramatize/Tighten), and a `LengthAssessment`.
- **`ProposeExpansion`** Claude call (strong tier) from the per-chunk notes + running state + measured word
  count + genre; the map call now also gathers `SummarizedMoments`/`ThinSpots`/`OffPageEvents`. A resumable
  `ComposeAnalysis` step gated by `BookAnalysis.ExpansionJson`. Graceful-empty (still emits the measured
  length line on a blank reply).
- **Pure `LengthGap.Describe`** frames the word count against the genre norm (short / within / over).
- **Report surface:** the frame opens with the length line; new "Show vs tell" and "Scenes to add"
  sections (the latter grouped by tier, ahead of the revision plan); mirrored in `ReportPage`.

Schema landed via the `Phase3Expansion` migration. Additions are per-draft (regenerated each draft). Pure
logic + client parsing are unit-tested; the renderer is exercised end-to-end by the pipeline test (raster
output can't be asserted at the pixel level). The Android head (`ReportPage`) is compile-checked in CI, not
built in-session.

## 3d. What Phase 4 delivered (shared reMarkable delivery)

Phase 4 (§8 of the brief) extracts DailyLoader's reMarkable code into a suite-shared library and delivers
Canonizer's report to the tablet:

- **`Storyworthtelling.Remarkable`** — a new shared library (repo root, net10.0, mirrors
  `Storyworthtelling.Voice`) owning the whole cloud path: `IRemarkableClient`/`RemarkableStorageClient`
  (upload root + folder overload, `EnsureFolder`, list/download/trash), `RemarkableAuthService` (device
  token → session JWT + health), `IRemarkableRegistration` (the device-code pairing), `RemarkableOptions`,
  `RemarkableDoc`, `RemarkableCredentialId`.
- **DailyLoader migrated onto it** — deleted its local `Remarkable/` copy, repointed namespaces, pairs via
  `IRemarkableRegistration`. Its uploads stay at the account root (the root `Upload` overload), so its
  behavior and tests are unchanged.
- **Canonizer delivery** — the `Archiving` stage delivers the report to a **"Canonizer" folder** through
  `IRemarkableClient` (optional ctor dep), as a **second, independent, best-effort target** that runs before
  and independently of the Drive upload. `BookAnalysis.DeliveredToRemarkable` marks it settled (delivered or
  given up) so a broken token can't wedge a review. Off by default (`ArchiveOptions.DeliverToRemarkable`,
  needs a device-code pairing); the head has a Settings pairing section + toggle and vault publish/restore
  of the token (shared with DailyLoader).

Schema via the `Phase4RemarkableDelivery` migration. The reMarkable v2 path — especially the **folder wire
format** — is reconstructed-not-verified; the tests pin the shape we send, the first live run verifies the
cloud accepts it. The Android heads (Canonizer's `SetupPage`/DI, DailyLoader's) are compile-checked in CI,
not built in-session.

## 4. Traps that still apply

**`ProjectStage`/`WorkStatus` persist as integers.** Append new values to the end of the enum, never
insert in pipeline order — inserting renumbers `Completed`/`Failed`/`Cancelled` and silently
re-stages every existing manuscript. Pipeline order lives in `ProjectStateMachine`, not enum order.

**A new non-nullable `bool` column lands as `false` on existing rows.** If `false` changes
behaviour, backfill it in the migration.

**`MigrationSafetyTests` must stay green.** It migrates a fresh DB and asserts `HasPendingModel
changes()` is false — the honest, runtime-checked guard against schema drift. `dotnet ef` works here
now, so scaffold migrations rather than hand-writing snapshots.

**A stage must not be able to strand a finished manuscript.** Every stage lets a finished review
complete; the scene-image and archive stages are built so failure still finishes the report.

**Check the feature is reachable.** A stage or service method with no path from the UI is not a
feature.

## 5. Deferred to later phases

- **Phase 2 ✅ (done):** first-class `Finding` rows + stable IDs + cross-draft history + regression
  detection + before/after **scene**-image pairs + running story-state. See §3b.
- **Phase 3 ✅ (done):** the expansion engine ("Scenes to add", show-vs-tell leverage, thin-spot flags,
  genre-norm length targets) — the additive feedback that closes the word-count gap. See §3c.
- **Phase 4 ✅ (done):** shared bidirectional **reMarkable** delivery library + vault-restored pairing —
  the report lands in a "Canonizer" folder to read + annotate by hand. See §3d.
- **Phase 5 ✅ (done):** series awareness — link related books (`SeriesId`/`PreviousBookId`/`SeriesSequence`
  on `Novel`, `CreateNextBookAsync`) and reuse the prior book's canon: returning-entity reference-image reuse
  (`CarryForwardSeriesEntities`), a resumable **Series continuity** read (`AssessSeriesContinuity` sub-step 2f
  over a `SeriesCanon`, folded via `WithSeriesContinuity`, rendered silent-by-default), and the prior book's
  open threads carried forward as tracked series-level threads. Android: "Start next book" card action +
  "Book N" badge. **The 5-phase roadmap is complete.**
- **Also deferred:** the image-vs-prose **vision** fidelity pass (Phase 1's fidelity gap is a
  prose-specification read, not a comparison of the generated image); owner-voice profile seeding
  (suite-wide pending).

## 6. Not yet live-verified

The Android head is not compiled in the pivot session (build it next session). The Gemini image
calls, the Drive writes, and the on-device look are reconstructed and **not yet verified on a
device** — the first real run is the verification. `ComparableTitles`/`MarketPositioning` remain on
the report record but are no longer rendered (market/readiness moved into the reviews); they can be
dropped from the schema in a later cleanup.

## 7. Waiting on the owner

- **Register the full `drive` scope** on the OAuth client in the Google Cloud project, then sign in
  again on the device. It covers both the report delivery beside the manuscript and the shared
  owner's-voice profile file; until it's registered, delivery degrades gracefully (the report stays
  on the device) and the voice greeting falls back to neutral copy.
