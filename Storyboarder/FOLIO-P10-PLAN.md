# Folio (P10) — Gate 4 implementation plan

Approved at Gate 4. Built on the Gate 2 finding (`FOLIO-P10-TECHNICAL-FINDING.md`, approved) and
the Gate 3 mockups.

---

## 0. The mockups are the specification

**Every screen and every printed page in this phase is drawn, approved, and published here:**

> ## https://claude.ai/code/artifact/f540076c-08d0-489b-8183-877f828214d2
> *Artifact: "Folio Design Book". Read it before writing any UI or renderer code.*

This URL is written into the plan on purpose. A long build compresses its own context, and the link
is the one thing that must survive every step — if it is not in working memory, it is here. Every UI
and document task below repeats it.

**Three rules govern the whole build:**

1. **Build the screens and pages as 1:1 replicas of the mockup.** The report pages are drawn in
   **points** at A4 595 × 842 with margins 52.8 / 48 / 62.4 and a 489 × 732 content box. Layout,
   order, wording, spacing and copy are the specification, not an impression to interpret. Where the
   mockup prints a sentence, ship that sentence.
2. **Surface anything you discover; assume nothing.** If a page cannot be built as drawn — the
   geometry does not fit, the data is not there, a caption's claim turns out to be false — **stop and
   put it to the owner** with options and a recommendation. Do not quietly substitute a different
   design, and do not build the nearest thing that compiles.
3. **The kit wins every disagreement.** Colour comes from `PaperColorTokens`' **Document (muted)**
   profile and type from `PaperTypeScale`. If a drawing and the kit disagree, the kit wins and the
   disagreement is a finding to surface, not a judgement call made in passing.

### Page and screen inventory (all in the artifact above)

| Group | Ids |
|---|---|
| App screens | Work desk (aerial) · Shelf · book page · action card · Setup |
| Report front matter | p. 1 the cover · p. 2 the frame |
| Whole-book section | p. 6 Pacing, with the tension chart |
| The manuscript | p. 61 marked-up manuscript · p. 63 scene block with evidence and the draft pair |
| The readers | p. 136 the contents of the seven · p. 139 Agent · p. 142 Harsh but fair critic — one page per reader, the same template |
| Closing | p. 146 revision plan · p. 147 verdict |
| Identity | cover · bookplate · icon · the ladder · the wheel · the owner's mark |

---

## 1. Addenda to the approved finding, from Gate 3

The finding was approved before the pages were drawn. Drawing them changed twelve things. These
continue the finding's numbering and are part of it.

### F36 — the owner's mark is **drawn**, not photographed *(D118)*

The approved mark is the one in *Mockup 01 rev 4 §02* — a canted oval pierced by an M whose
bottom-left and top-right legs push past it, as an SVG path
(`M34 116 L46 28 L60 84 L74 2 L84 90`, round caps, stroke 4 on a 120 box, oval `rx 47 ry 34`).
The repository still ships a photographic medallion. Every site that prints the old one:

| Site | What it does today |
|---|---|
| `/Branding/owner.png` | the source image — the black-oval render |
| `Tools/IconStudio/BrandingMedallion.cs` | circle-masks it and adds the hairline ring |
| `Tools/IconStudio/BrandAssets.cs` | fans it out to five targets: Folio Infrastructure plus `drawable-nodpi/owner_medallion.png` in Folio, **Foreword**, Post and Ledger |
| `Storyworthtelling.Paper/EmbeddedBranding.cs`, `OwnerMark.cs` | the Skia side every printed page reads |
| `Foreword.Infrastructure/Pdf/ForewordPage.cs:159` | `DrawMakersMark` on the brief's first page |
| `Storyboarder.Infrastructure/Imaging/CoverPageRenderer.cs` | today's report cover |
| `FocusPhone/Flyleaf.Android/CoverView.cs:177` | a hand-rolled ringed "M" in `PaperTheme.FlyleafMark` — close, but not the approved mark |

