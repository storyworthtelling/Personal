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

**Baseline: 234 passing, 0 failing.** Run this before every commit. Never commit a red suite.

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

- **Phase 2:** first-class `Finding` rows + stable IDs + cross-draft history + regression detection
  + before/after **scene**-image pairs. (Phase 1 leaves findings as JSON and keeps the finding diff;
  `DraftDiffPage` is findings-only for now.)
- **Phase 3:** the expansion engine ("Scenes to add", show-vs-tell leverage, thin-spot flags).
- **Phase 4:** shared bidirectional **reMarkable** delivery + vault-restored pairing.
- **Phase 5:** series awareness.
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
