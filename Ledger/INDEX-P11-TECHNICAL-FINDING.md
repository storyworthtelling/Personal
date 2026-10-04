# Index (P11) — Technical Finding

> **Superseded after the first install: D01 and D02 (a new package id, the old app retired).** The first P11 build
> took `com.storyworthtelling.index` and landed beside the old Ledger as a second install; Google's Android
> sign-in, registered against the package name, then pointed at the wrong app. Index keeps
> `com.storyworthtelling.ledger` and upgrades the old app in place, as Foreword, Folio, Post and Plate did. Where
> this document says the id is new, there is nothing to uninstall, or the Plaid tokens come back by vault restore,
> read it as: the phone already has them, and the vault restore is the fallback for a reset phone only.

## 0. Executive summary

Gate 1 closed at 114 locked decisions across 44 rounds (`index-design-book.html`), expanding Ledger→Index
from a straight rebrand into a five-tab app: **Accounts** (existing balances, regrouped by role),
**Snowball** (the debt payoff plan, largely copying the owner's live sheet), **Budget** (a monthly
zero-based worksheet delivered to and read back from the reMarkable), **Savings** (goal-tracked growth),
and **Settings**. This gate measured what that expansion actually costs against the code as it stands
today, rather than assuming the existing Ledger foundation carries it for free.

Three things came back different from what Gate 1 assumed:

1. **The package-id break (D01) removes a whole category of freeze work, not just one constant.**
   Because Index ships under a brand-new id, there is no existing install to keep alive across the
   rename — no WorkManager class-name freeze, no Preferences-key freeze, no versionCode band chosen to
   clear an old scheme. Those freezes matter starting the day Index itself ships, not before.