**Assumption, pending the owner's word:** the drawn mark becomes the shared default (generated, not
shipped as a photo) and Foreword's Release-I per-install *photo* override stays as an override on
top of it. If the owner wants the drawn mark to win outright, the photo path goes and
`OwnerMark`/`MedallionImage`/`MedallionCrop` lose their reason to exist — that is a larger deletion
and must be said out loud before it is done.

### F37 — the jacket sets the mark's colourway

Because the mark is drawn it carries a colourway, and a Folio report takes **its own jacket's two
most prominent tones** — ground and ink — so a new jacket recolours the mark with nobody choosing a
colour. This needs a pure `MarkColourway.From(...)` over the cover image's quantised histogram
returning (ground, ink, hairline), with a contrast floor between ground and ink so a low-contrast
jacket cannot produce an illegible mark. Guard G16.

### F38 — margin notes are **packed**, not placed

On the marked-up manuscript every note sits at its anchor line *unless* the note above it has not
finished, in which case it is pushed down until it clears and a leader takes it back to its line.
That is a layout pass, not a coordinate: pure `MarginColumn.Place(anchors, heights, contentHeight)`.
**Unresolved and to be surfaced before Release G:** what happens when the packed column runs past
the foot of the page — does the note move to the next page with its anchor line repeated, or does
the page break early so the note stays with its line? The mockup's page does not overflow, so it
does not answer this. Guard G17.

### F39 — the pacing chart carries the findings

The chart is not tension alone: a finding anchored to a chapter **range** recolours that run of the
line in its severity, a single-chapter finding is a dot, and the sag band takes the hue of the
finding that sits on it. So `PacingChart` takes the findings as well as `ChapterTension`, and the
form stays a **line** graph — as filled areas the four severity tones fail the adjacent-fill
contrast floor. Guard G18.

### F40 — "line rewrites" are diagnosis only

Gate 1's anatomy lists *line rewrites* among the marks, and **nudge never write** forbids
ghost-writing prose. Resolved the only way the brief allows: a mark names what the line is doing
wrong and stops. The guard is structural rather than editorial — the mark record carries no member
that could hold replacement prose. Guard G19.

### F41 — the scene block prints the pair only when the scene changed

An unchanged scene reuses its rendered image and costs nothing; a changed one re-renders and
**keeps its predecessor** to print as a before/after pair. The retention rule is therefore
load-bearing: the previous draft's image may not be swept up by any cleanup that runs before the
report is composed. Guard G20.

### F42 — the revision plan is ordered by **location**, not severity

The author makes one pass through the manuscript, not three, so items sort by manuscript offset and
the severity chip rides on the right where the tier is still readable down the page. The plan spills
overleaf when it is long, which means the tier headings repeat on the continuation page.

### F43 — strengths print before problems

A decision, not politeness: a judgement page that opens with faults gets read defensively, and this
one is asking for a specific piece of work. Fixed order in the renderer. Guard G21.

### F44 — every reader carries a condition

Each of the seven persona reads prints a **"Would need"** line. A verdict with no condition attached
is an opinion the author cannot act on, so the structured output gains a required `Condition` field
beside the verdict, and a read that comes back without one is a failed pass, not a shorter one.
Guard G22.

### F45 — the marked-up manuscript is ~140 pages

Confirmed on the drawn geometry: 45 lines at 12pt / 1.35 in the 489 × 732 content box. Gate 1's
figure of 203 was computed on the raster geometry this phase replaces. The artifact and this plan
both carry the corrected number; **the ~140-page upload has still never been attempted** (finding
§7).

### F46 — each reader gets a page, and keeps its pros and cons

Drawn first as seven three-line summaries on one page, which **lost what the shipped report already
does**: `Review` is `Perspective · Assessment · Strengths[] · Weaknesses[] · Recommendation`, and
`AnalysisDocumentRenderer.RenderReviews` prints all five today. The mockup now matches the record —
**one reader to a page**: the long review in character, that reader's own pros and cons in two
columns, and the recommendation printed as *"Would need"* where the author will read it. A contents
page in front carries the seven names, their verdict in a clause, and the page to turn to.

