# Foreword (P9) — Gate 4 implementation plan

Approved at Gate 4. Built on the Gate 2 finding (`FOREWORD-P9-TECHNICAL-FINDING.md`, approved)
and the Gate 3 mockups.

---

## 0. The mockups are the specification

**Every screen and every printed page in this phase is drawn, approved, and published here:**

> ## https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c
> *Artifact: "Foreword Mockups". Read it before writing any UI or renderer code.*

This URL is written into the plan on purpose. A long build compresses its own context, and the
link is the one thing that must survive every step — if it is not in working memory, it is here.
Every UI and document task below repeats it.

**Three rules govern the whole build:**

1. **Build the screens and PDF pages as 1:1 replicas of the mockup.** The drawings are not
   impressions to be interpreted — layout, order, wording, spacing, tab positions, section
   grouping and copy are the specification. Where the mockup shows a status sentence, ship that
   sentence.
2. **Surface anything you discover; assume nothing.** If a screen cannot be built as drawn — the
   platform will not do it, the data is not there, the geometry does not fit a real device, a
   caption's claim turns out to be false — **stop and put it to the owner for approval** with the
   options and a recommendation. Do not quietly substitute a different design, and do not build
   the nearest thing that compiles.
3. **The mockups already lost three design-system arguments.** Type roles, spacing and shape were
   corrected against the kit at Gate 3 and audited mechanically. If the drawing and
   `Codex.Ui.Android.PaperUi` / `DesignTokens` ever disagree, **the kit wins and the disagreement
   is a finding to surface**, not a judgement call to make in passing.

### Screen inventory (all in the artifact above)

| Group | Ids |
|---|---|
| The Day (landscape sheets) | `D1` page 1 · `D2` weather · `D3` outlook · `D4` checklist · `D5` a day the family is together |
| The Brief (portrait sheets) | `B1` the foreword · `B2` one article · `B2a` abstract-only · `B3` the comic |
| Prayer (portrait sheets) | `P1` the list · `P2` journalling |
| Today area | `T1` ordinary · `T2` something failed · `T5` Sources |
| Tasks area | `T3` list · `T6` adding one · `T4` what it understood · `T7` editing a smart rule · `T8` editing a plain rule |
| Calendar area | `C1` mapping list (+ scrolled, with work blocks) · `C2` column chooser · `C3` split by name |
| Settings area | `S1` (+ scrolled) · `S2` Diagnostics |
| Lifecycle | `F1` first run · `A1` away pause |
| Identity | cover · bookplate · icon · colour ladder · OKLCH measurements |

---

## 1. Addenda to the approved finding, from Gate 3

The finding was approved before the screens were drawn. Drawing them changed six things. These
continue the finding's numbering and are part of it.

**F31 — `Event.Attendees` is fetched and never read.** Verified by reading
`Google.Apis.Calendar.v3` assembly metadata: `Event.Attendees` exists, of `EventAttendee`
(`Email`, `DisplayName`, `ResponseStatus`, `Optional`, `Self`). `GoogleCalendarClient.ToCalendarEvent`
maps none of it. This is the reliable signal for a shared event and it costs no new API call.

**F32 — `IGoogleCalendarClient.ListCalendars()` has no callers.** Declared and implemented,
returning id → summary; nothing in the repo calls it. It was written for the mapping screen that
was never built. `C1` costs no new Google work.

**F33 — every unmapped calendar prints into one column.** `CalendarColumnMapper.Resolve` falls
through to `DefaultColumn`, and `FetchEvents` iterates **every** calendar on the account
unconditionally. A subscribed US Holidays or Birthdays calendar currently lands in Matt's column.

**F34 — the work schedule has no UI at all.** `WorkScheduleOptions` is hardcoded (Matt
08:00–16:00, Ashley 08:00–15:30; off-keywords Off/PTO/Vacation). Changing a shift means changing a
default and rebuilding.

**F35 — the calendar mapping is hand-typed JSON.** `SetupPage.xaml`'s `CalendarMappingEditor` is a
mono `Editor` holding serialised `CalendarMappingOptions` — opaque `…@group.calendar.google.com`
ids, no validation.

**F36 — DESIGN.md registers no point ladder for printed documents.** §4's second column is the
Canonizer analysis PDF at its own page size. **Decision D30** settles it.

