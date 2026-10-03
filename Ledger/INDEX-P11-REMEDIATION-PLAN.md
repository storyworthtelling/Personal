# Index (P11) — remediation plan

Written after a post-implementation audit of every screen and printed page against the seventeen
approved mockups (`Ledger/design/mockup-01` … `-17`), plus a check of the guard ledger in
`INDEX-P11-PLAN.md` §4 against what exists. The audit found **three Gap and fourteen Partial** verdicts
and no clean match. This plan closes what it found.

It extends `INDEX-P11-PLAN.md`; that plan's §0 (the mockups are the specification), §1a (commits on
`claude/index-rebrand-p11-vjnns3`, no PR until the owner asks), §2 (agent levels L1/L2/L3, mockups
embedded in every UI brief) and §2.1 (standing rules) all still apply and are not repeated here.

**Already done before this plan:** the three guard gaps the audit's first pass found —
`IdentitySeparationTests` now includes Index (22.5° from Folio, the closest pair), `NoEmptyCatchTests`
(G13) and `MakersMarkEverywhereTests` (G15) exist and were each watched to fail.

---

## 0. Decisions taken (the owner, this session)

| # | Decision | Effect on the plan |
|---|---|---|
| **R1** | Scope: **everything, in phases.** | Phases 1–5 below are all scheduled; each phase is its own commit set. |
| **R2** | Her install keeps **deriving its role from the household file** (no explicit "whose phone" choice). | Gating must work from *that* signal. §1.1 designs around its weakness (no signal before the passphrase) instead of replacing it. |
| **R3** | Admin mode is detected by **asking whether Chrome is suspended**; Flyleaf is not touched. | `PackageManager` flag check plus a `<queries>` entry for `com.android.chrome`. §1.2. |
| **R4** | **Keep tap**; amend the mockups. Long-press is not built. | The tap-for-long-press substitution becomes an *approved departure*, recorded in §6 and in the design book. Not a finding any more. |
| **R5** | **The owner's voice is not pending** (recently changed in Folio). | Voice seats are real work (§3.7). CLAUDE.md's "Owner's voice is pending" paragraph is stale and is corrected in §6. |
| **R6** | **Additive changes to the shared kit** for the Waiting page: optional notice timestamp, Reference and Progress (teal) hues, newest-first order. | Touches `Storyworthtelling.Maui` and `Codex.Ui.Android`; every app that uses the dog-ear is rebuilt and re-tested (§3.5). |
| **R7** | **Extend the household snapshot** with weekly Snowball and monthly Savings series. | New file version; older files still read (§3.6). |
| **R8** | **Map Plaid's category** onto the owner's spending categories; merchant rules still win. | `PlaidTransaction` gains the category; a mapping table the owner reviews (§2.4). |

**Residual risk accepted under R2, stated plainly.** Before the passphrase has been typed, an install
cannot read the household file, so it cannot know whose phone it is. §1.1 closes every *write* path
that depended on knowing, but the first-launch screen on her phone can still be wrong until the vault
opens. That is a documented limit, not a fix, and it is on the device test plan.

---

## 1. Phase 1 — Blockers (do first, in this order)

Each of these either exposes the owner's credentials, makes a screen unusable on the locked phone, or
silently corrupts data. All are **L3** for the design and the review; the edits themselves are L2.

### 1.1 Her install must never touch Plaid or the owner's vault section  *(mockup 11)*
Findings: restore, the refresh worker and the snapshot publisher are not gated on role; Save publishes
an empty Plaid section; `SecretsVaultService` is built without the signed-in account, so the
`VaultOwnership` guard never runs; five pages each re-read Drive to resolve the role.

1. **Wire the ownership guard.** `MauiProgram.cs:151` constructs `SecretsVaultService` with the signed-in
   account so `VaultOwnership` can refuse a write by the non-owning account. Guard **R-G1**.
