# Plan: acting on the November 2026 suite audit

This plan works through every finding in the reports in this folder (numbers like *11 §1* mean report 11, finding 1). It is organized as **one PR per workstream**, in priority order. Each PR follows the repo rules:
- Tests come first (TDD).
- Each head is compiled locally before pushing.
- Branch pushes run tests only.
- Nothing merges without your approval.

## Decisions already made

| Topic | Decision |
|---|---|
| Git history | **Rewrite now.** Rewrite `main`, delete the stale branches, fix author metadata, then you ask GitHub Support to purge the PR refs. |
| Consolidation | **Full extraction.** Build the shared pieces and move every app onto them. |
| Design vs code | **Ask per discrepancy.** Settled so far: no destructive red (undo only); the Index mockup's uppercase, letterspaced labels win, recorded as a DESIGN.md exception. |
| Voice typeface | **Replace Pinyon Script across the suite** with a new, more legible script face. You pick it from a specimen sheet. |
| Voice seats | **The profile goes everywhere.** Every voiced line, including Plate captions, Post's empty Mailbox and the prayer line, is composed from the profile. |
| Dead voice seats | **Wire them up.** |
| Numbers in voice facts | **Keep the existing exceptions** (Index savings rate, Constitution body figures and loads), and allow new ones. Add logging that flags a line asserting something the facts didn't give. |
| Orphaned code | **Wire features that fill real gaps; delete superseded leftovers.** |
| Plaintext Drive files | **Encrypt both** the Post archive and the voice profile. |
| Settings | **Commit on change, grouped cards**, with a fixed group order. |
| Accessibility | **Full kit-level pass.** |
| Delivery | **One PR per workstream.** |

## Order and dependencies

```
W0 History + leak guards  ─┐
W1 Data safety ────────────┼─► W3 Shared libraries ─► W4 Logging ─► W5 Errors/offline ─► W6 Efficiency
W2 Cross-app contracts ────┘                       └─► W8 Design kit ─► W9 Accessibility ─► W11 Wording
W7 Orphans (after W3)      W10 Voice (after W3 + font pick)      W12 Docs/packages (first part early, rest last)
```

W1 and W2 are first because they lose data or silently break features today. W3 comes next because most later fixes should land once, in the shared code, not nine times. W12's package bumps for the vulnerabilities go out early as a small PR of their own.

---

## W0 · Git history and leak guards (privacy report, kept outside the repo)

1. **Prepare the rewrite (I do this).**
   - Re-scan the full history in the scratchpad and build a `filter-repo` replace-text list plus a `.mailmap`. Both stay outside the repo.
   - Dry-run the rewrite in a scratch clone, then re-scan to confirm nothing remains.
   - List every branch, marking which to delete and which to keep.
2. **Run it (your step, since it force-pushes `main`).**
   - Approve the force-push, delete the stale branches, and ask GitHub Support to purge the PR refs and cached views.
   - Then turn on GitHub's "keep my email private" and set your local git email to the noreply address.
3. **Guards so it doesn't happen again**, in the same PR, on the rewritten `main`:
   - Make `NoKeysTests` and `InventedDataTests` run over the whole repo, in a shared test project.
   - Add key shapes (`sk-ant-`, `AIza`, `ghp_`, `github_pat_`, private keys) and a coordinate-pair rule to `leakscan --ci`.
   - Replace the real bank names in the Index fixtures and the design book with invented ones.
   - Gitignore `Branding/owner.png` and rewrite the "photo swap" note in CLAUDE.md.
   - Stop `generate-keystore.yml` writing the keystore into the public run summary.
4. **Runtime leaks.** Stop logging these:
   - Post: phone numbers and email links (08, privacy §4).
   - Index: lender names and balances (09 M5).
   - Handwriting replies (08).
   - Tap-watch row text (08).
5. **Your step:** trash any `storyworthtelling-voice-config.json` left in Drive, and rotate the Claude key if it is there.

## W1 · Data safety

Goal: no failure path can overwrite or lose data.