### Decisions added at Gate 3

**D30 — printed type derives from the sp ladder at a fixed ratio.** No second registered ladder;
one scale in the system, multiplied for the sheet. The ratio is chosen once, registered as a
token, and leak-guarded like the rest. *The approved sheets D1–D5, B1–B3, P1–P2 are drawn at ad-hoc
point sizes and must be re-fitted to the derived ladder — expect the fit to change and surface any
page that no longer works.*

**D31 — `CalendarEvent.Column` becomes a set.** One event can belong to several people. The set is
built from three sources, in this order: **guests on the event** (any of the four, matched by
address; `ResponseStatus` is ignored, because a family that never answers an invite would get
nothing; a guest outside the four is ignored entirely), **every name matched in the title** rather
than the first, and **nobody named on a shared calendar meaning everybody**. Where none of the
three can tell, it prints in one column and stays wrong until the owner renames the event or adds
the guest — no triage screen, and the model is never asked to guess who is going.

**D32 — a shared event draws two ways** (`D5`). *Contiguous participants, alone in their hour* →
**one box spanning them**, with a corner tag naming them. *Otherwise* → **a box in each person's
own column**, at its true time and full height, with a line naming who else is there. The second
mode exists because two groups at the same hour interleave across the columns and there is no
horizontal room left; stacking them vertically would draw a two-hour event as a one-hour one. A
notch and a tie rule were both tried and rejected — the tie read as a strikethrough.

**D33 — split-by-name is available to every calendar**, not only the one `FamilyCalendarId`. A
one-field change from a single id to a per-calendar rule set.

**D34 — an unmapped calendar prints nothing and says so.** It is held, listed under *Arrived since
you last looked*, and named on `T1` while it waits. Never silently printed, never silently dropped.

**D35 — no Save button, and no manual-check buttons.** Both from `Score.SettingsArea`, the head
that shares a phone: *"persisted as it is typed rather than on blur: there is no Save on this
screen, and leaving by a binder tab never blurs a field"*, and *"there is no manual rescan and no
manual update check: buttons that usually do nothing teach you to press them for reassurance rather
than for a reason."* So Foreword has **no Updates group**, **no Re-read from Google**, and **no Save**
on Settings, the calendar screens or the task edit forms. Values commit as they change.

**D36 — a working credential does not appear in Settings at all.** Not collapsed to a "Working ✓"
line — absent. `CredentialFieldState.Decide` already computes it. The coloured **Needs you** group
does not exist on a good morning.

**D37 — a `once` task edited in Foreword is written back to Google Tasks.** Requires the Tasks
**write** scope (`https://www.googleapis.com/auth/tasks`), not the read-only one. **The owner
re-registers the scope**; until then editing a Google task degrades to read-only and says so.

**D38 — run figures get their own store, separate from the diagnostics log.** The log is a
*failure* record and a line per morning would evict the failures it exists for — the split Post
made for the same reason. Tallies plus bounded samples (minutes taken, pages per document,
articles returned of those asked for, slowest feed, upload size), rendered into the Share export.

**D39 — Diagnostics copies `Score.AudioDiagnosticsActivity` exactly** (`S2`): Serif header, one
`PaperUi.Body` sentence, the rolling log as a single `PaperUi.Mono` block inside a
`PaperUi.HairlineCard`, oldest first, and a bottom bar of **Copy · Share · Clear** (two
`SecondaryButton`, one `QuietButton`, no primary). Entries keep `DiagnosticLogFormatter`'s shape.

**D40 — Settings uses Flyleaf's group furniture** (`S1`): `StartGroup`'s Serif SemiBold label at
Body size over a hairline-ruled paper-bright card, `BeginRow`'s hairline above every row but the
first, `AddCardRow`'s Body title over a Label description, `AddNavRow`'s chevron,
`AddCardCaption`'s quiet Label sentence under the card, and `PaperTheme.ResolvedMark` on the header
— **neutral grey, never the identity hue**.

### Risks added at Gate 3

**R11 — the sheets have never been re-fitted to a registered point ladder.** D30 changes every
printed page's type. The approved drawings are the target for *layout*; their *sizes* are provisional.