2. **Resolve the role once**, in one `InstallRoleService`, cached in the local settings and refreshed on
   sign-in or passphrase change. The five pages read the cached value; none re-reads Drive.
3. **Resolve it before restore.** Restore needs the passphrase to decrypt anyway, so the same step
   reads the household file: a file published by a *different* account means Hers (skip restore, skip
   scheduling `RefreshWorker` and the worksheet worker); no file means Owner. Guard **R-G2**.
4. **A publisher never takes over a file another account published.** `HouseholdSnapshotPublisher`
   refuses when the existing file's publisher is not the signed-in account. This is what keeps her
   phone from flipping the owner's install to read-only. Guard **R-G3**.
5. **Save merges, never replaces.** A Save from an install with an empty local Plaid section must not
   write one. Guard **R-G4**: save on an install with no local credentials leaves the vault's Plaid
   section byte-identical.
6. Her Settings is the mockup's (§4.1): no pinned Save bar, no Plaid wording, no environment text.
7. Her Accounts clears the pinned "Add an account" bar in the household branch.
8. Fix the Snowball overwrite (**R-G5**): `RenderLoansFromHouseholdAsync` sets `extraEntry.Text` under
   `suppressExtraEntryTextChanged`, the same guard the owner path already uses.

*Unresolved-role behaviour (the R2 limit):* an install with no passphrase shows first-run state 2
("Your banks are in the vault") rather than the Plaid empty state, and never offers Add.

### 1.2 Connect a bank: detect Admin mode, build frames 2 and 3  *(mockup 5)*
Findings: the page gates on `IsRestrictedViewer(defaultBrowser)`, which stays true in Admin mode, so
on the locked phone it shows "Admin mode needed" forever and no bank can be connected. Frames 2 and 3
are not built; the old "tap Done" flow and copy remain; the header and lit tab are wrong.

1. `ConnectBrowserPolicy` gains `AdminModeState` from Chrome's suspended flag (R3). Manifest `<queries>`
   gets `com.android.chrome`. Chrome absent is a third state with its own sentence. **Read the
   `PackageManager`/`ApplicationInfo` member names from the binding's metadata first.**
2. Build the three frames from `mockup-05.html`: frame 1 as today (copy already verbatim); frame 2
   "Admin mode is open" with the three numbered steps and the single pinned **Open Plaid in Chrome**;
   frame 3 "Finishing up" with the teal status naming the bank, "Open Chrome again", and **Check again**.
   **No Done button anywhere** (**R-G6**, the existing `AdminModeUntimedTests` extended to also scan
   `AddAccountChooserCard`, `MainPage` and Domain strings, and to match spelled-out durations).
3. Header "Connect a bank"; the **Accounts** tab lit, whichever screen opened it. Stop opening the
   browser automatically in `OnAppearing`.
4. The chooser path checks `SettingsStore.IsConfigured` as Settings does, so unset keys give a sentence
   rather than a raw error.
5. Update `ConnectBrowserPolicy`'s stale remarks and add tests for each state (**R-G7**).
6. **Not verifiable here:** the real flag value under the lockdown. It stays on the device test plan.

### 1.3 Fetch and store the bank's `primary_color`  *(mockups 3, 15, 16)*
`SetInstitutionColorAsync` and `GetInstitutionAsync` have no caller, so every post-it is the default
yellow and the tint pipeline is unreachable. Fetch on link (`PlaidLinkPage`) and as a once-only backfill
for banks already connected (in `BalanceRefreshProcessor`, failure logged to Diagnostics, never thrown).
Pass the colour through `SavingsAccountView` and `SnowballPage` so mockups 9/16/6 get it too.
Guard **R-G8**: a refresh of an institution with no stored colour calls the lookup exactly once.

### 1.4 Snapshot cadence is weekly, not daily  *(mockup 6, D78)*
`RecordCurrentWeekSnapshotAsync` keys on today's date, so History gains a "week" per day opened.
Normalise `weekOf` to the sheet's week (the Friday on or before today, Appendix A's dates are Fridays).
Guard **R-G9**: ten opens across a week write one row per loan; an open after the next Friday writes a
second.