1. **Drive reads must fail loudly.** `DriveTextFile.ReadAsync` checks `progress.Exception`, and "exists but empty" becomes Corrupt (11 §1). Do the same for Folio's `GoogleDocsClient`.
2. **One sealed-file policy.** Add a shared `SealedDriveFile`:
   - It refuses to write over an unreadable or newer file, reads back after writing, and compares `headRevisionId` before writing.
   - Move these onto it: the vault, the personal vault, the household files, the prayer archive, the snowball history, the Pulp usage file, the Constitution Body backup, and the Index snapshot (which today overwrites a Corrupt file).
   - Sources: 01 §5, 09 M6, 11 §3.
3. **Vault schema.**
   - Add `[JsonExtensionData]` on every vault record.
   - Add a vault schema number, and refuse to write when the file's number is newer.
   - An Empty read on an install that has seen a vault before means "do not create".
   - Sources: 09 H1, 11 §3.
4. **Vault merge.** Add a shared `Pick(local, inVault, changed)` and `MergeDriveFileIds`. Folio and Foreword stop blanking other sections (01 §2).
5. **Backups merge rather than replace.**
   - Post's archive restore runs on its own, retries until it is confirmed, and backs up as a union keyed by `SourceId`.
   - Score's library backup does the same.
   - Sources: 11 §2, §17.
6. **Encrypt the Post archive and the voice profile** with VaultEnvelope, migrating from the plaintext files: read the old file, write the sealed one, and trash the old one only after a confirmed read-back.
7. **Writes that can be cut off by process death.**
   - Plate: commit the Darkroom with `Commit()`, sweep for unlisted files at startup, persist the scan tray, use `IS_PENDING` on MediaStore inserts, and compose off the UI thread (11 §8, §18).
   - Index: `BalanceReceiver` uses `GoAsync` (11 §14).
   - Folio: restore with copy-then-swap and a database snapshot (11 §16).
8. **Index.**
   - Plaid release outbox: keep the token until `/item/remove` is confirmed (11 §9).
   - Retention purge runs on approval (03 §32).
   - Offline doesn't spend attempts, and Failed banks come back after 24 h (11 §5).

## W2 · Cross-app contracts

1. **Receivers request their own permissions.** Index, Flyleaf, Pulp and Constitution each add a `uses-permission` for the permission they declare, and Post requests `RECEIVE_BALANCE` (09 C1, C2).
   - Fix `BudgetManifestTests` and the 17→18 permission count.
   - Reset `ConstitutionBudgetLastSent` once.
2. **Declare each permission in both apps**, so install order doesn't matter (09 M2). Add Pulp's `<queries>` entry for Index (09 M1).
3. **Restricted-browser activity name.** `RestrictedBrowserActivity` gets an explicit `Name`, with a guard, and the contract is documented (09 H2).
4. **Plate errands.**
   - Move `READ_STATEMENT`/`READ_CARD` onto a signature-guarded activity (09 M3).
   - Wire the Index statement read and the Post card read.
   - Drop or build the VideoCapture filter (03).
5. **One contract guard.** For every `SendBroadcast(intent, P)`, the target head requests P and the content-read guards exist on **both** sides. Run all contract guards in `log-loop-guard.yml` so path filters can't skip them (09 M4, L4, L5).
6. **Couriers log their outcome.** Each courier checks that the target is installed and holds the permission, then logs send, refusal and receipt. Payloads stay out of the log.
7. **Add a `v` extra to every broadcast** (09 L1, L2).

## W3 · Shared libraries (full extraction)

Each item replaces copies in several apps; the source findings are in report 01 unless noted.