The two columns are the only place in the report where a tick and a cross are the right marks: these
are one reader's preferences, not the book's severity tiers, so they take the Positive and Problem
roles rather than the four-tier scale. A persona whose review arrives with an empty pros or cons list
is a **failed pass, not a shorter one** — the same rule as the missing condition. Guard G23.

### F47 — the display version graduates to 1.0.x

**The versionCode needs nothing.** `build-storyboarder.yml` already computes it through the shared
`.github/actions/version-code` composite at **band 1**, exactly as Post and Plate do, and
`SuiteVersionBands` records that band 1 clears Canonizer's last bare-`run_number` code of **169**.
The formula is wall-clock minutes since 2020-01-01 UTC, so it survives a workflow rename — which is
the failure this phase would otherwise have walked into, twice over, as `build-flyleaf` and then
`build-post` did.

**The display version does need it.** Canonizer ships `0.1.<run_number>` while Post and Plate
graduated to `1.0.<run_number>` on exactly this occasion — a rename plus the phase that makes the
app what it was meant to be. Folio ships **`1.0.<run_number>`**, which is the number the owner reads
in Settings and in the release notes, and it must never regress to `0.1` afterwards.

Three more strings carry the old name and are part of the same sweep, none of them load-bearing but
all of them visible: the workflow's own `name: Build Canonizer APK`, its uploaded artifact
`Canonizer-Android` — **the owner downloads that by name, so the rename goes in the PR body** — and
the `SuiteVersionBands` comment that still calls the app Canonizer. The app id under all of it stays
frozen. Guard G24.

---

## 2. Standing rules for every release

These are the owner's, restated so no step can quietly drop one.

- **Never push to `main`.** Feature branch → PR → owner's approval. Nothing merges until its CI run
  is green.
- **TDD strictly. Untested code is not done.**
- **Every guard is written FIRST and watched to fail.** Reintroduce the bug it guards, see it go
  red, then fix it — and **name in the commit message what was reintroduced**. Three of P7's guards
  passed with the bug present; that is what this rule exists for.
- **No colour hex or type-size literals outside the registered token files.**
- **No model identifier** in commit messages, PR titles or bodies, code comments, or anything else
  pushed to the repo.
- **Read binding and API member names out of the packages' own assembly metadata** before writing
  code against them. Do not guess from memory.
- **Say plainly what is not verified.** "Built and unit-tested against fakes" is not "works."
- **Never put a credential in an artifact, doc, log or commit.** The vault passphrase and every key
  it unlocks are read in-process only.
- **Nudge never write.** No release may add a surface that hands the author drafted prose.

---

## 3. Releases

Each release is its own PR off `main`. The order is the finding's, amended by Gate 3.

### Release A — the rename and the frozen set
`FolioIdentity`, frozen key constants, explicit Java names, `SchemaResetGeneration`, the
namespace / DB / context / solution rename, fresh start. **Ships before any other**, because D60
drops the database and must do so while the shelf holds nothing.

- App id `com.storyworthtelling.storyboarder` **frozen**, collapsed to one constant.
- `PipelineWorker`'s `[Register]` value and both unique work names pinned
  (`storyboarder-pipeline`, `storyboarder-pipeline-now`).
- `MainActivity` gains an explicit `Name` — today it has none and is generated from the namespace,
  so the rename would move it.
