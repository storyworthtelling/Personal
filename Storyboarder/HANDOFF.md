# Folio — session handoff

**P10 Release A is complete: the rename, and the frozen set.** Canonizer is now **Folio** —
folders, namespaces, solution, context, database file, CI artifact and the on-device label.
Everything the phase does after this rides on the rename having already happened, which is why
it ships first.

What did **not** move, and must never move, is the frozen set. Every item on it is invisible:
nothing fails at build time if it moves, and nothing fails loudly at run time either. The app
simply stops updating, or stops running its pipeline, or comes up as though it had never been
configured. `Folio.Tests/Guard/FolioInvariantsTests.cs` fails instead — do not "fix" these.

| Frozen | Why | Silent failure if it moves |
| --- | --- | --- |
| App id `com.storyworthtelling.storyboarder` — one constant, `FolioIdentity.ApplicationId` | The release channel installs by package id | A **second app** installs beside the first; the owner's books stay in the copy that never updates again |
| `PipelineWorker`'s `[Register]` Java name | WorkManager instantiates by class name from a row persisted on the device | The class named in that row no longer exists, so **the pipeline stops** |
| Both unique work names (`storyboarder-pipeline`, `-now`) | `EnqueueUniquePeriodicWork` with `Keep` matches on the name | The old schedule is never replaced: **two pipelines or none** |
| `MainActivity`'s Java name — it had none, so it was generated from the namespace | Home-screen shortcuts point at the class name | Every existing shortcut points at a class that is gone |
| The **twelve** preference keys, as literals (they were `nameof(…)`) | A renamed property renames its key | Settings come up **empty**, the vault passphrase among them — the one secret the vault itself cannot restore |
| `"CanonizerSchemaReset"`, still read | It is what already-upgraded devices have stored | The generation-2 reset would run a second time on a phone that has had it |

The old brand survives in exactly those places, by design. Everywhere else it is gone.

**The database moved, so the old one is deleted rather than orphaned.** `storyboarder.db` becomes
`folio.db`, which means a schema reset cannot just call `EnsureDeleted()` — that runs against the
new context and never touches the old file. `LegacyDatabase.Remove` deletes it and both its WAL
siblings, driven by `SettingsStore.SchemaResetGeneration` (a number now, not a boolean, so P10's
reset can run on a phone that already had the pivot's). A backup written under the old entry name
still restores: `ProjectBackupService` reads either.


---

## 1. Start here: environment

The toolchain is present in this environment — `dotnet 10.0.x` is installed by the SessionStart
hook, and `dotnet ef` (10.0.x global tool) **works here** (unlike the old handoff's assumption).

Verify the toolchain, then the suite:

```
dotnet --version     # expect 10.0.x
dotnet test Storyboarder/Folio.sln -p:FolioTfms=net10.0
```

**Baseline: 315 passing, 0 failing** (P10 Release A landed) — plus `Storyworthtelling.Remarkable.Tests` (12) and
Foreword's suite. Run these before every commit. Never commit a red suite. `dotnet ef` is installed but
not on PATH in a fresh container — `export PATH="$PATH:$HOME/.dotnet/tools"` before `dotnet ef migrations
add …`. Foreword is now on .NET 10 like the rest of the repo, so its tests build and run here directly
(`-p:ForewordTfms=net10.0` to skip the Android head).

The Android head needs the MAUI/Android workload + an Android SDK, neither present in a fresh
container. Run `Storyboarder/scripts/setup-android-build.sh` once (installs the workload from nuget,
pulls android.jar for API 36 from a GitHub mirror since `dl.google.com` is blocked, stitches a
minimal SDK at `/opt/android-sdk-min`), then:

```
dotnet build Storyboarder/Folio.Android/Folio.Android.csproj \
  -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false
```

This gets `dotnet build` green through XAML + C# + resource linking; it does **not** run the app (no
emulator/display). The setup is ephemeral — re-run the script each session you need the head. The head **was**
compiled in the Release A session and is green; build it before pushing anything that touches it,
since CI is otherwise its only compiler.

## 2. The product thesis (this shapes every decision)

Folio is a **manuscript revision instrument for an audience of one, in which the image is a
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
  (P10 renamed it again, to **Folio**, and moved the namespaces with it — see the header.)
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

Phase 2 (§7 of the brief) makes Folio a revision partner across drafts:

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
logic + client parsing are unit-tested; the renderer is exercised end-to-end by the pipeline test. (That
parenthetical used to read "raster output can't be asserted at the pixel level" — P10 Release E made the
pages vector, so the report's text is now read back out of the PDF with PdfPig and asserted directly.) The Android head (`ReportPage`) is compile-checked in CI, not
built in-session.

## 3d. What Phase 4 delivered (shared reMarkable delivery)

Phase 4 (§8 of the brief) extracts Foreword's reMarkable code into a suite-shared library and delivers
Folio's report to the tablet:

- **`Storyworthtelling.Remarkable`** — a new shared library (repo root, net10.0, mirrors
  `Storyworthtelling.Voice`) owning the whole cloud path: `IRemarkableClient`/`RemarkableStorageClient`
  (upload root + folder overload, `EnsureFolder`, list/download/trash), `RemarkableAuthService` (device
  token → session JWT + health), `IRemarkableRegistration` (the device-code pairing), `RemarkableOptions`,
  `RemarkableDoc`, `RemarkableCredentialId`.
- **Foreword migrated onto it** — deleted its local `Remarkable/` copy, repointed namespaces, pairs via
  `IRemarkableRegistration`. Its uploads stay at the account root (the root `Upload` overload), so its
  behavior and tests are unchanged.
- **Folio delivery** — the `Archiving` stage delivers the report to the tablet **root** through
  `IRemarkableClient` (optional ctor dep), as a **second, independent, best-effort target** that runs before
  and independently of the Drive upload. `BookAnalysis.DeliveredToRemarkable` marks it settled (delivered or
  given up) so a broken token can't wedge a review. Off by default (`ArchiveOptions.DeliverToRemarkable`,
  needs a device-code pairing); the head has a Settings pairing section + toggle and vault publish/restore
  of the token (shared with Foreword).

Schema via the `Phase4RemarkableDelivery` migration. The reMarkable v2 path — especially the **folder wire
format** — is reconstructed-not-verified; the tests pin the shape we send, the first live run verifies the
cloud accepts it. The Android heads (Folio's `SetupPage`/DI, Foreword's) are compile-checked in CI,
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
  the report lands on the tablet to read + annotate by hand. See §3d.
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
