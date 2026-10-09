# Index (P11) — Gate 4 implementation plan

> **Superseded after the first install: D01 and D02 (a new package id, the old app retired).** The first P11 build
> took `com.storyworthtelling.index` and landed beside the old Ledger as a second install; Google's Android
> sign-in, registered against the package name, then pointed at the wrong app. Index keeps
> `com.storyworthtelling.ledger` and upgrades the old app in place, as Foreword, Folio, Post and Plate did. Where
> this document says the id is new, there is nothing to uninstall, or the Plaid tokens come back by vault restore,
> read it as: the phone already has them, and the vault restore is the fallback for a reset phone only.

Built on the Gate 2 finding (`INDEX-P11-TECHNICAL-FINDING.md`, approved) and the Gate 3 mockups
(all seventeen approved, Gate 3 closed at D162).

---

## 0. The mockups are the specification

**Every screen and every printed page in this phase is drawn, approved, and published here:**

> ## https://claude.ai/artifact/PEohqNDRai19shxdphibaA
> *Artifact: "Index Design Book". Read it before writing any UI or document code.*

This URL is written into the plan on purpose. A long build compresses its own context, and the link
is the one thing that must survive every step.

**The mockups are also committed**, so no task depends on a link staying reachable:
`Ledger/design/mockup-NN.html` is each mockup as a standalone page (the book's own CSS, one
section), `Ledger/design/mockup-NN.png` is its render, and `Ledger/design/index-design-book.html` is
the whole book, decision ledger included. The cover painting is `Branding/index-cover.png`.

**Three rules govern the whole build:**

1. **Build the screens and pages as 1:1 replicas of the mockup.** Screens are drawn in **dp** at
   393 × 873 with the 66dp binder gutter; printed pages in **points** at A4 595 × 842 with margins
   52.8 / 48 / 62.4. Layout, order, wording, spacing and copy are the specification. Where the
   mockup prints a sentence, ship that sentence.
2. **Surface anything you discover; assume nothing.** If something cannot be built as drawn, stop
   and put it to the owner with options and a recommendation. Do not substitute the nearest thing
   that compiles.
3. **The kit wins every disagreement.** Colour and type come from `Storyworthtelling.Maui` and
   `PaperColorTokens`; printed pages from the Document profile and `PaperTypeScale`. A disagreement
   between a drawing and the kit is a finding, not a judgement call.

### Mockup inventory

| # | Mockup | File | Release |
|---|---|---|---|
| 1 | The cover (shelf cover, launcher icon) | `mockup-01` | A |
| 2 | Accounts (index cards, bank order card) | `mockup-02` | B |
| 3 | An account, opened (the post-it) | `mockup-03` | B |
| 4 | Add an account (chooser, by-hand form) | `mockup-04` | C |
| 5 | Connect a bank (Admin mode, Plaid Link) | `mockup-05` | C |
| 6 | Snowball (Loans, History) | `mockup-06` | D |
| 7 | Budget | `mockup-07` | E |
| 8 | Budget's other states (tracking mode, unread box) | `mockup-08` | E |
| 9 | Savings (Accounts, Growth, empty) | `mockup-09` | G |
| 10 | Settings | `mockup-10` | B |
| 11 | Her install | `mockup-11` | I |
| 12 | The worksheet (income on top) | `mockup-12` | F |
| 13 | The zero-based pages (worksheet, two follow-ups) | `mockup-13` | H |
| 14 | First run | `mockup-14` | B |
| 15 | Editing an account | `mockup-15` | C |
| 16 | A savings goal | `mockup-16` | G |
| 17 | The dog-ear, turned (Waiting page, undo) | `mockup-17` | B |

---

## 1. Addenda to the approved finding, from Gate 3

The finding was approved before anything was drawn. Drawing it changed these; they are part of the
finding and override it where they disagree.

### I1 — The identity is the bookplate green, and the icon is the cover *(D115, D117, D119)*
The finding's "coin-staircase artwork replacing `Glyphs.Ledger`" became the painting set into the
suite's cover construction: cloth `#4E5F3A`, cream `CoverKeyline`, and, unlike the other covers,
§12's **corner flourishes** on the shelf cover. The identity hue is 129°, 0° from the Positive role, a
recorded §3 departure; Index's pages therefore draw **no Positive role** (D116), guarded, with a
named exception in the suite's hue-separation guard for Index against Positive only.