- All **twelve** preference keys pinned as literals (the finding's Correction 1).
- **Display version `1.0.<run_number>`** (F47), matching Post and Plate. The versionCode already
  comes from the shared band-1 action and does not change.
- CI: the workflow keeps building the head under its frozen id; its `name:`, its uploaded artifact
  `Canonizer-Android` and the `SuiteVersionBands` comment take the new name — **say the artifact's
  new name in the PR body**, because the owner downloads it by name.
- **All guards from §4 below that do not depend on later releases.**
→ *Mockups: the Identity section.*

### Release B — the restart fix
R-1: `VerifyAndHealOutputsAsync` selects `Completed` novels and calls `Regress`, writing the stage
backwards past `NextStage`. Small, independently verifiable, and it protects every later release's
billing. Deliberately **not** folded into A.

### Release C — the kit and the screens
Delete both head-local token files, merge `PaperKit`, register the fonts, redraw **Work desk ·
Shelf · book page · action card · Setup** from the kit, binder tabs, Work as home.
`KitAdoptionTests` lands in the same release — P9's lesson, made a guard.
→ *Mockups: the five app frames.*

### Release D — the shelf
Spines from word count, series on their own shelves with bookends, the book page, archive as a
manual action, covers via Gemini with the plain-cloth fallback, replace and repaint (a repaint asks
first when the owner has replaced a cover).
→ *Mockups: Shelf, book page, action card.*

### Release E — the vector page and the report frame
`VectorPdfSink`, points geometry, `PaperTypeScale`, the Document (muted) palette wired through —
the shipped resolver still returns the retired terracotta for `Active` — the cover carrying the
book's own jacket at the drawn size, and **the owner's mark** (F36, F37). No content change: this
release is about the page, so a reflow regression is isolated.
→ *Mockups: report pages 1–2.*

### Release F — the reading model
Persona passes, grouped and cached, structured output, the model mix, the status sentence. Adds the
required `Condition` field (F44). The pipeline's largest change, on a page that already works.

### Release G — the marked-up manuscript
Offsets, proofreading marks (dele · caret · query · check), severity hues, inline reasons, the
margin packing pass (F38), scene blocks with the evidence image and the draft pair (F41), the
pacing chart's finding runs (F39). The biggest new surface, last, on top of a proven page and a
proven reading model.
→ *Mockups: report pages 6, 61, 63.*

### Release H — the voice
The refitted record (`Register` / `Cadence` / `Punctuation`, examples carrying a project id, no
`Summary`), discard-and-relearn, the per-project line persisted on state change, voiced empty states
and the error rule.

### Release I — reachability and delivery
`FileProvider`, drop `ReportPage` **only once the open-the-report action exists** (R-2), Drive and
reMarkable delivery of the new document, diagnostics. Closes with the readers' contents page, the
seven reader pages, the revision plan and the verdict.
→ *Mockups: report pages 136, 139, 142, 146, 147.*

### The mark, suite-wide *(separate PR, after E)*
F36 touches `Storyworthtelling.Paper`, `Tools/IconStudio` and four heads. It rides its own PR so a
regression in Foreword's brief or Flyleaf's cover is attributable, and so Folio's own release is not
blocked by it.

---

## 4. The guard ledger

Each is written **first**, watched to fail against the reintroduced bug, and the commit message
names what was reintroduced. G1–G11 come from the finding; G12–G22 are added at Gate 3.

| # | Guard | The bug it reintroduces to prove itself |
|---|---|---|
| G1 | `FolioInvariantsTests` — app id, both work names, the worker's `Register` value, `MainActivity`'s declared `Name`, all twelve keys, as **literals** | Rename any property; the stored value silently vanishes |
| G2 | `CompletedBooksDoNotHealTests` | Put `Completed` back in the heal selection; a finished book re-renders and re-bills |
| G3 | `TerminalStageTests` | Add a transition out of `Completed` |
| G4 | `ReportIsReachableTests` | Delete the open action after dropping `ReportPage`; the report becomes unopenable |
| G5 | `KitAdoptionTests` | Re-add `DesignTokens.xaml`; a head-local palette returns |
| G6 | `MarkPlacementTests` | Off-by-one a scene span |
| G7 | `SchemaResetGenerationTests` | Rewrite the reset key as `nameof(...)` |
| G8 | `PersonaCoverageTests` | Drop a persona; two sections share one |
| G9 | `SeveritySpansFourTests` | Remove `Great`; the fourth tier disappears |
| G10 | `IdentityLadderTests` | Move a tier toward caution; the 20° separation breaks |
| G11 | `VoiceRecordShapeTests` | Re-add `Summary` |
| G12 | `DocumentProfileTests` — printed colour resolves through the **Document** profile | Route a page through `PaperPalette`; terracotta returns |
| G13 | `PointLadderTests` — every printed size comes from `PaperTypeScale` | Hardcode a point size |
| G14 | `VectorTextTests` — the PDF's text is extractable by PdfPig | Render a page as a bitmap and wrap it |
| G15 | `CoverArtSlotTests` — the jacket is keyline-framed and never bled to the edge | Full-bleed the cover art |
| G16 | `MarkColourwayTests` — the mark's tones come from the jacket, never a constant, and clear the contrast floor | Hardcode the ground; ship a mark that vanishes on its ground |
| G17 | `MarginPackingTests` — no two notes overlap, every leader lands on its anchor line | Place notes at their anchors; two long notes collide |
| G18 | `PacingChartTests` — finding runs recolour the line, and the chart emits no filled series | Draw the four severities as fills; the adjacent-fill floor fails |
| G19 | `NudgeNeverWriteTests` — the mark record has **no member** that can hold replacement prose | Add `SuggestedText`; the product changes shape |
| G20 | `ScenePairRetentionTests` — a changed scene keeps its predecessor's image until the report is composed | Sweep old images at render time; the pair prints one panel |
| G21 | `VerdictOrderTests` — strengths render before problems | Swap them |
| G22 | `ReaderConditionTests` — every persona read carries a non-empty condition | Accept a read with no "Would need" |
| G23 | `ReviewShapeTests` — every persona read prints assessment, strengths, weaknesses and recommendation, and none of the four is empty | Flatten a review to a summary line; the pros and cons the shipped report already prints disappear |
| G24 | `DisplayVersionTests` — the head's display version is `1.0.x` and the versionCode still resolves through the shared band-1 action | Put `0.1.` back, or inline a version instead of calling the action; the rename resets the counter and the update channel rejects every later build |

---

## 5. Definition of done, per release

1. Guards written first and **watched to fail**, with the reintroduction named in the commit.
2. Unit tests green:
   `dotnet test Storyboarder/Storyboarder.sln -p:StoryboarderTfms=net10.0`.
3. The Android head **compiles**:
   `dotnet build Storyboarder/Storyboarder.Android/Storyboarder.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`.
4. The screen or page matches its mockup 1:1, checked against the artifact — **not** from memory.
5. Anything that could not be built as drawn is **surfaced for approval**, not substituted.
6. CI green on the PR before it merges. Never push to `main`.
7. What is *not* verified is stated plainly in the PR body.

---

## 6. Closing deliverables

- The CLAUDE.md section for Folio, replacing the Canonizer one.
- The P10 card on the roadmap artifact
  (`https://claude.ai/code/artifact/2f59319c-2fcd-43cc-a188-98778d187fe9`), **corrected** where it
  claims per-destination colour, which this phase drops (finding §6).
- The six guide artifacts refreshed, including Canonizer's
  (`https://claude.ai/code/artifact/554507db-368c-4a7c-b03b-502e671dbfe0`) renamed to Folio, plus the
  Monorepo Atlas (`be340748-6245-4fb5-bbed-10bfb50d167f`) and Shared Foundations
  (`3cb524e2-d530-4932-ba5e-8b5d439c4d4c`).
- The on-device test plan in Google Drive, in `[ID]` / `RESULT:` / `NOTES:` format, written so its
  questions are answerable on the phone. It must cover the four things no fake can answer: the
  in-place upgrade over Canonizer, the **~140-page** report opening on the device and reaching the
  reMarkable, a real Gemini jacket, and the first full reading run's cost against the estimate.