### 1.5 Dates must not move a day  *(mockups 2, 3, 15)*
Plaid dates and hand-entered due dates are stored as UTC midnight and shown with `.LocalDateTime`, so
every US-zone phone shows "Due Oct 2" for Oct 3. Treat date-only values as dates: display the stored
date, not its local instant. Guard **R-G10** runs under a UTC−7 and a UTC+10 zone.

### 1.6 Disconnecting a hand-entered bank must be undoable  *(mockup 2)*
Cascade delete plus a restore that skips blank-token credentials means the undo does nothing and the
accounts are lost. Snapshot the institution *and its accounts* (and each account's Savings override)
before removal; restore from the snapshot; skip `/item/remove` when the token is blank. Guard **R-G11**.

---

## 2. Phase 2 — Functional bugs

**L2** each, with an L3 review at the end of the phase.

### 2.1 Worksheet follow-up pages  *(mockup 13)*
1. **Overlap.** `FollowUpRegions` places the "change another line" boxes at a fixed offset the reference
   list runs through. Compute the reference list's height first and place the label, boxes and footer
   below it, in the mockup's order. Guard **R-G12**: no painted box intersects a painted text run, for
   0, 2 and 9 reference lines (the existing region/painter pair, asserted geometrically).
2. **Arrival.** The follow-up cannot start before Nov 1 for an October sheet because the first read-back
   only happens on the last day of the month. Add a daily read-back of a *freshly delivered* worksheet
   (the next morning, D153), which also applies the written limits at once. Guard **R-G13**.
3. **Blank savings box** keeps last month's figure: `EnsureSavingsSetAsideAmountsAsync` seeds the row from
   the printed figure, not zero. Guard **R-G14**.
4. **Page-two boxes.** `WorksheetRegions.AllBoxes`, `BoxesFor` and the read-back instruction describe one
   page; the painter lays out two. Give the regions the page index, so the painter and the prompt read
   the same boxes (G18 extended). Remove the empty-page "continued" header when page two has no rows.
5. **Savings lines** carry the account name ("Emergency fund"), not "Savings".
6. **Row order** in the over case is largest rise first (the trim order).
7. **Unprinted target.** Record `ZeroBasedTargetWholeDollars` only when the page printed it; with no
   set-aside lines the balance check does not run.
8. **Verify the double count** of the extra payment against Appendix A before touching it
   (`WorksheetPlanBuilder.cs:60` against the balance check). If confirmed, fix with a test from the
   mockup's own numbers ($3,100 with $350 inside); if not, add the test that proves it and stop.

### 2.2 Accounts and editing  *(mockups 2, 15)*
1. **Editing keeps focus.** `SaveAll` stops rebuilding the page; it updates the card in place. It
   writes only the field that changed (no `DateTime.Today` or "Month" defaults into untouched fields)
   and is no longer `async void` without a handler. Guard **R-G15**.
2. **Validation.** A bad APR/Payment/Balance turns the box rose with a line saying how to fix it;
   `$`, `,` and `%` are accepted. Same on the Add page. Guard **R-G16**: "6.75%" saves 6.75.
3. **Required tint** on APR and Payment until both are present.
4. **Keyboard.** The post-it lifts above the keyboard; the pinned bar hides while it is open.
5. Savings (Plaid) post-it omits APR when Plaid reports none.

### 2.3 Budget  *(mockups 7, 8)*
1. **Soft-deleted category** can crash the page (`categories.First(...)` over all amounts): filter to
   active, and make the month totals agree with the visible cards. Guard **R-G17**.
2. **Pending transactions** are keyed by their own id and double-count when they post. Replace the
   pending row with the posted one using Plaid's `pending_transaction_id`. Guard **R-G18**.
3. **Spending only (D140).** The month total and Uncategorized card exclude income, transfers and
   loan/card payments (taken from the Plaid category in §2.4). Guard **R-G19**.
4. A category with no limit and non-zero spending shows "No limit", not "$0.00 left".
5. First paint comes from cache; the Plaid refresh runs behind it with the status sentence (§3.4).

### 2.4 First category from Plaid  *(R8)*
`PlaidTransaction` carries `personal_finance_category` (primary and detailed). A pure
`PlaidCategoryMap` maps it onto the seeded spending categories; a merchant rule always wins; an unmapped
category lands in Uncategorized. **The mapping table is a deliverable the owner reviews** before it ships
(it is a short, readable file, not code buried in a service). Guard **R-G20**: rule beats map; map beats
Uncategorized; income and transfers are not spending.

### 2.5 Savings  *(mockups 9, 16)*
1. **Growth chart x-scale.** `MonthsBetween` clamps to 1 inside the position function, so the first
   point is not at the origin and a two-point line sits at the right edge. Clamp only the span
   denominator. Extract the mapping as a pure function. Guard **R-G21**.
2. **Empty state** is drawn as the mockup: no switch, no "Saved" block, the sentence centred in Serif
   italic. Applies to Growth and to her install.
3. Persist the goal on leaving the field *and* on navigating away (page disappearing).
4. A goal already met says so rather than "Around Oct 2026".

### 2.6 First run  *(mockup 14)*
1. **Restore after the passphrase.** Saving the passphrase runs `VaultSync.RestoreAsync` (only after the
   role is resolved, §1.1), so the banks return without a restart and Add never reappears. Guard
   **R-G22** extends G26.
2. State 3 fills in place while restoring (a poll or the worker's completion event), and a restored bank
   with no accounts yet or a failed first refresh stays visible with its last figure.
3. The voiced line stays hidden in first run (the late `ShowWelcomeAsync` re-shows it).
4. "Enter the passphrase" scrolls to and focuses the field.
5. **Known limit, not fixed:** before the passphrase the "vault holds banks" test is a file-exists proxy.
   Stated in the on-device plan.

### 2.7 Settings and her Settings  *(mockups 10, 11)*
1. Fields save themselves (passphrase on leaving the field); **no pinned Save bar**; no pop on save.
2. The category-removal undo uses the dog-ear, not a bespoke bar near the top.
3. Refresh on open: cached paint first, "Refreshing balances…" then "Refreshed just now" (D149).
4. **CI version** `build-index.yml` passes `1.0.<run_number>`; the foot reads "Index 1.0.N". (An earlier
   draft of this plan said the workflow passed `0.1.N`; it never did. That was an audit error, corrected.) Guard **R-G23** reads the workflow, as `DisplayVersionTests` does for Folio.
5. The leaked developer note on the Shared keys row is removed; "Set in Folio" stands alone.

---

## 3. Phase 3 — Drawn but not built

**L2** each; the kit changes in §3.5 are **L3**.

### 3.1 Snowball  *(mockup 6)*
Legend (swatch and name for each loan); y-axis with 50k/100k/150k/200k gridlines; touching a bar or panel
reports **that week's** date and balance (stacked bars report per-segment, panels report the touched
index); the History empty sentence; the `Focus` pill chip; the number inside the card; the ninth panel
half-width and "(mortgage)"; "Mon 'YY" axis labels at calendar months; the footnote's date prefix;
true strikethrough for paid-off loans. Guard **R-G24**: hit-test returns the touched week.

### 3.2 Budget  *(mockup 8)*
Tracking-only "Last month" row (D142); **D49 resend** — an unreadable box is carried on the next
follow-up page so "Sent back to the reMarkable to check" is true, with the resend asserted by test
(**R-G25**); "3 more" and "Earlier months" expand; her install raises the dog-ear for unreadable boxes
and totals the same as the owner's.

### 3.3 Accounts and the post-it  *(mockups 2, 3, 15)*
Category and relative date on each transaction row; "n more" lengthens the note in place (and the fetch
is no longer capped at 30); the "From Post, Sep 14" variant (a `BalanceSource` on the account, with a
migration; `ApplyBalanceReceiptAsync` sets it); "No payment yet"; "By hand"; the action card subtitle
"4 accounts · first on the page" and a dimmed Move up on the first bank; subtype labels ("Checking
····7301", not "Account"); absolute "As of Sep 22"; status line "Refreshed 2h ago" (no dash).

### 3.4 Loading and refresh
Every refresh paints from cache first and states progress in a sentence (DESIGN.md §5); nothing blocks
the first render on a Plaid call.

### 3.5 The Waiting page and dog-ear  *(mockup 17; shared kit, R6)*
1. `DogEarNotice` gains an optional timestamp (default null); `WaitingPage` prints the relative date and
   orders newest-first; notices without a date keep insertion order. Existing callers are unchanged.
2. `DogEarHue` and `DogEarPalette` gain **Reference** (slate) and **Progress** (teal); the native
   `Codex.Ui.Android` dog-ear gets the same two. Stale-balance becomes Caution, the Trial notice
   Reference, and the Budget "waiting" notice Progress.
3. The four notice texts become the mockup's, verbatim, with the actions "Reconnect Meridian Card" and "Open
   Evergreen Home Lending"; the actions find their target by id, not by matching the headline.
4. `WaitingPage` takes the Accounts frame (binder strip with the area lit, graph paper), hides the
   system nav bar, shows "For Accounts · newest first", and updates live so "Nothing waiting." can
   appear while it is open.
5. The ear sits at the page's top-right corner (right 66dp), 40dp, with the mockup's flap and stroked
   arrow, not below the header at 72dp.
6. **Guards:** **R-G26** asserts each notice's exact string against the text in `mockup-17.html`;
   **R-G27** asserts newest-first with dates; `AccountNoticesTests` stops asserting loose substrings.
7. Every other app that uses the dog-ear is rebuilt and its suite run; Plate and Foreword compile in this sandbox, so compile them here.

### 3.6 Her install's charts  *(R7)*
`HouseholdSnapshot` gains a version, a weekly Snowball series and a monthly Savings series. The reader
accepts the old version (series absent shows today's sentence). The publisher writes both. Her History
and Growth render from the series. Guard **R-G28**: a v1 file still reads; a v2 file round-trips.

### 3.7 Voice seats  *(R5)*
First read how Folio now composes and stores the voice (a short L1 read-only task; the audit did not).
Then add a seat on Snowball ("Nineteen percent down since the peak in June 2023."), Savings (a *rate*,
never a balance: "Three hundred a month, month after month." — D114) and Accounts (data-driven, from
the real trend and loans-behind-you counts, not the one fixed sentence). Each is hidden with no profile
or key. Guard **R-G29**: the Savings composer's instruction forbids amounts; the Accounts context carries
figures only as percentages.

### 3.8 Add by hand  *(mockup 4)*
Kind decides the form: a Card asks for its limit (`AddManualAccountAsync` gains it); a Bank account hides
APR, Payment, Every and Next due; a Loan is as drawn. Validation runs on blur, then live, per field.
Field material is the kit's `MonoEntry` and boxed style; the segmented control fills the width
(an additive option on `PaperSegmented`); Next due keeps the platform picker (a documented departure),
but opening it no longer saves today.

---

## 4. Phase 4 — Cosmetic drift (one sweep, L1 each, mockup embedded in each brief)

Grouped so each brief owns disjoint files.

| Group | Items |
|---|---|
| **Money and type** | Limits in whole dollars, rounded to nearest ("of $625"); **payments round up** to the next whole dollar, never down ("$466 a week now" for 465.38, "$546/wk", "Due Oct 6 · $286" for 285.42, and the worksheet's "Snowball payments" line) through the one `PlanMoney.Payment` formatter that `AccountsMoney.Payment` delegates to; balances keep cents. U+2212 minus. Curly apostrophes in copy. Debt-free date in mono. History note's possessive ("1 month's"). |
| **Chevrons** | Real down/up chevron shapes replace "▸/▾" on Accounts, Snowball, Budget and Savings cards. |
| **Spacing** | Cards 8dp apart, heading to card 8dp, group margin 22dp; well padding 24/16; graph paper behind the page header; post-it overlaps and indents (`-6 4 4 22`); no extra rule under the Budget month line. |
| **Materials** | Soft ink (not muted) on index-card secondary text and post-it footers; boxed `MonoEntry` fields with a `$` prefix and `%` suffix; required tint; the Add chooser and action cards use the paper-deep 86% haze over the whole page with icons and semibold green on the primary row. |
| **Charts** | Savings y-axis labels and yearly x labels, hollow marker at the projected goal end; Serif chart titles; "Sep '25" label format verified (the `''yy` literal). |
| **Worksheet pages** | Real previous-month name ("September's figure"); last-month column at the mockup's x; 1.5pt rules under headers and sections, hairline per row, 1pt above "Left to assign"; title at `PaperTypeScale.Display`; calc-column 270; name box 24pt; bold intro figure; mono right-aligned footer date; `InvariantCulture` on the printed date; follow-up columns right-aligned, Final box 120×30, row pitch 19, "Extra payment" with the loan name. |
| **Cover** | The flourish matches the mockup's SVG (bulging toward the corner, second arc, curl, dot) rather than the inverted quadrant. |
| **Copy** | Empty-state and kicker centring; "Remove" at 48dp with a rule above; the invented "Plaid doesn't call it savings." line either added to the mockup or removed (see §6). |

**Guard R-G30:** the existing `HexLiteralLeakTests` and the kit tests run unchanged and must stay
green; the cosmetic sweep adds no literal.

---

## 5. Phase 5 — Outside Index (separate FocusPhone commits, flagged in the message)

1. **Wire `cover_index` into Flyleaf's shelf.** `HomeActivity.CoverResources`, an `Index` package
   constant in `PolicyContext`, a `BookClothPalette` binding to `#4E5F3A`, and `ShelfCoverArtTests`
   updated from `com.storyworthtelling.ledger` to `…index` (it still names the old app). The shelf
   cover art becomes **window only** (the shelf owns the rule, the band and the brackets), so the
   baked cloth, keyline and flourishes in `cover_index.png` stop doubling the shelf's own. This is
   Flyleaf's *display* code, not its lockdown policy; `PolicyContext.LoginBrowserUrlAllowlist` stays
   **untouched** and remains a recorded standing gap.
2. `Glyphs.Ledger` is retired once the renderer test no longer needs it as a probe.
3. Rebuild and re-test every head that uses the shared dog-ear or `PaperUi` (Post, Score, Flyleaf,
   Folio, Foreword, Plate; all of them compile in this sandbox).

---

## 6. Documents to correct (L1)

- `INDEX-P11-PLAN.md`: addendum **I15** recording R4 (tap, not long-press) as an approved departure, and
  R2's accepted limit.
- `Ledger/design/index-design-book.html` and the affected `mockup-NN.html` notes: change "long-press" to
  "tap"; decide whether mockup 12's three write-in lines and 36pt rows (superseded by mockup 13 and
  D157) should read as "Release F's page, superseded". PNG renders are not regenerated for a text note.
- `CLAUDE.md`: the "Owner's voice is pending" paragraph is stale (R5); the Index section replaces
  Ledger's, as the original plan's closing deliverables already require.
- DESIGN.md: §13 gains the Reference and Progress hues on the dog-ear (R6).

---

## 7. How it is run

- **Order:** Phase 1 → 2 → 3 → 4 → 5, rolling on this branch, no PR (§1a of the original plan).
- **Levels:** as marked. An **L3 agent reviews each phase's diff** before the orchestrator pushes.
- **Briefs:** every UI task embeds its mockup (HTML pasted, PNG to be opened first, book URL) and
  the audit finding it closes, quoted. Every task embeds the standing rules (§2.1 of the original plan).
- **Guards:** each `R-G` above is written first, watched to fail against the reintroduced bug, and the
  bug is named in the commit message. The previous release's ceremony gaps (Release D's G7/G8) are not
  repeated: a commit that adds a guard without a recorded red run is not accepted.
- **Verification per phase:** `dotnet test Ledger/Ledger.Tests/Ledger.Tests.csproj -p:LedgerTfms=net10.0`;
  the suite libraries touched; `dotnet build Ledger/Ledger.Android/Ledger.Android.csproj
  -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`; Phase 3.5 and 5 also run
  `Storyworthtelling.Maui.Tests`, `FocusPhone/Codex.Tests` and each affected head's build.
- **Rate limits:** the audit lost two rounds of subagents to session and weekly limits. Briefs are
  therefore small and disjoint, dispatched four at a time, and a failed agent is redispatched unchanged
  after checking the stated reset time and a clean tree.

---

## 8. Findings deliberately **not** scheduled

- Long-press (R4, approved departure).
- Next due as the in-place flip picker (documented platform-picker departure, §3.8).
- Mockup 12's 36pt rows and three write-in lines (superseded by Release H, D157).
- The vault-holds-banks proxy before the passphrase (§2.6, a limit, not a bug).
- The cross-account Drive visibility of the household file (unverified here; on-device).

## 9. Not verified, and stays that way until a device

The real Chrome-suspended flag under the lockdown (D93); whether her account can read and write the
shared vault and household file; Plaid's `primary_color` and category coverage for the household's banks;
the mapped categories against the owner's real transactions; the follow-up cycle on a real month
boundary; the voice lines against a live profile. "Tested against fakes" is not "works."

## 10. Guard ledger additions

| # | Guard | Reintroduces |
|---|---|---|
| R-G1 | Vault service refuses a write by the non-owning account | Drop the account argument in `MauiProgram` |
| R-G2 | Restore is skipped when the household file was published by another account | Restore unconditionally |
| R-G3 | Publisher refuses a file another account published | Overwrite it |
| R-G4 | Save with no local credentials leaves the vault's Plaid section unchanged | Publish blanks |
| R-G5 | Household Snowball render survives the Extra field's change event | Remove the suppression |
| R-G6 / R-G7 | No Done button; no duration in any connect string; each Admin-mode state | Re-add "tap Done" |
| R-G8 | Institution colour fetched exactly once | Never call the lookup |
| R-G9 | One snapshot row per loan per week | Key on today |
| R-G10 | Date-only values do not shift in UTC−7 / UTC+10 | Use `.LocalDateTime` |
| R-G11 | Undo of a hand-entered disconnect restores its accounts | Restore only credentials |
| R-G12 | No painted box overlaps painted text (follow-up) | Fixed 56pt gap |
| R-G13 | A delivered sheet is read back the next morning | Read only on the last day |
| R-G14 | Blank savings box keeps last month | Seed with 0 |
| R-G15 / R-G16 | Edit writes only the changed field; "6.75%" saves | Write all five fields |
| R-G17–R-G19 | Soft-deleted category; pending replaced by posted; spending only | The three current bugs |
| R-G20 | Rule > map > Uncategorized | Map before rule |
| R-G21 | Growth x-scale starts at the origin | Clamp inside the position |
| R-G22 | Passphrase save restores the banks; Add never reappears | Restore only at launch |
| R-G23 | CI display version is `1.0.<run_number>` | A foot line that prints a build stamp |
| R-G24 / R-G25 | Hit-test returns the touched week; unreadable box is resent | Report the last week; flag only |
| R-G26 / R-G27 | Notice strings equal the mockup's; newest-first | Loose substrings |
| R-G28 / R-G29 | v1 household file reads; Savings voice carries no amount | Break the old file; pass a balance |
| R-G30 | No literal added by the sweep | A hex in a page |

---

## 11. Pre-public data cleanup

The repository is about to go public, and Index is the app whose work touched the household's finances
most closely. Several early artifacts of this phase (the Snowball verification table, the history
fixture, the mockups and their PNG renders, some test inputs and a few documents) had been built from
the owner's real sheet and accounts. They were reworked so that **nothing in the tree is the owner's or
the household's real data**. What was done, by class, with no values recorded here:

- **The recorded Snowball history moved to the owner's Drive.** It is no longer a fixture, a resource
  or anything embedded in the source, the test assembly or an APK. The owner exports the sheet's
  "Loans Over Time" tab as JSON, seals it with the vault passphrase using `Tools/IndexHistorySeal`, and
  uploads the result as `storyworthtelling-index-snowball-history.json`; Index reads it once on the
  owner's install (`Ledger/README.md`). This supersedes any earlier text in the plan family that
  describes the history, or a copy of it, as embedded or shipped.
- **Tests and the oracle.** Every test input is invented (fictional lenders, made-up figures, `example.com`
  addresses). Guard G7 (`SnowballSheetTests`) now checks `SnowballEngine` against an independent Python
  oracle (`Ledger/Ledger.Tests/Snowball/Oracle/sheet_oracle.py`, its output committed as
  `sheet_expected.json`) rather than against a table of real figures; G8 and the History tests use the
  invented `synthetic-snowball-history.json`. The finding's Appendix A carries the same invented table,
  and keeps the documented formulas and quirks word for word because they are the spec.
- **Mockups and renders.** `Ledger/design/mockup-NN.html`, `index-design-book.html` and the seventeen
  PNGs were regenerated from the invented figures (the lender, merchant and account names, the balances,
  rates and payments, the dates, the card-number suffixes and the charts, which are re-derived from the
  synthetic history). A render carries whatever the page showed, so the PNGs were re-rendered, not
  edited.
- **People.** Household members' first names and relationship words were replaced with invented
  stand-ins in code, tests, mockups and these documents. The one in the Foreword tests and the one in
  these documents are deliberately different names, so a search for either cannot confuse the two.
- **Documents.** This plan, `INDEX-P11-PLAN.md`, the technical finding and
  `FocusPhone/POST-P7-TECHNICAL-FINDING.md` now describe the real sheet by shape ("the owner's own Debt
  Snowball Google Sheet; its values are not in the repository") and use the invented figures wherever a
  worked example is needed. Decision numbers (D-ids), release structure and guards G1–G27 are unchanged.
- **How it was checked.** A private scanner (kept outside the repository, because it necessarily holds the
  values it looks for) matches the tracked files, and separately the added lines and messages of every
  commit, against fixed strings and patterns in three tiers: strong (real values), weak (generic-looking
  shapes that need a human look) and people (names and relationship words). It prints only file paths,
  short commit hashes and integer counts, never a match, so its output is safe to paste into a report. The
  tree-wide gate is zero strong hits and zero people hits; weak hits are triaged by hand. A secret scanner
  was also run over the full history.

Two statements this work found to be stale were corrected in the same pass: §2.7 item 4 above said the
CI display version was `0.1.N` (it was always `1.0.<run_number>`; the audit was wrong), and several
places said Plate and Foreword can only be compiled by CI (both compile in this sandbox).

**What remains for the owner, and is the owner's decision:** the working tree is clean, but **history is not**.
Earlier commits and messages on this branch and on `main` still contain real values, and a commit stays
retrievable by its hash after a force-push. Making the repository public safely means (1) rewriting the
affected history on `main` and on this branch, using the same stand-in mappings so the rewritten history
agrees with the tree, and then (2) asking GitHub Support to purge the old commits and any cached views
and pull-request refs. Neither step has been taken. `CLAUDE.md` also still carries a few project notes
the owner should review before the switch.