| New or extended shared piece | Replaces | Source |
|---|---|---|
| `Storyworthtelling.Voice` → `ClaudeMessagesClient` + `ClaudeKeySource` (live from `Vault.Claude`) | 18 hand-rolled clients; Foreword's separate `AnthropicApiKey`; the copied keys; the `"anthropic"` id | 01 §3–4 |
| Shared credential ids + one `CredentialHealthStore` with a lock | 4 stores, 6 id files | 01 §14–15 |
| Android Google kit: `DeviceAccountTokens(scopes)`, sign-in, one `DriveService` factory, `AppSigningInfo` | 12 transport factories, 5 token providers, 4 sign-ins | 01 §8, §10 |
| Delete `Storyworthtelling.Google`; move Foreword's weather client into a kept library; give Constitution a direct Calendar package | dead library | 01 §9, 03 §1 |
| `Storyworthtelling.Pipeline` (runner, backoff, `NetworkFailure`, `ErrorText`, `PlainError`, `MemoryFailure`) | Index/Folio copies | 01 §7, 11 §22 |
| BCL time library (`RelativeTime`, `TimeFormats`, plus a zone-aware overload) | 6 copies, Pulp's own, Index's drifted copy | 01 §13, 02 §15 |
| `DogEarState` in a BCL library, used by native, MAUI and Constitution; native `DogEar` gains an `onTurned` callback | 3 decision engines, Constitution's painter fork | 01 §16, 02 §21 |
| `LiveOptions<T>`, Drive query and paging helpers, `HttpClient` helper per head | ad-hoc copies; Folio's and Score's unpaged listings | 01 §17–19 |
| `IPaperDelivery` as the only reMarkable path (size cap, OOM handling, timeout, cancellation, one client factory) | direct uploads in Foreword, Index, Constitution | 01 §11 |
| Shared handwriting result shape + one reMarkable page profile | 3 read-back pipelines | 01 §12 |

## W4 · Logging and the log loop (report 08)

1. **Reader fixes.**
   - Parse both line formats, or have `FileLogger` write the native format; test it with real output.
   - Report missed workers using a per-app cadence table.
   - Group by severity, and carry the version per group.
   - Flag an expected app or phone that sent nothing.
   - Show `DROPPED` at the top.
   - Improve exception keys.
   - Sources: 08 §1–5, §34–35.
2. **One `VaultTrail` line** for every restore and publish outcome, native and MAUI (08 §6–8, §28).
3. **Constitution.** Worker failures at WARN with type chain and frames; problem lines at WARN; meals get their own trail (08 §9–11).
4. **Score.** Taste, Discovery, TIDAL, the update service and podcast feeds log to the file; add a player-error listener (08 §12–15, §27).
5. **Flyleaf.** Log the update-token rejection and the run summary (08 §16).
6. **Workers.**
   - A shared base worker with `OnStopped` and skip reasons.
   - Index's `WorksheetDeliveryWorker` wrapped in the worker trail, and the guard extended to the MAUI heads.
   - Log upload in a `finally`.
   - Sources: 08 §18, §20–21, §37.
7. **MAUI crash hooks** on every head; `crash.log` included in the upload (08 §19).
8. **Clients and permissions.**
   - Voice failures at WARN.
   - An optional log sink on the Codex clients.
   - Permission denials logged once.
   - Plaid request ids.
   - Sources: 08 §22–24, §26.
9. **ISO-8601 stamps** in files; ANR trace excerpts; Foreword's HttpClient chatter filtered out; Pulp per-title warnings counted instead of logged one by one (08 §25, §32–33, §36).
10. **Shared source vocabulary** (`Vault`, `Drive`, `Http:<svc>`, `Worker`, `Contract:<name>`, `Permission`) (08 §31).

## W5 · Errors and offline (report 11)

1. **Folio's offline classification everywhere**: Index refresh, Foreword delivery, Plate send, Pulp (which today calls every failure "offline").
2. **A "Sign in again" row** from a shared `google-account` health entry, with re-consent from the foreground and the token cache invalidated on 401 (11 §4).
3. **Foreword delivery.**
   - Report the outcome per document.
   - Add a network constraint and a longer backoff.
   - Keep a "delivered today" mark per document, so a retry doesn't make a duplicate brief.
   - Sources: 11 §6–7.