2. **`Account`'s money fields are `double`, and P11 is the first phase that does arithmetic with them.**
   Ledger only ever displayed a balance. Index sums them into a snowball total, subtracts a budget's
   categories toward exactly zero, and projects a savings payoff date — the one place in the suite that
   already crosses this exact line (Post's `IndexHandoff`) chose `decimal` for the same reason this
   finding now raises for Index.
3. **D101's income detector has no precedent anywhere in this codebase.** A repo-wide search for a
   recurring-deposit or income-inference component returned nothing — it is new algorithmic work, not an
   adaptation of something that already exists, and D105 was right to schedule it after the simpler
   Budget tracking ships.

The existing Ledger suite passes at **80/80** and the Android head **compiles locally** (verified in
this session, not left to CI) once IconStudio's generated icon resources are regenerated. Both are real
baselines this phase's releases are measured against, not assumptions carried over from the plan.

## 1. Facts — verified in this session

### The build and the suite

- `dotnet test Ledger/Ledger.Tests/Ledger.Tests.csproj -p:LedgerTfms=net10.0` → **Passed! Failed: 0,
  Passed: 80, Skipped: 0, Total: 80**, 2s. This is the floor every release below has to hold.
- `Ledger.Android` (a MAUI head, confirmed via `<UseMaui>true</UseMaui>` in `Ledger.Android.csproj`)
  **compiles locally**, per the standing rule to compile MAUI heads rather than leave CI to find the one
  API mistake per round. `Storyboarder/scripts/setup-android-build.sh` is the correct setup script
  despite its path — it is not Folio-specific; it stitches the same minimal Android SDK
  (`/opt/android-sdk-min`, API 36 `android.jar` from the GitHub mirror, the workload's own `aapt2`,
  `zipalign` from apt) that every net10.0-android MAUI head in this repo needs, and its own header
  comment just happens to echo Folio's build command. It ran clean on a fresh container.
- The first build attempt **failed** with `APT2260: resource mipmap/appicon not found` — expected,
  per CLAUDE.md: unlike every other app, Ledger's launcher icon binaries are **not committed**; CI
  regenerates them with IconStudio before packaging. Running `dotnet run --project Tools/IconStudio --
  --repo-root .` first (which also regenerated the other six heads' committed-but-idempotent output,
  confirmed via `git status` showing no diff afterward — IconStudio's Ledger output is the one
  git-ignored exception) fixed it. The rebuild then succeeded: **0 errors, 23 warnings** — all
  pre-existing (`NU1608` dependency-version mismatches in the AndroidX Concurrent.Futures/Lifecycle
  packages, `NU1903` on `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 — see Risks — and four `CS0618` MAUI
  obsolescence warnings already present in `App.xaml.cs`/`SetupPage.xaml.cs`/`PlaidLinkPage.xaml.cs`).
  None of the 23 are new; none block Index's rename.

### The frozen surface, measured against what's actually there

- `Account`'s money columns (`Ledger.Domain/Model/Entities.cs`) — `CurrentBalance`, `AvailableBalance`,
  `CreditLimit`, `LastStatementBalance`, `NextPaymentAmount`, `Apr` — are **all `double?`**, with the
  file's own comment calling them "provider-reported doubles," a decision that was fine while the app
  only ever printed a balance to the screen. Compare Post's `MoneyMail.cs` (already shipped, frozen,
  and the receiving contract Index must match): `MoneyFacts` and `IndexHandoff.BalanceExtra` are
  deliberately `decimal`/exact-text, with a comment on `BalanceExtra` naming the exact reason — "0.1 +
  0.2 is not 0.3 in binary floating point, and a ledger that disagrees [with the sender] is worse than
  one that is merely late." Index inherits that same argument the moment it starts summing balances for
  a snowball total or checking a budget balances to zero.
- **No income-detection or recurring-deposit code exists anywhere in the repo.** `grep -rln
  "RecurringDeposit\|IncomeDetect" --include=*.cs .` returned nothing. D101–D103 are new algorithmic
  work, confirmed rather than assumed.
- `Storyworthtelling.Remarkable`'s `IRemarkableClient` already exposes `Upload(title, pdfBytes,
  parentFolderId)` and `EnsureFolder(name)` (`RemarkableStorageClient.cs`) — the folder-scoped delivery
  D52 asks for (Index's own document, independent of Foreword's) needs **no new capability** in the
  shared library, only a call site in Index.
- `Storyworthtelling.Voice.Health`'s `CredentialFieldState.Decide(isSet, health)` and
  `CredentialHealth` are already provider-agnostic (`Storyworthtelling.Voice/Health/*.cs`) — D80's ask
  (Index's Settings surfacing the shared Claude/Gemini key's health) is a second `ICredentialHealthSink`
  consumer of an existing pattern, not new design.
- `Ledger.Domain/Health/CredentialIds.cs` today defines exactly one id, `Plaid = "plaid"` — extending it
  for D80 is additive, one more constant.
- `ConnectBrowserPolicy.IsRestrictedViewer` and `PlaidLinkPage.OnAppearing` (read in full) confirm the
  **current, pre-D20 dead end** precisely: on the FocusPhone, the page detects the restricted default
  browser and shows "This phone can't open a bank's sign-in page. Connect your banks on a phone with a
  browser…" with the Open button hidden — connecting on this phone is not attempted at all today. D20's
  Admin-mode Chrome flow replaces this with an actual on-device path; the message above becomes the
  fallback for when Admin mode is declined, not the primary instruction.
- `Storyworthtelling.Updates.SuiteVersionBands.All` lists Ledger's existing package
  (`com.storyworthtelling.ledger`) at band 1 with `LastShippedBeforeUnification: 58` — this entry is
  history for a package that D02 retires (uninstalled once Index is confirmed working), not something
  Index inherits or needs to clear.
- `Foreword.Infrastructure/Handwriting/ClaudeVisionHandwritingExtractor.cs` and
  `Foreword.Domain/Interfaces/IHandwritingExtractor.cs` (both read in full) confirm D54/D61's premise:
  the interface is already minimal (`Task<IReadOnlyList<ParsedNote>> Extract(byte[] annotatedPdf)`), and
  the one thing standing between this and a general-purpose primitive is that the prompt and the parsed
  shape are hardcoded to Foreword's own calendar-note fields and household names (`PersonColumn`,
  `Rowan`/`Jordan`/`Wren`/`Linden`). `Foreword.Tests/Handwriting/ClaudeVisionHandwritingExtractorTests.cs`
  and `Foreword.Tests/Prayer/JournalTranscriptionTests.cs` (both read in full) show the extraction's
  actual test surface: HTTP-shape assertions (api key header, PDF as a `document` content block,
  `cache_control` present), JSON-wrapped-in-prose tolerance, and the "every failure is quiet and returns
  nothing" contract — all of which travel cleanly to a generalized extractor whose prompt/region
  description becomes a constructor parameter instead of a literal.

## 2. Risks

1. **Admin-mode Chrome bank OAuth (D20/D93) is the phase's one unverified mechanism with no fallback
   if it's wrong.** The allowlist mechanism (`PolicyContext.LoginBrowserUrlAllowlist`,
   `AdminModeActive`) already exists and already carries Google/GitHub/reMarkable/TIDAL sign-in, so this
   is extension, not invention — but no bank's actual OAuth host list has been walked end to end, and a
   consent screen that needs one more asset host fails silently into "blocked by your administrator"
   with no error surfaced to Index at all. Given the same weight as Post's ROLE_SMS risk, per D93.
2. **`double`-typed balances are a live correctness risk the moment Index computes rather than displays.**
   A snowball total across nine loans, or a budget that must balance to exactly $0.00, can drift by
   cents in ways a user will notice and a test using round numbers will not catch. This is Index's, not
   inherited: nothing in Ledger today sums or zero-checks anything.
3. **Income detection (D101–103) is unproven algorithm design, not integration.** Distinguishing a
   paycheck's cadence from a refund or a one-off transfer, and reconciling 2-vs-3-paycheck months, is
   real signal-processing work against Plaid's transaction feed with no existing pattern in this repo to
   lean on. D105 sequencing it after simpler Budget tracking ships is the right call, made more clearly
   right by this session's finding that there is nothing here to reuse.
4. **`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 carries a known high-severity advisory** (`NU1903`,
   GHSA-2m69-gcr7-jv3q), surfaced on every Ledger build today and inherited unchanged by Index. Not a
   regression this phase introduces, but worth a line in the plan rather than another silent carry-over.
5. **Jordan's read-only, no-Plaid-of-her-own install (D09/D25/D29/D35/D36/D90) has no sibling
   pattern anywhere else in the suite to model against.** Score's Jordan-vs-owner ambiguity (flagged in
   D44 as a *different*, unresolved problem) is the closest thing, and D44 explicitly defers it. Index's
   two-install split — one Plaid-connected, one reading a published snapshot — is being designed from
   nothing.
6. **Extending `MoneyMail`/`IndexHandoff` isn't touching Post, but the match logic in Index (D87, D91)
   is new and has two independent callers to get right**: matching a payment to a *manual* account's
   servicer by merchant name (D87, reusing D58's rule mechanism) and matching a BalanceReceiver receipt
   against a *Plaid-connected* institution as a staleness fallback (D91). Same underlying contract, two
   different match targets, and D91 was flagged in Gate 1 itself as widening scope beyond what D87
   originally covered.

## 3. Decisions changed by this evidence

### Correction 1 — the versionCode band is fresh, not high

Gate 1 didn't set a band; the natural assumption, following Foreword's band 7 and Folio's band 8, would
be to reach for a high band so Index "upgrades in place" over Ledger. That assumption is wrong here:
D01/D02 already chose a **new package id** with **vault-restore migration**, not an in-place upgrade —
there is no existing `com.storyworthtelling.index` install for a low band to collide with. Index gets
**band 1**, `LastShippedBeforeUnification: 0`, a genuinely fresh entry in `SuiteVersionBands.All`.
Ledger's existing band-1 entry stays, unedited, as the historical record of a package that's being
retired.

### Correction 2 — the balance-type risk is real and unrecorded

No Gate 1 decision addresses `Account`'s `double` fields, because Gate 1 discovery never had reason to
open `Entities.cs` — the phase was scoped as UI/screens/decisions, not a schema audit. This finding adds
what Gate 1 didn't know to ask: **Account's money fields migrate to `decimal` as part of Index's own
migration**, matching the convention `IndexHandoff` already set. This isn't a reversal of a locked
decision, it's a gap Gate 1's own question rounds didn't reach.

### Confirmed as already correctly captured — no gap

The earlier session flagged a risk that D66 ("transaction line items kept only until the next budget is
approved, then only category totals survive") might exist only as an unnumbered verbal answer. Reading
the design book directly in this session confirms **D66 already states this precisely, as a locked
decision** — there is no gap to backfill.

### Resolved — the four items Gate 1 left as "Technical" open threads

Gate 1's "Still open → Technical" thread named five questions as Gate 2's to answer. Answered here:

- **Computed-balances Drive file:** a new, independent file — not a `SecretsVault` section, because it
  holds published household display data (computed balances, snowball/budget/savings summaries), not a
  credential — encrypted with the same vault passphrase and crypto (`SecretsCrypto`/`SecretsEnvelope`)
  the vault already uses, over the same `IDriveTextFile` seam `DriveAudioLibraryStore` established.
  Working name: `storyworthtelling-index-household.json`.
- **EF schema for Budget/Savings:** `BudgetCategory` (Id, Name, IsActive — D55's soft delete),
  `BudgetMonth` (Id, YearMonth, Status: Draft/DeliveredToRemarkable/AwaitingConfirmation/Approved),
  `BudgetCategoryAmount` (BudgetMonthId, CategoryId, LimitAmount `decimal`, ActualAmount `decimal`,
  TrackingOnly `bool` for D95), `Transaction` (Id, AccountId, PlaidTransactionId, MerchantName, Amount
  `decimal`, Date, CategoryId, IsManualOverride — purged per D66 to category totals after approval),
  `MerchantCategoryRule` (MerchantName, CategoryId — independent of `Transaction` per D67),
  `SavingsGoal` (AccountId, TargetAmount `decimal` — D82, no target date column), `SnowballPlan`
  (LoanAccountId, FocusOrder, ExtraPaymentAmount `decimal`), `SnowballSnapshot` (WeekOf, AccountId,
  Balance `decimal` — D78's dedupe-at-write), `SavingsSnapshot` (MonthOf, AccountId, Balance `decimal`
  — D81's monthly cadence). `Account` gains a `Source` enum (Plaid/Manual/BalanceReceiver, D39) rather
  than a separate table.
- **Transcription pipeline architecture:** extract `IHandwritingExtractor` +
  `ClaudeVisionHandwritingExtractor` into a new shared `Storyworthtelling.Handwriting` library
  (net10.0, its own `.csproj`+`.Tests`, matching the Voice/Remarkable/Maui/Updates pattern), generalizing
  the prompt/region-description/parsed-field-shape into constructor parameters. Foreword becomes its
  first consumer (its journal prompt's household-name literals move to a parameter it supplies);
  Index's Budget worksheet reader is the second. D61 said this is a straightforward move-and-reverify
  against Foreword's existing suite, and the two test files read in this session confirm that suite is
  narrow enough (HTTP shape, prose-tolerant JSON parsing, quiet-failure contract) to carry over intact.
- **versionCode band:** answered above — band 1, fresh.
- **`ConnectBrowserPolicy`'s fallback message:** flips from primary to secondary instruction. Today's
  exact text ("This phone can't open a bank's sign-in page. Connect your banks on a phone with a
  browser…") is verified live in `PlaidLinkPage.xaml.cs` as the *only* message shown when the restricted
  viewer is detected — Release A replaces it with an instruction to open Admin mode first, keeping this
  wording only for the case Admin mode is declined or unavailable.

## 4. Guards

Per D92, freeze/invariant guards get write-first-watch-fail discipline; ordinary feature logic follows
the repo's normal tests-before-implementation rule. What needs a guard, and why:

- **`IndexHandoff` contract compliance.** Index's `BalanceReceiver` must match Post's already-shipped,
  frozen contract exactly — package, action, permission, and the two extras (`balance` as
  `"0.00"`-formatted text, `asOf` as `"yyyy-MM-dd"`). A guard test constructs the receiver's manifest
  declaration and asserts it against `IndexHandoff`'s own constants, so a typo here is a build-time
  failure rather than a receiver that Post silently can never reach.
- **The new frozen surface, effective from Index's first ship onward.** `IndexIdentity.ApplicationId`,
  the renamed WorkManager class/work-name, and any `PreferenceKeys` Index keeps — none of these existed
  to freeze before this phase (D01 starts a new package), but once Index ships, every one of P11's own
  releases after the first must not move them. The guard is written once Release A picks the actual
  names, following `FrozenInvariantsTests`'s pattern in the other heads.
- **Zero-based balance check.** `BudgetMonth.IsBalanced` (income minus every category equals zero,
  D100) is pure arithmetic over `decimal` — a guard against a `double`-typed regression sneaking back in
  anywhere on this path, given Finding 3.2 above.
- **`D66`'s retention rule.** A guard that a `Transaction` row for an approved `BudgetMonth` older than
  the current cycle is gone, and that its category's `BudgetCategoryAmount.ActualAmount` survives —
  written to fail first against a naive implementation that keeps everything.
- **No two BalanceReceiver match targets collide.** D87 (manual account, merchant-rule match) and D91
  (Plaid-connected institution, staleness fallback) must not both claim the same incoming balance —
  a guard exercises a receipt against both a manual and a Plaid account with the same merchant name and
  asserts exactly one match wins, by the precedence D91 states (fresher receipt over stale Plaid).

## 5. Release breakdown

Per D57, everything discovered in Gate 1 stays inside P11 — this is sequencing, not a second phase.

- **Release A — Rebrand core.** Package id, app identity, CI workflow rename (`build-index.yml`),
  IconStudio entry with the locked coin-staircase artwork replacing `Glyphs.Ledger`, full
  `Storyworthtelling.Maui` kit adoption (D28), `Account`'s `double`→`decimal` migration, the
  `BalanceReceiver` implementation + its guard, manual accounts (D08/D14/D108), the Admin-mode Chrome
  connect flow (D20) replacing today's dead end, Jordan's install skeleton (D09/D25/D35/D36) reading
  the new household snapshot file, versionCode band 1.
- **Release B — Snowball.** Sheet import (one-time seed, D03), `SnowballPlan`/`SnowballSnapshot`
  schema, focus-order editing (D04/D43), extra-payment input (D05/D73), the two burndown charts
  (D17/D76/D113), balances regrouped by role (D13/D84).
- **Release C — Budget v1 (simple, not zero-based).** `Storyworthtelling.Handwriting` extraction
  (D54/D61), the reMarkable worksheet delivery (D52, using `IRemarkableClient`'s existing
  folder-upload), category limits vs. actual (D72), transactions-in-Budget-tab (D63), merchant
  recategorization rules (D58/D62/D67), the D49 illegible-category resend path.
- **Release D — Savings.** `SavingsGoal`/`SavingsSnapshot` schema, per-account goals (D50/D82), the
  interest-rate and trend-based projection paths (D82/D83), the monthly growth chart (D81).
- **Release E — Zero-based budgeting + income detection.** D100–104, sequenced last per D105 precisely
  because Finding 3 above confirms there is no existing component to build on — this is where the new
  algorithmic risk lives, isolated from every earlier release's simpler tracking.
- **Release F — Tracking-only mode.** D94–97, layered onto Release C's schema once it exists.
- **Release G — Credential-health surfacing, Jordan's-install parity, and the on-device test plan.**
  D80, closing the documented UI diff (D29), and the Drive-Field-notes-style on-device plan naming D93
  as its top risk.

## 6. Deliberately not built

- **No automatic cascade from a cleared debt into savings** (D53) — Snowball, Budget, and Savings stay
  independent views; Budget's line items (D98/D99) are the only connective tissue, and even that is
  manual entry, not an automated hand-off.
- **No per-loan payoff milestone markers on the burndown** (D18) — one clean total-debt line.
- **No target-date input on a savings goal** (D50/D82) — only a target amount; the date is always
  computed.
- **No staleness nudge for manual accounts** (D40) — credential health stays scoped to Plaid auth
  failures.
- **No paper roundtrip for Savings** (D51) — Budget is the only pillar that touches the reMarkable.
- **No device notification for a snowball milestone** (D31) or for the worksheet's silent delivery
  (D89).
- **No separate transactions-register screen** (D63) — transactions live inside the Budget tab only.
- **No "whose install is this" label** (D44) — the signed-in Google account is the only signal, and
  Score's identical, unresolved ambiguity is explicitly out of scope for P11.

## 7. What is **not** verified

- **The Admin-mode Chrome bank-OAuth path end to end, on a real locked phone** (D20/D93) — the
  mechanism exists and is exercised for other providers, but no bank's actual consent flow has been
  walked through it. This is the phase's single highest-weight on-device risk.
- **A real Plaid production connection** — D45 accepts sandbox data + the existing fake-HTTP suite as
  Gate 2 evidence; the first live linking is deferred to the on-device plan, same as every other phase.
- **Jordan's actual phone reading a published household snapshot** — the file shape is decided in
  this gate (§3) but has never been produced or consumed by real code.
- **Income detection's real-world accuracy** (D101–103) — there is no data to validate it against
  until Release E, and no comparable prior art in this codebase to estimate accuracy from.
- **The reMarkable worksheet's actual printed page and the vision transcription's real accuracy against
  the owner's and Jordan's handwriting** — `Storyworthtelling.Remarkable` and the generalized
  handwriting extractor are each proven independently (fake-HTTP tests), but never together, on a real
  device, with this specific worksheet layout (which Gate 3's mockups haven't drawn yet).
- **The generalized `Storyworthtelling.Handwriting` extraction actually compiling and passing once
  moved** — read and scoped in this gate, not yet extracted; D61's "straightforward" claim is a
  prediction based on the test surface, not a completed move.
- **Whether the exact bank OAuth hosts (the large national banks') that D79 asks to
  front-load are complete** — no bank's real consent-page asset hosts have been enumerated against
  `LoginBrowserUrlAllowlist`; this is explicit on-device tuning territory per the existing comment in
  `PolicyContext.cs`.

## 8. What this phase must not break

- **Post's `IndexHandoff` contract** (`FocusPhone/Codex.Domain/Post/MoneyMail.cs`) — already shipped,
  frozen; Index implements the receiving end exactly as specified, with no changes requested of Post or
  Plate.
- **The existing Ledger/Index test baseline** — 80/80 today; every release above must hold or grow that
  number, never shrink it.
- **`Storyworthtelling.Remarkable`, `.Voice`, `.Maui`, and `.Updates`** — Index is a new consumer of
  each; none of Release A–G's work should require a shared-library change beyond the
  `Storyworthtelling.Handwriting` extraction itself (which is additive — Foreword's existing behavior is
  re-verified against its own suite, not redesigned).
- **`PolicyContext.LoginBrowserUrlAllowlist`'s existing entries** (Google/GitHub/reMarkable/TIDAL) —
  D20/D79 add bank hosts; nothing already there should need to move.
- **Every other app's `SecretsVault` section** — the new household-snapshot file (§3) is deliberately
  *not* a vault section, so it carries no risk of colliding with another app's wire name the way
  Foreword's `SecretsVault.Foreword`/`"DailyLoader"` mismatch once did.

## Appendix A. The Debt Snowball sheet's formulas (added at Gate 3)

Snowball calculates with the sheet's own predictive formulas (design book D134). The sheet is **the owner's
own Debt Snowball Google Sheet** (tabs "Loans Today" and "Loans Over Time", plus two chart tabs); **its values
are not in this repository**, because this repository is public and they are the household's finances. What
is recorded here is its *shape*: the formulas, which were read out of the workbook itself (exported as `.xlsx`
and opened with `openpyxl`), not inferred, and the quirks they have. A Python reimplementation reproduced
every figure on the real sheet at Gate 3. Record the formulas here so implementation does not have to
rediscover them.

Where the numbers live instead:

- **The engine's guard (G7)** is checked against an independent Python oracle,
  `Ledger/Ledger.Tests/Snowball/Oracle/sheet_oracle.py`, which is written from the formulas below and shares no
  code or approach with `SnowballEngine`. Its inputs are **invented** (fictional lenders, made-up figures; only
  the shape is the owner's: ten loans in focus order, the first three already paid off, a mortgage-like loan
  last). Its output is committed as `sheet_expected.json` and embedded in the test assembly. The verification
  tables below are that output.
- **The recorded weekly history** (tab "Loans Over Time") is not in the source or in any APK. It arrives from
  the owner's Drive: the owner exports the tab as JSON, seals it with the vault passphrase using
  `Tools/IndexHistorySeal`, and uploads the result to the root of their Drive as
  `storyworthtelling-index-snowball-history.json` (`Ledger/README.md`, "Importing your Debt Snowball history").
  Index reads it once, on the owner's install. Tests and mockups use an invented, shape-faithful fixture
  (`Ledger/Ledger.Tests/Snowball/Fixtures/synthetic-snowball-history.json`).

### Tab "Loans Today": one row per loan, in snowball (focus) order

| Col | Header | Kind | Formula for row *n* (first loan row is 2) |
|---|---|---|---|
| A | Loan | input | name |
| B | Amount | input | current balance |
| C | APR | input | fraction, e.g. `0.2450` |
| D | Weekly Payment | input | the loan's own weekly payment |
| E | Focused Weekly Payment | formula | row 2: `=D2`; row *n*: `=E(n-1) + D(n)` (a running total of weekly payments) |
| F | Weeks Before Focused | formula | row 2: blank (0); row *n*: `=H(n-1) + F(n-1)` |
| G | Amount Before Focused | formula | `=B(n) - (D(n) * F(n) * (1 - C(n)))` |
| H | Weeks to Payoff | formula | `=IF(G(n)=0, 0, CEILING.MATH(NPER(C(n)/52, -E(n), G(n))))` |
| I | Payoff Date | formula | row 2: `=M1 + 7*H2`; row *n*: `=I(n-1) + 7*H(n)` |
| M1 | (Last payment date) | input | a date; label in L1 |
| M(n) | | formula | `=B(n)` |
| B13 / D13 / H13 / M13 | Total | formula | `=SUM(B2:B11)`, `=SUM(D2:D11)`, `=SUM(H2:H11)`, `=I11` (debt-free date) |

The sheet itself has no extra-payment cell. Index's one overall extra weekly payment (D05/D73) is added to
row 2's focused payment only (`E2 = D2 + extra`) and rolls down through the running total like any other
payment.

What these formulas actually do, which implementation must reproduce exactly and must not "correct":

- **Interest before a loan becomes the focus is a share of the payment, not of the balance.**
  Each week before the loan is the focus, its balance drops by `payment × (1 − APR)`. A balance-based
  weekly model gives a materially different debt-free date (on the invented example below, about twenty
  weeks later than the sheet's), mostly because it charges a mortgage-sized loan nearly half of each
  payment as interest (about 45% of the $310 payment in the example) where the sheet charges the loan's APR
  as a share of the payment (4.25% in the example). The owner chose the sheet's formulas (Gate 3); the
  difference is known, not a bug to fix.
- **After it becomes the focus, the loan amortises properly** at `APR/52` on the focused payment
  (Excel `NPER`, payment negative, no future value, payments at period end), rounded **up** to whole weeks
  by `CEILING.MATH`.
- **The focused payment is cumulative:** each loan's is the previous loan's focused payment plus its own
  weekly payment, so paid-off loans' payments roll down the list.
- **Dates are whole weeks:** the payoff date is the previous loan's payoff date plus `7 × H` days. It is
  not the balance-crossing date.
- **Row 2 quirk:** `G2` multiplies by `E2` (the focused payment) where every other row uses `D`. It is
  harmless only because `F2` is blank. Implement the general rule (`D × F`); with F = 0 both agree.
- **`H = 0` when `G = 0`,** so a paid-off loan (balance 0) takes no weeks and adds nothing to the dates of
  the loans after it. (The `IF` is redundant with the arithmetic: `NPER` of a zero balance is already 0, at a
  positive rate and at 0% alike, so `CEILING.MATH(0)` gives the same answer. It is kept in the spec because
  the engine must not take a logarithm of a zero balance, and so must special-case it the same way.)
- **A 0% loan has no logarithm to take:** with `C = 0` the weeks are simply `G / E`, rounded up.

### Verification: invented inputs, checked by the oracle

Inputs (B, C, D) and the expected outputs (E, F, G, H, I). The guard test seeds exactly these inputs and
must reproduce every output, to the cent and the day. **All lenders and figures below are invented**; the
tables are the committed `sheet_expected.json`, regenerated by `sheet_oracle.py`. The first scenario is the
one the Snowball mockups and `SyntheticSnowballHistory` also use.

**Scenario 1: ten loans, three paid off, mortgage last.** Last payment date (M1): 2027-03-19. Extra weekly payment: 0.

| Loan | B | C | D | E | F | G | H | I |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Northfield Student Loans 1 | 0.00 | 0.0550 | 150 | 150 | 0 | 0.00 | 0 | 2027-03-19 |
| Northfield Student Loans 2 | 0.00 | 0.0540 | 150 | 300 | 0 | 0.00 | 0 | 2027-03-19 |
| Cedar Hollow Bank | 0.00 | 0.1650 | 75 | 375 | 0 | 0.00 | 0 | 2027-03-19 |
| Meridian Card | 7412.36 | 0.2650 | 90 | 465 | 0 | 7412.36 | 17 | 2027-07-16 |
| Skyline Miles | 11238.90 | 0.2450 | 80 | 545 | 17 | 10212.10 | 20 | 2027-12-03 |
| Bluebird Visa | 13905.25 | 0.2150 | 85 | 630 | 37 | 11436.43 | 19 | 2028-04-14 |
| Lakeside Credit Union | 15620.40 | 0.0350 | 40 | 670 | 56 | 13458.80 | 21 | 2028-09-08 |
| Harbor Auto Finance | 24912.40 | 0.0690 | 140 | 810 | 77 | 14876.22 | 19 | 2029-01-19 |
| Cobblestone Finance | 36402.80 | 0.0745 | 130 | 940 | 96 | 24852.56 | 27 | 2029-07-27 |
| Maple Ridge Mortgage | 171350.00 | 0.0425 | 310 | 1250 | 123 | 134840.53 | 113 | 2031-09-26 |
| **Total** | 280842.11 | | 1250 | | | | 236 | **2031-09-26** |

**Scenario 2: live first loan with an extra weekly payment and a 0% loan.** Last payment date (M1): 2027-03-19. Extra weekly payment: 35.

| Loan | B | C | D | E | F | G | H | I |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Bluebird Visa | 5200.00 | 0.2299 | 60 | 95 | 0 | 5200.00 | 63 | 2028-06-02 |
| Cobblestone Finance | 9000.00 | 0.0000 | 50 | 145 | 63 | 5850.00 | 41 | 2029-03-16 |
| Harbor Auto Finance | 14000.00 | 0.0690 | 140 | 285 | 104 | 444.64 | 2 | 2029-03-30 |
| Lakeside Credit Union | 14800.75 | 0.0475 | 55 | 340 | 106 | 9247.68 | 28 | 2029-10-12 |
| **Total** | 43000.75 | | 305 | | | | 134 | **2029-10-12** |

**Scenario 3: paid-off first loan with an extra weekly payment.** Last payment date (M1): 2027-03-19. Extra weekly payment: 20.

| Loan | B | C | D | E | F | G | H | I |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Northfield Student Loans 1 | 0.00 | 0.0550 | 120 | 140 | 0 | 0.00 | 0 | 2027-03-19 |
| Meridian Card | 3050.00 | 0.2650 | 45 | 185 | 0 | 3050.00 | 18 | 2027-07-23 |
| Skyline Miles | 8800.40 | 0.2450 | 70 | 255 | 18 | 7849.10 | 34 | 2028-03-17 |
| **Total** | 11850.40 | | 235 | | | | 52 | **2028-03-17** |

Rounding: G is money, so round to cents (half up); the NPER argument uses the unrounded rate `C/52`. The
oracle finds the weeks by exact rational arithmetic (pay one week at a time until the balance clears) and
computes the logarithm form only as a cross-check; it refuses to write anything where the two disagree or
where a value sits within 1e-6 of a whole week.

### Tab "Loans Over Time": the recorded history (shape only)

- One row per loan (rows 2–11, the same order and names as "Loans Today"), one column per week,
  header row 1 holding the date as text (`M/D/YY` in the first column; later weeks written `M/D/YYYY`).
- **About seventy recorded weeks** in the owner's history, typed in by hand each week. The columns run on
  for years beyond the last recorded week, empty. The invented fixture uses 70 weeks and ten loans.
- **One interpolated week:** a week the owner missed is a formula in every loan row, `=(next + previous)/2`,
  the average of the weeks either side. An imported history has to keep that value rather than treat the
  week as missing.
- **Row 15 "Total"** is `=SUM(B2:B11)` per week, over 104 columns; row 13 is labelled "Payoff Date" and
  holds nothing.
- What the history looks like, in shape: the total is almost flat over the period because one loan first
  appears part-way through (a zero before it, a balance after); the total without the mortgage peaks in the
  week that loan arrives and falls steadily afterwards; one loan reads zero from some week on. The invented
  fixture reproduces those features (a loan that arrives mid-history, an interpolated week, a loan that
  reaches zero) with made-up figures.

### The sheet's two graphs (chart tabs)

- **"Loans Chart"**: a **stacked bar** chart, one bar per week, one segment per loan.
- **"Loans Except Mortgage"**: a **line** chart, one line per loan.
- **The exported file is not a reliable record of the live charts.** In the `.xlsx` export both charts list
  nine series, rows 2–10, with no reference to the mortgage's row (the last). The owner confirmed the
  **live** stacked-bar chart does include the mortgage, so Google's export evidently drops series. Treat the
  live sheet as the truth, and don't infer chart contents from an export.
- Both exported ranges stop at the same fixed column (the 104th week), so recorded weeks after that would
  fall off the sheet's graphs unless the range were widened by hand. Index's charts have no fixed end.

## Appendix B. Plaid plan and billing (checked at Gate 3, 26 Sep 2026)

From Plaid's billing documentation (plaid.com/docs/account/billing), read on 26 Sep 2026:

- **Pricing models.** One-time per connected account (Auth, Identity); a monthly subscription per Item for
  as long as it exists (Transactions, Liabilities, Investments); and a flat fee per successful request
  (Balance, `/accounts/balance/get`, and Signal).
- **Trial plan.** Free, for Plaid teams in the US and Canada **created on or after 15 April 2026**. It covers
  Assets, Auth, Balance, Identity, Investments, Liabilities, Transactions and Statements for up to
  **10 Production Items**. The cap counts Items *created*: after the tenth, more Items need a paid plan.
- **Pay-as-you-go.** No minimum; each Balance call is billed.

What Index does with this:

- An Item is one sign-in at one institution (one bank's checking, savings and two cards are one Item). The
  household is a handful of Items, inside the Trial's ten.
- **Refresh on every open, no cool-off** (design book D149), because Balance isn't billed on the Trial. If the
  owner's Plaid team predates 15 Apr 2026 and is on Pay-as-you-go, add a 15-minute cool-off. **Not yet
  checked against the owner's Plaid dashboard.**
- **Reconnect with Link's update mode** on the existing Item, never a fresh Link (D150), so repairing a broken
  connection doesn't spend one of the ten Items.