**R12 — the Tasks write scope may not be granted before Release F.** D37 degrades to read-only,
but the "edit a Google task" path is then untestable on device.

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
  code against them. Do not guess from memory. (F31 was established this way.)
- **Say plainly what is not verified.** "Built and unit-tested against fakes" is not "works."
  Nothing is marked complete on the owner's say-so alone.
- **Never put a credential in an artifact, doc, log or commit.** The vault passphrase is read
  in-process only. *(This was violated once at Gate 3 and the value must be rotated.)*

---

## 3. Releases

Each release is its own PR off `main`. The order is the finding's, amended by Gate 3.

### Pre-A — suite-wide versionCode unification *(separate PR)*
Prove the formula on apps that already work, before Foreword depends on it.

### Release A — Guardrails
Deliberately almost empty. It exists so the rename cannot break the update channel.

- Rename to `Foreword.*`; **freeze** `com.storyworthtelling.dailyloader`, collapsed to one constant.
- Pin `MainActivity` and the `DeliveryWorker` `[Register]` name — a `PeriodicWorkRequest` is
  persisted by class name. Check for anything else with the same property before renaming a namespace.
- Freeze all nine `CredentialStore` preference keys (they derive from `nameof`, so a rename silently
  loses the value) with migrate-on-read.
- versionCode: **band 7** of the suite formula settled in Pre-A (`band × 100,000,000 + minutes since
  2020-01-01 UTC`), which keeps the monotonic UTC-timestamp scheme and clears DailyLoader's old
  ~609,000,000 codes. The P7/P8 `floor + run_number` guardrail is *wrong here* and would brick updates.
- Pin JDK 21 in the workflow.
- Painted icon + on-device label, from the artifact's Identity section.
- **All guards from §4 below.**
- Settings, Diagnostics and first run (`S1` + scrolled, `S2`, `F1`) — Release A must be verifiable
  on the device, and these are the screens that show whether it worked.
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — `S1`, `S2`, `F1`, and the Identity section for the icon.*

> **Reversed at Pre-A, and recorded here rather than edited away.** The plan assumed the owner would
> uninstall DailyLoader first, because no versionCode scheme was known that could clear its old
> ~609,000,000 timestamp codes. Pre-A's band formula does exactly that at band 7, so **the owner does
> not uninstall**: the in-place upgrade is back on the table and is the main thing Release A has to
> demonstrate. That also saves the vault passphrase, which a clean install would have wiped along with
> the other eight preference values — so the vault restore is now a *second* thing to prove on a
> phone that has been reset, not the only thing Release A can show.

### Release B — The shared MAUI kit
`Storyworthtelling.Maui`: the binder tab strip, dog-ear, status line, list rows, accordion, card
groups. Foreword is MAUI, so it has no `Codex.Ui.Android`/`PaperUi` — **this release is where the
kit's roles are ported**, and it must reproduce `PaperUi`'s role table exactly: Display 44 Serif
Black · Title 28 · Heading 18 · Body 16 (Sans UI / Serif reading) · EmptyState 16 Serif Italic ·
Secondary 14 · Label 13 Sans SemiBold · Mono 14 · Voice 26 Pinyon. **No letterspacing, buttons
sentence-case at Label size, Serif Italic only for empty states.**
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — every app screen.*

### Release C — Vector documents
Vector sink in `Storyworthtelling.Paper`, `PaperPalette` routing + leak guard, Folio switched over.
**Also D30**: derive the printed point ladder from the sp ladder, register the ratio as a token, and
re-fit the sheets. Measured at Gate 2: 79.2 KB vector vs 2,670.9 KB raster for identical pages.
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — sheets `D1`–`D5`, `B1`–`B3`, `P1`–`P2`.*

### Release D — The calendar page *(the owner's top priority)*
- Overlap splitting (**bug B2**), the work block as a **container** drawn behind what sits inside
  it (**bug B3**), the overflow list, travel drawn *and* flagged.
- **D31/D32**: `CalendarEvent.Column` → a set; the two shared-event drawing modes.
- **D33/D34**: split-by-name for any calendar; unmapped calendars held and named.
- The mapping screens, replacing the JSON editor (F35), and the work-block rows (F34).
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — `D1`, `D5`, `C1` (both), `C2`, `C3`.*