4. **Post's mail sorter** stops at the first failure and reports health (11 §10). Post's Gmail health id mismatch is fixed (11 §12).
5. **reMarkable health shown in every app**; a vault value replaces a local value that is failing; health shared through the vault (11 §11, §13).
6. **Shared MAUI `async void` helper** that routes failures to the dog-ear (11 §15).
7. **Remaining items.**
   - Constitution and Pulp read Google's 403 reason codes (11 §19–20).
   - Plate's camera-permission copy (11 §23).
   - Folio's desk refresh notice (11 §24).
   - Foreword's prayer save reasons (11 §25).
   - `PlainError` replaces every raw `ex.Message` on screen (11 §22, 12 §3).

## W6 · Efficiency (report 05)

1. **Drive traffic.**
   - A "last backed-up" marker removes the archive download from every Post sync (§1).
   - Cache the vault per process, check `headRevisionId` before downloading, cache the derived key, keep one `DriveService` per head, and cache the TIDAL token (§3, §12).
2. **Gmail.** Reconcile by listing ids, use the history API, fetch metadata first, and fetch attachments on demand (§2).
3. **Main-thread work.**
   - Score artwork decoded once per track (§5).
   - Plate editor sampled decode (§11).
   - Score catalog loaded off the UI thread (§15).
   - Flyleaf bookcase drawn as tiles (§14).
4. **Folio.** The worker checks for work before going to the foreground; the desk poll becomes a change event (§6).
5. **Post data.** Thread-scoped queries, batched MMS part reads, timeline indexes, keyset paging, and a body-free projection (§7–8, §23).
6. **Network and API calls.**
   - Podcast feeds: conditional GET plus throttling; episode cache moved to a file (§9).
   - Discovery and greetings: catalog as a cached prefix, greeting cached, timed-out calls cancelled (§4, §10).
   - Mail sorting batched (§17).
   - reMarkable uploads stream (§18).
   - The update channel uses a manifest, downloads only on unmetered networks (§13), and launch skips a needless vault restore (§24).
7. **CI.**
   - Gate Index's build.
   - Cache NuGet and the workloads.
   - Run `Codex.Tests` once.
   - Split shared-libs into a matrix.
   - Deduplicate `log-loop-guard`.
   - Fix the path-filter gaps (§19–22, 13 B).

## W7 · Orphaned and incomplete code (report 03)

**Wire up** (these fill real gaps):
- Plate: face correction (§26).
- Post: tombstone fold (§21); voicemail parser (§22).
- Pulp: landing check and sports-feed notice (§28–29).
- Score: update status on the Settings screen (§19).
- Index: "Refresh now" marks every bank due (§33).
- Constitution: `WorkoutWeekNotices` and `BodyBackupNote` (§41).
- Tools: `IconStudio.Tests` and `IndexHistorySeal.Tests` in CI (§46).
- Score's per-device EQ, sound-check gain and headset gestures (§14) are **flagged for your decision** before wiring, since they're product features.

**Delete** (superseded):
- `Storyworthtelling.Google`, `DriveVoiceConfigStore`, `EmbeddedBranding` and its `owner.png` embeds.
- `PaperClockView`, `shelf_bookcase_home.png`, `SegmentedToggle`, `PlayRatingClient`.
- The TIDAL device-code flow, `PodcastLibrary`, `SeatCacheEncouragement`, `PuzzleType`, `GoogleAccessTokenProvider`, `PipelineOptions.IdlePollSeconds`.
- Unused members and constants.
- Unused Moq / Logging / Http package references (§47–49).

## W8 · Design kit and cross-app UI (reports 02, 04)

1. **MAUI kit token fixes.**
   - Error and problem styles use Problem rose.
   - The primary button fills with the deep tier and has no colour change on press.
   - The status line uses the In-progress hue.
   - The spinner style is deleted; `Ink` defaults to the Ink token.
   - Sources: 04 §1–5, §10.
