# Constitution (P13) — implementation plan

**Status:** approved (D92–D95), section by section with the owner.

This plan builds what three approved records already decided, and reopens none of them:

- the **UX design brief**, Claude Docs `https://claude.ai/artifact/74ihasgrUPeiMy6imZsyao` (cited **brief §…**);
- the **design book**, 19 approved pages, `https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb` (cited **p.N**),
  with its decisions **D1–D95** in [`CONSTITUTION-P13-DESIGN-NOTES.md`](CONSTITUTION-P13-DESIGN-NOTES.md);
- the **technical finding**, [`CONSTITUTION-P13-TECHNICAL-FINDING.md`](CONSTITUTION-P13-TECHNICAL-FINDING.md)
  (cited **TF §…**, gaps **G1–G12**, risks **R1–R12**), with the owner's answers recorded as D80–D87.

Where the plan had to choose something those records left open, it says so under **Plan choices
(PC)**, and each choice is the owner's to overturn. **Everything in this plan is invented**: the
household (Sam, Lee, Robin, Juniper), dishes, figures and readings come from the design book, and no
real person, reading, account or key appears here.

---

## 0. How to read this plan

### 0.1 The mockups are the specification

Every UI and document task is built **1:1** from its approved page. Anything that cannot be built as
drawn is **reported back, not substituted**. Each PBI links its page as
`book p.N` → `https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-N`.

| p. | Page | Approved | Epic | Release |
|---|---|---|---|---|
| 1 | Cover studies (POS.6) | D9 | E1 | A |
| 2 | Binding cloth and identity hue | D10 | E1 | A |
| 3 | Shelf cover and launcher icon | D12 | E1 | A |
| 4 | Ink targets | D16 | E2 | C |
| 5 | Workout: cover and exercise pages | D19, D23–D25 | E2 | C |
| 6 | Workout: assessment week, injury day | D26 | E2 | C |
| 7 | Weekly meal sheet | D33 | E2 | D |
| 8 | Recipe: ingredients and timed steps | D34 | E2 | D |
| 9 | Recipe: kids' meal and rating page | D36 | E2 | D |
| 10 | Saturday digest | D38 | E2 | E |
| 11 | Today, and the rest day | D49 | E3, E4 | A, C |
| 12 | Workouts, Skip / Swap and undo | D50 | E5 | C |
| 13 | Meals and the recipe box | D61 | E6 | D |
| 14 | Adding a recipe, the empty box | D62 | E6 | D |
| 15 | "I ate something else" | D67 | E6 | D |
| 16 | Body and a signal's page | D69 | E7 | B |
| 17 | Settings | D73 | E8 | A |
| 18 | Dog-ear, Waiting page, voice fallbacks | D76 | E9 | A–E |
| 19 | Flyleaf's second suite shelf | D78 | E10 | A, E |

### 0.2 Identifiers, sizes and flags

- **PBI ids** are `C-<epic>.<feature>.<n>`, e.g. `C-5.2.1` is Epic 5, Feature 2, PBI 1. Features are
  `F-<epic>.<n>`, epics `E<n>`.
- **Sizes:** XS (under an hour, fully specified) · S (a day) · M (two or three days) · L (a week) ·
  XL (split it before starting). An XL in a later round means the PBI is not yet small enough.
- **Verify** flags say who has to see it working: **Tests** (green suites are enough) · **Owner**
  (the owner reviews it in the running app or on paper) · **Device** (only the real phone or tablet
  can prove it, so it lands on the §11 list).
- **Agent level and effort** say who should build a PBI and how hard they should think, on the
  levels Index's and Pulp's plans used:

  | Level | For | Effort |
  |---|---|---|
  | **L1 · mechanical** | Fully specified here, plus a repo precedent (a constant, a token, an IconStudio spec, a workflow step) | **low** |
  | **L2 · standard** | One project and an existing pattern; a screen or page built 1:1 from its mockup | **medium** |
  | **L3 · judgement** | New algorithms, cross-app contracts, and anything whose failure is **silent** (frozen names, merge, encryption, ink reading, calendar placement, the privacy rules) | **high**, or **xhigh** where a wrong answer is both silent and costly (the read-back worker, the household merge, calendar placement) |

  The orchestrating session owns the gates, the owner's questions, every push and the PR. An L3
  agent reviews every L1 and L2 diff in its release before it is pushed, and the orchestrator runs
  the suites and compiles the head itself; an agent's report is not verification.
- **Strict TDD:** every PBI names the tests written first. A guard is watched failing, and its red
  run is recorded in the commit message, before the code that turns it green.

---

## 1. Overview

### 1.1 Goal