### I2 — Accounts is grouped by institution *(D118, reverses D13/D84)*
The finding's Release B item "balances regrouped by role" is **dropped**. Banks appear in the owner's
order (D123), paid-off accounts disappear (D124), and each card answers "when is it due?" and "can I
afford it?" (D125).

### I3 — Three new materials *(D122, D126, D127, D129, D131, D141)*
Graph paper (identity-ground lines, 16dp at 8% and 64dp at 14%, the measured contrast ceiling),
index cards (`MaterialTokens.NoteCard`, red head rule `#E4A7A2`, blue rules `#D6E3EF`) and post-its
(the bank's `primary_color` lightened to OKLCH L 0.94, chroma capped at 0.045; `MaterialTokens.PostIt`
when there is none; secondary text always soft ink). The two rule colours join `MaterialTokens`.
Fetching `primary_color` is new work: Ledger never looks an institution up.

### I4 — Snowball is the sheet *(D134, D136, D138, D139; supersedes D17, D135)*
The engine is the sheet's own formulas (Appendix A of the finding), not a balance simulation; the
history is imported from the owner's Drive (a sealed file; about seventy weeks, one interpolated) and drawn as the sheet's two graphs, with the
mortgage in both.

### I5 — Refresh on open; reconnect in place; production only *(D144, D149, D150)*
No manual refresh anywhere: balances refresh every time Index opens, plus the daily worker, no
cool-off. A broken connection is repaired with Link's **update mode**, never a fresh Link, because the
Trial's ten-Item cap counts Items created. The app knows only Plaid's **production** environment; the
vault's `PlaidSecrets.Environment` field (default `"sandbox"`) stays on the wire for compatibility and
is ignored. **Not verified:** that the owner's Plaid team is on the Trial plan (Appendix B). If it is
on Pay-as-you-go, a 15-minute cool-off is added.