### Release E — The Day document
Merge the checklist in, the weather page, the outlook, the write-space, section tabs with link
annotations, date naming.
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — `D1`, `D2`, `D3`, `D4`.*

### Release F — Tasks
Smart tasks, standing items, weather/daylight/season/work-day rules, the Google Tasks mirror
(**D37**, write scope), and the away pause.
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — `T3`, `T6`, `T4`, `T7`, `T8`, `A1`.*

### Release G — The brief
Voiced opening, xkcd, the This-Day factoid, article instrumentation (**bug B1**), dedupe, feeds,
section order, and the taste profile. A failing feed is **never** unsubscribed on the owner's
behalf — it stays listed with its status until removed.
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — `B1`, `B2`, `B2a`, `B3`, `T5`.*

### Release H — Prayer
The prayer document, private Drive transcription, type-level isolation + guard.
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — `P1`, `P2`.*

### Release I — Ashley's install
A second install against her credentials and tablet. Nothing hardcoded to one person.
→ *Mockups: https://claude.ai/code/artifact/6bb65b0d-04da-47f3-bf72-a04020217e0c — `F1`.*

### Score refit *(separate PR)*
Per-area colour coding, per DESIGN.md §9.

**Settings, Diagnostics and first run** (`S1`, `S2`, `F1`) land with Release A, since Release A
must be verifiable on the device and those are the screens that show whether it worked.

---

## 4. The guard ledger

Each is written **first**, watched to fail against the reintroduced bug, and the commit message
names what was reintroduced.

| # | Guard | The bug it reintroduces to prove itself |
|---|---|---|
| G1 | App id frozen to `com.storyworthtelling.dailyloader`, one constant | Change the id; the update channel stops seeing the app |
| G2 | `DeliveryWorker`'s `[Register]` name pinned | Rename it; the persisted `PeriodicWorkRequest` never fires again |
| G3 | `MainActivity`'s registered name pinned | Rename it; the launcher entry breaks |
| G4 | All nine `CredentialStore` keys pinned, with migrate-on-read | Rename a property; the stored value silently vanishes |
| G5 | versionCode is monotonic and ≥ the last shipped code | Apply the P7/P8 `floor + run_number`; the code drops ~3,000× and updates stop |
| G6 | No colour hex or type-size literal outside the token files | Inline one; the leak guard must catch it |
| G7 | Printed point sizes derive from the sp ladder (D30) | Hardcode a point size; the guard must catch it |
| G8 | Prayer content is isolated by type, not by discipline | Route a prayer through the general path; it must fail |
| G9 | A finished task never reaches the paper | Mark one done and render; it must not print |
| G10 | The work block is drawn **behind** what sits inside it (B3) | Append the synthetic block last; it paints over the meetings |
| G11 | Overlapping events split the column (B2) | Draw both full-width; the guard must catch the collision |
| G12 | An unmapped calendar prints nothing (D34, F33) | Fall through to `DefaultColumn`; US Holidays lands in Matt's column |
| G13 | A shared event is drawn once, never duplicated per column (D32) | Emit a copy per participant in the contiguous case |
| G14 | Every UI text role matches the kit's table (Release B) | Introduce a letterspaced label or an italic-sans run |
| G15 | JDK 21 pinned in the workflow | Remove `setup-java`; the runner inherits 17 and the build fails as PR #161 did |

---

## 5. Definition of done, per release

1. Guards written first and **watched to fail**, with the reintroduction named in the commit.
2. Unit tests green: `dotnet test Foreword/Foreword.Tests/Foreword.Tests.csproj -p:ForewordTfms=net10.0`.
3. The screen or page matches its mockup 1:1, checked against the artifact — **not** from memory.
4. Anything that could not be built as drawn is **surfaced for approval**, not substituted.
5. CI green on the PR before it merges. Never push to `main`.
6. What is *not* verified is stated plainly in the PR body.

---

## 6. Closing deliverables

- The CLAUDE.md section for Foreword.
- The P9 card on the roadmap artifact
  (`https://claude.ai/code/artifact/2f59319c-2fcd-43cc-a188-98778d187fe9`).
- The on-device test plan in Google Drive, in `[ID] / RESULT: / NOTES:` format — written so its
  questions are answerable on the phone. **If a step needs a number the UI does not show, surface
  the number first** (this is what D38's run-figures store exists for).