Constitution exists so the owner can **see their health trend honestly** (brief §Who it's for).
Workouts and meals feed that trend. Success in six months is three things together: **most planned
workouts done, a visible trend moved, and barely having to think about it.** The tablets are the
working surface, ink is the input, and the phone glances and records in under 30 seconds
(brief §Design rules).

### 1.2 Scope (P13)

- **One codebase, two installs** (brief §Two installs): the owner's on the locked FocusPhone, the
  partner's on her unrestricted phone. Meals is shared; Workouts and Body never cross between
  installs.
- **Three pillars**, each switchable per install (brief §Who it's for):
  - **Body:** passive reads from Health Connect plus the Sleep Number bed, from install day
    (no backfill), charted against the owner's own past only (p.16).
  - **Workouts:** a Claude-planned longevity mix placed into calendar gaps, delivered as a workout
    document to each adult's tablet, read back by ink (p.5–6, p.12).
  - **Meals:** a household planner over the family's scanned recipe box. It plans every meal of the
    week, prints the Thursday meal sheet and tonight's recipe, and learns from ink and ratings
    (p.7–9, p.13–15).
- **Four reMarkable documents:** workout, weekly meal sheet, recipe, Saturday digest (p.4–10).
- **Five binder tabs:** Today, Workouts, Meals, Body, Settings (p.11–17).
- **Notices:** the dog-ear and the Waiting page (p.18), the Flyleaf bookmark on the owner's phone
  (p.19) and ordinary notifications on the partner's (D83).
- **Flyleaf:** Constitution's cover-out book and a second suite row on its own ledge (p.19).
- **Cross-app contracts:** Index sends the grocery budget (brief §Meal planning); Plate scans
  recipes on the owner's phone (D86); Score's tempo bands are printed (D25).

### 1.3 Non-goals (brief §Out of scope, plus the rulings since)

- A watch interface (P14). Watch data still arrives passively through Health Connect.
- Grocery ordering or delivery (no Instacart), and a Keep list that is read back (D81).
- Children's fitness or body tracking; children's nutrition is balanced but never counted.
- The unified household library across apps (roadmap, not P13).
- On-demand workouts, a workout history list, swapping tonight's dinner (brief §Phone screens).
- Writing to Health Connect, and Health Connect history backfill.
- Any faith content. The only Sabbath rule is that Sunday holds gentle walks.
- Streaks, goals, scores, population norms, and good/bad colour on a trend.
- **NYT Cooking scraping, in any form** (D82). NYT arrives only through an official API or written
  permission, after the E11 spike.
- Push notifications on the owner's phone (brief; D83 narrows "never pushed" to the owner's install).

### 1.4 Releases (D88) and what each gives the owner

The brief says **Body first, then the other pillars**. All releases land as labelled commits on one
branch, with one phase PR (D89).

| Release | Delivers to the owner | Epics |
|---|---|---|
| **A — The book on the shelf** | Constitution installs over the update channel at band 10. Its icon and cover stand on Flyleaf's second suite row. The five-tab scrapbook shell opens (Today shows its neutral line and nothing planned yet), Settings carries pillars, equipment, household and the vault passphrase, and every connection restores from the vault. The dog-ear and Waiting page work. The NYT and Health Connect spikes report back. | E1, E3, E8, E9 (frame), E10 (shelf), E11 (spikes), E12 |
| **B — Body** | The Body tab: a doctor's-chart scrap per signal, Month/Quarter/Year, worsening first, the signal page, per-signal Health Connect permission, the Sleep Number bed with its "Waiting for the bed" state, the monthly and per-signal voice seats, and the encrypted Body backup. **The trend the app exists for is visible from here on.** | E7, E9 (Body seats), E11 (Health Connect manifest) |
| **C — Workouts** | Assessment week, then the weekly plan placed into calendar gaps (household events first), with the forecast and recovery swaps. The workout document reaches the tablet each morning, its ink is read back the next morning, and the Workouts tab offers Skip / Swap with undo. Today shows the workout clipping and its voiced line. | E2 (workout + ink), E4, E5, E9 (Today + encouragement seats) |
| **D — Meals** | The shared household file, the recipe box (Plate scan on the owner's phone, camera on the partner's, the empty box counting to 10), the Thursday meal sheet on both tablets, tonight's recipe, ratings and "We didn't cook this tonight", the Index budget flag, the weekly Keep note, cook and shopping blocks on the calendars, and "I ate something else". | E2 (sheet + recipe), E4 (dinner card), E6, E9 (sheet intro), E11 (Index, Plate) |
| **E — The digest and the notices** | The Saturday digest, the full Waiting page set, the Flyleaf bookmark on the owner's phone and ordinary notifications on the partner's, and the Saturday notes read into next week's plan. | E2 (digest), E9, E10 (bookmark) |
| **F — On the devices** | The device checks in §11, run with the owner on both phones and both tablets. Each one that fails reopens its PBI. | E13 |

Each release is usable on its own: a release that stops early still leaves the owner with something
that works.

---

## 2. Architecture sketch

### 2.1 Projects (D80; TF §1.1)

`Constitution/` at the repo root, on Pulp's template, in one `Constitution.sln`:

| Project | TFM | Holds | References |
|---|---|---|---|
| `Constitution.Domain` | `net10.0`, pure | The planners (workouts, meals, calendar placement), the ink resolvers (D14–D18), signal ordering (D70), the voice seats' facts, `ConstitutionIdentity`, the household record kinds | `Codex.Domain` (for `TempoBands`, `DesignTokens`) |
| `Constitution.Infrastructure` | `net10.0` | Health Connect adapter seam, SleepIQ client, Claude planning/vision clients, the household and Body files through `IDriveTextFile`, document composers over `Storyworthtelling.Paper`, reMarkable delivery and read-back, calendar writes. Every external service is faked in tests | Domain, `Storyworthtelling.Voice`, `.Paper`, `.Remarkable`, `.Handwriting`, `.Google`, `.Household` (PC4) |
| `Constitution.Android` | `net10.0-android` native head | The five tabs over `Codex.Ui.Android` (`BinderFrame`, `DogEar`, `WaitingActivity`, `ActionCard`, `PaperUi.Segmented`, `Checkbox`, `AccordionPicker`), the scrapbook painters, the Health Connect platform calls (`Android.Health.Connect`, no binding package, TF §1.3), the workers, the receivers | Infrastructure, `Codex.Ui.Android`, `Storyworthtelling.Updates` |
| `Constitution.Tests` | `net10.0` | Domain + Infrastructure + guards; no device, no keys | Domain, Infrastructure |

Tests: `dotnet test Constitution/Constitution.Tests/Constitution.Tests.csproj`. The head compiles here:
`dotnet build Constitution/Constitution.Android/Constitution.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`,
after `FocusPhone/scripts/setup-android-build.sh`. **Compile the head locally every round; CI is not
the first compiler** (CLAUDE.md).

### 2.2 Frozen identity (TF §4)

| Identifier | Value | Guard |
|---|---|---|
| App id | `com.storyworthtelling.constitution`, one constant `ConstitutionIdentity.ApplicationId` | `ConstitutionInvariantsTests` |
| Flyleaf's key | `PolicyContext.ConstitutionPackage`, equal to the app id | `FrozenInvariantsTests` (both sides) |
| versionCode | **band 10**, `new("com.storyworthtelling.constitution", 10, 0)` in `SuiteVersionBands` | `Storyworthtelling.Updates.Tests` |
| Display version | `1.0.<run_number>`, injected by the workflow | `DisplayVersionTests` |
| `MainActivity` and every worker | Registered Java names plus unique work names | `ConstitutionInvariantsTests` |
| Preference file names and keys | String literals, never `nameof` | `ConstitutionInvariantsTests` |
| Household record kinds | One constant set (`ConstitutionKinds`) | `ConstitutionKindsTests` |

### 2.3 CI (TF §1.13)

`.github/workflows/build-constitution.yml`, copied from `build-pulp.yml`:

- **Test job** on `main` and `claude/**` pushes, nothing else (the CI-minutes rule).
- **Signed build** on `main` and on **Run workflow** only, with JDK 21, the shared keystore secrets,
  versionCode from the `version-code` action at **band 10**, and the rolling `apps-latest` release
  step.
- **Fatal APP ID, ICON and FONT checks.** The FONT check names all **eight** fonts, not the six the
  older workflows check.
- `concurrency` cancels a branch's in-flight run; `main` is never cancelled.
- Artifact **Constitution-Android**.

### 2.4 Secrets and Drive (TF §1.11, D75, D85)

| Where | What | Notes |
|---|---|---|
| Shared vault, new section `SecretsVault.Constitution` with `[JsonPropertyName("Constitution")]` | The household **Sleep Number login** | The wire-name test is written as the file on Drive, not as a round trip (Foreword's lesson) |
| Shared vault, existing sections | `Remarkable.DeviceToken` (D85: the same pairing Foreword uses), `Claude.ApiKey`, `Gemini.ApiKey` (illustrations, D20), `Nyt` (only if the spike succeeds, D82) | Reused, never copied |
| **Personal vault** (`PersonalVault`, per account) | **Health Connect choices** (PC3), and `WorkScheduleJson` as read from Foreword | Per-install facts never go in the shared vault (Foreword Release I) |
| Drive, `storyworthtelling-constitution-household.json` | Recipe box, meal plan, taste profile, pantry, roster, ratings, the planning lease | Encrypted with `VaultEnvelope`, merge-safe (PC4); **never written over a file that cannot be decrypted** (Pulp G14) |
| Drive, `storyworthtelling-constitution-body.json`, one per account | Each adult's Body history, kept forever | Encrypted, private to that account, same never-overwrite rule |
| App storage plus a Drive backup | Exercise illustrations | Runtime art, generated from exercise descriptions; **never committed** |

The vault passphrase is entered by the owner on each phone and **never stored, logged or written
anywhere by this plan**.

### 2.5 Cross-app contracts

Each contract is signature-permission protected, aims at an explicit component, and is pinned equal
on both sides by content-read guards (Pulp G16–G19).

| Contract | Direction | Shape | Source |
|---|---|---|---|
| Grocery budget | Index → Constitution | `com.storyworthtelling.constitution.BudgetReceiver`, permission `…constitution.RECEIVE_BUDGET`; payload month, budget, spent so far, as exact decimal text | brief §Meal planning; TF §1.12 |
| Recipe scan | Constitution → Plate (owner's phone only) | A new `READ_RECIPE` read action through Plate's `ScanTray`, 1–N page images returned to the caller transiently and dropped after the read | D86; TF §1.8, G7 |
| Meal photo | Constitution → camera | `IMAGE_CAPTURE` aimed at Plate's package on the owner's phone; the system camera on the partner's | D65, D86 |
| Bookmark / notice | Constitution → Flyleaf (owner) / the shade (partner) | A posted notification carrying a role extra (PC1) | D83, p.19 |
| Score tempo | none (printed only) | `TempoBands.Of(bpm)`'s name beside the number, set by hand on Score | D25; TF §1.12 |
| Flyleaf shelf | Flyleaf reads the package | `PolicyContext.ConstitutionPackage`, `CoverResources` entry `cover_constitution`, cloth binding | p.19, D79; TF §1.9 |
| Calendar | Constitution → Google Calendar | Primary calendars, full `calendar` scope, a hidden extended-property key that never includes the date (Pulp's pattern) | brief §Calendar; TF §1.6 |
| Groceries | Constitution → Keep | Keep's share intent, once a week, after one tap (PC10) | D81 |

### 2.6 reMarkable

Documents are uploaded to the account **root** through `Storyworthtelling.Remarkable` (the only path
Foreword has exercised), using the pairing that phone's Foreword uses (D85). Each document is
trashed once its ink is read; the digest goes after **three days**, since the cloud's "opened" signal
is unverified and is planned as absent (TF §1.4, R4). Ink is read by **Constitution's own morning
worker**, after Foreword's delivery window (D87): a vision read of the flattened PDF through
`Storyworthtelling.Handwriting`, box by box from one shared set of rectangles, with Index's
**0.75 confidence** threshold. Below it, a read goes to the Waiting page as a Caution chip pair
(p.18). The 445 × 594 pt page (D13) is exact on the rM2 and about 1.14× larger on the Paper Pro;
both are 3:4, so nothing is cropped (TF §1.4).

### 2.7 Plan choices (the owner's to overturn)

| # | Choice | Why |
|---|---|---|
| PC1 | **One notification path on both phones** (D83). On the locked phone the lockdown suppresses the alert, so the notice shows only as the Flyleaf bookmark. Flyleaf's `NotificationHue` learns to read a role extra, so the ribbon can be rose, teal or slate. | One code path, no new broadcast, and the colours page 19 needs |
| PC2 | **Bookmark colours, settled by D91:** a waiting scan and the box under ten are Reference slate, as page 19 draws them; the three errors are Problem rose. The waiting scan's dog-ear stays In-progress teal (D77). | Page 19 and D77 differed; the owner chose page 19 for the bookmark |
| PC3 | **Health Connect choices live in each person's personal vault**, not the shared vault D75's list implies | Otherwise the owner's choices would be restored onto her phone (TF §1.11) |
| PC4 | **Pulp's household-file machinery** (`HybridLogicalClock`, tombstoned records, `VaultEnvelope`, never-overwrite) **is extracted into a shared `Storyworthtelling.Household` library**, and Pulp re-points under its frozen guards, rather than copied | One merge engine, not two drifting copies (TF §1.11) |
| PC5 | **A registered "Dose" rung** in `PaperTypeScale` for the 54 pt dose (p.5), recorded in DESIGN.md §19 | The no-literal guard would otherwise fail (TF §1.5, G6) |
| PC6 | **Index's card rule tokens and 22 dp pitch move into the shared `MaterialTokens.cs`**, and Index re-points | "The same object in both apps" (p.11) then holds by construction (TF §1.2) |
| PC7 | **A planning lease record** in the household file (who plans, with a heartbeat), so the partner's install takes over only while the owner's is silent | Two planners must never plan the same week (R8) |
| PC8 | **Page 14's "Paste an NYT Cooking link" row is not built** (spike C-11.4.1, D96: no official recipe API; permission needed). The picker shows the scan row alone, and the change goes back to the owner as a book revision; revisit only on written permission | D82: raise, don't substitute |
| PC9 | **Weather and air quality extend `Storyworthtelling.Google`** with the hourly forecast and Google's Air Quality API, on the same key | The bad-weather swap needs the slot's forecast, not current conditions (TF §1.6, G10) |
| PC10 | **The weekly Keep hand-off is a Reference notice on the Waiting page**: "This week's groceries are ready", with one button, Send to Keep, which opens Keep's share sheet with the list. It appears once the sheet is approved. | Confirmed by D90 |

---

## 3. Epics E1–E4

Links used below: **book p.N** = `https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-N`;
**brief** = `https://claude.ai/artifact/74ihasgrUPeiMy6imZsyao` (cited by section heading);
**notes** = [`CONSTITUTION-P13-DESIGN-NOTES.md`](CONSTITUTION-P13-DESIGN-NOTES.md);
**TF** = [`CONSTITUTION-P13-TECHNICAL-FINDING.md`](CONSTITUTION-P13-TECHNICAL-FINDING.md).

Each PBI's last line reads **Size · Release · Verify · Agent level · Effort** (§0.2), and **Depends** names the PBIs it waits on.

### E1 — Identity and icon

- **Goal:** Constitution has its frozen identity, its registered colour ladder, and its cover and
  launcher icon, all generated by IconStudio with nothing hand-made.
- **Business value:** a phone upgrades an app in place only while its id is unchanged, and the
  ids fail silently when they move, so freezing them first protects every later release. The book
  on the shelf is the owner's first sight of the app.
- **Sources:** brief §What to design first; [book p.1](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-1),
  [p.2](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-2),
  [p.3](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-3); D1–D12, D80; TF §1.2, §1.13, G1–G3.
- **Exit criteria:** every frozen-identity guard is red-then-green; the four token files and DESIGN.md
  §3 carry one identical ladder; IconStudio regenerates Constitution's icon and cover from the
  committed painting with the approved crop, and every other app's icons are byte-identical to before.
- **Depends on:** nothing. Every other epic depends on F-1.1.

#### F-1.1 Frozen identity and the solution

- **Description:** the four projects on Pulp's template, and the constants that must never move.
- **User-facing behaviour:** none directly. The owner sees an app that upgrades in place for the
  life of the phone.
- **Design / spec:** TF §1.1, §1.13, §4; D80.
- **Risks:** a `nameof`-derived key or a generated Java name, the trap that bit Folio and Foreword.

**C-1.1.1 · The solution and `ConstitutionIdentity`**
- *Story:* As the owner, I want Constitution's app id fixed in one constant, so that every future
  build upgrades the one already on my phone.
- *Acceptance:*
  - Given the solution, when it builds, then `Constitution.Domain`, `.Infrastructure`, `.Tests`
    target `net10.0` and `.Android` targets `net10.0-android`, with Pulp's reference shape (§2.1).
  - Given `ConstitutionIdentity.ApplicationId`, when the head's `ApplicationId` and manifest
    package are read, then both equal `com.storyworthtelling.constitution`.
  - Given `MainActivity`, when its `[Activity(Name = …)]` is read, then it is a literal built from
    the frozen constant, not generated from the namespace.
- *Tests first:* `Constitution.Tests/Guard/ConstitutionInvariantsTests`: `AppIdIsFrozen`,
  `HeadUsesTheFrozenAppId` (reads the csproj by content), `MainActivityNameIsFrozen`.
- *Design / spec:* TF §4 · *Depends:* — · **S · A · Tests · L3 · high**

**C-1.1.2 · Flyleaf knows the package**
- *Story:* As the owner, I want Flyleaf to recognise Constitution as a sibling, so that its book
  stands on the suite shelf and the lockdown clears it.
- *Acceptance:* Given `PolicyContext.ConstitutionPackage`, when compared with
  `ConstitutionIdentity.ApplicationId` (read by content), then they are equal, and the package
  carries the `com.storyworthtelling.` prefix that `AppCategorizer` clears.
- *Tests first:* `FocusPhone/Codex.Tests/Guard/FrozenInvariantsTests.ConstitutionPackageMatchesItsHead`.
- *Design / spec:* TF §1.9, §1.10 · *Depends:* C-1.1.1 · **XS · A · Tests · L1 · low**

**C-1.1.3 · The frozen set grows by test edit only**
- *Story:* As the owner, I want every worker name, unique work name and preference key held as a
  literal under a guard, so that a rename can never silently drop my settings or stop a job.
- *Acceptance:*
  - Given `ConstitutionIdentity.Workers`, `WorkNames` and `PreferenceKeys`, when the guard
    enumerates them, then each value equals its pinned literal and none is `nameof`-derived.
  - Given an empty set, when the guard runs, then it fails: a guard over nothing is vacuous.
  - Adding a worker or key later is a deliberate edit to the guard's expected list.
- *Tests first:* `ConstitutionInvariantsTests.WorkersAreFrozen`, `PreferenceKeysAreLiterals`
  (a source scan for `nameof(` inside the keys class), `FrozenSetsAreNotEmpty`.
- *Design / spec:* TF §4; CLAUDE.md (Folio and Foreword rename lessons) · *Depends:* C-1.1.1 · **S · A · Tests · L3 · high**

#### F-1.2 The identity ladder

- **Description:** the cerulean 230° ladder (D10), registered once and held identical everywhere.
- **User-facing behaviour:** Today's header rule, the Today tab, the shelf cloth and the Document
  header rule all wear the same cerulean.
- **Design / spec:** [book p.2](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-2); D10; notes §Identity.
- **Risks:** cerulean sits 23° from both In-progress teal and Reference slate, inside the 20°
  floor's margin (p.2), so a careless retune could cross it.

**C-1.2.1 · Register the ladder in four places**
- *Story:* As the owner, I want Constitution's colours defined once and checked everywhere, so that
  the shelf, the icon and the app never drift apart.
- *Acceptance:*
  - Given `Tools/IconStudio/Tokens.cs`, `Storyworthtelling.Paper/AppIdentityTokens.cs`,
    `Codex.Domain` `DesignTokens.Identity.Constitution` and DESIGN.md §3's ladder table, when read,
    then all four carry fill `#A4C1D0`, ground/cloth `#82ACC1`, mark `#1B6A89`, text `#15546E`,
    deep `#143B4C`.
  - Given the role and sibling hues, when the separation guards run, then every Constitution tier
    clears every role and every sibling by at least 20°.
- *Tests first:* `TokenParityTests.ConstitutionLadderIsIdenticalInEveryFile`;
  `DesignTokensTests.IdentityTiersStayClearOfEverySemanticRole` and
  `Storyworthtelling.Paper.Tests/IdentitySeparationTests` extended to Constitution (watched red
  first, with Constitution present but its hex deliberately wrong).
- *Design / spec:* p.2; D10; TF G3 · *Depends:* C-1.1.1 · **S · A · Tests · L2 · medium**

**C-1.2.2 · The Document cerulean and the cloth's ink inversion**
- *Story:* As the partner reading on a greyscale rM2, I want the documents' only colour to be a
  muted header rule, so that nothing on the page depends on seeing it.
- *Acceptance:*
  - Given the Document profile, when Constitution's documents ask for the identity, then they get
    `#476F83` and nothing else coloured.
  - Given `#82ACC1`, when `BookClothPalette.MarksInvertToInk` decides, then it returns ink
    (luma 0.635 against the 0.6 threshold), so the shelf draws rule and title in ink (D12).
- *Tests first:* `AppIdentityTokensTests.ConstitutionDocumentHueIsTheMutedCerulean`;
  `BookClothPaletteTests.ConstitutionClothInvertsToInk` (pins the thin margin).
- *Design / spec:* p.3, p.4; notes §Identity; TF §1.2 · *Depends:* C-1.2.1 · **XS · A · Tests · L1 · low**

#### F-1.3 IconStudio's crop, the cover and the icon

- **Description:** the approved painting, a crop rectangle on the spec, and the generated icon and
  shelf-cover window.
- **User-facing behaviour:** the launcher shows icon B: full bleed, cropped close to the runners and
  summit, no keyline (D12). Flyleaf shows the cover on its cerulean cloth.
- **Design / spec:** [book p.3](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-3); D9, D11, D12; TF G1, G2.
- **Risks:** a crop parameter that changes other apps' output; the art going in hand-edited.

**C-1.3.1 · A crop rectangle on `IconSpec`**
- *Story:* As the owner, I want the icon's crop written in the spec, so that regenerating the icon
  always gives the approved close crop rather than a centre square.
- *Acceptance:*
  - Given an `IconSpec` with `Crop` set, when `AdaptiveIconRenderer` draws the plate, then it uses
    that source rectangle instead of `CentreSquare`.
  - Given a crop that is not square or falls outside the image, when the spec is validated, then
    IconStudio fails with a message naming the spec and the bad rectangle.
  - Given every existing spec (no `Crop`), when regenerated, then the output is byte-identical to
    what is committed.
- *Tests first:* `Tools/IconStudio.Tests/AdaptiveIconRendererTests`: `CropReplacesTheCentreSquare`,
  `CropMustBeSquareAndInsideTheImage`, `SpecsWithoutACropAreUnchanged` (hashes the committed
  resources).
- *Design / spec:* p.3 ("this needs a crop-rectangle parameter"); TF G1 · *Depends:* — · **M · A · Tests · L2 · medium**

**C-1.3.2 · Commit the approved painting**
- *Story:* As the owner, I want POS.6 in the repository as IconStudio's source, so that the icon and
  cover are reproducible.
- *Acceptance:* Given the design book's published files, when POS.6 is copied to
  `Branding/constitution-cover.jpg`, then it measures 864 × 1184 and is the approved image (invented
  art, no personal content), and the commit says where it came from.
- *Tests first:* `BrandAssetsTests.ConstitutionCoverIsTheApprovedSize`.
- *Design / spec:* p.1 (POS.6), D9, D11; TF G2 · *Depends:* — · **XS · A · Tests · L1 · low**

**C-1.3.3 · The Constitution spec, its icon and its shelf window**
- *Story:* As the owner, I want IconStudio to write Constitution's launcher icon and Flyleaf cover,
  so that I never hand-edit a generated resource.
- *Acceptance:*
  - Given the `Constitution` `IconSpec` (resources under `Constitution/Constitution.Android/Resources`,
    an empty `Glyphs.ConstitutionPlate`, cloth `ConstitutionClothHex`, `PlateImage` the painting,
    `Crop` rows 190–974 centred, `CoverImage` `FocusPhone/Flyleaf.Android/Resources/drawable/cover_constitution.png`),
    when IconStudio runs, then it writes the adaptive mipmaps and the cover window.
  - Given the 48 px icon, when the owner looks at it, then the two runners read as two figures on
    the slope, as page 3 drew.
- *Tests first:* `BrandAssetsTests.ConstitutionResourcesAreGenerated`,
  `ConstitutionPlateDrawsNoKeyline`.
- *Design / spec:* p.3; D12; notes §Identity · *Depends:* C-1.2.1, C-1.3.1, C-1.3.2 · **S · A · Owner · L1 · low**

### E2 — The reMarkable documents

- **Goal:** the four documents (workout, weekly meal sheet, recipe, Saturday digest) render 1:1 at
  the tablet's 445 × 594 pt page, read correctly in greyscale, deliver to each tablet's root, and
  have their ink read back the next morning.
- **Business value:** the tablets carry the product (brief §At a glance). Planning, approval,
  following a workout, cooking and reading the digest all happen on paper.
- **Sources:** brief §reMarkable documents, §The weekly rhythm, §Voice seats;
  [book p.4–10](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-4); D13–D39, D68, D85, D87;
  TF §1.4, §1.5, G6.
- **Exit criteria:** every page renders as drawn (the owner compares each against its book page);
  every ink target comes from one shared source and is honoured by the reader; nothing carries
  meaning by colour; every resolver rule has a test; a delivery failure lands on the Waiting page.
- **Depends on:** E1 (identity), E9's notice plumbing (F-9.1). F-2.3 waits on E5's planner, F-2.4
  and F-2.5 on E6, F-2.6 on E7.

#### F-2.1 The page, the type and the ink targets

- **Description:** the shared frame every document draws in.
- **User-facing behaviour:** every place a pen can answer is a muted, well-spaced target of a
  known size; the pen's black always stands out from the print.
- **Design / spec:** [book p.4](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-4); D13–D18; TF §1.5, G6.
- **Risks:** a painter and a reader that drift apart, so a tick is read from the wrong box.

**C-2.1.1 · The 3:4 page and the Dose rung**
- *Story:* As the owner, I want every document on the tablet's own page shape, so that nothing is
  scaled or cropped on the glass.
- *Acceptance:*
  - Given `ConstitutionPage`, when a document begins a page, then it is 445 × 594 pt through
    `VectorPdfSink.BeginPage`.
  - Given the 54 pt dose (p.5), when it is drawn, then it uses a registered `PaperTypeScale.Dose`
    rung (PC5), recorded in DESIGN.md §19, and no composer names a size of its own.
- *Tests first:* `Storyworthtelling.Paper.Tests/PaperTypeScaleTests.DoseRungIsRegistered`; the
  existing no-literal composer guard extended to `Constitution.Infrastructure/Documents`.
- *Design / spec:* p.4, p.5; D13; TF §1.5 · *Depends:* C-1.1.1 · **S · C · Tests · L2 · medium**

**C-2.1.2 · `InkTargets`, one source of rectangles**
- *Story:* As the owner, I want the page painter and the ink reader to share one map of where every
  box is, so that the reader is told "box 3 is push-ups, in reps" instead of guessing.
- *Acceptance:*
  - Given `InkTargets`, when queried, then it supplies the page-4 sizes: large tick 28 × 28,
    standard tick 18 × 18 with rows 26 apart, circle lanes 84 pt (68 pt for four words) with
    hairline dividers, number box 96 × 40 with the unit outside, the shaded "No, because" field
    opposite YES, and the 91 pt notes margin.
  - Given any page layout, when targets are placed, then each keeps at least 8 pt clear, and nothing
    is printed inside a target.
  - Given a painter and the reader, when both run on the same page, then both read the same
    instances (Foreword's `JournalRegions` and Index's `WorksheetRegions` pattern).
- *Tests first:* `Constitution.Tests/Documents/InkTargetsTests`: `SizesMatchPageFour`,
  `EveryTargetKeepsEightPointsClear`, `NothingPrintsInsideATarget`, `PainterAndReaderShareOneMap`.
- *Design / spec:* p.4; D14–D16; TF §1.4 · *Depends:* C-2.1.1 · **M · C · Tests · L3 · high**

**C-2.1.3 · Nothing carries meaning by colour**
- *Story:* As the partner on a greyscale rM2, I want every document to say everything in ink
  weights, outlines and words, so that I lose nothing to the missing colour.
- *Acceptance:* Given any Constitution document rendered for the Document profile, when inspected,
  then the targets use the muted ink token, the only coloured stroke is the `#476F83` header rule,
  and every rating, state and change is also written in words.
- *Tests first:* `DocumentGreyscaleTests.TargetsPrintInMutedInk`,
  `OnlyTheHeaderRuleIsColoured` (walks the PDF's colour operators with PdfPig),
  `OneRenderServesBothTablets`.
- *Design / spec:* p.4–10 (each page's rM2 view); brief §Design rules · *Depends:* C-2.1.2 · **M · C · Tests · L3 · high**

#### F-2.2 Delivery and ink read-back

- **Description:** upload to each tablet's root, and Constitution's own morning worker that reads
  the ink, resolves it and trashes the page.
- **User-facing behaviour:** documents appear on the tablet unannounced. The next morning the plan
  knows what was ticked, circled and written; anything unclear waits as a chip pair on the Waiting
  page.
- **Design / spec:** [book p.4](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-4),
  [p.18](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-18); D17, D18, D85, D87; TF §1.4; brief §Voice seats, notices and failure states.
- **Risks:** R2 (pen marks read wrongly), R3 (the cloud omits the annotations), R4 (no "opened"
  signal).

**C-2.2.1 · The ink resolver**
- *Story:* As the owner, I want silence and every mark to mean exactly what the book wrote down, so
  that the plan never invents an answer I didn't give.
- *Acceptance:*
  - Given a box with any mark, a scribble included, then it is a tick (D18).
  - Given a meal row with a tick and a written reason, then it is a denial; the reason wins (D17).
  - Given silence: an empty workout box is skipped; an untouched meal-sheet item is approved at its
    deadline; an uncircled rating is unchanged; a blank "Still sore?" means better (D28); a blank
    staple means no need.
  - Given a circled word with no tick on an exercise page, then the page is done and the rating is
    used (p.5).
  - Given a circle touching two lanes, or a read below 0.75 confidence, then the item is not
    guessed and becomes a Caution notice with two chips.
- *Tests first:* `Constitution.Tests/Ink/InkResolverTests`, one test per rule above, named for it.
- *Design / spec:* p.4, p.5, p.7, p.9, p.18; D14–D18, D28 · *Depends:* C-2.1.2 · **M · C · Tests · L3 · high**

**C-2.2.2 · Delivery to the root**
- *Story:* As the owner, I want each document uploaded to the right tablet with nothing else to do,
  so that the morning's pages are just there.
- *Acceptance:*
  - Given a document and that phone's pairing (D85), when delivered, then it is uploaded at the
    root under `DeliveredDocument`'s single name for it.
  - Given a refused token or a failed upload, when delivery fails, then a Problem notice says what
    happened and how to fix it ("Pair again"), with no phone copy of the pages, and delivery retries
    once the token is fixed.
- *Tests first:* `DeliveryServiceTests` (fake `IRemarkableClient`): `UploadsAtTheRoot`,
  `RefusedTokenRaisesTheProblemNotice`, `RetriesAfterThePairingIsFixed`, `NeverFallsBackToThePhone`.
- *Design / spec:* p.18 (delivery failure); brief §failure table · *Depends:* C-1.1.1, C-9.1.1 · **M · C · Device · L2 · medium**

**C-2.2.3 · The morning read-back worker**
- *Story:* As the owner, I want yesterday's ink read each morning after Foreword's run, so that the
  plan learns from the paper without my touching the phone.
- *Acceptance:*
  - Given yesterday's documents on the tablet, when the worker runs after Foreword's delivery window
    (D87), then it downloads each by title, reads it with the region instruction from `InkTargets`,
    resolves it (C-2.2.1), and trashes it only after the result is saved.
  - Given a document found with no annotations rendered, then Diagnostics records "document found,
    nothing read" and the page is not trashed (R3).
  - Given the digest, then it is trashed after three days (R4).
  - The worker's Java name and unique work name are in the frozen set (C-1.1.3).
- *Tests first:* `ReadBackServiceTests` (fake reMarkable + fake extractor): `TrashesOnlyAfterSaving`,
  `LowConfidenceGoesToWaiting`, `MissingAnnotationsAreReportedNotSilent`, `DigestGoesAfterThreeDays`.
- *Design / spec:* D87; TF §1.4; brief §The weekly rhythm · *Depends:* C-2.2.1, C-2.2.2 · **L · C · Device · L3 · xhigh**

#### F-2.3 The workout document

- **Description:** a cover and one page per exercise, plus the assessment and injury variants.
- **User-facing behaviour:** the propped-up tablet shows the dose at 54 pt, the load, the rest, one
  Score setting, one voiced line, and the rating and Done tick fixed at the foot.
- **Design / spec:** [book p.5](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-5),
  [p.6](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-6); D19–D29; brief §Workout document.
- **Risks:** an illustration showing the wrong form (D21's catch); a forecast re-send after the slot
  began.

**C-2.3.1 · The cover**
- *Story:* As the owner, I want the workout's first page to list the exercises, doses and total time
  with its three rules in plain words, so that I know the session before I start.
- *Acceptance:* Given a planned session, when the cover renders, then it lists exercises with doses
  and page numbers, the total time and the three rules (tick, circle, unticked means skipped), with
  the stopped-early rule (D24) and **no voiced line**.
- *Tests first:* `WorkoutCoverTests.CarriesTheThreeRulesAndTheStoppedEarlyRule`, `HasNoVoicedLine`.
- *Design / spec:* p.5; D24 · *Depends:* C-2.1.2, C-5.1.1 · **S · C · Owner · L2 · medium**

**C-2.3.2 · An exercise page**
- *Story:* As the owner mid-session, I want each exercise on its own page with the rating and tick
  in the same place every time, so that my hand finds them without looking.
- *Acceptance:*
  - Given an exercise, when its page renders, then top to bottom it shows the dose on the Dose rung,
    load and rest in **pounds** (D22), the illustration if due (C-2.3.3), **one** Score setting
    printed `Score · 132 bpm · Steady` with the band from `TempoBands.Of(bpm)` (D25), the
    encouragement seat (one line, progression lives here), then easy / right / hard lanes and the
    large Done tick fixed at the foot (D23), and the notes margin.
- *Tests first:* `ExercisePageTests.ScoreLineNamesTheTempoBandsBand` (a guard pinning the printed
  band to `TempoBands.Of`), `RatingAndTickSitAtTheFixedFoot`, `WeightsArePounds`,
  `CarriesOneEncouragementLine`.
- *Design / spec:* p.5; D20–D25 · *Depends:* C-2.1.2, C-5.1.1, C-9.2.1 · **M · C · Owner · L2 · medium**

**C-2.3.3 · Exercise illustrations**
- *Story:* As the owner, I want a health-poster figure while an exercise is new to me, checked
  against its description, so that I never learn a wrong form from the page.
- *Acceptance:*
  - Given an exercise done fewer than five times, or not in the last month, then the page carries
    its figure; otherwise none.
  - Given a newly generated figure (Gemini, health-poster style, D20), when a second Claude pass
    compares it with the written description and finds a mismatch, then it is regenerated once;
    a second failure prints the page with no figure (D21).
  - Figures are cached in app storage with a Drive backup and are never committed.
- *Tests first:* `IllustrationPolicyTests`: `ShownUntilTheFifthTime`, `ShownAgainAfterAMonthAway`,
  `ARepeatedMismatchPrintsNoFigure`; `NoIllustrationsInTheRepoTests` (no image under
  `Constitution/` outside the generated launcher resources).
- *Design / spec:* p.5 (style round, form check); D20, D21; TF §1.5 · *Depends:* C-2.3.2 · **M · C · Owner · L3 · high**

**C-2.3.4 · Assessment week pages**
- *Story:* As the owner starting out, I want the assessment pages to ask only for a number or a
  circled word, so that the plan starts from where I really am.
- *Acceptance:* Given an assessment session, then its cover says what the week is for and that
  there is no score; test pages carry one number box with the unit outside and **no** easy / right
  / hard (D27); the toe reach is four circled words in 68 pt lanes.
- *Tests first:* `AssessmentPageTests.TestPagesCarryNoRating`, `ToeReachUsesFourNarrowLanes`.
- *Design / spec:* p.6; D15, D26, D27 · *Depends:* C-2.3.2 · **S · C · Owner · L2 · medium**

**C-2.3.5 · Injury day pages**
- *Story:* As the owner with something sore, I want "Still sore?" at the top of every page until I
  leave it blank, and a doctor suggestion once if it lasts, so that the plan works around it calmly.
- *Acceptance:* Given an open soreness note, then every page carries "Still sore?" on a light band
  at the top with the place and date noted; the plan's substitution is in plain words and the tempo
  drops to Easy; a lasting soreness prints the doctor note **once**, in plain neutral type, not the
  owner's voice (D29).
- *Tests first:* `InjuryPageTests.StillSoreOnEveryPage`, `DoctorNotePrintsOnceInNeutralType`.
- *Design / spec:* p.6; D28, D29 · *Depends:* C-2.3.2, C-5.2.3 · **S · C · Owner · L2 · medium**

**C-2.3.6 · Re-sent only before the slot**
- *Story:* As the owner, I want a forecast change to replace the workout silently only before it
  starts, so that the page never changes under me mid-session.
- *Acceptance:* Given a flipped forecast, when the slot has not started, then the document is
  replaced at the root; when it has, nothing is sent.
- *Tests first:* `RedeliveryTests.ReplacesBeforeTheSlot`, `NeverReplacesOnceStarted`.
- *Design / spec:* brief §reMarkable documents (Arrives) · *Depends:* C-2.2.2, C-5.2.2 · **S · C · Tests · L2 · medium**

#### F-2.4 The weekly meal sheet

- **Description:** five pages, identical on both tablets, every line approvable.
- **User-facing behaviour:** on Thursday both adults find next week's meals, with a YES box and a
  shaded "No, because" field on every row; silence approves at the deadline.
- **Design / spec:** [book p.7](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-7); D30–D33, D81, D82; brief §Weekly meal sheet, §The weekly rhythm.
- **Risks:** a Saturday denial read only on Sunday morning, so the deadline must allow for it.

**C-2.4.1 · Dinners**
- *Story:* As either adult, I want each dinner as a row I can tick or deny in my own words, so that
  approving the week takes a minute with a pen.
- *Acceptance:* Given the week's dinners, then each row shows a YES box, the dish, the cook, any
  outlined-word marker (SLOW COOKER / EATING OUT / NEW RECIPE), and the shaded "No, because" field
  opposite YES (D32); the field's rectangle comes from `InkTargets`.
- *Tests first:* `MealSheetTests.DinnerRowsCarryBothEnds`, `MarkersAreWordsNotColours`.
- *Design / spec:* p.7 (treatment B); D32 · *Depends:* C-2.1.2, C-6.3.1 · **M · D · Owner · L2 · medium**

**C-2.4.2 · Breakfasts, lunches and lunchboxes**
- *Story:* As either adult, I want every breakfast, lunch and lunchbox part approvable line by line,
  weekends included, so that the whole week is planned.
- *Acceptance:* Given the week, then breakfast and lunch rows appear per person where they differ,
  weekends included (D31); lunchboxes take their own page (sandwich, fruit, snack, separate snack);
  every line has its own YES box and "No, because" field (D30).
- *Tests first:* `MealSheetTests.WeekendsArePlanned`, `EveryLineIsApprovable`, `LunchboxesHaveTheirOwnPage`.
- *Design / spec:* p.7; D30, D31 · *Depends:* C-2.4.1 · **M · D · Owner · L2 · medium**

**C-2.4.3 · Groceries and staples**
- *Story:* As the household planner, I want the grocery preview with an over-budget flag in words
  and the staples to tick, so that the paper is how the pantry learns.
- *Acceptance:*
  - Given the week's list, then groceries print by store section; a week over Index's grocery budget
    shows an ink-framed flag in words.
  - Given staples, then each is a plain box ("need more") with blank lines for new ones; a new word on
    a blank line becomes a staple.
  - The printed list is the preview only; the Keep note is the separate hand-off (D81, D90).
- *Tests first:* `GroceryPageTests.OverBudgetIsAFlagInWords`, `ANewWordBecomesAStaple`,
  `StaplesHaveNoYesNo`.
- *Design / spec:* p.7; D81; brief §Meal planning · *Depends:* C-2.4.1, C-11.1.1, C-6.4.2 · **S · D · Owner · L2 · medium**

**C-2.4.4 · The holiday ask-ahead**
- *Story:* As the household planner, I want a "what are we bringing?" page only the week before a
  holiday we host or visit, so that the paper asks early.
- *Acceptance:* Given a hosting or visiting event in the calendar next week, then the ask-ahead page
  is included; otherwise it is not.
- *Tests first:* `MealSheetTests.AskAheadOnlyTheWeekBefore`.
- *Design / spec:* p.7; brief §Meal planning · *Depends:* C-2.4.1 · **S · D · Tests · L1 · low**

**C-2.4.5 · Both tablets, one deadline**
- *Story:* As both adults, I want the same sheet on both tablets, where either tick approves and
  either denial wins, so that we never contradict each other.
- *Acceptance:*
  - Given Thursday, then an identical sheet reaches both tablets, opening with the meal-sheet voice
    seat.
  - Given ink from both tablets, then a tick on either approves, a denial on either wins, and an
    untouched item is approved at Sunday morning's read.
  - Given a denial, then a revised sheet replaces the old one on both tablets.
- *Tests first:* `ApprovalTests.EitherTickApproves`, `EitherDenialWins`,
  `SilenceApprovesAtTheDeadline`, `ADenialSendsARevisedSheet`.
- *Design / spec:* p.7; brief §The weekly rhythm, §Weekly meal sheet · *Depends:* C-2.4.1, C-2.2.3, C-9.2.1 · **M · D · Device · L3 · high**

#### F-2.5 The recipe

- **Description:** tonight's cook's recipe, with ingredients scaled, steps on clock times, the
  kids' page when needed, and a rating page.
- **User-facing behaviour:** an hour before the cook block, the cook's tablet has the recipe on
  times worked back from dinner; the next morning the ratings are known.
- **Design / spec:** [book p.8](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-8),
  [p.9](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-9); D34–D37, D68; brief §Recipe document.
- **Risks:** dinner time inferred wrongly from the calendar, so every step time is wrong.

**C-2.5.1 · Ingredients and equipment, scaled**
- *Story:* As tonight's cook, I want quantities already scaled to who is eating, guests included, so
  that I never do arithmetic at the counter.
- *Acceptance:* Given tonight's headcount (with guests from the calendar), then the page states it
  first ("for 4: 2 adults, 2 children"), groups ingredients by when they're used with quantities
  right-aligned in mono, lists Have out and the equipment, says what the card was scaled from, and
  carries the notes margin (D35).
- *Tests first:* `RecipeScalingTests.ScalesToTheHeadcount`, `GuestsComeFromTheCalendar`.
- *Design / spec:* p.8; D34, D35 · *Depends:* C-2.1.2, C-6.3.1 · **M · D · Owner · L2 · medium**

**C-2.5.2 · Steps on the clock**
- *Story:* As tonight's cook, I want each step at a clock time worked back from dinner, so that
  dinner lands on time without my planning it.
- *Acceptance:* Given dinner time from the calendar, then every step carries a time at the Title
  rung in Cascadia, with asides for the why and the hand-offs to the kids' page and one line for
  running late; a slow-cooker variant puts the morning steps first and names the placed calendar
  event ("Start the slow cooker").
- *Tests first:* `StepTimingTests.TimesWorkBackFromDinner`, `SlowCookerMorningStepsComeFirst`,
  `TheMorningStepNamesTheCalendarEvent`.
- *Design / spec:* p.8; D34 · *Depends:* C-2.5.1, C-6.4.1 · **M · D · Owner · L3 · high**

**C-2.5.3 · The kids' page**
- *Story:* As tonight's cook, I want the children's different meal on its own page on nights they
  eat differently, so that both dinners run on one clock.
- *Acceptance:* Given a night the children eat differently, then a kids' page appears with one line
  saying why, its own ingredients and timed steps, and the notes margin; nothing on it counts
  nutrition.
- *Tests first:* `KidsPageTests.OnlyOnNightsTheyEatDifferently`, `CountsNothing`.
- *Design / spec:* p.9; brief §Meal planning (kids' meals) · *Depends:* C-2.5.2 · **S · D · Owner · L2 · medium**

**C-2.5.4 · The rating page and "We didn't cook this tonight"**
- *Story:* As tonight's cook, I want to circle only a first or changed rating, and tick one box if
  we didn't cook, so that the taste profile and pantry stay true.
- *Acceptance:*
  - Given four people, then each row names the dish that person ate (D37), Loved / Fine / Never
    again in circle lanes, each person's last rating pre-underlined, "First time · please circle"
    otherwise, and the Never-again rule printed at the foot.
  - Given "We didn't cook this tonight" ticked, then the ratings are ignored and the dish's fresh
    ingredients are written off (D68).
- *Tests first:* `RatingPageTests.PreUnderlinesTheLastRating`, `EachRowNamesTheDishThatPersonAte`;
  `RatingReadTests.DidntCookIgnoresRatingsAndWritesOffFresh`.
- *Design / spec:* p.9; D37, D68 · *Depends:* C-2.5.1, C-2.2.1 · **M · D · Owner · L3 · high**

**C-2.5.5 · Delivered an hour before the cook block**
- *Story:* As tonight's cook, I want the recipe on my tablet an hour before I start, and only on
  mine, so that it is there when I need it.
- *Acceptance:* Given the cook block, then the recipe reaches only the cook's tablet about an hour
  before it (before the morning block for a slow-cooker meal), as a delayed WorkManager job.
- *Tests first:* `RecipeDeliveryTests.OnlyTheCooksTablet`, `AnHourBeforeTheBlock`,
  `SlowCookerArrivesBeforeTheMorningBlock`.
- *Design / spec:* brief §reMarkable documents; TF §1.4 · *Depends:* C-2.2.2, C-6.4.1 · **S · D · Device · L2 · medium**

#### F-2.6 The Saturday digest

- **Description:** five pages per adult, private except the meals page.
- **User-facing behaviour:** on Saturday morning each adult reads their week, their Body trends
  against their own past, and writes notes to the planner.
- **Design / spec:** [book p.10](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-10); D38, D39; brief §Saturday digest.
- **Risks:** a pattern read as advice.

**C-2.6.1 · Workouts, done against planned**
- *Story:* As the owner, I want a plain count and the week day by day, so that I see what happened
  without being scored.
- *Acceptance:* Given the week, then the page shows done against planned as a count and minutes, a
  filled square per done day (with its ratings summarised), an open square per skip, nothing on rest
  days, and "What moved" in sentences; no streak, percentage or comparison with anyone.
- *Tests first:* `DigestWorkoutsTests.CarriesNoStreakOrPercentage`, `RestDaysAreUnmarked`.
- *Design / spec:* p.10; brief §Design rules · *Depends:* C-2.1.2, C-5.2.1 · **M · E · Owner · L2 · medium**

**C-2.6.2 · Body, every signal, every week**
- *Story:* As the owner, I want every signal on two pages against my own last 12 weeks, so that the
  digest is the honest trend the app exists for.
- *Acceptance:* Given eight signals, then each gets one small neutral-ink line of weekly averages
  over the owner's last 12 weeks, the latest week as a dot with its value and the change in words,
  dotted gaps for missing stretches (D39); the patterns block is headed "not causes" and is the
  **only** place in Constitution a correlation appears.
- *Tests first:* `DigestBodyTests.EverySignalEveryWeek`, `ComparesOnlyToTheOwnersPast`;
  `CorrelationsOnlyInTheDigestTests` (no other composer or screen calls the correlation service).
- *Design / spec:* p.10; D39; brief §Body · *Depends:* C-2.6.1, C-7.1.2 · **M · E · Owner · L3 · high**

**C-2.6.3 · Meals and next week**
- *Story:* As either adult, I want the week's meals in three facts and next week's plan with ruled
  lines for notes to the planner, so that I can steer next week with a pen.
- *Acceptance:* Given the week, then the meals page is identical on both copies (the loved dish, the
  stretch dish's verdict and what follows, three facts); next week lists the planned workouts and
  known dinners, then "Notes to the planner" lines read at Sunday's morning run; pages 1–3 keep the
  notes margin.
- *Tests first:* `DigestMealsTests.TheMealsPageIsSharedContent`; `PlannerNotesTests.ReadAtSundaysRun`.
- *Design / spec:* p.10 · *Depends:* C-2.6.1, C-6.3.1 · **S · E · Owner · L2 · medium**

**C-2.6.4 · No voice, no colour, trashed after three days**
- *Story:* As the owner, I want the digest plain and gone after three days, so that it reads as a
  record, not a pep talk.
- *Acceptance:* Given the digest, then it carries no voiced line and no calligraphy, and is trashed
  three days after delivery (R4).
- *Tests first:* `DigestTests.HasNoVoicedLine`; covered for trashing by C-2.2.3.
- *Design / spec:* p.10; R4 · *Depends:* C-2.2.3 · **XS · E · Tests · L1 · low**

### E3 — The phone shell and its scrapbook material

- **Goal:** the five-tab binder over `Codex.Ui.Android`, and the scrapbook materials (crinkled
  exam-table ground, tape, clippings, index cards, the notepad leaf, the receipt and the chart
  scrap) as registered tokens drawn by thin painters over pure, tested geometry.
- **Business value:** the screens say what each thing is by its material (D42), which is what keeps
  a phone visit under 30 seconds.
- **Sources:** brief §Phone screens; [book p.11](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-11)
  (the ground and the tab strip), p.12–17; D40–D48, D71, D84; TF §1.2, G4, G5.
- **Exit criteria:** five tabs as drawn; every material registered with leak-guard coverage; the
  scrap layout never overlaps; DESIGN.md §2, §9 and §19 record the departures.
- **Depends on:** E1.

#### F-3.1 The binder and its five tabs

- **Description:** Today · Workouts · Meals · Body · Settings, deep cloths with paper lettering.
- **User-facing behaviour:** a switched-off pillar's tab disappears; Meals stays a household plan.
- **Design / spec:** p.11 (tab strip B), p.17 (pillars); D40, D45, D84; brief §Who it's for.
- **Risks:** the Meals steel cloth unregistered anywhere today.

**C-3.1.1 · Five areas and their cloths**
- *Story:* As the owner, I want the five tabs in the colours I chose, so that I know where I am by
  colour and label.
- *Acceptance:*
  - Given the binder, then Today wears the cerulean identity, Workouts deep red, Meals dark stovetop
    steel, Body deep green, Settings the neutral grey, inactive tabs as deep cloths with paper
    lettering (D45).
  - Given D84, then `AreaHueTests` allows Constitution's cloths to share a hue with each other or
    with a role, and a fold wearing its own tab's hue on Workouts or Body passes.
- *Tests first:* `Codex.Tests/Design/AreaHueTests` amended with a Constitution case (written first,
  red under the old rule); `ConstitutionAreaTokensTests.EveryClothIsRegistered`.
- *Design / spec:* p.11; D40, D45, D84; TF §1.2 · *Depends:* C-1.2.1 · **S · A · Owner · L2 · medium**

**C-3.1.2 · Pillars hide their tabs**
- *Story:* As either adult, I want to switch a pillar off and see its tab go, so that the app only
  shows what I use.
- *Acceptance:* Given a pillar switched off, then its tab is gone and the strip closes up; given
  Meals switched off on one install, then the household plan continues for the other.
- *Tests first:* `PillarVisibilityTests.OffHidesTheTab`, `MealsStaysAHouseholdPlan`.
- *Design / spec:* p.17; brief §Who it's for · *Depends:* C-3.1.1 · **S · A · Tests · L1 · low**

**C-3.1.3 · DESIGN.md records the departures**
- *Story:* As a future contributor, I want DESIGN.md to say why Constitution's tabs and materials
  differ, so that a later guard doesn't "fix" an approved choice.
- *Acceptance:* DESIGN.md §9 records D45's deep inactive cloths and D84's shared hues; §2 records the
  scrapbook materials as a scoped departure (like Flyleaf's bookcase); §19 lists the new token file
  entries and the Dose rung.
- *Tests first:* `DesignDocTests.ConstitutionDeparturesAreRecorded` (reads DESIGN.md by content).
- *Design / spec:* D45, D84; TF §1.2 · *Depends:* C-3.1.1, C-3.2.1 · **XS · A · Tests · L1 · low**

#### F-3.2 The scrapbook materials

- **Description:** the ground and every scrap kind, as tokens and painters.
- **User-facing behaviour:** each screen looks like a calm, messy family scrapbook of taped scraps
  on crinkled exam-table paper, with no shadows and no overlaps.
- **Design / spec:** [book p.11](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-11)–[p.16](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-16); D41–D48; TF G4.
- **Risks:** scraps that overlap or crowd on a small phone; Canvas work that drifts from the mockup.

**C-3.2.1 · Register the materials**
- *Story:* As the owner, I want the scrapbook's materials defined as tokens, so that no colour is
  invented on a screen.
- *Acceptance:* Given `MaterialTokens.cs`, then it carries the crinkle ground's crease and tick
  inks, the perforation, tape, magazine stock and halftone, the notepad leaf, the receipt, the
  doctor's-chart grid (the Reference mark at 10%), and Index's card head rule, body rule and 22 dp
  pitch moved here with Index re-pointed (PC6). The mockups' CSS values are the starting point.
- *Tests first:* `HexLiteralLeakTests` extended to `Constitution/` (red on a planted literal);
  `MaterialParityTests.IndexAndConstitutionShareOneCard`.
- *Design / spec:* p.11, p.13, p.16; D42–D46; TF §1.2, G4 · *Depends:* C-1.1.1 · **M · A · Tests · L2 · medium**

**C-3.2.2 · The crinkled ground**
- *Story:* As the owner, I want the page ground to read as crinkled exam-table paper with one tear
  line, so that the app feels like the scrapbook I approved.
- *Acceptance:* Given a page, then a seeded, deterministic `CrinkleField` (Domain) yields creases
  and crinkle ticks on the suite paper with one perforated tear line; the painter draws it in the
  well only, never in the binder gutter, and never letters anything onto it (D43). Settings has no
  crinkle (D71).
- *Tests first:* `CrinkleFieldTests.IsDeterministicForASeed`, `StaysOutOfTheGutter`,
  `HasExactlyOnePerforation`; `SettingsIsPlainTests` (no crinkle painter on Settings).
- *Design / spec:* p.11; D43, D71 · *Depends:* C-3.2.1 · **M · A · Owner · L2 · medium**

**C-3.2.3 · Scraps, tilts and tape**
- *Story:* As the owner, I want each thing on its own taped scrap at a slight tilt, never touching
  another, so that the page stays calm.
- *Acceptance:*
  - Given a page's scraps, then a pure `ScrapLayout` places one thing per scrap with a tilt inside
    the drawn range and **no two scraps overlap** at any phone width from 360 dp to 430 dp (D47).
  - Given each kind, then a thin painter draws it: magazine clipping (halftone, heavy kicker rule,
    Serif Black head, scissor edge, folio, a sliver of the next column), index card (Index's
    tokens), notepad leaf (torn top), receipt (serrated foot, mono), chart scrap (grid, form head,
    torn foot). Every scrap is held by Scotch tape (D48); none has a shadow.
- *Tests first:* `ScrapLayoutTests.NoTwoScrapsOverlap` (over a width sweep),
  `TiltsStayInRange`; `NoShadowsTests` (no `SetShadowLayer` or elevation under the scrap painters).
- *Design / spec:* p.11–16; D41, D42, D44, D46–D48 · *Depends:* C-3.2.1 · **L · A · Owner · L3 · high**

**C-3.2.4 · Page furniture**
- *Story:* As either adult, I want every screen to open with its Serif header, load with a status
  sentence and keep its one action in a pinned bar, so that Constitution behaves like every sibling.
- *Acceptance:* Given any tab, then it opens with a Serif Black in-page header and the identity rule
  (Today), loads with a teal status sentence (never a spinner), keeps primary actions in a pinned
  bottom bar, and the well ends above the bar so nothing scrolls behind it (D59).
- *Tests first:* `PageFurnitureTests.NoSpinnerAnywhere` (source scan for `ProgressBar`),
  `WellEndsAboveTheBar`.
- *Design / spec:* p.11–15; DESIGN.md §5–§11; D59 · *Depends:* C-3.1.1 · **S · A · Owner · L2 · medium**

### E4 — Today

- **Goal:** the landing page: one voiced line, the workout clipping, the dinner card, and one action.
- **Business value:** the 30-second glance (brief §Design rules). Today is how the plan reaches
  the owner without opening anything.
- **Sources:** brief §Today; [book p.11](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-11),
  [p.15](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-15), [p.18](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-18); D46, D49, D65; voice seats 1 and 2.
- **Exit criteria:** the three Today states (voiced workout day, voiced rest day, neutral fallback)
  match page 11; rows open nothing; the only action is "I ate something else".
- **Depends on:** E3; E9 for the seats; E5 and E6 for content (Releases C and D).

#### F-4.1 Today's page

- **Description:** the landing tab on both installs.
- **User-facing behaviour:** the owner glances, reads one line, sees the workout and dinner, and
  leaves; or taps the one action.
- **Design / spec:** p.11 (three states), p.15 (estimate inline); brief §Today.
- **Risks:** a row that looks tappable; a seat that hides instead of falling back.

**C-4.1.1 · The top line**
- *Story:* As either adult, I want one line at the top in the owner's voice, or a plain one when
  there is no voice, so that Today always says something.
- *Acceptance:* Given a profile and key, then the seat is a taped notepad leaf in the calligraphy
  hand; given neither, then the same facts print as a plain Serif sentence ("Strength at 5:30.
  Lentil soup tonight."), never hidden. On a rest day with dinner out, the rest-day seat replaces it.
- *Tests first:* `TodaySeatTests.FallsBackNeverHides`, `RestDayUsesItsOwnSeat` (seats from F-9.2).
- *Design / spec:* p.11, p.18 · *Depends:* C-3.2.3, C-9.2.1 · **S · A · Owner · L2 · medium**

**C-4.1.2 · The workout clipping**
- *Story:* As the owner, I want today's workout as one clipping with its time, length, type and Score
  setting, so that I know when and what without opening anything.
- *Acceptance:* Given a planned session, then a magazine clipping shows "Workout · 5:30", "Strength,
  40 minutes" and "Score 132 bpm · Steady" (band from `TempoBands.Of`); on a rest day it is absent.
- *Tests first:* `TodayModelTests.WorkoutLineHasTimeLengthTypeAndBand`, `RestDayHasNoClipping`.
- *Design / spec:* p.11; brief §Today · *Depends:* C-4.1.1, C-5.1.1 · **S · C · Owner · L2 · medium**

**C-4.1.3 · The dinner card**
- *Story:* As either adult, I want tonight's dinner and who cooks on an index card, or a receipt when
  we're eating out, so that dinner is never a question.
- *Acceptance:* Given a planned dinner, then an index card shows "Dinner · 6:15", the dish and who
  cooks; given an eating-out night, then a restaurant receipt shows the place and when to leave (D46).
- *Tests first:* `TodayModelTests.DinnerCarriesTheCook`, `EatingOutIsAReceipt`.
- *Design / spec:* p.11; D46 · *Depends:* C-4.1.1, C-6.3.1 · **S · D · Owner · L2 · medium**

**C-4.1.4 · One action, nothing else tappable**
- *Story:* As either adult, I want Today's rows to do nothing on a tap and "I ate something else" to
  be the only action, so that the visit stays under 30 seconds.
- *Acceptance:* Given Today, then no row has a click or long-press handler and none shows a chevron;
  the bar's single action is "I ate something else" (F-6.6), whose estimate appears inline on the
  meal's card (p.15).
- *Tests first:* `TodayHasOneActionTests` (scans the Today view source for listeners on rows).
- *Design / spec:* p.11, p.15; brief §Today · *Depends:* C-4.1.1 · **XS · A · Tests · L1 · low**

**C-4.1.5 · Dates and times**
- *Story:* As either adult, I want Today's date and times in the suite's 12-hour form, so that it
  reads like every other app.
- *Acceptance:* Given Today, then the header date reads "Tuesday, Nov 17" and times read "5:30" /
  "6:15" through `TimeFormats`; Constitution's copy of `RelativeTime`/`TimeFormats` is held
  identical by the existing parity test.
- *Tests first:* the cross-Domain `TimeFormats` parity test extended to `Constitution.Domain`.
- *Design / spec:* p.11; DESIGN.md §11 · *Depends:* C-1.1.1 · **XS · A · Tests · L1 · low**


## 4. Epics E5–E9

### E5 — Workouts

- **Goal:** a longevity plan for each adult that adapts to the calendar, recovery, weather and
  soreness, placed into real calendar gaps without ever causing calendar chaos, and a small
  Workouts tab with Skip / Swap and undo.
- **Business value:** "most planned workouts done" is a third of the success definition, and a bad
  placement is the dealbreaker the owner named (brief §Design rules).
- **Sources:** brief §Workout planning, §Calendar, §Workouts, §First run;
  [book p.12](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-12), p.5–6; D50–D52;
  TF §1.6, R7, R8, G10.
- **Exit criteria:** the placement property suite never places into busy time or double-books
  across two planners; every adaptation row in the brief's table has a test; the Workouts tab
  matches page 12 in all four states.
- **Depends on:** E1, E3, F-2.2 (read-back), F-9.1 (undo fold), F-8.2 (work schedule from the
  personal vault).

#### F-5.1 The planner

- **Description:** the session model and the weekly plan.
- **User-facing behaviour:** a balanced week of strength, outdoor cardio, treadmill and mobility,
  sized to free gaps of at least 30 minutes, using only ticked equipment.
- **Design / spec:** brief §Workout planning; p.5, p.6, p.12.
- **Risks:** a plan too hard for a "mostly inactive, starting fresh" owner.

**C-5.1.1 · Sessions and the weekly plan**
- *Story:* As either adult, I want a balanced week sized to my free time and my equipment, so that I
  never have to plan a workout.
- *Acceptance:*
  - Given the week's free gaps, then each session fits a gap of at least 30 minutes, and frequency
    adapts to the calendar and recovery with no fixed days.
  - Given the equipment checklist, then no session prescribes an unticked item (D72).
  - Given a Sunday, then only gentle walks are planned.
  - Given a family walk or an adult session after the children's bedtime, then it is a joint session
    that replaces each adult's own session that day and appears on the family calendar and both cards.
- *Tests first:* `WorkoutPlannerTests`: `SessionsFitGapsOfThirtyMinutes`, `NeverPrescribesUntickedEquipment`,
  `SundaysAreGentleWalks`, `AJointSessionReplacesBothOwn`.
- *Design / spec:* brief §Workout planning; p.12 · *Depends:* C-1.1.1, C-5.3.1 · **L · C · Tests · L3 · high**

**C-5.1.2 · Assessment week**
- *Story:* As the owner starting out, I want a gentle assessment week before anything else, so that
  the plan starts from my real baseline.
- *Acceptance:* Given a first install, or two or more weeks without a done session, then the week is
  three gentle sessions (strength baselines, a timed walk or jog, a mobility check) whose numbers are
  read back; moderate sessions start in week two.
- *Tests first:* `AssessmentTests.FirstWeekIsAssessment`, `TwoWeeksOffReRunsIt`, `WeekTwoStartsModerate`.
- *Design / spec:* brief §First run; p.6 · *Depends:* C-5.1.1, C-2.2.1 · **S · C · Tests · L2 · medium**

**C-5.1.3 · Progression from the pages**
- *Story:* As the owner, I want each exercise's rating and tick to move the next session, so that the
  plan eases or progresses without my saying anything.
- *Acceptance:* Given a ticked page, then it was done as written; given "hard" on an exercise, or the
  session stopped early, then the last ticked exercise was too much or the session ran long, and the
  plan eases; given "easy" repeatedly, then load progresses and the encouragement line says so.
- *Tests first:* `ProgressionTests.HardEasesThatExercise`, `StoppedEarlyEasesTheSession`,
  `EasyProgressesTheLoad`, `ProgressionReachesTheEncouragementFacts`.
- *Design / spec:* brief §Workout planning; p.5; D23 · *Depends:* C-5.1.1, C-2.2.1 · **M · C · Tests · L3 · high**

**C-5.1.4 · A rules-only plan when Claude is away**
- *Story:* As the owner, I want a sensible template week when Claude can't be reached, so that
  nothing looks different on the page.
- *Acceptance:* Given Claude unavailable at planning time, then a template workout week is placed by
  the same rules, and nothing on the page or phone says so; Diagnostics records it.
- *Tests first:* `FallbackPlanTests.TemplateWeekObeysTheSameRules`, `NothingOnThePageSaysSo`.
- *Design / spec:* brief §failure table; R10 · *Depends:* C-5.1.1 · **S · C · Tests · L2 · medium**

#### F-5.2 Adapting the plan

- **Description:** the brief's situation table, row by row.
- **User-facing behaviour:** the next card simply changes, with a one-line reason on the Workouts tab
  where something moved ("Moved indoors: rain forecast").
- **Design / spec:** brief §Workout planning (table); p.12 (reason lines).
- **Risks:** background Health Connect reads unavailable (R5).

**C-5.2.1 · Skips and done days**
- *Story:* As the owner, I want an unticked session counted as skipped with no message, so that
  missing one costs me nothing but an easier plan.
- *Acceptance:* Given end of day with no mark on a session's pages, then it is a skip, the plan eases,
  and nothing is said; done and skipped days feed the digest.
- *Tests first:* `SkipLedgerTests.UntickedIsSkipped`, `ASkipSaysNothing`, `TheDigestSeesTheWeek`.
- *Design / spec:* brief §Workout planning · *Depends:* C-2.2.1 · **S · C · Tests · L2 · medium**

**C-5.2.2 · Weather and air**
- *Story:* As the owner, I want outdoor sessions moved indoors or rested in rain, snow, heat, ice or
  poor air, so that the weather never decides for me on the day.
- *Acceptance:* Given the slot's hourly forecast or air quality is bad, then the outdoor session
  becomes an indoor one or a rest day, the card says why, and the workout document is re-sent if
  still before the slot (C-2.3.6). The hourly forecast and Air Quality API extend
  `Storyworthtelling.Google` on the existing key (PC9).
- *Tests first:* `Storyworthtelling.Google.Tests/HourlyForecastClientTests`, `AirQualityClientTests`
  (fake HTTP); `WeatherSwapTests.RainMovesIndoors`, `PoorAirMovesIndoors`, `TheReasonIsWritten`.
- *Design / spec:* brief §Workout planning; p.12; TF §1.6, G10 · *Depends:* C-5.1.1 · **M · C · Tests · L3 · high**

**C-5.2.3 · Soreness**
- *Story:* As the owner, I want a margin note about soreness to change the plan around it until I say
  it's better, so that I'm never asked to train through it.
- *Acceptance:* Given a margin note read as soreness, then the plan works around the area, every page
  asks "Still sore?" until a blank (D28), and if it persists or the signals look alarming, the doctor
  suggestion prints once (D29). Only injury changes the plan's shape.
- *Tests first:* `SorenessTests.WorksAroundTheArea`, `BlankClearsIt`, `DoctorOnlyOnce`.
- *Design / spec:* brief §Workout planning; p.6; D28, D29 · *Depends:* C-2.2.1, C-5.1.1 · **M · C · Tests · L3 · high**

**C-5.2.4 · Recovery**
- *Story:* As the owner, I want a low HRV, short sleep or raised resting heart rate to swap the day
  to mobility, so that I recover instead of overreaching.
- *Acceptance:* Given under-recovery signals by the morning, then the session becomes mobility, on
  the card and Today's row; given no background read (R5), then the last values read while the app
  was open are used and Diagnostics notes the gap.
- *Tests first:* `RecoverySwapTests.UnderRecoveredBecomesMobility`, `StaleValuesAreUsedAndNoted`.
- *Design / spec:* brief §Workout planning · *Depends:* C-5.1.1, C-7.1.2 · **M · C · Device · L3 · high**

**C-5.2.5 · Trips and holidays**
- *Story:* As the owner, I want a work trip to keep my plan in the room or a hotel gym, and a family
  holiday to pause it, so that travel needs no input.
- *Acceptance:* Given a trip only the owner takes, then sessions continue as bodyweight in the room,
  outdoors, or the hotel gym if the calendar names a hotel and a lookup finds one; given a family
  holiday, then the plan pauses and the Workouts tab says so.
- *Tests first:* `TravelTests.WorkTripContinues`, `HotelGymOnlyWhenFound`, `FamilyHolidayPauses`.
- *Design / spec:* brief §Workout planning · *Depends:* C-5.1.1, C-5.3.1 · **M · C · Tests · L3 · high**

#### F-5.3 Calendar placement

- **Description:** the busy-time model and the placement engine, shared by workouts and meals.
- **User-facing behaviour:** events titled only "Workout · 40 min", "Cook: chili", "Groceries",
  "Start the slow cooker" appear where they fit, move when the owner moves them, and vanish when
  deleted.
- **Design / spec:** brief §Calendar; TF §1.6; R7, R8.
- **Risks:** R7, the dealbreaker; R9, the partner's share not done.

**C-5.3.1 · What counts as busy**
- *Story:* As either adult, I want the planner to see every reason I'm not free, so that nothing is
  ever placed on top of my life.
- *Acceptance:* Given the calendars, then busy is the person's own calendar, the family calendar, and
  children's events when that person is on pickup (a kids' event naming a parent blocks that parent;
  one naming nobody blocks both); never-placed time is work hours from Foreword's work schedule (the
  personal vault's `WorkScheduleJson`, a free WFH lunch gap allowed), family dinner and the children's
  bedtime routine (the bedtime setting).
- *Tests first:* `BusyTimeTests`, one per rule above, over an invented household's calendars.
- *Design / spec:* brief §Calendar · *Depends:* C-1.1.1, C-8.2.2 · **L · C · Tests · L3 · xhigh**

**C-5.3.2 · Placing events**
- *Story:* As the household, I want household events placed first and workouts around them, so that
  two planners never double-book.
- *Acceptance:* Given a week, then meals and shopping are placed before workouts; sweaty sessions get
  a shower buffer after; each event's title is clean and its hidden extended-property key never
  includes the date, so a moved event is found again; writes use the full `calendar` scope on primary
  calendars.
- *Tests first:* `PlacementTests.HouseholdEventsFirst`, `ShowerBufferAfterSweatySessions`,
  `TheKeyNeverIncludesTheDate`, `TitlesCarryNoKey`.
- *Design / spec:* brief §Calendar; TF §1.6 · *Depends:* C-5.3.1 · **M · C · Device · L3 · xhigh**

**C-5.3.3 · The owner's edits win, silently**
- *Story:* As either adult, I want a moved event to be the new time and a deleted one to mean
  skipped, with no question, so that the calendar is mine.
- *Acceptance:* Given an event moved in Google Calendar, then the plan takes the new time and never
  moves it back; given one deleted, then the session is skipped and not re-placed; no notice, no
  question.
- *Tests first:* `OwnerEditTests.AMoveIsTheNewTime`, `ADeleteIsASkip`, `NeverReplaced`, `NoNotice`.
- *Design / spec:* brief §Calendar · *Depends:* C-5.3.2 · **M · C · Tests · L3 · high**

**C-5.3.4 · The placement property suite**
- *Story:* As the owner, I want the placement rules proven against many invented weeks, so that
  calendar chaos is ruled out by test, not hope.
- *Acceptance:* Given a few thousand generated invented calendars (seeded), then no placement ever
  overlaps busy or never-placed time, no two planners' events overlap for one person, and a re-run
  on an unchanged week moves nothing.
- *Tests first:* `PlacementPropertyTests` (seeded generator, the seed printed on failure).
- *Design / spec:* R7, R8 · *Depends:* C-5.3.2, C-6.4.1 · **M · C · Tests · L3 · xhigh**

#### F-5.4 The Workouts tab

- **Description:** today's large clipping and the next seven days, with Skip / Swap.
- **User-facing behaviour:** as page 12 draws it, in its four states.
- **Design / spec:** [book p.12](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-12); D50–D52.
- **Risks:** a swap into a day without room.

**C-5.4.1 · Today, then seven days**
- *Story:* As the owner, I want today's session large and the next seven days small, each with why it
  changed, so that I can see the week at a glance.
- *Acceptance:* Given the plan, then today's clipping shows time, length, type, exercise count and
  kit, the Score setting and "on your tablet"; each later day shows day, time · length · type, the
  Score setting and a one-line reason if changed; a rest day is a bare clipping; taps open nothing;
  there is no history list, no "workout now" and no settings.
- *Tests first:* `WorkoutsTabModelTests.TodayCarriesTheKitAndWhereThePagesAre`, `ChangedDaysCarryAReason`,
  `NoHistoryNoWorkoutNow`.
- *Design / spec:* p.12 · *Depends:* C-5.1.1, C-3.2.3 · **M · C · Owner · L2 · medium**

**C-5.4.2 · The action card**
- *Story:* As the owner, I want a long-press on a day to offer Skip or Swap, so that changing a day
  is one gesture.
- *Acceptance:* Given a long-press, then the clipping takes the inset state and the Paper
  `ActionCard` rises over a full-screen PaperDeep haze, binder included, with exactly Skip this day
  and Swap with…, and no confirm dialog.
- *Tests first:* `WorkoutActionsTests.ExactlyTwoActions`; `NoConfirmDialogTests` (source scan).
- *Design / spec:* p.12; DESIGN.md §7, §8 · *Depends:* C-5.4.1 · **S · C · Owner · L2 · medium**

**C-5.4.3 · Swap with…**
- *Story:* As the owner, I want to swap a day only with a day that has room, and see why the others
  don't, so that a swap never breaks the calendar.
- *Acceptance:* Given Swap with…, then the same card turns into the other six days in place; a day
  without room is greyed with its reason ("no 40-minute gap"), never hidden (D51); picking one moves
  both sessions and their calendar events.
- *Tests first:* `SwapTests.ADayWithoutRoomIsGreyedWithItsReason`, `BothSessionsAndEventsMove`.
- *Design / spec:* p.12; D51 · *Depends:* C-5.4.2, C-5.3.2 · **M · C · Owner · L3 · high**

**C-5.4.4 · Skip, with undo**
- *Story:* As the owner, I want a skip to happen at once with six seconds to undo, so that I'm never
  asked "are you sure?".
- *Acceptance:* Given Skip this day, then the clipping strikes through ("Skipped · counted as a rest")
  and the pine undo dog-ear folds in for 6 s, outranking any notice; a tap restores the day; when the
  window closes, the calendar event is removed with no message.
- *Tests first:* `SkipUndoTests.RestoresWithinTheWindow`, `RemovesTheEventAfterIt`, `UndoOutranksNotices`.
- *Design / spec:* p.12, p.18; D77; DESIGN.md §13 · *Depends:* C-5.4.2, C-9.1.1 · **S · C · Owner · L2 · medium**

**C-5.4.5 · Move a workout here**
- *Story:* As the owner, I want to long-press a rest day and move a workout into it, so that I can
  rearrange the week from either end.
- *Acceptance:* Given a rest day's long-press, then the card offers "Move a workout here…" and lists
  the days whose session fits this day's gaps, greying the rest with reasons (D52).
- *Tests first:* `MoveHereTests.OffersOnlySessionsThatFit`.
- *Design / spec:* p.12; D52 · *Depends:* C-5.4.3 · **S · C · Owner · L2 · medium**

### E6 — Meals

- **Goal:** a household meal planner over the family's own recipe box, shared between the two
  installs through one merge-safe encrypted file, with scanning, the empty box counting to ten,
  the calendar's cook and shopping blocks, the weekly Keep note, and "I ate something else".
- **Business value:** all meals planned with barely any thought; meals feed the trend through
  nutrition (brief §Who it's for, §Meal planning).
- **Sources:** brief §Meal planning, §Meals, §"I ate something else", §Two installs;
  [book p.13](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-13),
  [p.14](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-14),
  [p.15](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-15); D53–D68, D74, D81, D82, D86, D90;
  TF §1.6–§1.8, §1.11.
- **Exit criteria:** two installs' writes converge; planning waits for ten cards; every brief rule
  for dinners, lunches, cooks, guests, holidays and groceries has a test; pages 13–15 match.
- **Depends on:** E1, E3, F-5.3 (placement), F-9.1, E11's Index and Plate contracts.

#### F-6.1 The household file

- **Description:** the shared, encrypted file both installs read and write (PC4, PC7).
- **User-facing behaviour:** a recipe scanned on either phone is in both boxes; the partner can plan
  while the owner's install is down.
- **Design / spec:** brief §Two installs; TF §1.11; D74.
- **Risks:** a write over an undecryptable file loses the household's box.

**C-6.1.1 · Extract `Storyworthtelling.Household` from Pulp**
- *Story:* As the owner, I want Constitution and Pulp to share one tested merge engine, so that a
  fix to one is a fix to both.
- *Acceptance:* Given Pulp's `HybridLogicalClock`, records with tombstones, `VaultEnvelope` sealing
  and the never-overwrite rule, when moved into a new `Storyworthtelling.Household` library, then
  Pulp re-points and every Pulp guard (including G11 and G14) passes unchanged, and the library has
  its own test project.
- *Tests first:* move Pulp's household tests into `Storyworthtelling.Household.Tests` first (they go
  red with no implementation), then `PulpHouseholdStillConvergesTests` in Pulp.
- *Design / spec:* PC4; TF §1.11 · *Depends:* — · **L · D · Tests · L3 · xhigh**

**C-6.1.2 · Constitution's record kinds and file**
- *Story:* As the owner, I want every kind of household record named once and frozen, so that a
  rename never orphans our recipes.
- *Acceptance:* Given `ConstitutionKinds` (recipe, week-plan, rating, taste-note, staple, person,
  planning-lease), and the file name `storyworthtelling-constitution-household.json`, then each is a
  pinned literal; growing the set is a test edit.
- *Tests first:* `ConstitutionKindsTests.KindsAreFrozen`, `FileNameIsFrozen`.
- *Design / spec:* TF §4 · *Depends:* C-6.1.1 · **S · D · Tests · L3 · high**

**C-6.1.3 · The planning lease**
- *Story:* As the partner, I want my install to take over planning only while the owner's is silent,
  and hand back when it returns, so that two planners never plan the same week.
- *Acceptance:* Given the owner's install heartbeating, then only it plans; given its heartbeat silent
  past the lease, then the partner's install takes the lease and plans; given the owner's install
  back, then the lease returns at the next week boundary.
- *Tests first:* `PlanningLeaseTests.OnlyTheHolderPlans`, `SilenceHandsItOver`, `ItReturnsAtTheWeekBoundary`,
  `TwoInstallsNeverPlanOneWeek` (interleaved writes).
- *Design / spec:* PC7; R8 · *Depends:* C-6.1.2 · **M · D · Tests · L3 · xhigh**

**C-6.1.4 · The roster, one writer**
- *Story:* As the owner, I want the household roster editable only on my install, with birthdates
  encrypted and never listed, so that our private facts stay private.
- *Acceptance:* Given the partner's install, then Household is read-only (D74); birthdates live only
  in the encrypted file and never appear in a list or Diagnostics.
- *Tests first:* `RosterTests.PartnerInstallIsReadOnly`, `BirthdatesNeverRender`.
- *Design / spec:* D74; brief §Two installs · *Depends:* C-6.1.2 · **S · D · Tests · L3 · high**

#### F-6.2 The recipe box

- **Description:** a pile of spread-out cards per cuisine (D56–D60).
- **User-facing behaviour:** titles show; a tap pulls a card out with its history and Cook this soon.
- **Design / spec:** [book p.13](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-13),
  [p.14](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-14); D53, D56–D60, D63, D64.
- **Risks:** piles that crowd or overlap unreadably at phone width.

**C-6.2.1 · Piles by cuisine, and search**
- *Story:* As either adult, I want the box as small piles by cuisine with a search under the
  switcher, so that I can find a card in a glance.
- *Acceptance:* Given the box, then a pure `PileLayout` places one pile per cuisine under a label
  strip, cards overlapping at loose angles with every title readable; search sits under the switcher
  (D59) and narrows the piles in place; nothing scrolls behind the action bar.
- *Tests first:* `PileLayoutTests.EveryTitleStaysReadable`, `SearchNarrowsInPlace`.
- *Design / spec:* p.13; D53, D56–D60 · *Depends:* C-6.1.2, C-3.2.1 · **M · D · Owner · L2 · medium**

**C-6.2.2 · Picking a card out**
- *Story:* As either adult, I want a tapped card to come out of its pile with its history, so that I
  can decide whether to cook it soon.
- *Acceptance:* Given a tap, then the card lies on top, tilted, leaving its gap, and shows "Last
  cooked 2 weeks ago · loved by all", who loved it, its source and time, and Cook this soon; tapping
  the pile puts it back. No tape (D57).
- *Tests first:* `CardHistoryTests.HistoryReadsInTheBriefsWords`.
- *Design / spec:* p.13 · *Depends:* C-6.2.1 · **M · D · Owner · L2 · medium**

**C-6.2.3 · Cook this soon**
- *Story:* As either adult, I want Cook this soon to put the dish in next week, with the card itself
  saying so, so that there's no toast and no doubt.
- *Acceptance:* Given Cook this soon, then the dish is placed in next week's plan and the card reads
  "Planned for next week"; no toast.
- *Tests first:* `CookSoonTests.PlacesItNextWeek`, `SuccessIsTheCardsOwnWords`.
- *Design / spec:* p.13; DESIGN.md §6 · *Depends:* C-6.2.2, C-6.3.1 · **S · D · Owner · L2 · medium**

**C-6.2.4 · Moving a card's cuisine**
- *Story:* As either adult, I want to long-press a card to move it to another cuisine, so that a
  silent misfile is easy to fix.
- *Acceptance:* Given a long-press on a card, then the action card offers the cuisines, and the move
  is undo-first.
- *Tests first:* `CuisineMoveTests.MoveIsUndoable`.
- *Design / spec:* D64 · *Depends:* C-6.2.2, C-9.1.1 · **S · D · Owner · L2 · medium**

**C-6.2.5 · The empty box, counting to ten**
- *Story:* As the household, I want the box to invite ten favourites before planning starts, so that
  the first plan is ours, not generic.
- *Acceptance:* Given fewer than ten cards, then Meals shows the Serif-italic invitation (not a voice
  seat), a mono "3 of 10" and ten places (filled and dashed); the primary action is Scan a recipe
  card; planning does not start; a Reference notice and the slate bookmark wait (D91).
- *Tests first:* `EmptyBoxTests.PlanningWaitsForTen`, `TheInvitationIsNotAVoiceSeat`.
- *Design / spec:* p.14; brief §First run · *Depends:* C-6.2.1, C-9.1.1 · **S · D · Owner · L2 · medium**

#### F-6.3 The meal planner

- **Description:** every meal of the week from the household's cards and taste.
- **User-facing behaviour:** the Thursday sheet, mostly loved dishes, one stretch, and
  healthier weeks pushed in as a whole.
- **Design / spec:** brief §Meal planning; p.7, p.13.
- **Risks:** a planned dish someone said "Never again" to.

**C-6.3.1 · The week's meals**
- *Story:* As the household, I want seven dinners, breakfasts, lunches and lunchboxes planned from our
  cards, so that nobody has to ask "what's for dinner?".
- *Acceptance:*
  - Given the box and taste profile, then seven dinners are mostly household cards plus one stretch
    dish (a card not cooked lately, D82, until the NYT spike says otherwise); healthier recipes are
    pushed into the week as a whole and balanced against workout load.
  - Given "Never again" from one person, then that dish is planned only when that person eats
    differently or is away.
  - Given a night the adults eat something the children dislike, then the children get a different
    meal.
  - Breakfast and lunch are uncooked, easy weekday prep, may differ by person; lunchboxes are a
    sandwich, fruit and a snack, plus a separate snack, no heating; no planned leftovers.
- *Tests first:* `MealPlannerTests`, one per rule above.
- *Design / spec:* brief §Meal planning; p.7 · *Depends:* C-6.1.2, C-6.2.5 · **L · D · Tests · L3 · high**

**C-6.3.2 · The taste profile**
- *Story:* As the household, I want the planner to learn from dislikes, ratings, denial reasons and
  recipe notes, so that each week suits us better without being asked.
- *Acceptance:* Given seed dislikes, ratings, a written denial or a recipe-margin note, then Claude
  decides whether it is a lasting lesson or a one-off, and the invisible profile updates per person;
  nothing about the profile is shown.
- *Tests first:* `TasteProfileTests.SeedsFromDislikes`, `AOneOffDoesNotStick` (fake Claude),
  `ProfileIsNeverRendered`.
- *Design / spec:* brief §Meal planning; D35 · *Depends:* C-6.3.1, C-2.2.1 · **M · D · Tests · L3 · high**

**C-6.3.3 · Rules-only meals**
- *Story:* As the household, I want a favourites-only week when Claude is away, so that Thursday's
  sheet still comes.
- *Acceptance:* Given Claude unavailable, then the week is built from favourites by the same rules
  and nothing on the sheet says so.
- *Tests first:* `MealFallbackTests.FavouritesWeekObeysTheRules`.
- *Design / spec:* brief §failure table · *Depends:* C-6.3.1 · **S · D · Tests · L2 · medium**

**C-6.3.4 · The NYT stretch dish (gated) — CLOSED UNBUILT (D96)**
- *Story:* As the household, I want one NYT recipe a week that suits us, if NYT allows it, so that we
  try something new.
- *Acceptance:* Built **only if** the spike (C-11.4.1) finds an official API or written permission
  (D82). Then: one stretch dish a week through that route only; a loved stretch dish joins the box.
  If the spike fails, this PBI closes unbuilt and the owner is told.
- *Tests first:* `NytStretchTests` against a fake of the permitted API; `NoScrapingTests` (no
  `cooking.nytimes.com` HTML fetch anywhere in source).
- *Design / spec:* D82; brief §Meal planning · *Depends:* C-11.4.1, C-6.3.1 · **M · D · Tests · L3 · high**

#### F-6.4 Calendar blocks and groceries

- **Description:** who cooks, when, and the shop.
- **User-facing behaviour:** "Cook: chili" ending at dinner on the cook's calendar, "Start the slow
  cooker" in the morning, a "Groceries" trip on that week's shopper's, and a Keep note once a week.
- **Design / spec:** brief §Meal planning, §Calendar; D81, D90.
- **Risks:** R9 (the partner's share).

**C-6.4.1 · Cooks, blocks and dinner time**
- *Story:* As the household, I want the cook chosen from our calendars and the cooking blocked so it
  ends at dinner, so that dinner fits the day.
- *Acceptance:*
  - Given the calendars, then dinner time is inferred, the cook is whoever is free, and the
    prep-and-cook block ends at dinner on the cook's primary calendar; a slow-cooker meal adds a
    morning "Start the slow cooker" event.
  - Given nobody clearly free, then the planner may pick a slow-cooker meal, the eating-out night or
    whoever is home first; tight evenings allow one eating-out night a week, beyond that a packable
    or slow-cooker dinner.
  - Given guests in the calendar, then the recipe is scaled; given a holiday, hosting or visiting is
    read from the calendar.
- *Tests first:* `CookAssignmentTests`, one per rule above.
- *Design / spec:* brief §Meal planning, §Calendar · *Depends:* C-5.3.2, C-6.3.1 · **L · D · Device · L3 · xhigh**

**C-6.4.2 · Groceries and the pantry**
- *Story:* As the household, I want one list by store section, staples reordered, and one tap to send
  it to Keep, so that shopping is a single trip.
- *Acceptance:*
  - Given the approved week, then the list is the plan's ingredients minus stock, by store section;
    staples marked "need more" reorder; an uncooked dinner's perishables are written off and its
    other ingredients assumed in stock.
  - Given approval, then a "Groceries" trip is blocked on one adult's calendar, alternating weeks
    unless one is busy, after approval and before Monday.
  - Given approval, then a Reference notice "This week's groceries are ready" waits with one button,
    Send to Keep, which opens Keep's share sheet with the list as a new note; nothing is read back (D81, D90).
- *Tests first:* `GroceryListTests.MinusStockBySection`, `StaplesReorder`, `UncookedWritesOffPerishables`;
  `ShoppingTripTests.AlternatesUnlessBusy`; `KeepHandOffTests.OneNoticeOneButton`.
- *Design / spec:* brief §Meal planning; D81, D90 · *Depends:* C-6.3.1, C-5.3.2, C-9.1.1 · **M · D · Owner · L3 · high**

**C-6.4.3 · The budget**
- *Story:* As the household planner, I want the week's estimate checked against Index's grocery
  budget, so that the sheet flags a week that runs over.
- *Acceptance:* Given Index's budget and spend (C-11.1.1), then the estimate is calibrated against
  actual spend and a week over budget is flagged on the sheet; with no broadcast yet, no flag.
- *Tests first:* `BudgetCalibrationTests.CalibratesToSpend`, `NoBudgetNoFlag`.
- *Design / spec:* brief §Meal planning · *Depends:* C-11.1.1, C-6.4.2 · **S · D · Tests · L2 · medium**

#### F-6.5 Adding a recipe

- **Description:** scan on either phone, confirm only the uncertain words (D63, D86).
- **User-facing behaviour:** as page 14 draws it: the picker grows from the bar, the scanned card
  with its hard words highlighted, one choice row per word.
- **Design / spec:** [book p.14](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-14); D62–D64, D86; PC8.
- **Risks:** the partner's camera loop departing from page 14's "Opens Plate" subtitle.

**C-6.5.1 · Scanning on the owner's phone (Plate's tray)**
- *Story:* As the owner, I want "Scan a recipe card" to open Plate's multi-page tray and come back
  with the pages, so that a long recipe is one scan.
- *Acceptance:* Given the owner's install, then the picker's scan row sends Plate's `READ_RECIPE`
  request (C-11.2.1) aimed at Plate's package, receives 1–N page images transiently, and returns to
  the confirm step.
- *Tests first:* `RecipeScanRequestTests.AimsAtPlateExplicitly`, `PagesAreTransient`.
- *Design / spec:* p.14; D86 · *Depends:* C-11.2.1 · **M · D · Device · L3 · high**

**C-6.5.2 · Scanning on the partner's phone (camera)**
- *Story:* As the partner, I want to photograph a card one page at a time with my camera, so that I
  can add recipes without Plate.
- *Acceptance:* Given the partner's install, then the scan row opens the system camera
  (`IMAGE_CAPTURE`) once per page with "Add a page" / "Done" between, then the confirm step. The row's
  subtitle on her install reads "One photo per page".
- *Tests first:* `CameraLoopTests.OnePhotoPerPage`, `SubtitleMatchesTheInstall`.
- *Design / spec:* p.14; D86 · *Depends:* C-6.5.3 · **S · D · Device · L2 · medium**

**C-6.5.3 · Read, confirm, keep only the words**
- *Story:* As either adult, I want only the hard-to-read words asked about, with the best reading
  already chosen, so that confirming takes seconds with the card still in hand.
- *Acceptance:* Given page images, then a Claude vision read returns the text and, per uncertain word,
  a best reading and two alternatives; the card shows with those words under a Caution highlighter
  wash and one choice row each (best pre-chosen, two alternatives, "Other…"); Looks right files it,
  Scan again reopens the scan; a clean scan still shows its card once (D63); cuisine is assigned
  silently (D64); **the images are dropped** after the read.
- *Tests first:* `RecipeReadTests.OnlyUncertainWordsAreAsked`, `CleanScanShowsOnce`,
  `ImagesAreNeverStored` (no image bytes in the household file or app storage after the read).
- *Design / spec:* p.14; D63, D64; brief §Meals · *Depends:* C-6.1.2 · **M · D · Owner · L3 · high**

**C-6.5.4 · The picker**
- *Story:* As either adult, I want Add a recipe to grow its choices in place from the bar, so that
  there's no dialog.
- *Acceptance:* Given Add a recipe, then the inline picker grows up out of the bar with the scan row;
  the paste row appears only if the NYT spike succeeded (PC8), with its mono field and teal status
  sentence ("Reading the recipe from NYT Cooking…").
- *Tests first:* `RecipePickerTests.PasteRowOnlyWithPermission`.
- *Design / spec:* p.14; D82, PC8 · *Depends:* C-6.5.1, C-6.5.2 · **S · D · Owner · L2 · medium**

#### F-6.6 "I ate something else"

- **Description:** one photo, an estimate inline, about the person's own intake only.
- **User-facing behaviour:** as page 15: Plate (or the camera) for one shot, straight back,
  "Estimating your dinner…", then "You had something else · about 900 kcal".
- **Design / spec:** [book p.15](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-15); D65–D67, D86.
- **Risks:** a photo with faces in it.

**C-6.6.1 · One photo, back to the card**
- *Story:* As either adult, I want one photo and nothing else, so that logging a different meal takes
  ten seconds.
- *Acceptance:* Given the action on Today or Meals, then one `IMAGE_CAPTURE` (aimed at Plate on the
  owner's phone, the system camera on hers) returns straight to the meal's card, which says
  "Estimating your dinner…" in the teal status voice, then "You had something else · about 900 kcal"
  inline (figure in mono), with no toast and no screen between. The planned dish stays as it is.
- *Tests first:* `AteSomethingElseFlowTests.NoScreenBetween`, `ResultIsInlineNoToast`.
- *Design / spec:* p.15; D65, D67 · *Depends:* C-4.1.4, C-6.6.2 · **S · D · Owner · L2 · medium**

**C-6.6.2 · The estimate**
- *Story:* As either adult, I want the estimate to be about my own intake at that meal, so that the
  numbers are mine alone.
- *Acceptance:* Given the photo, then a Claude vision estimate of kcal is recorded for this install's
  own person, for the meal inferred from the time (before 11 am breakfast, before 4 pm lunch, then
  dinner, D66); no faces, no picker (D65); the photo is discarded after the estimate; the estimate
  corrects the Nutrition signal on this install only.
- *Tests first:* `IntakeEstimateTests.MealComesFromTheTime`, `OnlyThisInstallsPerson`,
  `PhotoIsDiscarded`, `NeverEntersTheHouseholdFile`.
- *Design / spec:* p.15; D65, D66; brief §Body (nutrition) · *Depends:* C-7.1.4 · **S · D · Tests · L3 · high**

#### F-6.7 Meals, This week

- **Description:** the Meals tab's first view.
- **User-facing behaviour:** tonight's dinner on a large taped card, the approved week on one card.
- **Design / spec:** [book p.13](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-13); D54.
- **Risks:** none beyond the shell's.

**C-6.7.1 · This week**
- *Story:* As either adult, I want tonight and the approved week on two cards, so that the week is
  one glance.
- *Acceptance:* Given an approved week, then a §7 segmented switcher (This week / Recipe box, selected
  half in a Meals-steel wash) heads the tab; tonight's card shows the dish, cook and start time; the
  week card has a ruled line a day, today's in bold, with markers (slow cooker, out, new); "I ate
  something else" stays in the bar; tonight's dinner cannot be swapped.
- *Tests first:* `ThisWeekModelTests.TodayIsBold`, `NoSwapTonight`.
- *Design / spec:* p.13; D54 · *Depends:* C-6.3.1, C-3.2.3 · **M · D · Owner · L2 · medium**

### E7 — Body and the signal pages

- **Goal:** each adult's health trend, read passively from Health Connect and the bed, charted
  against their own past only, private to their install and kept forever in an encrypted backup.
- **Business value:** this is why Constitution exists ("see my health trend honestly"), and it
  ships first (Release B).
- **Sources:** brief §Body, §Body (screens), §First run; [book p.16](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-16),
  p.10, p.18; D44, D69, D70, PC3; TF §1.3, §1.10, R1, R5, R6.
- **Exit criteria:** every signal charts from install day; permissions ask per signal; no chart can
  carry a norm, score or good/bad colour; Body data cannot leave the install except to its own backup.
- **Depends on:** E1, E3, F-8.2 (vault), F-9.1, F-9.2.

#### F-7.1 Health Connect and the derived signals

- **Description:** read-only platform access, per signal, from install day.
- **User-facing behaviour:** the first time a chart opens, it asks for that signal and says it only
  reads.
- **Design / spec:** p.16 (permissions); TF §1.3.
- **Risks:** the rationale activity missing, so the request opens nothing; R5, R6.

**C-7.1.1 · The manifest, read-only**
- *Story:* As either adult, I want Constitution able to ask for Health Connect access and unable to
  write to it, so that it reads and never changes my health data.
- *Acceptance:* Given the merged manifest, then the permissions-rationale activity is declared
  (`VIEW_PERMISSION_USAGE` with the `HEALTH_PERMISSIONS` category), only read permissions appear, and
  no write permission does; the exact shape is checked against the platform docs during the E11 spike.
- *Tests first:* `HealthManifestTests.DeclaresTheRationaleActivity`, `ReadOnly` (reads the manifest
  attributes by content).
- *Design / spec:* TF §1.3; brief §Body · *Depends:* C-1.1.1, C-11.4.2 · **S · B · Device · L3 · high**

**C-7.1.2 · Reading the signals**
- *Story:* As either adult, I want weight and body composition, resting heart rate, HRV, steps,
  active minutes and sleep read from Health Connect from install day, so that the trend starts at once.
- *Acceptance:* Given a granted signal, then readings are taken through
  `Android.Health.Connect.HealthConnectManager` (no binding package) from the install date only, with
  no history permission; given a signal not yet granted, then nothing is read until its chart opens;
  the choices are saved in the personal vault (PC3).
- *Tests first:* `SignalReaderTests` over a fake reader seam: `StartsOnInstallDay`,
  `ReadsOnlyGrantedSignals`; `HealthChoicesTests.GoToThePersonalVault`.
- *Design / spec:* brief §Body (signals); TF §1.3; PC3 · *Depends:* C-7.1.1, C-8.2.2 · **L · B · Device · L3 · high**

**C-7.1.3 · Background reads**
- *Story:* As the owner, I want readings taken in the background where Android allows, so that the
  morning recovery check is current.
- *Acceptance:* Given `READ_HEALTH_DATA_IN_BACKGROUND` granted, then a Body sync worker reads
  overnight; otherwise values are refreshed each time the app opens and the gap is recorded in
  Diagnostics (R5). The worker's names are frozen.
- *Tests first:* `BodySyncTests.FallsBackToOpenTimeReads`, frozen-set entry in C-1.1.3.
- *Design / spec:* TF §1.3; R5 · *Depends:* C-7.1.2 · **M · B · Device · L3 · high**

**C-7.1.4 · Strength and nutrition**
- *Story:* As the owner, I want strength and calories charted too, from the pages and meals, so that
  the trend covers what I do and eat.
- *Acceptance:* Given done workout pages, then strength progress counts planned load as done; given
  planned meals, then estimated calories per person, corrected by "I ate something else" estimates.
- *Tests first:* `StrengthSignalTests.PlannedLoadCountsAsDone`, `NutritionSignalTests.DeviationCorrectsTheEstimate`.
- *Design / spec:* brief §Body (signals) · *Depends:* C-7.1.2, C-5.1.3, C-6.3.1 · **M · B · Tests · L3 · high**

#### F-7.2 The Sleep Number bed

- **Description:** the bed as sleep's stand-in when the watch reads zero (unofficial access, R1).
- **User-facing behaviour:** a source note ("From the Sleep Number bed. The watch read zero on those
  nights."), and "Waiting for the bed" when it is offline.
- **Design / spec:** p.16 (signal page, the bed offline); TF G12, R1.
- **Risks:** R1, unofficial endpoints.

**C-7.2.1 · The SleepIQ client**
- *Story:* As either adult, I want the bed's sleep used on nights the watch reads zero, so that sleep
  is charted even when I don't wear the watch.
- *Acceptance:* Given the household SleepIQ login from the shared vault, then the client signs in,
  maps each sleeper's bed side, and supplies a night's sleep only when the watch's is zero; every
  SleepIQ call goes through one class so a breaking change has one place to fix.
- *Tests first:* `SleepIqClientTests` (fake HTTP, invented responses): `SignsInWithTheVaultLogin`,
  `MapsTheSleepersSide`, `OnlyWhenTheWatchReadsZero`.
- *Design / spec:* brief §Body; TF G12 · *Depends:* C-8.2.1 · **M · B · Device · L3 · high**

**C-7.2.2 · Waiting for the bed**
- *Story:* As either adult, I want an offline bed to stop its line calmly and tell me where to fix it,
  so that a missing night never looks like bad sleep.
- *Acceptance:* Given no bed reading since a date, then the sleep line stops at the last reading with
  dotted space and "Waiting for the bed / Last reading Thursday night"; the Body sentence says so
  calmly; a Problem notice and the rose bookmark wait; the error itself is never voiced.
- *Tests first:* `BedOfflineTests.LineStopsAtTheLastReading`, `ErrorIsNotVoiced`.
- *Design / spec:* p.16 (the bed offline), p.18 · *Depends:* C-7.2.1, C-9.1.1 · **S · B · Owner · L2 · medium**

#### F-7.3 The Body screens

- **Description:** the Body tab and a signal's page.
- **User-facing behaviour:** as page 16 draws it, in its four states.
- **Design / spec:** [book p.16](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-16); D44, D69, D70.
- **Risks:** a chart colour or comparison sneaking in.

**C-7.3.1 · Ordering by what moved**
- *Story:* As the owner, I want the signal that moved most, worsening first, at the top, so that the
  honest news comes first.
- *Acceptance:* Given the window's changes, then signals order by size of movement with worsening
  first; a rising weight counts as worsening (D70).
- *Tests first:* `SignalOrderTests.WorseningFirst`, `RisingWeightIsWorsening`.
- *Design / spec:* p.16; D70 · *Depends:* C-7.1.2 · **S · B · Tests · L3 · high**

**C-7.3.2 · Honest charts**
- *Story:* As the owner, I want each chart to be a neutral line against my own past only, so that it
  never judges me.
- *Acceptance:* Given a window (Month default, Quarter, Year), then `ChartGeometry` draws one neutral
  ink line with a dot at the latest point and a faint dotted bridge across a missing day; no overlay,
  no target, no population norm, no score, no streak, no good/bad colour.
- *Tests first:* `ChartGeometryTests.MissingDaysAreDottedBridges`; `HonestChartTests` (guard: no
  chart or Body model member is named for a goal, norm, score, streak or target, and the painter uses
  only the neutral ink token).
- *Design / spec:* p.16; brief §Design rules · *Depends:* C-7.1.2 · **M · B · Tests · L3 · high**

**C-7.3.3 · The Body tab**
- *Story:* As the owner, I want the month in one sentence and a chart scrap per signal, so that the
  trend is a 30-second read.
- *Acceptance:* Given Body, then a segmented Month / Quarter / Year switch, the monthly voice seat on
  a taped note, then one doctor's-chart scrap per signal (grid, mono form head with signal, units and
  period, torn foot, tape) with the latest value in mono and the change in words; the rest scroll.
- *Tests first:* `BodyTabModelTests.ChangeIsInWords`, `FormHeadNamesSignalUnitsAndPeriod`.
- *Design / spec:* p.16; D44 · *Depends:* C-7.3.1, C-7.3.2, C-9.2.1, C-3.2.3 · **M · B · Owner · L2 · medium**

**C-7.3.4 · A signal's page**
- *Story:* As the owner, I want to tap a chart and see it larger with its own sentence, so that I can
  read one signal properly.
- *Acceptance:* Given a tap on a chart, then its page shows the larger chart with the window switch,
  the latest value and its date, the signal voice seat against the owner's own past, and plain source
  notes.
- *Tests first:* `SignalPageModelTests.CarriesValueDateAndSource`.
- *Design / spec:* p.16 · *Depends:* C-7.3.3 · **S · B · Owner · L2 · medium**

**C-7.3.5 · Asking per signal**
- *Story:* As either adult, I want each permission asked the first time its chart opens, so that I'm
  never faced with a wall of permissions.
- *Acceptance:* Given an ungranted signal's first open, then its chart scrap says what it needs and
  that it only reads, and the bar carries Allow in Health Connect; a revoked permission later becomes
  a Problem notice and that chart waits.
- *Tests first:* `PermissionAskTests.AsksOnFirstOpenOnly`, `RevokedBecomesAProblemNotice`.
- *Design / spec:* p.16; brief §Body · *Depends:* C-7.1.2, C-9.1.1 · **S · B · Device · L2 · medium**

**C-7.3.6 · The device reminder**
- *Story:* As the owner, I want the day after I skip the watch to be mentioned in the monthly
  sentence, so that I'm reminded without a nag.
- *Acceptance:* Given a device with no readings yesterday, then today's monthly sentence carries the
  reminder (voiced, or "No watch readings yesterday." neutral); it is not a seventh seat.
- *Tests first:* `DeviceReminderTests.OnlyTheDayAfter`, `NotASeventhSeat`.
- *Design / spec:* p.18; brief §Body · *Depends:* C-7.3.3, C-9.2.2 · **S · B · Tests · L2 · medium**

#### F-7.4 Privacy and the Body backup

- **Description:** Body stays on its install, kept forever, backed up encrypted.
- **User-facing behaviour:** a reinstalled phone gets its Body history back; the partner never sees
  the owner's, and the other way round.
- **Design / spec:** brief §Body, §Two installs; TF §1.11.
- **Risks:** a write over an undecryptable backup.

**C-7.4.1 · The encrypted Body file**
- *Story:* As either adult, I want my Body history backed up, encrypted, to my own Drive and restored
  to a fresh install, so that a reset never loses it.
- *Acceptance:* Given `storyworthtelling-constitution-body.json` in that account's Drive, then it is
  `VaultEnvelope`-sealed, written debounced, restored only onto an install with no Body history, and
  **never written over a file that cannot be decrypted**.
- *Tests first:* `BodyBackupTests.RestoresOnlyOntoAnEmptyInstall`, `NeverOverwritesAnUndecryptableFile`,
  `RoundTripsEverySignal`.
- *Design / spec:* brief §Body; TF §1.11 · *Depends:* C-7.1.2, C-6.1.1 · **M · B · Tests · L3 · xhigh**

**C-7.4.2 · Body never crosses**
- *Story:* As either adult, I want my Body data to stay on my install, so that the household file
  never carries it.
- *Acceptance:* Given every outbound path (household file, broadcasts, notifications, Diagnostics,
  logs), then none carries a Body value; only the private backup does.
- *Tests first:* `BodyStaysHomeTests` (a guard over the household record kinds and every outbound
  payload type: no Body field reachable).
- *Design / spec:* brief §Two installs · *Depends:* C-7.4.1 · **S · B · Tests · L3 · high**

**C-7.4.3 · No numbers on Flyleaf**
- *Story:* As the owner, I want no health number ever to reach Flyleaf's home, so that the shelf stays
  calm.
- *Acceptance:* Given any notification or bookmark Constitution posts, then it carries no number.
- *Tests first:* `NoNumbersLeaveTests.NoticesCarryNoDigits`.
- *Design / spec:* brief §Design rules; p.19 · *Depends:* C-9.1.3 · **XS · B · Tests · L2 · medium**

### E8 — Settings and the vault

- **Goal:** a plain Settings screen (pillars, equipment, household, connections, the vault
  passphrase in the clear, Diagnostics), first run, and every connection loaded from and saved to
  the vault.
- **Business value:** a factory reset costs one passphrase, not an afternoon (the vault's whole
  point).
- **Sources:** brief §Settings, §First run; [book p.17](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-17);
  D71–D75, D85, PC3; TF §1.11.
- **Exit criteria:** page 17 in both states; the vault section's wire name guarded; a fresh install
  restores every connection from one passphrase.
- **Depends on:** E1, E3.

#### F-8.1 The Settings screen

- **Description:** plain paper, the grey tab, one accordion pattern (D71).
- **User-facing behaviour:** as page 17.
- **Design / spec:** [book p.17](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-17); D71–D74.
- **Risks:** none beyond the shell's.

**C-8.1.1 · Pillars, equipment, passphrase, diagnostics**
- *Story:* As either adult, I want Settings plain and short, so that I can change a thing and leave.
- *Acceptance:* Given Settings, then accordion groups show Pillars (one switch each; Meals' muted
  caveat), Equipment as §7 checkbox rows (D72), the vault passphrase in a clear mono field (never
  masked, suite rule), and Export diagnostics as a quiet button at the foot; no crinkle, no scraps.
- *Tests first:* `SettingsModelTests.PassphraseFieldIsNotMasked`, `EquipmentIsCheckboxRows`.
- *Design / spec:* p.17; D71, D72 · *Depends:* C-3.1.1 · **M · A · Owner · L2 · medium**

**C-8.1.2 · Household**
- *Story:* As the owner, I want to keep the household's people, dislikes and bedtime here, so that the
  planner knows who it's feeding.
- *Acceptance:* Given Household, then each person shows an adult/child tag and dislikes in one muted
  line (a tap opens the person), the children's bedtime in mono, and Add a person; birthdates are kept
  encrypted and never shown in a list; on the partner's install the group is read-only (D74).
- *Tests first:* `HouseholdGroupTests.ReadOnlyOnThePartnersInstall`, `BirthdatesNeverListed`.
- *Design / spec:* p.17; D74 · *Depends:* C-8.1.1, C-6.1.4 · **M · A · Owner · L2 · medium**

**C-8.1.3 · Connections**
- *Story:* As either adult, I want a working connection to collapse to "Working ✓" and a broken one to
  say what to do, so that Settings only asks when it must.
- *Acceptance:* Given Health Connect, Sleep Number, reMarkable (and NYT only if the spike succeeds),
  then each follows `CredentialFieldState.Decide`: Working ✓, or Problem-rose text with one secondary
  button; a broken one also lights the dog-ear and the bookmark.
- *Tests first:* `ConnectionsTests.WorkingCollapses`, `BrokenShowsOneButtonAndRaisesANotice`.
- *Design / spec:* p.17; CLAUDE.md (credential health) · *Depends:* C-8.1.1, C-8.2.3, C-9.1.1 · **M · A · Owner · L2 · medium**

#### F-8.2 The vault

- **Description:** Constitution's shared section, the reused sections, and the personal vault.
- **User-facing behaviour:** one passphrase restores everything.
- **Design / spec:** D75, D85, PC3; TF §1.11; CLAUDE.md (Secrets vault).
- **Risks:** a per-install value in the shared vault restored onto the wrong phone.

**C-8.2.1 · `SecretsVault.Constitution`**
- *Story:* As the owner, I want Constitution's section pinned to its wire name, so that a rename never
  silently empties it.
- *Acceptance:* Given `SecretsVault.Constitution` with `[JsonPropertyName("Constitution")]` holding the
  Sleep Number login, then the wire-name test, written as the file on Drive (not a round trip), passes.
- *Tests first:* `Storyworthtelling.Voice.Tests/Secrets/VaultWireNamesTests.ConstitutionSection`.
- *Design / spec:* TF §1.11 · *Depends:* — · **S · A · Tests · L3 · high**

**C-8.2.2 · Restore and publish**
- *Story:* As either adult, I want every connection restored on launch and saved on change, so that a
  reset is a passphrase away.
- *Acceptance:* Given the passphrase, then on launch Constitution fills missing values from the shared
  vault (Sleep Number, reMarkable, Claude, Gemini, NYT if used) and the personal vault (Health Connect
  choices, the work schedule); on a change it publishes its section, never overwriting a vault it
  couldn't read; only the owning account publishes shared sections (`VaultOwnership`).
- *Tests first:* `ConstitutionVaultSyncTests.FillsOnlyWhatIsMissing`, `NeverPublishesOverAnUnreadVault`,
  `PersonalFactsStayPersonal`.
- *Design / spec:* D75, D85, PC3 · *Depends:* C-8.2.1 · **M · A · Device · L3 · high**

**C-8.2.3 · Credential health**
- *Story:* As either adult, I want a rejected key or login to show itself, so that a silent failure
  isn't a mystery.
- *Acceptance:* Given an auth rejection from SleepIQ, the reMarkable, Claude or Gemini, then the
  client reports it through `ICredentialHealthSink` under its id, and Settings reveals that field;
  throttling and network errors never do.
- *Tests first:* `CredentialHealthTests.OnlyAuthRejectionsFlag`.
- *Design / spec:* CLAUDE.md (credential health) · *Depends:* C-8.2.2 · **S · A · Tests · L2 · medium**

#### F-8.3 First run and Diagnostics

- **Description:** minimal onboarding, and an export that is safe to share.
- **User-facing behaviour:** sign in, passphrase, equipment; everything else asked when first needed.
- **Design / spec:** brief §First run; p.17.
- **Risks:** a Diagnostics export that leaks health values.

**C-8.3.1 · First run**
- *Story:* As either adult, I want first run to be three steps, so that I'm using the app in a minute.
- *Acceptance:* Given a fresh install, then it asks only for Google sign-in (calendar and Drive
  scopes), the vault passphrase and the equipment checklist; Health Connect, Sleep Number and the
  reMarkable are asked when first needed or restored from the vault.
- *Tests first:* `FirstRunTests.ThreeStepsOnly`.
- *Design / spec:* brief §First run · *Depends:* C-8.1.1, C-8.2.2 · **M · A · Device · L2 · medium**

**C-8.3.2 · Diagnostics, safe to share**
- *Story:* As the owner, I want a Diagnostics export that shows what went wrong without any health
  value or name, so that I can share it to get help.
- *Acceptance:* Given an export, then it carries counts, outcomes, document names and where they went,
  notice kinds and credential states; no reading, weight, calorie, name, birthdate or recipe text.
- *Tests first:* `DiagnosticsPrivacyTests.NoReadingsNamesOrRecipes` (renders an export from a seeded
  invented household and scans it).
- *Design / spec:* brief §Settings; CLAUDE.md (public-repo data rule) · *Depends:* C-8.1.1 · **S · A · Tests · L3 · high**

### E9 — Voice seats and the dog-ear

- **Goal:** the six voice seats as a closed list with neutral fallbacks that never hide, and the
  notices: one fold coloured by the most serious thing waiting, the Waiting page, and ordinary
  notifications on the partner's phone.
- **Business value:** nothing is pushed on the owner's phone and nothing is hidden; errors are never
  voiced (brief §Voice seats, notices and failure states).
- **Sources:** [book p.18](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-18); D76, D77, D83,
  D90, D91; brief §Voice seats; DESIGN.md §13, §14.
- **Exit criteria:** the seat list cannot grow without a test edit; each seat has its fallback; every
  notice kind maps to its role; page 18 matches.
- **Depends on:** E1, E3.

#### F-9.1 Notices

- **Description:** the fold, the Waiting page, and the partner's notifications.
- **User-facing behaviour:** as page 18: rose for errors, ochre for a low-confidence read, teal for a
  scan to check, slate for the box under ten and the Keep hand-off, pine for undo.
- **Design / spec:** p.18; D77, D83, D90, D91.
- **Risks:** a notice that outlives its cause.

**C-9.1.1 · The notice model**
- *Story:* As either adult, I want one fold that takes the colour of the most serious thing waiting,
  so that I know at a glance whether anything needs me.
- *Acceptance:* Given notices, then each kind maps to its role (Problem: reMarkable delivery, Health
  Connect revoked, the bed offline; Caution: a low-confidence read; In-progress: a scan to check;
  Reference: the box under ten, the Keep hand-off), the fold wears the most serious, an undo
  outranks every notice for its window and hands the corner back, and a notice goes when its cause
  does.
- *Tests first:* `ConstitutionNoticeTests.EachKindHasItsRole`, `MostSeriousWins`, `UndoOutranks`,
  `GoesWithItsCause`; reuses `NoticeSeriousnessTests`' ordering.
- *Design / spec:* p.18; D77; DESIGN.md §13 · *Depends:* C-1.1.1 · **M · A · Tests · L3 · high**

**C-9.1.2 · The Waiting page**
- *Story:* As either adult, I want the fold to open a page that says what happened and what to do, so
  that fixing anything is one tap.
- *Acceptance:* Given the fold turned, then the Waiting page lists notices newest first on the
  crinkled ground in the app's frame, each with a role bar, a relative date and at most one button; a
  low-confidence read shows two big chips ("18" / "13"), and a pick is the answer.
- *Tests first:* `WaitingPageModelTests.NewestFirst`, `AtMostOneButton`, `AChipIsTheAnswer`.
- *Design / spec:* p.18 · *Depends:* C-9.1.1, C-3.2.2 · **M · A · Owner · L2 · medium**

**C-9.1.3 · Notifications on the partner's phone**
- *Story:* As the partner, who doesn't use Flyleaf, I want the brief's short list as ordinary
  notifications, so that I hear about a scan to check or a broken connection.
- *Acceptance:* Given the brief's short list (a scan waiting, the box under ten, the three errors),
  then Constitution posts an ordinary notification on both phones, carrying its role as an extra
  (PC1) and no number; on the owner's locked phone the lockdown suppresses the alert so it shows only
  as Flyleaf's bookmark (E10).
- *Tests first:* `NoticeNotificationTests.OnlyTheShortList`, `CarriesTheRole`, `NoDigits`.
- *Design / spec:* D83, PC1; brief §Voice seats, notices · *Depends:* C-9.1.1 · **S · E · Device · L2 · medium**

#### F-9.2 The six voice seats

- **Description:** a closed list, facts in, a line out, a neutral fallback never hidden.
- **User-facing behaviour:** as page 18's table, voiced or neutral.
- **Design / spec:** p.18 (the seats table); DESIGN.md §14; CLAUDE.md (owner's voice).
- **Risks:** an error, or faith content, reaching a seat.

**C-9.2.1 · `VoicedOccasions`, closed**
- *Story:* As the owner, I want my voice in exactly six places and never on an error, so that it
  stays mine and stays calm.
- *Acceptance:* Given `VoicedOccasions`, then it holds exactly Today's top line, the rest-day line,
  Body's monthly sentence, a signal's sentence, the workout encouragement and the meal sheet's intro;
  each composes through `VoicedMessenger` and falls back to a plain Serif sentence from the same facts
  when there is no profile or key, never hidden; no error type can reach one.
- *Tests first:* `VoicedOccasionTests.ExactlySixSeats`, `ErrorsAreNeverVoiced`, `FallbackNeverHides`.
- *Design / spec:* p.18; brief §Voice seats · *Depends:* C-1.1.1 · **M · A · Tests · L3 · high**

**C-9.2.2 · Each seat's facts**
- *Story:* As either adult, I want each line built from the facts that matter there and nothing
  private or religious, so that the voice says the right thing.
- *Acceptance:* Given each seat, then a facts builder supplies only its facts (Today: workout and
  dinner; Body: the month's movement stated plainly, worsening included; a signal: against the
  owner's own past; encouragement: progression news; sheet intro: the week); no faith content; the
  page-18 fallbacks are reproduced exactly from invented facts.
- *Tests first:* `SeatFactsTests.FallbacksMatchPageEighteen`, `NoFaithContent` (a word-list guard
  over composed prompts), `BodyStatesWorseningPlainly`.
- *Design / spec:* p.18; brief §Design rules (faith) · *Depends:* C-9.2.1 · **M · A · Tests · L3 · high**

**C-9.2.3 · Composed once a day**
- *Story:* As the owner, I want each seat composed at most once per facts and day, so that the voice
  costs pennies and doesn't flicker.
- *Acceptance:* Given unchanged facts on the same day, then the stored line is reused; a change in
  facts composes again.
- *Tests first:* `SeatCacheTests.SameFactsSameDayReuse`.
- *Design / spec:* CLAUDE.md (Index's seat rule) · *Depends:* C-9.2.2 · **S · A · Tests · L2 · medium**


## 5. Epics E10–E13

### E10 — Flyleaf: the second suite shelf and the bookmark

- **Goal:** Constitution's book stands cover-out on Flyleaf's suite shelf, the wrapped second row
  stands on its own full ledge, and the bookmark ribbon takes the notice's role colour.
- **Business value:** the owner reaches Constitution from the locked phone's only home, and sees
  something waiting without a push.
- **Sources:** brief §Phone screens, §Notices; [book p.19](https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb#page-19);
  D78, D79, D83, D91, PC1, PC2; TF §1.9, §1.10, G9.
- **Exit criteria:** page 19 in both states; the cover guard covers Constitution; the ribbon shows
  rose and slate from Constitution's notices; the watch and scale apps run under the lockdown.
- **Depends on:** E1 (cover art and cloth), F-9.1 (notices).

#### F-10.1 The book and the shelf

- **Description:** a sixth sibling on the cover-out suite shelf, wrapping to a second row.
- **User-facing behaviour:** scrolling into the tray, three books a row, Constitution on the second
  row's own ledge.
- **Design / spec:** p.19; D78, D79; TF §1.9.
- **Risks:** a missing `CoverResources` entry silently shows the launcher icon instead (as happened to
  Foreword and Folio).

**C-10.1.1 · Constitution's book**
- *Story:* As the owner, I want Constitution's approved cover on the suite shelf, so that it reads as
  one of the family.
- *Acceptance:* Given Flyleaf, then `HomeActivity`'s `CoverResources` maps
  `PolicyContext.ConstitutionPackage` to `cover_constitution`, `BookClothPalette` binds it to
  `DesignTokens.Identity.Constitution.Cloth`, and the shelf draws the cloth, ink rule, flourishes and
  title band around the window art (D12).
- *Tests first:* `FrozenInvariantsTests.EverySiblingHasACoverResource` extended to Constitution
  (red first), `BookClothPaletteTests.ConstitutionBindsItsCloth`.
- *Design / spec:* p.19, p.3; D12, D79 · *Depends:* C-1.3.3, C-1.1.2 · **S · A · Owner · L2 · medium**

**C-10.1.2 · Every row on its own ledge**
- *Story:* As the owner, I want the wrapped second row to stand on a full ledge, so that no book ever
  floats.
- *Acceptance:* Given more siblings than fit a row, then every wrapped row's ledge rectangle from
  `BookshelfLayout.PlaceBookRows` is painted at full weight (lit top face and front edge), as the first
  row's is.
- *Tests first:* `BookshelfLayoutTests.EveryWrappedRowHasALedge` (already true; pinned), and
  `ShelfSceneTests.WrappedLedgesArePaintedAtFullWeight` on the pure scene description.
- *Design / spec:* p.19; D78 · *Depends:* C-10.1.1 · **S · A · Device · L2 · medium**

**C-10.1.3 · Body's sources run under the lockdown**
- *Story:* As the owner, I want my watch's and scale's apps cleared by the lockdown, so that their
  readings reach Health Connect and Body isn't empty.
- *Acceptance:* Given the package ids the owner supplies (§10), then `PolicyContext.AllowlistedPackages`
  carries them and `PolicyManager` un-suspends them like the existing four; they stay out of the tray
  unless the owner shelves them.
- *Tests first:* `PolicyContextTests.BodySourcesAreAllowlisted`, `AppCategorizerTests.ClearsTheBodySources`.
- *Design / spec:* TF §1.10, R6 · *Depends:* owner's package ids · **S · B · Device · L3 · high**

**C-10.1.4 · Google Keep runs under the lockdown**
- *Story:* As the owner, I want Keep runnable on the locked phone, so that "Send to Keep" opens
  something.
- *Acceptance:* Given Keep's package (confirmed on the device), then it is allowlisted the same way
  and the share intent from C-6.4.2 resolves to it; if the owner declines Keep on the locked phone,
  the hand-off notice is shown on the partner's install only.
- *Tests first:* `PolicyContextTests.KeepIsAllowlisted`; `KeepHandOffTests.FallsToThePartnersInstall`.
- *Design / spec:* D81, D90; R13 · *Depends:* C-6.4.2 · **XS · D · Device · L2 · medium**

#### F-10.2 The bookmark

- **Description:** Flyleaf's existing ribbon at the spine edge, coloured by role.
- **User-facing behaviour:** a slate ribbon for a scan to check or the box under ten, rose for the
  three errors; never a count.
- **Design / spec:** p.19; D79, D83, D91; PC1, PC2.
- **Risks:** the current importance-only mapping (high → Problem, low → Resolved, else In-progress)
  can't show slate.

**C-10.2.1 · The ribbon reads the role**
- *Story:* As the owner, I want the ribbon's colour to say what kind of thing is waiting, so that I
  know whether to stop.
- *Acceptance:* Given a Constitution notification carrying the role extra (C-9.1.3), then
  `TileNotificationListener` forwards the role and `NotificationHue` maps it (Problem → rose,
  Reference → slate); a notification with no role keeps today's importance mapping, so other apps are
  unchanged.
- *Tests first:* `NotificationHueTests.RoleExtraWins`, `NoRoleKeepsTheImportanceMapping`; a
  contract parity guard on the extra's key in both apps.
- *Design / spec:* p.19; D91; PC1, PC2 · *Depends:* C-9.1.3 · **S · E · Device · L3 · high**

**C-10.2.2 · No count, at the spine**
- *Story:* As the owner, I want the ribbon to carry no number and sit at the spine edge, so that the
  home stays numberless.
- *Acceptance:* Given a lit bookmark, then it is drawn at the cover-out book's left edge (D79) with no
  count or text.
- *Tests first:* `BookmarkTests.NoCountIsEverDrawn` (pins existing behaviour).
- *Design / spec:* p.19; D79; brief §Design rules · *Depends:* C-10.2.1 · **XS · E · Tests · L1 · low**

### E11 — Cross-app contracts and the spikes

- **Goal:** the Index budget broadcast, Plate's `READ_RECIPE`, the Score tempo pin, and four spikes
  in Release A that turn the riskiest unknowns into facts before the code depends on them.
- **Business value:** every contract that can fail silently is pinned on both sides, and the
  device-only risks are met in week one instead of at the end.
- **Sources:** brief §Meal planning, §Out of scope (risks); D25, D82, D86; TF §1.3, §1.4, §1.7,
  §1.8, §1.12, G7, G12; R1, R3.
- **Exit criteria:** each contract's action, permission, component and payload is equal on both
  sides by guard; each spike has a written outcome in this folder, and the PBIs it gates are updated.
- **Depends on:** E1.

#### F-11.1 Index → Constitution: the grocery budget

- **Description:** the first contract where Index is the sender.
- **User-facing behaviour:** the meal sheet flags a week over budget.
- **Design / spec:** brief §Meal planning (Budget); TF §1.12.
- **Risks:** two separately installed apps; a payload drift.

**C-11.1.1 · The budget contract, both sides**
- *Story:* As the household planner, I want Index to tell Constitution the month's grocery budget and
  spend, so that the plan's cost is honest.
- *Acceptance:* Given Index's Groceries category (`PlaidCategoryMap`'s `FOOD_AND_DRINK_GROCERIES`),
  then Index sends an explicit broadcast to `com.storyworthtelling.constitution.BudgetReceiver`,
  protected by the signature permission `com.storyworthtelling.constitution.RECEIVE_BUDGET`, carrying
  month, budget and spent so far as exact decimal text; Constitution stores the latest per month.
- *Tests first:* contract guards on both sides (`Ledger.Tests/Handoff/BudgetContractTests`,
  `Constitution.Tests/Contracts/BudgetContractTests`) reading each other's constants by content;
  `BudgetPayloadTests.DecimalsAreExactText`.
- *Design / spec:* TF §1.12 · *Depends:* C-1.1.1 · **M · D · Device · L3 · xhigh**

**C-11.1.2 · When Index sends**
- *Story:* As the household planner, I want the budget sent when it changes, so that Thursday's sheet
  sees this week's spend.
- *Acceptance:* Given a refresh that changes the month's grocery spend or budget, then Index sends;
  given nothing changed, then it doesn't; Constitution absent is the ordinary case (no outbox, Post →
  Index's rule).
- *Tests first:* `BudgetSendPlanTests.SendsOnlyOnChange`, `AbsentReceiverIsOrdinary`.
- *Design / spec:* TF §1.12 · *Depends:* C-11.1.1 · **S · D · Tests · L2 · medium**

#### F-11.2 Constitution → Plate: `READ_RECIPE`

- **Description:** a third read action in Plate, owner's phone only (D86).
- **User-facing behaviour:** "Scan a recipe card" opens Plate's tray for as many pages as it takes
  and returns.
- **Design / spec:** p.14; D86; TF §1.8, G7.
- **Risks:** Plate compiles here only while dl.google.com is reachable (CLAUDE.md), so CI may be the
  only compiler for the Plate half.

**C-11.2.1 · Plate's recipe read**
- *Story:* As the owner, I want Plate to scan a recipe across pages and hand the pages back, so that
  Constitution can read it and keep only the words.
- *Acceptance:* Given `READ_RECIPE` from an explicit caller, then Plate opens its `ScanTray` for 1–N
  pages, returns the page images to the caller transiently, keeps none of them (no Darkroom entry), and
  is listed in `Errands` with its intent filter; Constitution's request constants equal Plate's.
- *Tests first:* `Codex.Tests/Plate/ReadRequestTests.RecipeIsAReadAction`, `ErrandsTests.RecipeIsDeclared`
  (reads the `[IntentFilter]` attributes), `RecipeReadKeepsNothing`; the contract parity guard both
  sides.
- *Design / spec:* p.14; D86; TF §1.8 · *Depends:* C-1.1.1 · **M · D · Device · L3 · high**

#### F-11.3 Score: the printed tempo

**C-11.3.1 · The printed band is Score's band**
- *Story:* As the owner, I want the band name on my page to be the one Score's dial shows, so that I
  set the dial without thinking.
- *Acceptance:* Given any bpm, then the printed name equals `TempoBands.Of(bpm)` and the names are
  Score's `Names` (Easy / Steady / Hard); there is no cross-app call.
- *Tests first:* `TempoPrintTests.BandNameIsScoresOwn` across 90–180 bpm.
- *Design / spec:* D25; TF §1.12 · *Depends:* C-2.3.2 · **XS · C · Tests · L1 · low**

#### F-11.4 The Release A spikes

- **Description:** four time-boxed investigations, each ending in a written outcome
  (`Constitution/spikes/<name>.md`, invented data only) and an update to the PBIs it gates.
- **User-facing behaviour:** none; the risks are met early.
- **Design / spec:** TF §6; R1, R3, R5, R12.
- **Risks:** a spike that finds "no" late; this is why they're in Release A.

**C-11.4.1 · NYT: an official route, or none** — *outcome: PERMISSION NEEDED, treated as NO; see `spikes/nyt-recipes.md`, D96*
- *Story:* As the household, I want to know early whether NYT will allow its recipes in our planner,
  so that the box and the sheet are built for the answer.
- *Acceptance:* Given developer.nytimes.com and NYT's terms, then the spike records whether any
  official API covers recipes; it drafts a written-permission request for the owner to send (§10);
  the outcome decides PC8 and C-6.3.4. No NYT page is fetched by code during the spike.
- *Tests first:* none (an investigation); its outcome file is checked into the folder.
- *Design / spec:* D82; TF §1.7 · *Depends:* — · **S · A · Owner · L3 · high**

**C-11.4.2 · Health Connect on the real phone**
- *Story:* As the owner, I want to see one signal read on my phone before Body is built on it, so that
  the platform's silent traps are found first.
- *Acceptance:* Given a throwaway build of the head with one signal, then the spike confirms the
  rationale-activity shape that makes the permission request open, whether background reads are
  allowed on this platform version, and the install-day floor; the findings correct C-7.1.1–C-7.1.3.
- *Tests first:* the manifest guard of C-7.1.1 is written from the spike's findings.
- *Design / spec:* TF §1.3; R5 · *Depends:* C-1.1.1 · **S · A · Device · L3 · high**

**C-11.4.3 · SleepIQ against the household's bed**
- *Story:* As the owner, I want the unofficial bed endpoints proven on our account first, so that we
  know whether the bed's chart is real.
- *Acceptance:* Given the household login from the vault (never written to the repo, logs or the
  spike file), then the spike records which calls sign in, list sleepers and return a night, in shape
  only; the client in C-7.2.1 is written to that shape.
- *Tests first:* invented fixtures in the shape recorded.
- *Design / spec:* TF G12; R1 · *Depends:* C-8.2.1 · **S · A · Device · L3 · high**

**C-11.4.4 · Does the ink come back?**
- *Story:* As the owner, I want proof that the cloud's PDF carries my pen marks, so that the whole
  ink design isn't built on an assumption.
- *Acceptance:* Given a page with the page-4 targets delivered to each tablet and marked by hand, then
  the spike downloads it and records whether ticks, circles, a number and the "No, because" field come
  back, and how the vision read scores them; a "no" goes to the owner before Release C starts.
- *Tests first:* none (an investigation).
- *Design / spec:* p.4; R3; TF §1.4 · *Depends:* C-2.1.2 · **S · A · Device · L3 · high**

### E12 — CI and the frozen invariants

- **Goal:** the workflow, the version band, and the standing guards that keep the public repository
  clean and the frozen set frozen.
- **Business value:** CI minutes are the budget, and the repository is public: both are protected by
  machinery rather than care.
- **Sources:** CLAUDE.md (CI minutes, public-repo rule); TF §1.13, §4.
- **Exit criteria:** the workflow runs tests on `claude/**` and builds signed on `main`; the checks
  are fatal; the guards in §6 exist and were each watched failing.
- **Depends on:** C-1.1.1.

#### F-12.1 The workflow

**C-12.1.1 · `build-constitution.yml`**
- *Story:* As the owner, I want Constitution built, checked and published like its siblings, so that
  my phone gets it through the update channel.
- *Acceptance:* Given the workflow (from `build-pulp.yml`), then the test job runs on `main` and
  `claude/**`; the signed build runs on `main` and Run workflow with JDK 21 and the shared keystore;
  APP ID, ICON and FONT (all eight) checks are fatal; versionCode comes from the shared action at band
  10; the rolling-release step publishes to `apps-latest`; branch runs cancel in-flight; artifact
  **Constitution-Android**.
- *Tests first:* `Storyworthtelling.Updates.Tests/WorkflowTests.ConstitutionWorkflowHasItsChecks`,
  the existing "every workflow names a real folder" guard.
- *Design / spec:* §2.3; TF §1.13 · *Depends:* C-1.1.1 · **S · A · Tests · L1 · low**

**C-12.1.2 · Band 10**
- *Story:* As the owner, I want Constitution's version codes in their own band, so that no other app's
  update is ever refused because of it.
- *Acceptance:* Given `SuiteVersionBands`, then it carries `new("com.storyworthtelling.constitution", 10, 0)`
  and no other app uses band 10.
- *Tests first:* `SuiteVersionBandsTests.BandsAreUnique`, `ConstitutionIsBandTen`.
- *Design / spec:* TF §1.13 · *Depends:* — · **XS · A · Tests · L1 · low**

#### F-12.2 Standing guards

**C-12.2.1 · No keys in source**
- *Story:* As the owner, I want a guard that fails on any key-shaped string under `Constitution/`, so
  that a key never reaches the public repository.
- *Acceptance:* Given `Constitution/`, then no key-shaped literal, token, password or passphrase
  appears in source, config or fixtures; the guard proves it scanned files.
- *Tests first:* `NoKeysTests` (Pulp's pattern, non-vacuous).
- *Design / spec:* CLAUDE.md (keys and tokens) · *Depends:* C-1.1.1 · **S · A · Tests · L2 · medium**

**C-12.2.2 · Invented data only**
- *Story:* As the owner, I want fixtures and docs held to the invented household, so that no real
  name, address, number or reading slips into the public repository.
- *Acceptance:* Given every test fixture and doc under `Constitution/`, then email addresses are
  `example.com`, phone numbers are `555-01xx`, and people come from the book's invented household; a
  failure names the file and line. (A guard cannot know real values, so it catches the shapes; review
  catches the rest.)
- *Tests first:* `InventedDataTests` (shape scan, non-vacuous).
- *Design / spec:* CLAUDE.md (This repository is public) · *Depends:* C-1.1.1 · **S · A · Tests · L3 · high**

**C-12.2.3 · CLAUDE.md learns Constitution**
- *Story:* As a future contributor, I want CLAUDE.md to carry Constitution's commands, frozen set and
  unverified list, so that the next phase starts from facts.
- *Acceptance:* Given the phase's end, then CLAUDE.md has a Constitution section (test and build
  commands, the frozen identifiers, the contracts, the not-yet-verified list), DESIGN.md §3/§19 are
  current, and the FONT-check note covers the new workflow.
- *Tests first:* none (documentation); the phase review checks it.
- *Design / spec:* CLAUDE.md (Overview) · *Depends:* all releases · **S · F · Owner · L2 · medium**

### E13 — On-device verification

- **Goal:** prove on the owner's two phones and two tablets what tests against fakes cannot.
- **Business value:** "built and tested against fakes" is not "works" (TF §6).
- **Sources:** TF §6; §11 below.
- **Exit criteria:** every check in §11 has a recorded result; a failure reopens its PBI.
- **Depends on:** Releases A–E.

#### F-13.1 The device test plan

**C-13.1.1 · The test plan document**
- *Story:* As the owner, I want a step-by-step test plan I can run with the phones and tablets, so
  that each check brings back a number, not an impression.
- *Acceptance:* Given §11, then a test plan (in the owner's Drive, like Flyleaf's; nothing real in the
  repo) lists each check's steps, what to record, and the PBI it reopens on a failure.
- *Tests first:* none (a document).
- *Design / spec:* §11 · *Depends:* Releases A–E · **S · F · Owner · L2 · medium**

**C-13.1.2 · Run the checks**
- *Story:* As the owner, I want each device check run and recorded, so that P13 is done when it
  works, not when it compiles.
- *Acceptance:* Given the test plan, then each §11 item is run with the owner and recorded
  (pass, fail and the PBI reopened); the first real week's cost is measured against R11's estimate.
- *Tests first:* none (verification).
- *Design / spec:* §11; R11 · *Depends:* C-13.1.1 · **L · F · Device · L3 · high**

---

## 6. Frozen identifiers and the guard ledger

| Id | Fails when | Where | PBI |
|---|---|---|---|
| G1 | the app id, `MainActivity`'s name, a worker or work name, or a preference key moves | `Constitution.Tests/Guard/ConstitutionInvariantsTests` | C-1.1.1, C-1.1.3 |
| G2 | Flyleaf's `ConstitutionPackage` drifts from the app id | `Codex.Tests/Guard/FrozenInvariantsTests` | C-1.1.2 |
| G3 | the identity ladder differs between the four files, or crowds a role or sibling below 20° | `TokenParityTests`, `DesignTokensTests`, `IdentitySeparationTests` | C-1.2.1 |
| G4 | a hex or type-size literal appears outside the token files | `HexLiteralLeakTests`, the composer no-literal guard | C-2.1.1, C-3.2.1 |
| G5 | the vault section's wire name changes | `VaultWireNamesTests.ConstitutionSection` | C-8.2.1 |
| G6 | a household record kind or the household file name changes | `ConstitutionKindsTests` | C-6.1.2 |
| G7 | the workflow loses a fatal check or band 10 | `WorkflowTests`, `SuiteVersionBandsTests` | C-12.1.1, C-12.1.2 |
| G8 | the painter and the ink reader stop sharing `InkTargets` | `InkTargetsTests` | C-2.1.2 |
| G9 | an ink rule (D17, D18, silence) changes | `InkResolverTests` | C-2.2.1 |
| G10 | a document carries meaning by colour | `DocumentGreyscaleTests` | C-2.1.3 |
| G11 | a seventh voice seat appears, or an error reaches one | `VoicedOccasionTests` | C-9.2.1 |
| G12 | a Body chart can carry a norm, score, streak, target or good/bad colour | `HonestChartTests` | C-7.3.2 |
| G13 | Body data reaches any outbound path but its own backup | `BodyStaysHomeTests` | C-7.4.2 |
| G14 | a store writes over a file it cannot decrypt | `BodyBackupTests`, `Storyworthtelling.Household.Tests` | C-7.4.1, C-6.1.1 |
| G15 | two installs plan one week, or interleaved writes fail to converge | `PlanningLeaseTests`, household tests | C-6.1.3, C-6.1.1 |
| G16 | a placement overlaps busy time or double-books | `PlacementPropertyTests` | C-5.3.4 |
| G17 | Health Connect gains a write permission or loses its rationale activity | `HealthManifestTests` | C-7.1.1 |
| G18 | the budget contract drifts on either side | `BudgetContractTests` (both sides) | C-11.1.1 |
| G19 | the `READ_RECIPE` contract drifts, or Plate keeps a recipe page | `ReadRequestTests`, `ErrandsTests`, parity guard | C-11.2.1 |
| G20 | the bookmark role extra drifts between Constitution and Flyleaf | `NotificationHueTests`, parity guard | C-10.2.1 |
| G21 | the printed tempo band differs from Score's | `TempoPrintTests` | C-11.3.1 |
| G22 | a recipe image, or a meal photo, is stored | `RecipeReadTests.ImagesAreNeverStored`, `IntakeEstimateTests.PhotoIsDiscarded` | C-6.5.3, C-6.6.2 |
| G23 | a key-shaped literal, or a non-invented contact shape, appears under `Constitution/` | `NoKeysTests`, `InventedDataTests` | C-12.2.1, C-12.2.2 |
| G24 | a correlation appears outside the digest | `CorrelationsOnlyInTheDigestTests` | C-2.6.2 |
| G25 | an NYT page is scraped | `NoScrapingTests` | C-6.3.4 |
| G26 | a sibling has no cover resource | `FrozenInvariantsTests.EverySiblingHasACoverResource` | C-10.1.1 |

Each guard's first commit records its red run and names the bug it would let back in. **A guard
that passes against an empty input is vacuous**, so each asserts it found something to check.

**Verification, every round** (the orchestrator, not an agent):

```
dotnet test Constitution/Constitution.Tests/Constitution.Tests.csproj
dotnet test FocusPhone/Codex.Tests/Codex.Tests.csproj
dotnet test Storyworthtelling.Voice.Tests/Storyworthtelling.Voice.Tests.csproj
dotnet test Storyworthtelling.Paper.Tests/Storyworthtelling.Paper.Tests.csproj
dotnet test Storyworthtelling.Updates.Tests/Storyworthtelling.Updates.Tests.csproj
dotnet test Storyworthtelling.Household.Tests/Storyworthtelling.Household.Tests.csproj   # from Release D
dotnet test Pulp/Pulp.Tests/Pulp.Tests.csproj                                             # Release D (extraction)
dotnet test Ledger/Ledger.Tests/Ledger.Tests.csproj -p:LedgerTfms=net10.0                  # Releases A (tokens), D
dotnet test Tools/IconStudio.sln                                                           # Release A
FocusPhone/scripts/setup-android-build.sh                                                  # once per session
dotnet build Constitution/Constitution.Android/Constitution.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false
```

Then the branch's Actions runs are checked after every push and red runs fixed here.

**Definition of done.** Per release: guards red-then-green; suites green and the head compiled
locally; CI green; an L3 review of every L1/L2 diff; each screen and page matched 1:1 or the gap
reported. Per phase: an independent read-only review of the whole diff (correctness, vacuous guards,
privacy by file and line, doc accuracy) with its blockers fixed; CLAUDE.md and DESIGN.md updated
(C-12.2.3); one PR on the repo's template (D89).

## 7. Data and privacy

- **The repository is public.** Every fixture, mockup, doc and screenshot uses the book's invented
  household (Sam, Lee, Robin, Juniper) and invented dishes, figures and readings. No real name,
  birthdate, reading, recipe, calendar entry, address or account goes into a commit (G23).
- **Health data never lives in the repo.** Body readings live on the device and in each account's
  encrypted Body file (`VaultEnvelope`, never overwritten when unreadable). The household's recipes,
  plans, taste, pantry and roster live in the encrypted household file. Both go through
  `IDriveTextFile`.
- **Workouts and Body never cross installs** (G13); Meals is the only shared pillar.
- **Images are transient.** A scanned recipe keeps only its words; a meal photo keeps only its
  estimate (G22). Exercise illustrations hold no household data and are runtime art, never committed.
- **Keys and logins** live in the vault (Sleep Number in `SecretsVault.Constitution`; the reMarkable,
  Claude and Gemini keys in their shared sections); per-install facts live in the personal vault (PC3).
  The vault passphrase is entered by hand on each phone and never stored, logged or written by this
  plan or its code.
- **Diagnostics** carry shapes and outcomes, never values or names (C-8.3.2). Spike files record
  shapes only (C-11.4.3).
- **Birthdates** stay encrypted and are never listed (C-6.1.4, C-8.1.2).
- **No health number reaches Flyleaf** (C-7.4.3).
- **If personal data lands in a commit**, say so at once; rewriting history is the owner's decision
  (CLAUDE.md).

## 8. Accessibility and greyscale (the documents)

- **One render, both tablets.** Every document reads on the rM2's greyscale: lines, dots, gaps,
  ratings and states are ink weights, outlines and words; the only colour is the muted `#476F83`
  header rule, which says nothing (p.4; G10).
- **Targets in muted ink** so the pen's black always stands out; nothing printed inside a target; at
  least 8 pt clear around each (D16).
- **Sizes under the pen:** large tick 28 pt (9.9 mm on the rM2, about 11.3 mm on the Paper Pro), standard
  18 pt with 26 pt rows, circle lanes 84 pt (68 pt for four), number box 96 × 40 pt (D13–D16; TF §1.4).
- **Read from a distance:** the dose at 54 pt (the Dose rung) for a tablet propped by the mat; step
  times at the Title rung in Cascadia for the counter (p.5, p.8).
- **Shaded fields survive greyscale** (the "No, because" field, the "Still sore?" band) as light grey
  (D32, p.6).
- **Words, not colours:** easy / right / hard, Loved / Fine / Never again, every change "in words".
- **Fixed positions:** the rating and Done tick at the same foot of every exercise page (D23).
- **The phone:** Paper & Ink's AA contrast and the 20° hue floor hold for the identity (G3); tab
  labels are words as well as colours; every notice states what happened and what to do in words.

## 9. Risk register

| # | Risk | Likelihood · impact | Mitigation | PBI |
|---|---|---|---|---|
| R1 | Sleep Number access is unofficial | Medium · low (sleep only) | Watch first; bed only when the watch reads zero; one client class; spike first | C-11.4.3, C-7.2.1, C-7.2.2 |
| R2 | Pen marks read wrongly | Medium · high | 0.75 threshold → Caution chips; rectangles sent to the reader; silence keeps its meaning | C-2.2.1, C-2.2.3 |
| R3 | The cloud's PDF omits annotations | Unknown · fatal to ink | Spike in Release A; "found, nothing read" never silent | C-11.4.4, C-2.2.3 |
| R4 | No "opened" signal | High · low | Three days is the real behaviour | C-2.2.3 |
| R5 | Background Health Connect reads unavailable | Medium · medium | Read on every open; record the gap | C-11.4.2, C-7.1.3 |
| R6 | The lockdown suspends watch/scale apps | High · high (Body empty) | Allowlist their packages | C-10.1.3 |
| R7 | A bad calendar placement (the dealbreaker) | Medium · very high | Household first; property suite; owner edits win silently | C-5.3.1–C-5.3.4 |
| R8 | Two planners | Medium · high | The planning lease | C-6.1.3 |
| R9 | The partner's calendar share not done | Medium · medium | Owner action; cook and shop events wait for it | §10, C-6.4.1 |
| R10 | Claude unavailable at planning time | Low · low | Rules-only plans | C-5.1.4, C-6.3.3 |
| R11 | Cost (daily vision reads ×2 tablets, weekly planning, images) | Medium · medium | Cached prefixes; seats once a day; measure the first real week | C-9.2.3, C-13.1.2 |
| R12 | NYT terms | Settled | No scraping (D82); spike | C-11.4.1, G25 |
| R13 | Google Keep suspended on the locked phone | High · low | Allowlist Keep, or the hand-off moves to the partner's install | C-10.1.4 |
| R14 | Notifications on the partner's phone break the "nothing pushed" feel | Accepted (D83) | Only the brief's short list; no numbers | C-9.1.3 |
| R15 | Plate compiles locally only while dl.google.com is reachable | Medium · low | CI as fallback compiler for the Plate half | C-11.2.1 |
| R16 | The household extraction breaks Pulp | Low · high | Move Pulp's tests first; Pulp's guards unchanged | C-6.1.1 |
| R17 | Exercise figures show a wrong form | Medium · medium | Second Claude pass; omit on a repeat failure | C-2.3.3 |

## 10. The owner's outstanding actions

- **Supply the package ids** of the watch's companion app, the smart scale's app and Google Keep on
  the locked phone, for Flyleaf's allowlist (C-10.1.3, C-10.1.4).
- **Send NYT the written-permission request** the spike drafts, if the API check finds nothing
  (C-11.4.1).
- **The partner shares her primary calendar** with the owner's account, with edit rights (brief §Calendar).
- **Enter the Sleep Number household login** once in Settings; it goes to the vault (C-8.2.1).
- **Re-register Google sign-in** for Constitution's scopes (calendar, Drive) on both phones.
- **Grant Health Connect** per signal on both phones as the charts first open (C-7.3.5).
- **Run the device test plan** with the phones and tablets (C-13.1.2).

## 11. Not verified until a device

1. The Health Connect permission request opening at all, per signal, on both phones; background reads (C-11.4.2, C-7.1.x).
2. Watch and scale data reaching Health Connect under the lockdown (C-10.1.3).
3. The cloud's PDF carrying pen strokes, and the vision read of ticks, circles, numbers and the
   "No, because" field on both tablets (C-11.4.4, C-2.2.3).
4. The documents on a Paper Pro and an rM2: target sizes under a real pen, greyscale legibility (F-2.1).
5. The SleepIQ sign-in, sleeper side and nightly read (C-11.4.3, C-7.2.1).
6. Calendar writes, including to the partner's shared primary calendar (C-5.3.2, C-6.4.1).
7. Plate's `READ_RECIPE` round trip and the photo errand under the lockdown; the camera loop on her
   phone (C-11.2.1, C-6.5.1, C-6.5.2).
8. Flyleaf's second row and the ribbon's rose and slate (C-10.1.2, C-10.2.1).
9. The Index budget broadcast between the separately installed apps (C-11.1.1).
10. The in-place update at band 10, `SelfOnly` on the partner's phone, and her notifications (C-12.1.1, C-9.1.3).
11. Keep's share sheet on the locked phone (C-10.1.4).
12. The first real week's cost (C-13.1.2).

---

## 12. Traceability

### 12.1 Decisions → PBIs

| D | Decision (short) | PBIs |
|---|---|---|
| D1 | Cover first, hue from it | C-1.2.1 |
| D2 | Five subjects in oil | C-1.3.2 (realised as the approved art) |
| D3 | Styles tried after the subject | C-1.3.2 (realised as the approved art) |
| D4 | The food pyramid run | C-1.3.2 |
| D5 | Gym clothes, sweatbands | C-1.3.2 |
| D6 | Eight manners | C-1.3.2 (realised as the approved art) |
| D7 | Gothic and sculpture defined | C-1.3.2 (realised as the approved art) |
| D8 | Vintage health poster | C-1.3.2, C-2.3.3 |
| D9 | POS.6 approved | C-1.3.2, C-1.3.3 |
| D10 | Cerulean light cloth, 230° ladder | C-1.2.1, C-1.2.2 |
| D11 | Summit as painted | C-1.3.2 |
| D12 | Icon B, shelf cover | C-1.3.1, C-1.3.3, C-10.1.1 |
| D13 | 445 × 594 pt page | C-2.1.1 |
| D14 | Choices by circling | C-2.1.2, C-2.2.1 |
| D15 | One open number box | C-2.1.2, C-2.3.4 |
| D16 | Ink targets in muted ink, one source | C-2.1.2, C-2.1.3 |
| D17 | Reason beats tick | C-2.2.1 |
| D18 | Any mark is a tick | C-2.2.1 |
| D19 | Page 5 approved | C-2.3.1, C-2.3.2 |
| D20 | Health-poster figures | C-2.3.3 |
| D21 | Second Claude pass on figures | C-2.3.3 |
| D22 | Pounds | C-2.3.2 |
| D23 | Rating per exercise page | C-2.3.2, C-5.1.3 |
| D24 | Last page dropped | C-2.3.1 |
| D25 | One tempo, "Score · 132 bpm · Steady" | C-2.3.2, C-11.3.1 |
| D26 | Page 6 approved | C-2.3.4, C-2.3.5 |
| D27 | No rating on test pages | C-2.3.4 |
| D28 | Blank "Still sore?" is better | C-2.2.1, C-2.3.5, C-5.2.3 |
| D29 | Doctor note in neutral type | C-2.3.5, C-5.2.3 |
| D30 | Line-by-line approval | C-2.4.2 |
| D31 | Weekend breakfast and lunch | C-2.4.2 |
| D32 | Shaded "No, because" field | C-2.4.1 |
| D33 | Page 7 approved | C-2.4.1–C-2.4.5 |
| D34 | Page 8 approved | C-2.5.1, C-2.5.2 |
| D35 | Recipe notes margin → taste | C-2.5.1, C-6.3.2 |
| D36 | Page 9 approved | C-2.5.3, C-2.5.4 |
| D37 | Each row rates the dish that person ate | C-2.5.4 |
| D38 | Page 10 approved | C-2.6.1–C-2.6.4 |
| D39 | Every signal every week, two pages | C-2.6.2 |
| D40 | Tab colours | C-3.1.1 |
| D41 | The scrapbook | C-3.2.3 |
| D42 | Index cards and clippings | C-3.2.1, C-3.2.3 |
| D43 | Crinkled ground | C-3.2.2 |
| D44 | Doctor's-chart scraps | C-3.2.3, C-7.3.3 |
| D45 | Deep cloths | C-3.1.1, C-3.1.3 |
| D46 | Dinner out is a receipt | C-3.2.3, C-4.1.3 |
| D47 | Calm scraps, no overlaps | C-3.2.3 |
| D48 | Tape only | C-3.2.3 |
| D49 | Page 11 approved | C-4.1.1–C-4.1.4 |
| D50 | Page 12 approved | C-5.4.1–C-5.4.4 |
| D51 | Greyed days with reasons | C-5.4.3 |
| D52 | Move a workout here | C-5.4.5 |
| D53 | Dividers by cuisine | C-6.2.1 |
| D54 | One Meals tab, a switcher | C-6.7.1 |
| D55 | *Superseded by D56* | — (superseded; D56 is implemented) |
| D56 | Overlapping cards, pick one out | C-6.2.1, C-6.2.2 |
| D57 | No tape in the box | C-6.2.2 |
| D58 | A pile, not a box | C-6.2.1 |
| D59 | Search under the switcher | C-6.2.1, C-3.2.4 |
| D60 | A pile per cuisine | C-6.2.1 |
| D61 | Page 13 approved | C-6.2.1–C-6.2.3, C-6.7.1 |
| D62 | Page 14 approved | C-6.2.5, C-6.5.1–C-6.5.4 |
| D63 | A clean scan shows once | C-6.5.3 |
| D64 | Cuisine assigned silently, movable | C-6.5.3, C-6.2.4 |
| D65 | Own intake only | C-6.6.1, C-6.6.2 |
| D66 | Meal from time of day | C-6.6.2 |
| D67 | Page 15 approved | C-6.6.1 |
| D68 | "We didn't cook this tonight" | C-2.5.4 |
| D69 | Page 16 approved | C-7.2.2, C-7.3.1–C-7.3.5 |
| D70 | Rising weight is worsening | C-7.3.1 |
| D71 | Settings plain | C-8.1.1, C-3.2.2 |
| D72 | Equipment as checkbox rows | C-8.1.1, C-5.1.1 |
| D73 | Page 17 approved | C-8.1.1–C-8.1.3 |
| D74 | Household read-only on the partner's install | C-6.1.4, C-8.1.2 |
| D75 | Connections in the vault | C-8.2.2 |
| D76 | Page 18 approved | C-9.1.1, C-9.1.2, C-9.2.1 |
| D77 | The fold's colours | C-9.1.1 |
| D78 | Page 19 approved | C-10.1.1, C-10.1.2 |
| D79 | Cover-out, bookmark at the spine edge | C-10.1.1, C-10.2.2 |
| D80 | Native head | C-1.1.1 |
| D81 | Weekly Keep note, nothing read back | C-6.4.2, C-2.4.3, C-10.1.4 |
| D82 | No NYT scraping; spike | C-11.4.1, C-6.3.4, C-6.5.4 |
| D83 | Notifications on the partner's phone | C-9.1.3 |
| D84 | Cloths may share hues | C-3.1.1, C-3.1.3 |
| D85 | reMarkable paired as Foreword | C-2.2.2, C-8.2.2 |
| D86 | Camera on hers, Plate's tray on the owner's | C-6.5.1, C-6.5.2, C-11.2.1 |
| D87 | Constitution's own morning reader | C-2.2.3 |
| D88 | Releases A–F | process: governs §1.4 and every PBI's release |
| D89 | One phase PR | process: governs §6's definition of done |
| D90 | Keep hand-off on the Waiting page | C-6.4.2 |
| D91 | Slate bookmark for a waiting scan | C-10.2.1 |
| D92 | Round 1 approved | process: plan approval |
| D93 | Round 2 approved | process: plan approval |
| D94 | Round 3 approved; rounds 4–5 merged | process: plan approval |

**Orphans: none.** D55 is superseded and carries no work by design. D88, D89 and D92–D94 are
decisions about the plan itself, not the product, so they map to sections rather than PBIs. D2, D3,
D6 and D7 were steps in choosing the cover; their outcome is the committed art (C-1.3.2).

### 12.2 Book pages → PBIs

| p. | Page | PBIs |
|---|---|---|
| 1 | Cover studies | C-1.3.2 |
| 2 | Binding cloth and hue | C-1.2.1, C-1.2.2 |
| 3 | Shelf cover and icon | C-1.3.1, C-1.3.3, C-1.2.2, C-10.1.1 |
| 4 | Ink targets | C-2.1.1–C-2.1.3, C-2.2.1, C-11.4.4 |
| 5 | Workout cover and pages | C-2.3.1–C-2.3.3 |
| 6 | Assessment and injury | C-2.3.4, C-2.3.5 |
| 7 | Weekly meal sheet | C-2.4.1–C-2.4.5 |
| 8 | Recipe: ingredients and steps | C-2.5.1, C-2.5.2 |
| 9 | Recipe: kids' meal and rating | C-2.5.3, C-2.5.4 |
| 10 | Saturday digest | C-2.6.1–C-2.6.4 |
| 11 | Today and the rest day | C-3.1.1, C-3.2.1–C-3.2.4, C-4.1.1–C-4.1.5 |
| 12 | Workouts, Skip / Swap | C-5.4.1–C-5.4.5 |
| 13 | Meals and the recipe box | C-6.2.1–C-6.2.3, C-6.7.1 |
| 14 | Adding a recipe, the empty box | C-6.2.5, C-6.5.1–C-6.5.4, C-11.2.1 |
| 15 | "I ate something else" | C-6.6.1, C-6.6.2 |
| 16 | Body and a signal's page | C-7.2.2, C-7.3.1–C-7.3.6 |
| 17 | Settings | C-8.1.1–C-8.1.3 |
| 18 | Dog-ear, Waiting page, voice | C-9.1.1–C-9.1.3, C-9.2.1–C-9.2.3 |
| 19 | Flyleaf's second shelf | C-10.1.1, C-10.1.2, C-10.2.1, C-10.2.2 |

**Orphans: none.** Every page maps to at least one PBI.

**Two departures from the book, both raised rather than substituted:** page 14's "Paste an NYT
Cooking link" row waits on the NYT spike (D82, PC8), and on the partner's install the scan row's
subtitle reads "One photo per page" rather than "Opens Plate" (D86).

---

## Open questions (running list)

| # | Question | Raised by | Status |
|---|---|---|---|
| OQ1 | Where does the weekly "Send to Keep" tap live? | D81 | Answered, D90 (a Waiting-page notice) |
| OQ2 | Is a waiting scan's bookmark slate (page 19) or teal (D77)? | p.18 vs p.19 | Answered, D91 (slate bookmark, teal fold) |
| OQ3 | The watch companion and smart-scale apps' package ids, for Flyleaf's allowlist (TF §1.10) | TF §0.9 | **Open** (owner action) |
| — | Native or MAUI | TF §1.1 | Answered, D80 |
| — | Release order and PR shape | plan | Answered, D88, D89 |
| — | Groceries, NYT, notices, tab hues, reMarkable, scanning, ink reader | TF Q1–Q7 | Answered, D81–D87 |