2. **No destructive red.**
   - Retire `DestructiveButton` from both kits, and remove the red from action cards.
   - Guard tests ban `Toast.MakeText`, `DisplayAlert`/`DisplayActionSheet`/`DisplayPromptAsync` and `AlertDialog` in the heads.
   - Every call site moves to inline status, an `ActionCard`, an `AccordionPicker` or the dog-ear.
   - Sources: 02 §1, §6, §12, 04 §3, §26.
3. **New MAUI kit components:** `ActionCard`, `PaperBottomBar` (buttons ordered by role), `PaperBinderPage`, `PaperField` (with a focus state, also added to the native field), and a shared `DiagnosticsPage` matching the native screen (02 §4–5, §13, §22; 04 §27–29).
4. **Settings standard.**
   - Grouped cards; commit on change or blur; group order Connections → Vault → app → Diagnostics → build line.
   - "Working ✓ + Quiet Replace" in every app; `BuildLine.Format` everywhere.
   - Index Settings uses the suite grey.
   - Sources: 02 §3, §7–9, §23.
5. **Guards across the suite.**
   - Type size (Pulp's guard ported to every app), letterspacing, spacing, radius, black scrims, `Colors.Black`/`White`.
   - Folio's ~25 off-ladder sizes, near-square corners and capitals.
   - Index's retyped hexes.
   - Sources: 04 §6–9, §13–19.
6. **Index mockup exception.** DESIGN.md §14 records it, the guard allows it for Index's small section labels only, and the code follows the mockup (04 §14).
7. **Motion.**
   - Add an `InkMs = 320` token, use it on action cards, and slide at 200 ms.
   - MAUI page turns and a dog-ear fold-in.
   - Flyleaf's cover timings move to tokens.
   - Sources: 04 §21–24.
8. **Icons.** An IconStudio glyph set emits stroked area and tab glyphs for both platforms, replacing the hand-made ones (04 §33).
9. **Screen-level fixes.**
   - Index Settings bank removal moves to the dog-ear, and the kebab is removed (02 §11, 04 §31).
   - Two headers missing their underline (02 §24).
   - The Snowball caption (04 §32).
   - The mark is drawn with `OwnersMark` everywhere (04 §31).

## W9 · Accessibility (report 10, full pass)

1. **Flyleaf shelf.** An `ExploreByTouchHelper` with a node per book, a clock node and bookend headings; spine hit targets of at least 48dp; spine ink chosen for contrast; bookmark shape cues (§1, §14–15, §25).
2. **Kit semantics.**
   - Headings, live-region status lines, announced dog-ears.
   - Checkbox, tab, segment and disabled states exposed to TalkBack.
   - A guard for unlabeled custom views.
   - Sources: §2, §6.
3. **Contrast.**
   - Secondary and label text moves to an InkSoft-class token.
   - A `HairlineStrong` token for control boundaries.
   - The contrast guard checks 4.5:1 measured on the grain pixel.
   - A Foreword text tier.
   - Sources: §3–4, §11.
4. **Dog-ear.** A 48dp target, a timeout that honours `GetRecommendedTimeoutMillis`, a pause while it has accessibility focus, and semantics in MAUI (§5).
5. **App and control fixes.**
   - Plate's chosen badge (§7).
   - The Folio shelf as an accessible list; chart summaries (§8).
   - Score's dial range and actions (§9).
   - Wrapping tab labels (§10).
   - Canvas text in sp (§13).
   - Modal action cards (§17).
   - `IconButton` (§18).
   - Index fields (§19).
   - Named long-press actions (§20).
6. **Lockdown.** Accessibility apps in the allowlist; an Accessibility row in Flyleaf's Connectivity settings; the shortcut tested in a locked session (§21).
7. **PDFs, motion and states.** PDF metadata and a larger minimum type size (§22); a reduced-motion helper (§23); disabled-state dimming (§24).

## W10 · Voice (report 06)

1. **New script face.**
   - I produce a specimen artifact of legible OFL handwriting faces (for example Dancing Script, Caveat, Kalam, Patrick Hand, Satisfy) at the real seat sizes, in ink-blue on paper, including the large and children's cases.
   - You pick one, and it replaces Pinyon in `/Fonts`, both kits, the PDF renderers, the FONT CHECKs and DESIGN.md §4.
2. **The profile goes everywhere.**
   - Post's empty Mailbox, Plate's Darkroom lines and captions (the vision prompt carries the profile), and Foreword's prayer line.
   - `PrayerIsolationTests` changes so the prayer composer may *read* the profile. Prayer text still never flows into it.
3. **One seat pipeline in every app.**
   - A closed occasion list.
   - Errors never voiced, guarded by test.
   - One caching rule (per seat, facts and day; persisted; a short retry after failure).
   - A tier chosen per seat.
   - One fallback rule per seat type.
   - Flyleaf and Score join; Flyleaf's hello renders in the voice face.
4. **Wire the dead seats.** Foreword's Brief, Day and Checklist; Constitution's Today, Rest day and Meal sheet; Pulp's six empty pages and PlaylistLine.
5. **Facts.**
   - Keep the documented numeric exceptions and record them in §14.
   - Add a **groundedness check** that logs `VOICE UNGROUNDED <seat>` when a reply contains a number, name or claim token absent from its facts. The line is never shown to the user.
   - Fix Pulp's WhyThis claim and viewer standing, Folio's failed-review fact and the per-child cache keys.
6. **Profile hygiene.**
   - Act on `NeedsRelearn`.
   - Cap the examples and cache the profile as the prompt prefix.
   - Sample across the manuscript instead of only its first 12k characters.
   - Make `Discard` re-derive from what remains.
   - Encrypt the profile (W1 §6).

## W11 · Fixed wording (report 12)

1. Adopt the **glossary** (Vault passphrase, Vault, reMarkable, Delete / Remove from, Undo, Refresh, Connect/Disconnect, admin mode, Claude key, {App} diagnostics, couldn't, …).
2. American spelling, sentence case, no stray glyphs, no "please" or capitals on printed pages.
3. Remove jargon and the "FocusPhone" codename from user-facing text.
4. Every error and notice says what happened and what to do; notifications and Waiting pages use the same headlines.
5. A shared plural/number/size formatter, invariant culture, one duration style (§14).
6. Stale Storyboarder-era and tray-era copy; Pulp mockup drift; notification channel names and descriptions.

## W12 · Docs and packages (report 13)

1. **Early, as its own small PR.** Bump MailKit/MimeKit and AngleSharp, and pin patched SQLitePCLRaw and System.Security.Cryptography.Pkcs.
2. **Central Package Management.**
   - A root `Directory.Packages.props` with transitive pinning, unifying the drifted packages and replacing the `1.74.0.*` float.
   - A `Directory.Build.props` for the shared settings, one Android minimum (34), and coverlet everywhere.
3. **CI.** Pin the workload versions; JDK 21 everywhere; Foreword gets ICON and FONT CHECKs; Constitution's permission test runs after the Release build.
4. **Docs.** Correct every drift item in CLAUDE.md, DESIGN.md, the READMEs and LOG-LOOP-PLAN.md, at the end, so the docs describe the code as it ends up.

---

## Decisions still to ask (per discrepancy, when the workstream starts)

- Post's Filed and Rolodex filter fields wearing their tab's role hue (W8).
- Folio's book cloths using role colours: register material tokens, or keep (W8).
- Button padding: §7's 18×11 or the kits' 16×12 (W8).
- Folio's near-square paper radii: register as material tokens, or move to 12 (W8).
- The voiced line inside Constitution's never-hidden fallback (W10).
- Score's unwired device-EQ, sound-check and gesture features: build or delete (W7).
- Whether Pulp's child seats get extra voice guidance beyond reading level (W10).

## How each PR is checked

- New behaviour arrives with a failing test first.
- Guards are added for every rule the PR enforces.
- Every head touched is compiled locally.
- Each PR description lists the findings it closes (report § refs), and this plan is ticked as PRs merge.