### I6 — Income detection moves forward to the worksheet's release *(D155, amends D105/D152)*
The worksheet's top figure is the month's **expected income** with a printed **left to assign**
(income − Snowball payments × Fridays in the month − last month's spending). So income detection
(D101–D104) ships with the worksheet (Release F), not with zero-based budgeting. Release H keeps only
the set-aside section, the balance check and the follow-up page.

### I7 — D161 reverses D40
The finding's §6 lists "no staleness nudge for manual accounts (D40)". The approved dog-ear (D161)
**does** raise a notice when a hand-entered balance is over a month old. D161 is the later, approved
decision; §6's line is withdrawn.

### I8 — Diagnostics replaces the refresh log *(D145, replaces D37)*
One shareable export on Folio's model, with nothing secret in it, and every caught exception logged
with its stack. A guard fails on an empty `catch` outside the logger.

### I9 — Three changes leave Index for the suite *(D121, D146, D162)*
The MAUI kit's binder fills its gutter with the active tab's cloth, as `BinderTabs` already does
natively (D121). The maker's mark goes into every app's Settings (D146). Turning the dog-ear becomes a
**page turn to a Waiting page** in every app, replacing `DogEar.OpenNotices`' action card (D162). Each
rides its own PR (§3, "Suite-wide").

### I10 — Admin mode is named, never timed; Flyleaf is not touched *(D132, D133)*
The finding's "instruction to open Admin mode first" is drawn as "Admin mode needed" and where to
find it, with no button (Flyleaf's screens are not exported) and no durations anywhere in Index.

### I11 — First run restores rather than re-links *(D107, D158)*
When the vault holds banks and no passphrase is set, the only action offered is **Enter the
passphrase**. Adding a bank there would double-link and spend Trial Items.

### I12 — Every edit is on a post-it *(D159, D160; sharpens D56, D82, D83, D111)*
A connected account offers the **Savings** switch only; a hand-entered one offers its fields in place,
saved on leaving each field, with an undo-first Remove. A savings goal is one Goal field beside its
worked-out **Reaches it** date and the **Pace** behind it.

### I14 — The balance handoff carries a sender name, by the owner's decision *(amends §8)*

Release C found that D87/D91's manual/Plaid match precedence had nothing to match against: Post's
`IndexHandoff` wire payload carries only a balance and an optional date — deliberately, per the contract's
own original reasoning, no institution or sender identity. Put to the owner directly rather than guessed
at: the owner chose to widen the frozen contract with one new field, a plain, human-readable sender name
(Post already resolves and shows this via `ContactNameResolver.DisplayName`; Plate's statement-OCR path,
never yet exercised, passes none until a future release extracts one from a scanned document).

This is a **deliberate exception to §8** ("What this phase must not break... Post's `IndexHandoff`
contract... already shipped, frozen; Index implements the receiving end exactly as specified, with no
changes requested of Post or Plate"). §8 stands for everything else in the contract; this one field is the
named exception, made once, by the owner, not inferred from the finding's own reasoning about signature-level
permissions already limiting receipt to the owner's own signed apps.

### I15 — Action cards open on a tap, and her role is unknown until the passphrase *(remediation R4, R2)*

Two departures from the drawn design, both taken by the owner during the remediation. **R4:** the action
cards (reorder or disconnect a bank, move a transaction, rename a category, reorder a loan) open on a **tap**
rather than the long-press the mockups first drew, because MAUI has no first-class long-press recognizer on
every platform; the mockups and the design book now say "tap". **R2:** her install's role is derived from the
household file, so until the passphrase restores that file the role is unknown and the first-run screen cannot
tell the two installs apart. That is an accepted limit, not a bug to chase; it is listed under what stays
unverified until a device.

### I13 — The paper *(D151, D154, D156, D157)*
Whole dollars, rounded down — except the month's Snowball payments, which round up (owner decision: payments round up). Three write-in category lines on the worksheet (two on Release H's
denser page, 30pt rows). The follow-up's suggestions: over → trim the lines furthest above last
month, largest rise first, never below last month's figure, set-aside untouched; under → the
remainder to the extra debt payment. An empty box takes the suggestion.

---

## 1a. One PR for the whole phase, not one per release

Amended after Gate 4 approval: GitHub Actions on this repo aren't available again until **1 Oct**, so a
release-by-release PR would sit with no CI signal anyway. Every release lands as commits on this same
branch (`claude/index-rebrand-p11-vjnns3`), rolling straight from one release into the next with no PR
opened between them. Everywhere below that says "its own PR off `main`", read it as "its own set of
commits on this branch" instead — the release boundaries, guard discipline, and definition-of-done still
apply per release; only the PR-per-release mechanic is suspended. A single PR opens once the owner asks
for one (expected on or after 1 Oct, once Actions are back and CI can actually run).

## 2. How the work is run: agent levels and briefs

The build is done by **subagents**, to keep cost and context down. This session (the
*orchestrator*) owns the gates, the owner's questions, the PRs and every push; subagents do bounded
tasks and report back.

### The three levels

Every task below carries a level. The level-to-model mapping is chosen at dispatch time and is **not**
recorded in the repository.

| Level | For | Examples |
|---|---|---|
| **L1 — mechanical** | Fully specified by this plan plus an existing precedent in the repo; no design choice left. | Copy a workflow, add a constant, register an IconStudio spec, adopt a shared component in one more app. |
| **L2 — standard** | Implementation plus its tests inside one project, following an existing pattern; UI built 1:1 from a mockup. | A screen, a composer page, an EF entity and its service. |
| **L3 — judgement** | New algorithms; contracts other apps depend on; cross-project extraction; anything whose failure is **silent** (money arithmetic, frozen names, OAuth, encryption). | Income detection, the handwriting extraction, `BalanceReceiver`, the household file. |

**An L3 agent reviews every L1 and L2 diff in its release before the orchestrator pushes.**

### What every brief carries

Subagents start cold. A brief is self-contained, and it always includes:

- The task, its release and level, and the files it may touch.
- **The guards to write first**, from §4, and the instruction to watch each fail and name the
  reintroduced bug in the commit message.
- The verification commands from §5.
- The standing rules from §2.1, pasted in rather than referred to.
- The graphify rule: orient with `graphify query "<real type names>"` before reading source files.

**Every UI or printed-page task also carries its mockup embedded in the brief itself:**

- the mockup's section **pasted verbatim** from `Ledger/design/mockup-NN.html` (markup, notes and
  captions, so every size, colour and sentence is in the prompt, not behind a link);
- the render `Ledger/design/mockup-NN.png`, to be opened with the Read tool before any code is written;
- the book's URL above, as the reference of last resort;
- the instruction: build 1:1, and **report back** anything that cannot be built as drawn, rather than
  substituting it.

A subagent never pushes, never opens a PR, and never asks the owner anything: findings come back to
the orchestrator, which puts them to the owner.

### 2.1 Standing rules for every release

- **Never push to `main`.** Feature branch → PR → the owner's approval; nothing merges red.
- **TDD strictly.** Untested code is not done.
- **Every guard is written first and watched to fail**, with the reintroduced bug named in the commit.
- **No colour hex or type-size literals** outside the registered token files.
- **No model identifier** in commit messages, PR titles or bodies, code comments, or anything pushed.
- **Read binding and API member names out of the packages' own metadata** before coding against them.
- **Compile the MAUI head locally** (§5); CI is not the first compiler.
- **Money is `decimal`.** No `double` on any money path, ever again (finding §3, Correction 2).
- **Never put a credential in an artifact, doc, log or commit.** The vault passphrase and every key it
  unlocks are used in-process only.
- **Say plainly what is not verified.** "Tested against fakes" is not "works."

---

## 3. Releases

Each release is its own PR off `main`, in this order. The suite-wide PRs S1 and S2 land before
Release B, which consumes them.

### Release A — the new app and its frozen set
New package id, so nothing on a phone has to survive the rename (finding §0.1). What is frozen is
frozen **from Index's first ship**.

- **[L3]** `IndexIdentity.ApplicationId = "com.storyworthtelling.index"`, one constant; namespaces
  `Ledger.*` → `Index.*` (the folder stays `Ledger/`, as Folio kept `Storyboarder/`); the worker's
  registered Java name, its unique work names, `MainActivity`'s declared `Name`, and every
  `PreferenceKeys` value as **literals**, never `nameof`. Guard G1.
- **[L3]** `Account`'s money fields `double?` → `decimal?`, on a fresh `index.db` with a new migration
  baseline (the finding's schema, §3). Guard G3.
- **[L2]** Vault restore on first run from the existing `Plaid` section (wire key "Plaid", untouched by
  the rename); production pinned (I5). Guard G17.
- **[L1]** `build-index.yml` from `build-ledger.yml`: artifact **Index-Android**, fatal APP ID and ICON
  checks, a FONT CHECK over **all eight** fonts (as `build-plate.yml`), display version
  `1.0.<run_number>`, versionCode from the shared action at **band 1**, a fresh `SuiteVersionBands`
  entry with `LastShippedBeforeUnification: 0` (Ledger's entry stays, as history). Name the new
  artifact in the PR body.
- **[L1]** IconStudio: an `Index` spec from `Branding/index-cover.png` with `CoverKeyline` and the
  corner flourishes on the shelf cover (D119), cloth `#4E5F3A`, `cover_index` for Flyleaf's shelf;
  retire the `Ledger` spec and `Glyphs.Ledger`. Index **commits** its generated icons like every other
  head (Ledger was the one exception). → *Embed mockup 1.*
- **[L2]** The identity ladder in `Tools/IconStudio/Tokens.cs`, registered in DESIGN.md §3 as a
  recorded departure; guards G9 and G10.
- **[L1]** `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 advisory (finding risk 4): bump if a fixed version
  exists; otherwise record it in the PR body as carried.

### Suite-wide S1 — the kit's binder fills its gutter *(before B)*
- **[L2]** `Storyworthtelling.Maui`'s binder fills the gutter with the active area's cloth, as
  `BinderTabs.UpdateGutterColor` does natively (D121). Folio and Foreword change with it; say so in
  the PR.

### Suite-wide S2 — the Waiting page *(before B)*
- **[L3]** Turning the dog-ear becomes a page-curl to a **Waiting page** in the shared components,
  `Codex.Ui.Android/DogEar` and the MAUI kit (`DogEarState` stays the decision; the page is new):
  a Serif header, notices on the page, newest first, a role bar, a relative date, at most one
  secondary-button action, no pinned bar, "Nothing waiting." when the last one goes (D162).
  → *Embed mockup 17.*
- **[L1 each]** Adopt it in Post, Plate, Score, Flyleaf, Folio and Foreword, one small commit each.
  Plate and Foreword compile in this sandbox as well (checked in the pre-public cleanup); compile them rather than leaving CI to find the mistake.

### Release B — the kit, the paper, and Accounts
- **[L2]** Kit adoption: delete Ledger's `Resources/Styles/DesignTokens.xaml` and the palette in
  `Ledger.Domain.Design.DesignTokens`, merge `PaperKit`, register the fonts. Guard G12.
- **[L2]** `MaterialTokens`: the two index-card rules and the graph-paper line alphas. Guards G11, G24.
- **[L3]** The three materials as kit-style views (graph-paper ground, index card, post-it with the
  tilt and adhesive strip), laying labels on rules and centring fields in three rules (D129).
  → *Embed mockups 2, 3, 4.*
- **[L2]** Five tabs with their own hues (D120) and the gutter fill from S1; `AreaHueTests` extended to
  Index's MAUI source. Guard G9. → *Embed mockup 2.*
- **[L2]** Accounts: institution headings in the owner's order with the long-press card (D123),
  paid-off hidden (D124), due-and-payment lines and limits (D125), status line, voice line.
  → *Embed mockup 2.*
- **[L2]** The post-it: APR, Available, 30 days of transactions (D128); `primary_color` fetched and
  stored on the institution, tinted by a pure function (D127). Guard G24. → *Embed mockup 3.*
- **[L2]** Refresh on open plus the daily worker, no cool-off, shown in the status line (D149).
- **[L2]** First run: nothing connected, vault holds banks without a passphrase, restoring with a count
  (D107, D158). Guard G26. → *Embed mockup 14.*
- **[L2]** Settings: vault passphrase in the clear, credential health (D80), Export diagnostics (D145),
  the maker's mark and version line (D146). Guards G13, G14. → *Embed mockup 10.*
- **[L2]** Accounts' notices on the Waiting page from S2: lost connection, stale hand-entered balance,
  failed refresh, 9-of-10 Items (D161). Guard G25. → *Embed mockup 17.*

### Release C — connecting and editing
- **[L3]** Connect: the Admin-mode path (D20), "Admin mode needed" with no timings (D132, D133), the
  restricted-viewer message kept only as the fallback. Bank OAuth hosts (D79) go into
  `PolicyContext.LoginBrowserUrlAllowlist` in a **separate FocusPhone commit**, since that is
  Flyleaf's policy. Guard G27. → *Embed mockup 5.*
- **[L2]** Reconnect in update mode (D150). Guard G16.
- **[L2]** Add by hand: the chooser and the index-card form with Every and Next due (D108, D130).
  → *Embed mockup 4.*
- **[L2]** Editing on the post-it: the Savings switch; a hand-entered account's fields saved on leaving
  each; undo-first Remove (D112, D159). → *Embed mockup 15.*
- **[L3]** `BalanceReceiver` to Post's frozen `IndexHandoff` contract, and the manual-vs-Plaid match
  precedence (D87, D91). Guards G2 and G6.

### Release D — Snowball
- **[L2]** The formula engine, exactly the finding's Appendix A, with its invented-input table (checked
  against an independent Python oracle, `sheet_oracle.py`) as its test. Guard G7, written first.
- **[L2]** One-time history import from the owner's Drive (the sealed file written by
  `Tools/IndexHistorySeal`; a formula-filled week kept as recorded, a loan that arrives part-way through)
  and weekly snapshots after (D03, D78). Guard G8, against the invented synthetic fixture.
- **[L2]** Loans: debt-free date, extra payment, focus order (D04, D43), cards with this week's payment
  and the focused payment on the post-it (D137). → *Embed mockup 6.*
- **[L3]** History: the stacked weekly bars with the mortgage at the foot, and small multiples at
  per-panel scales (D136, D138, D139), drawn to the dataviz rules. → *Embed mockup 6.*

### Release E — Budget
- **[L2]** The schema (finding §3), all money `decimal`. Guard G3 extended.
- **[L2]** Budget: spending categories only (D140), budget lines as index cards with transaction
  post-its (D141), recategorising with merchant rules (D58, D62, D67), and the other states including
  tracking-only mode's last-month spend and history post-it (D142). → *Embed mockups 7, 8.*
- **[L2]** Transaction retention (D66). Guard G5.

### Release F — the worksheet, and income
- **[L3]** Extract `Storyworthtelling.Handwriting` from Foreword (D61, finding §3); Foreword becomes its
  first consumer, re-verified against its own suite. Foreword's head compiles in this sandbox as well; compile it locally.
- **[L3]** Income detection (D101–D104): recurring deposits by cadence and amount, a projected monthly
  figure rather than a calendar-month sum, scoped to this install's accounts, and "not sure" when it
  can't tell. Guard G22. **The largest unproven piece of the phase.**
- **[L2]** The worksheet composer through `VectorPdfSink`: income on top with the correction box, left
  to assign, one line per category, three write-in lines, whole dollars (D151, D154, D155).
  `WorksheetRegions` is the one place the boxes' geometry lives, read by both the painter and the
  reader's prompt. Guards G18, G19, G20. → *Embed mockup 12.*
- **[L2]** Delivery to the reMarkable (D52, D60, silent per D89), read-back, limits applied, unreadable
  boxes resent (D49). → *Embed mockup 8* (the unread-box state).

### Release G — Savings
- **[L2]** Savings: Accounts and Growth, the empty state, the monthly growth charts (D81, D109, D113).
  → *Embed mockup 9.*
- **[L2]** The goal post-it: Goal, Pace, Reaches it, "not at this pace", undo-first Remove goal (D160);
  the projection from recent pace (D83). The interest-rate path (D82) waits on confirming Plaid reports
  a deposit rate at all (mockup 9's open question). Guard G23. → *Embed mockup 16.*

### Release H — zero-based
- **[L2]** The Release E worksheet: set aside (extra debt payment to the Snowball's focus, one line per
  savings goal), 30pt rows, two write-in lines, the target in the intro (D98–D100, D157).
  → *Embed mockup 13.*
- **[L2]** The balance check in `decimal`, and the next-day follow-up pages with the suggestion engine
  (D153, D156); the Budget tab tracks what was written until it balances. Guards G4, G21.
  → *Embed mockup 13.*

### Release I — her install and the household file
- **[L3]** `storyworthtelling-index-household.json`: computed balances and the Snowball, Budget and
  Savings summaries, encrypted with the vault passphrase through `VaultEnvelope`, written by the owner's
  install, read by hers (finding §3).
- **[L2]** Her install: the five tabs read-only from the file, Settings with only the passphrase and
  her Google account (D09, D25, D29, D35, D36, D90). → *Embed mockup 11.*

### Suite-wide S3 — the maker's mark in every app *(any time after B)*
- **[L2]** The shared piece: the install's mark (`OwnerMark`), falling back to `/Branding/owner.png`,
  and a suite guard that every app's Settings shows it (D146).
- **[L1 each]** Flyleaf, Score, Post, Plate, Folio, Foreword.

---

## 4. The guard ledger

Each guard is written **first**, watched to fail against the reintroduced bug, and the commit message
names what was reintroduced. G1–G6 come from the finding; G7 onward from Gate 3.

| # | Guard | The bug it reintroduces to prove itself |
|---|---|---|
| G1 | `IndexInvariantsTests`: app id, the worker's registered name, the work names, `MainActivity`'s `Name`, every preference key, as **literals** | Rename a property behind a `nameof` key; the stored value silently vanishes |
| G2 | `IndexHandoffContractTests`: the receiver's manifest against `IndexHandoff`'s own constants | Typo the action; Post can never reach Index, and nothing says so |
| G3 | `MoneyIsDecimalTests`: no `double` or `float` money member on any entity or money path | Put one `double` back; a snowball total drifts by cents |
| G4 | `ZeroBalanceTests`: balanced means exactly zero in `decimal` | Compare doubles with a tolerance |
| G5 | `TransactionRetentionTests` (D66) | Keep every transaction forever |
| G6 | `BalanceReceiverPrecedenceTests`: one receipt, one match (D87, D91) | Let the manual and Plaid match both claim it |
| G7 | `SnowballSheetTests`: the finding's Appendix A table reproduced to the cent and the day | Use a balance-based weekly model; debt-free moves by about twenty weeks |
| G8 | `HistoryImportTests`: the synthetic fixture's 70 weeks, its formula week kept as recorded, the mortgage present | Treat the formula week as missing |
| G9 | `AreaHueTests` extended to Index; `NoPositiveRoleTests` (D116, D120) | Give Budget's page its own tab's ochre; draw an under-limit line in Positive |
| G10 | Index's identity ladder and its named Positive exception | Widen the exception to every app |
| G11 | `GraphPaperContrastTests`: muted ink ≥ 3:1 and soft ink ≥ 4.5:1 at the heavier line (D122) | Darken the 64dp line to 16% |
| G12 | `KitAdoptionTests` for Index | Re-add `DesignTokens.xaml` |
| G13 | `NoEmptyCatchTests` outside the logger (D145) | Swallow a refresh exception |
| G14 | `DiagnosticsRedactionTests`: no access token, key or full account number in the export | Log the Item's access token |
| G15 | `MakersMarkEverywhereTests` (S3, D146) | Drop the mark from one app's Settings |
| G16 | `UpdateModeReconnectTests`: a reconnect's link token carries the Item's access token (D150) | Create a fresh link token; a repair spends a Trial Item |
| G17 | `ProductionOnlyTests`: no sandbox or development host reachable from the app (D144) | Honour the vault's `Environment` |
| G18 | `WorksheetRegionsTests`: the painter and the prompt read the same boxes | Hardcode a box in the painter |
| G19 | `WholeDollarsTests`: every printed figure floored (a payment ceiled), the printed parts sum (D154) | Round to nearest |
| G20 | `LeftToAssignTests`: payments × the Fridays in the month (D155) | Use 52 ÷ 12 weeks |
| G21 | `FollowUpSuggestionTests`: largest rise first, never below last month, set-aside untouched; under goes to the extra payment (D156) | Trim proportionally; trim a savings line |
| G22 | `IncomeProjectionTests`: projected from the pattern, not the month's deposits (D102); "not sure" below confidence (D103) | Sum the calendar month's deposits; a three-paycheck month inflates the income |
| G23 | `SavingsPaceTests`: "not at this pace" for a flat or falling pace (D160) | Divide by a zero pace |
| G24 | `PostItTintTests`: L 0.94, chroma ≤ 0.045, soft ink ≥ 4.5:1 on every tint (D127) | Use Plaid's colour unlightened |
| G25 | `NoticeLifetimeTests`: a notice goes when its cause does, not when read (D161) | Clear notices on open |
| G26 | `NoDoubleLinkTests`: vault holds banks and no passphrase → Add is not offered (D158) | Show Add an account on first run regardless |
| G27 | `AdminModeUntimedTests`: no Index string states an Admin-mode duration (D133) | Write "opens in 6 hours" into the connect page |

---

## 5. Definition of done, per release

1. Guards written first and **watched to fail**, the reintroduction named in the commit.
2. Tests green: `dotnet test Ledger/Ledger.Tests/Ledger.Tests.csproj -p:LedgerTfms=net10.0` (Release A
   renames the projects and the property; the command in CLAUDE.md changes with it), plus the suite
   libraries a release touches.
3. The MAUI head **compiles locally**:
   `dotnet build <the head>.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`
   after `Storyboarder/scripts/setup-android-build.sh`.
4. The screen or page matches its mockup 1:1, checked against the committed render, **not** from memory.
5. Anything that could not be built as drawn is **surfaced for approval**, not substituted.
6. An L3 review of the release's L1 and L2 diffs.
7. CI green on the PR before it merges. Never push to `main`.
8. What is not verified is stated plainly in the PR body.

---

## 6. Closing deliverables

- The CLAUDE.md section for Index, replacing Ledger's.
- DESIGN.md: §2 (graph paper, D122), §3 (the 129° departure, D115), §12 (the maker's mark on every
  Settings page, D146, and the corner-flourish cover, D119), §13 (the Waiting page, D162).
- The P11 card on the roadmap artifact.
- The on-device test plan in Google Drive, in `[ID]` / `RESULT:` / `NOTES:` format. Its questions, in
  order of weight: the Admin-mode bank sign-in on the locked phone (D93); that the Plaid team is on the
  Trial plan; a real `primary_color`; the worksheet's handwriting read-back for both hands; income
  detection against real deposits; her install reading the household file; the uninstall of Ledger
  once Index is confirmed (D02).
- The unused canvas artifact from Gate 3 (`9hhmuGy7oq5CGXG6KBXDvH`) deleted **only if the owner says
  so**.

---

## 7. What is **not** verified

- The Admin-mode Chrome path through a real bank's sign-in on the locked phone (D20, D93).
- That the owner's Plaid team qualifies for the Trial plan (created on or after 15 Apr 2026).
- That Plaid returns `primary_color` for the household's banks, and whether it reports a deposit
  account's interest rate (D82).
- Income detection's accuracy on real deposits; there is no data until Release F runs.
- The handwriting read-back of the worksheet's boxes, for either writer.
- The household file on her phone.
- Everything drawn here on a real screen: no mockup has been opened on a device.
