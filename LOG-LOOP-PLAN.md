# Log loop — implementation plan

A weekly, log-driven maintenance loop: every Android app ships its diagnostic logs, encrypted, to
the owner's Google Drive; a scheduled cloud Claude Code session reads them, triages, fixes what is a
bug, opens PRs, and trashes the logs it has fully processed.

This is a plan only. No application code is written here. Every claim about existing code cites the
file it was read from (paths relative to the repo root, line numbers as of `1666a3b`).

---

## 0. Decisions this plan builds on

From the owner:

| # | Decision |
|---|---|
| O1 | Logs may carry full content (messages, amounts, names). They are encrypted before upload. |
| O2 | Encryption is the existing `VaultEnvelope` (`Storyworthtelling.Voice/Secrets/VaultEnvelope.cs`). The reader is a scheduled cloud session that gets a key as an environment secret. |
| O3 | Auto-merge only for bug fixes derived from log files. Everything else: PR and wait (CLAUDE.md, Git Workflow). |
| O4 | The session may open PRs for any app, including the FocusPhone lockdown and Index's money code. |

Defaults chosen by the assistant, kept here with reasons:

| # | Default | Kept? | Reason |
|---|---|---|---|
| A1 | Drive deletion = **trash**, never permanent delete. | Kept | The connector's `trash_file` does exactly this; 30 days of recovery covers a bad triage run. The Drive connector exposes no permanent delete anyway. |
| A2 | Prayer text (`Foreword.Domain/Prayer/PrayerText`) never enters any log. | Kept, and **made structural** (§2.5) | Today it is true by care, not by mechanism: `ClaudeJournalTranscriber` logs exceptions whole (`Foreword/Foreword.Infrastructure/Prayer/ClaudeJournalTranscriber.cs:80`), and an exception message from a parse of the model's reply is one refactor away from quoting the reply. |

One revision to O2, argued in §3.2: the session gets a **log key derived from** the vault passphrase,
never the passphrase itself.

---

## 1. Inventory of existing logs

| App / head | Log | Where | What it captures | Cap | Sensitive today? |
|---|---|---|---|---|---|
| Shared (FocusPhone heads, Pulp) | `DiagnosticLog` | `FocusPhone/Codex.Domain/Diagnostics/DiagnosticLog.cs` | Append-only non-fatal failures (`DiagnosticEntry`); rolls to `diagnostic.log.1` | 128 KB live + 128 KB backup (`:29`, `:127-138`) | Depends on caller (below) |
| Shared | `CrashLogStore` | `FocusPhone/Codex.Domain/Diagnostics/CrashLogStore.cs:18` (`crash-log.txt`) | **Latest crash only**, overwritten (`:30-37`) | One report | Stack traces; low |
| Shared | `CrashReporter` | `FocusPhone/Codex.Ui.Android/Diagnostics/CrashReporter.cs` | Installs the uncaught handler into `CrashLogStore`; used by Flyleaf (`Flyleaf.Android/FocusApplication.cs:20`), Score, Post, Plate, Pulp | — | — |
| **Flyleaf** | *(none beyond the crash store)* | `Flyleaf.Android/PolicyManager.cs`, `UpdateService.cs`, `CallScreen.cs` … log to **logcat only** (11 `Log.*` calls in `PolicyManager.cs` alone) | Policy passes, install outcomes, call surfacing | logcat ring | Package names; low. **Gap: the most safety-critical head keeps no file log.** `UpdateStatusStore` (`UpdateService.cs:342`) keeps one status sentence only. |
| **Plate** | *(none beyond the crash store)* | `Plate.Android/PlateActivity.cs`, `FaceRecogniser.cs`, `Rolodex.cs` → logcat | Capture/recognition failures | logcat ring | Contact names possible. **Same gap.** |
| **Score** | `DiagnosticLog` via `AudioDiagnostics` | `FocusPhone/Score.Android/AudioDiagnostics.cs:38` | Playback, Drive scan, TIDAL sign-in (client id / redirect / scope) | 128 KB ×2 | Low |
| Score | `ListeningLog` | `FocusPhone/Score.Android/ListeningLog.cs:18` | Not a log: the Listening Genius event store (prefs) | ~200 sessions | Listening history. **Excluded** — product data, not diagnostics. |
| **Post** | `DiagnosticLog` via `PostDiagnostics` | `FocusPhone/Post.Android/PostDiagnostics.cs:45` | Mail sync, bridge, timeline store, MMS parse failures (`MmsPduShape.Redacted`) | 128 KB ×2 | **Yes, partly**: mail errors can carry addresses/subjects |
| Post | texts trail | `PostDiagnostics.cs:31-32,46`; lines from `FocusPhone/Codex.Domain/Post/TextTrail.cs` | One line per arriving text: route, lag, LATE; sender as last two digits (`TextTrail.cs:71-75`) | 96 KB ×2 | Deliberately not (by design) |
| Post | `FieldNotes` | `FocusPhone/Codex.Domain/Post/FieldNotes.cs`, `FieldNotesCodec.cs`; prefs `fieldNotes` (`Post.Android/FieldNotesStore.cs:17`) | Tallies + bounded samples of mail shape and surface outcomes | Bounded by construction | Samples may name senders |
| Post | `DiagnosticsReport` | `FocusPhone/Codex.Domain/Diagnostics/DiagnosticsReport.cs:23` | The export that stitches the above + `TextsStatus` | — | Union of the above |
| **Pulp** | `DiagnosticLog` via `PulpDiagnostics` | `Pulp/Pulp.Android/App/PulpDiagnostics.cs:18` | Lookup/availability/sports-source failures, lookup counters (`:40`) | 128 KB ×2 | Titles watched; household names possible |
| **Index** | `PipelineFileLogger` → `refresh.log` | `Ledger/Ledger.Android/PipelineFileLogger.cs:13,15` | `ILogger` sink: Plaid refresh, per-institution failures (e.g. `Ledger.Infrastructure/Pipeline/BalanceRefreshProcessor.cs:101,127`) | 1 MB + backup | **Yes**: institution names; Plaid error bodies |
| Index | `crash.log` | `Ledger/Ledger.Android/LaunchFaults.cs:16` | Launch crashes | — | Low |
| Index | `PendingCrashStore` | `Ledger/Ledger.Domain/Diagnostics/PendingCrashStore.cs:11` (`pending-crash.txt`) | Crash awaiting display | One | Low |
| **Folio** | `PipelineFileLogger` → `pipeline.log` | `Storyboarder/Folio.Android/PipelineFileLogger.cs:15,17` | Every Claude call (name, persona, tokens, stop reason, request id), failures with `ErrorText.Describe` chains | 1.5 MB + backup | **Yes**: error bodies may quote manuscript text |
| Folio | `crash.log` | `Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs:17` | Crashes | — | Low |
| **Foreword** | `DeliveryFileLogger` → `delivery.log` | `Foreword/Foreword.Android/DeliveryFileLogger.cs:11` | Morning delivery, calendar, NYT, reMarkable, **prayer pipeline** (`Foreword.Infrastructure/Prayer/PrayerDeliveryService.cs:41-237`) | **None — unbounded** (`:43-44` appends forever) | **Yes**: calendar titles; prayer-adjacent (§2.5) |
| Foreword | `crash.log` | `Foreword/Foreword.Android/Pages/DiagnosticsPage.xaml.cs:20` | Crashes | — | Low |

Three findings from the inventory feed the phases:

1. **Two `PipelineFileLogger`s are near copies** (Index and Folio, differing in cap and file name) and
   Foreword's `DeliveryFileLogger` is a third, cap-less variant. The loop needs one spool hook in
   each; consolidating them into a shared MAUI-side logger is worth doing in Phase 2 rather than
   patching three.
2. **Flyleaf and Plate have no file log.** A log loop that cannot see the device-owner head is blind
   where a bug costs most. Phase 3 adds a `DiagnosticLog` to both.
3. **`CrashLogStore` keeps only the latest crash.** Post already appends crashes to its rolling log
   (`PostDiagnostics.cs` after `:47`); the spool (§2.1) makes every head do the same.

---

## 2. Upload path (device side)

### 2.1 Shape: a spool beside each log, not a cursor into it

Each log keeps working exactly as today (the on-screen Diagnostics pages read the same files). In
addition, every write is **mirrored into a spool** — the outbox. Mirroring rather than tracking a
read cursor into the live file is deliberate: the live files roll (`DiagnosticLog.RollIfTooLarge`,
`PipelineFileLogger.RollIfTooLarge`) and are cleared by the owner with undo (`DiagnosticLog.Clear` /
`RestoreRaw`), and a cursor into a file that rolls or is restored silently skips or duplicates.

New types (Domain, pure `System.IO`, so they unit-test against a temp folder like `DiagnosticLog`):

- **`LogSpool`** (`Storyworthtelling.Diagnostics` — a new net10.0 library at the repo root so
  FocusPhone, Pulp and the MAUI heads share one copy; Codex.Domain stays dependency-free by
  referencing nothing and receiving the spool through a small `ILogMirror` interface it declares).
  - Directory `FilesDir/log-outbox/`. Writes go to `open.seg`; a segment is **sealed** (renamed
    `NNNNNN.sealed`, `NNNNNN` a monotonic per-install counter persisted in `seq`) when it reaches
    **64 KB plaintext** or is **older than 24 h** at upload time.
  - Each line carries `app`, `log` (which source log), UTC timestamp, level, text.
  - Outbox cap **4 MB** of sealed segments. Past it, the oldest segment is dropped and a single
    `DROPPED n segments (oldest …)` line is written into the next segment, so the reader knows the
    record has a hole rather than assuming a quiet week.
- **`LogBatch`** — the payload for one Drive file: header (`schema` = 1, `app`, `installId`,
  `segment`, `firstUtc`, `lastUtc`, `appVersion`, `versionCode`, `logKeyEpoch`) plus the lines.
  Serialized to JSON, **gzip-compressed, base64'd**, then sealed with `VaultEnvelope.Write`.
  Compression first (logs compress ~5–10×) keeps each Drive file around 10–15 KB, which matters for
  the reader's transport (§3.4).
- `installId`: a random id generated once per install and stored in prefs. Not the device serial,
  not an account address.

### 2.2 The upload worker

`LogUploadWorker` per head (WorkManager, unique periodic work `log-upload`, daily, constraint
`NetworkType.Connected`; the Score/Post/Folio/Index/Foreword/Pulp heads already run WorkManager,
Flyleaf already has the 6-hour `UpdateService` worker). The decisions live in a pure
`LogUploadPlan.Decide(sealedSegments, keyState, now)`; the worker only carries them out.

For each sealed segment, oldest first:

1. **No key → stop.** If the vault passphrase is absent (`VaultPassphraseStore` /
   the MAUI `VaultPassphrase` setting is empty — the post-reset state), upload nothing. Segments keep
   accumulating under the 4 MB cap. The Diagnostics screen shows one status sentence: *"Logs are
   waiting for the vault passphrase."* Never upload plaintext, never encrypt under an empty key —
   the same refusal `DrivePrayerArchive.SaveAsync` makes (`Foreword/Foreword.Infrastructure/Prayer/DrivePrayerArchive.cs:65-70`).
2. **Name.** `swt-log.<app>.<installId>.<segment>.json` in the Drive folder `storyworthtelling-logs`
   (created on first use, found by name like every other suite file).
3. **Idempotency.** Search the folder for that exact name first. If a file exists and decrypts to a
   payload whose SHA-256 equals the local one, skip to step 6 (a crash between upload and local
   delete must not create a duplicate). If it exists and does **not** match, write
   `…<segment>.r2.json` and log the collision — never overwrite (the `DrivePrayerArchive` rule).
4. **Create.** Upload the envelope.
5. **Read back.** Fetch the file by the id the create returned, `VaultEnvelope.Read` it with the
   same key, gunzip, and compare SHA-256 with the local plaintext.
6. **Delete locally** only when step 5 (or 3) matched. Any failure leaves the segment in place for
   the next run; failures are counted in prefs and shown as a status sentence after three in a row.

Offline phones simply accumulate under the cap; the next connected day drains the outbox oldest
first, at most **50 segments per run** so a phone back from a long trip does not spend an hour on
the radio.

### 2.3 A new Drive seam, because `IDriveTextFile` is one file

`IDriveTextFile` (`Storyworthtelling.Voice/IDriveTextFile.cs`) is "a single named file": read and
create-or-overwrite. The loop needs many files in a folder and never overwrites. Add, beside it in
`Storyworthtelling.Voice`:

```csharp
public interface IDriveLogFolder
{
    Task<string?> FindAsync(string name, CancellationToken cancellationToken);   // id or null
    Task<string> CreateAsync(string name, string content, CancellationToken cancellationToken); // new id
    Task<string?> ReadAsync(string id, CancellationToken cancellationToken);
}
```

`DriveLogFolder` implements it over the same `Func<CancellationToken, Task<DriveService>>` factory
`DriveTextFile` takes (`Storyworthtelling.Voice/DriveTextFile.cs:30-40`). No delete member on the
device side: the phone never removes anything from Drive.

### 2.4 Scopes

Every head that ships logs already holds a full-`drive` token for the vault (CLAUDE.md, Secrets
vault: Index, Foreword widened; FocusPhone's `VoiceGoogleAccount`, used by Flyleaf, Plate, Post,
Score; Pulp; Folio). No new OAuth scope.

### 2.5 Prayer stays out — by mechanism

- The spool sink for the MAUI loggers drops the formatted message and exception **message** for any
  category under `Foreword.Infrastructure.Prayer` and `Foreword.Infrastructure.Pdf.PrayerPages`, and
  for any type implementing `IPrayerReader` (the five listed by `PrayerIsolationTests`). It keeps the
  **template**, the level, the exception **type** and stack frames. So `"Journal transcription failed
  for {Date}"` survives; whatever the exception said does not.
- Guard `PrayerNeverSpooledTests` (Foreword.Tests): drives each prayer-category logger with a
  `PrayerText` whose words are a sentinel and an exception whose message carries the sentinel, and
  asserts the spool never contains it.

### 2.6 Partner's phone

Her installs (Index's reader role, Pulp, Foreword) would upload to **her** Drive, which the
owner-account connector cannot see. Out of scope for the pilot; see Open question Q3.

---

## 3. Reader session

### 3.1 Schedule and trigger

- `create_trigger`, `create_new_session_on_fire = true` (each week starts clean),
  `cron_expression = "CRON_TZ=<owner's zone> 47 2 * * 0"` — Sunday 02:47 local: after Saturday's
  uploads, outside the 6–9 AM Pacific congestion window, not on the hour.
- `connectors = ["Google Drive"]` — the only grant. No Gmail, no Calendar.
- `notifications = { push: true }` — the owner gets one push when the run ends.
- **Environment:** a **dedicated** cloud environment, `log-loop`, used by nothing else. It holds the
  one secret (§3.2) and the repo. Network policy: GitHub + nuget.org + the Drive connector; nothing
  else is needed.
- Model: inherited; not named in any artifact (working rule).
- The prompt is a standalone instruction that points at a committed runbook,
  `.claude/log-loop/RUNBOOK.md` (Phase 1), so the procedure is versioned and reviewed like code
  rather than living in a trigger nobody reads.

### 3.2 The secret: a derived log key, not the vault passphrase

**Rule (CLAUDE.md, Secrets vault):** the passphrase is "the single thing NOT in the vault", entered
per app, never stored. **Conflict:** an environment secret is a stored copy, readable by every session
in that environment. **Mechanism:**

- `LogKey.Derive(vaultPassphrase, epoch)` = PBKDF2-SHA256(passphrase, salt =
  `"storyworthtelling-log-key/" + epoch`, 210,000 iterations — `SecretsCrypto.DefaultIterations`,
  `Storyworthtelling.Voice/Secrets/SecretsCrypto.cs:15`) → 32 bytes → lowercase hex. The phone derives
  it in memory from the stored passphrase and passes the hex string to `VaultEnvelope.Write` as its
  "passphrase" (the envelope then runs its own salted PBKDF2 per file — harmless).
- The environment secret is **`SWT_LOG_KEY`** = that hex string. The vault passphrase never leaves the
  phones.
- **What a leaked session could decrypt:** every log file (that is the cost the owner accepted in O1),
  and nothing else. The log key cannot open `storyworthtelling-secrets.json`, the prayer archive, the
  snowball history or the household file — they are sealed under the passphrase, and PBKDF2 is
  one-way. A leaked session *also* holds the Drive connector, which can read the ciphertext of those
  files but not open them.
- **Who can read it:** anyone who can open or edit the `log-loop` environment in the owner's Claude
  account, and every session started in it. Hence the dedicated environment: no interactive session
  should be started there.
- **Rotation:** `LogKeyEpoch` is a constant in `Storyworthtelling.Diagnostics` (starts at 1) and is
  written into every batch header. Rotating = bump the epoch in a reviewed PR, ship, then the owner
  derives the new key with `Tools/LogKeyDerive` (local console, reads the passphrase from stdin,
  prints the hex; never logs it) and replaces the secret. Changing the vault passphrase also rotates
  it. Files under an old epoch are left in Drive and reported as "unreadable — old key"; they are
  **not** trashed.
- `LogKeyDeriveTests` pins the derivation (fixed passphrase/epoch → fixed hex; different epoch →
  different key; key ≠ any `SecretsEnvelope` field).

### 3.3 Branch naming

- One branch per fix: `claude/logfix-<yyyy-MM-dd>-<app>-<slug>` (fits CI's `claude/**` filter, so
  branch pushes run tests only).
- One report branch per run is **not** used; the report goes in an issue (§3.6) so nothing about the
  week's content is committed.

### 3.4 Finding and decrypting

1. `search_files` with `parentId = '<logs folder id>' and title contains 'swt-log.'` paged to the end.
2. For each file: `download_file_content` (base64) → the session writes it to
   `$TMPDIR/log-loop/in/<name>` — **outside the repo** — and runs
   `dotnet run --project Tools/LogLoopReader -- decrypt <in> <out>`, which uses
   `VaultEnvelope.Read` with `SWT_LOG_KEY`, gunzips, validates the header, and writes plaintext to
   `$TMPDIR/log-loop/plain/`. A transfer that garbled even one base64 character fails the GCM tag and
   is reported as `Corrupt` — the failure is safe, never a wrong read.
3. `LogLoopReader merge` produces one timeline per app, sorted by time, deduplicated by
   `(installId, segment)`, with `DROPPED` holes marked.

The transport step (model-relayed base64 into a file) is the plan's least-proven piece. Phase 1's
first exit criterion tests it; the fallback is a read-only Drive OAuth token for the reader tool,
which would widen the environment's secrets (Q4).

### 3.5 Trash and failure partway

A file is trashed (`trash_file`) only when **all** hold: it decrypted, every finding from it has a
recorded outcome (fix PR opened, reported, or no action), and the run's report has been posted. The
run writes a manifest `$TMPDIR/log-loop/manifest.json` as it goes: file id → state
(`downloaded`/`decrypted`/`triaged`/`done`).

Failure partway:

- Anything not yet `done` is **left in Drive** and picked up next week (it is still in the folder).
- PRs already opened stay open; next week's run looks up existing open `logfix` PRs by the
  fingerprint in their body (§4.2) and does not open a duplicate.
- Trashing is the **last** step of the run, in one pass, so a crash leaves either everything or the
  completed prefix — never a trashed file whose finding was lost.
- Files that fail to decrypt are never trashed; after **4 weeks** of failing they are listed in the
  report for the owner to decide.

### 3.6 Reporting

- One GitHub **issue** per run, title `Log loop — week of <date>` (no content in the title), body:
  counts per app and class (§4), links to PRs, the verification column (§5), files left untrashed and
  why. It quotes **no** log line verbatim — only sanitized shapes (§6.1). Ends with the attribution
  footer.
- The trigger's push notification carries the one-sentence summary.
- The session's own transcript necessarily contains plaintext: it lives in the owner's Claude
  account, not the repo, and is the accepted exposure of O1.

---

## 4. Triage rubric

### 4.1 Classes

| Class | Typical evidence | Outcome |
|---|---|---|
| **C1 Crash / unhandled exception** in our code | Crash store entry, stack ends in `Storyworthtelling`/`Codex`/`Folio`/`Index`/`Foreword`/`Pulp` frame | **Fix** (auto-merge eligible) |
| **C2 Logic error** — a contract our code broke | Parse failure of our own format, invariant violated, wrong branch taken, a state the domain says is impossible | **Fix** (auto-merge eligible) |
| **C3 Parsing a third party's well-formed but unexpected input** | `MmsNotificationParser` wrapper, a Plaid field we did not map, a feed shape | **Fix** if the shape is evidenced in the log; else **report** asking for a capture. Auto-merge eligible only with a synthetic reproduction test. |
| **C4 Retry / backoff behaviour wrong** | Same failure retried with no wait, budget not spent, re-billing (the Folio `CallGate` class of bug) | **Fix** (auto-merge eligible) |
| **C5 Transient network** | DNS failure, timeout, offline, connection reset | **No action** unless rate > threshold (≥ 20% of a source's attempts over the week) → **report** |
| **C6 Upstream outage / rate limit** | Plaid `INSTITUTION_DOWN`, 429, 5xx from Anthropic/Gemini/TIDAL/ESPN | **No action**; a pattern lasting > 3 days → **report** |
| **C7 Carrier / platform rejection** | MMS send failure, `ROLE_SMS` lost, Android killing a worker | **Report** (needs device or owner); fix only if our handling of the rejection is wrong (then C2) |
| **C8 Credential rejection** | 401/403, `API_KEY_INVALID`, `ITEM_LOGIN_REQUIRED` | **Report** — owner action; the existing credential-health UI already reveals the field |
| **C9 Owner action pending** | "no passphrase", "no profile", unconfigured setting | **No action**; listed once in the report |
| **C10 Logging defect** | Missing context, a log that should exist, sensitive data where it should not be | **Fix**, but **never auto-merge** (it changes what is logged — a privacy surface) |
| **C11 Performance / cost** | Token counts, retry storms, slow scans | **Report** with numbers; a fix is a normal PR (design judgment) |
| **C12 Unknown / unclassifiable** | Anything else | **Report** |

### 4.2 Recording the link to the log

Every fix PR body carries a machine-readable block:

```
<!-- log-loop
fingerprint: <sha256 of app + exception type + top 3 own frames, or of the normalized message template>
class: C2
source: swt-log.post.<installId>.<segment>.json
occurrences: 14
first-seen: 2026-10-24
-->
```

The fingerprint is computed by `LogLoopReader fingerprint` from the **template and stack**, never
from content, so it can be public.

---

## 5. Verification limits

Every finding in the report and every PR carries one of three marks:

| Mark | Meaning | Examples |
|---|---|---|
| **Test-verified** | A failing test reproduced the bug with synthetic data and now passes | Domain parsers (`MmsNotificationParser`, `MmsRetrieveConfParser`), `TimelineMerger`, `DeleteBuffer`, `ReadingPlan`, `CallGate`, `SnowballEngine`, `PolicyPlanBuilder`, `SiblingUpdatePlan`, `ChildFilter`, `EventLanes` — anything in a `*.Domain` or pure Infrastructure type |
| **Compile-verified** | The fix is in an Android head; it builds locally (`setup-android-build.sh`) but behaviour needs a phone | `PolicyManager`, `MmsDownloader`, `CallScreen`, receivers, WorkManager wiring, `HomeActivity` |
| **Device-pending** | Matches an item CLAUDE.md already marks "pending on-device", or depends on a carrier/service | Role-consent dialog under lockdown, MMSC acceptance, lock-task launch, TIDAL Player |

Auto-merge requires **Test-verified**. Compile-verified and Device-pending fixes are opened as normal
PRs for approval, with the device check written out as a checklist in the body.

---

## 6. The four conflicts

### 6.1 Full content vs. the public-repo rule

**Rule:** CLAUDE.md, "This repository is public": no real balances, messages, names, or anything from
Drive/Gmail/phone in any commit, fixture, PR text or log. **Conflict:** the reader sees exactly that.
**Mechanism (four layers):**

1. **Plaintext never enters the working tree.** It lives under `$TMPDIR/log-loop/`, outside the
   repo; `LogLoopReader` refuses to write under the repo root (`OutputOutsideRepoTests`). A
   `.gitignore` entry for `log-loop/` is belt-and-braces.
2. **Synthetic reproductions only.** A fix's test is built from the **shape** of the failing input,
   not its values: invented names (`example.com`, `555-01xx`, fictional lenders), invented amounts.
   For binary inputs (MMS PDUs) the test builds the bytes with the existing synthetic encoders. The
   runbook says: describe the shape in your own words first, then write the fixture from the
   description, never by editing a copy of the real input.
3. **Sanitizer for anything quoted.** `LogExcerptSanitizer` (in `Tools/LogLoopReader`) is the only
   way an excerpt reaches a PR or issue: it keeps the message **template** and exception type, and
   replaces digits runs ≥ 3, email addresses, phone shapes, currency amounts, URLs, and any token
   that appears in a capitalized-name position with typed placeholders (`<n>`, `<email>`, `<amount>`,
   `<name>`). Tests: `LogExcerptSanitizerTests` with a table of real-looking synthetic inputs.
4. **A diff scanner that gates the PR.** `LogLoopLeakScan` (tool + CI job `log-loop-guard`, runs on
   `claude/logfix-**` pushes) gets the run's plaintext **fingerprint set** from the session — salted
   SHA-256 of every distinct token of length ≥ 4 in the week's logs (names, numbers, amounts,
   addresses), salt random per run, the set itself never committed: the session runs the scan locally
   before pushing, against the diff, the PR title and body. Any hit blocks the push. CI runs a
   content-free subset (patterns: card-like numbers, `@` addresses outside `example.com`, phone
   numbers outside `555-01xx`, `$` amounts in test files) that also guards ordinary PRs.
   Tests: `LogLoopLeakScanTests` (a planted real-looking token in a fake diff is caught; invented
   fixture values pass).

Residual risk, stated plainly: the scanner catches tokens it has seen; a paraphrase ("the owner's
Chase card") gets through layer 4 and is caught only by layers 2–3 and review. That is why C10 and
anything touching fixtures of personal data is never auto-merged. **If personal data lands in a
commit anyway**, the runbook says: stop, do not push a covering commit, say so in the report — the
CLAUDE.md rule.

### 6.2 The passphrase as an environment secret

Resolved in §3.2: the environment holds only `SWT_LOG_KEY`, a one-way derivative scoped to log files,
in a dedicated environment, rotatable by epoch without changing the vault passphrase.

### 6.3 Auto-merge

**Rule:** CLAUDE.md, Git Workflow: never push to `main`; open a PR and wait. O3 carves out one
exception. **Definition:** a PR is a *log-derived bug fix* when **all** hold:

1. Branch matches `claude/logfix-*`.
2. Body has a `log-loop` block (§4.2) whose `class` is C1–C4, and the fingerprint matches a finding
   in that week's report issue (the gate reads the issue).
3. The diff contains a **new or changed test** that the PR's own CI shows passing, and the session's
   report records it **failing before** the fix (TDD evidence: the runbook commits the test first, as
   its own commit, and the gate checks the first commit's CI result is red on that test).
4. Mark is **Test-verified** (§5).
5. Diff touches no path on the **never-auto list** (§6.4) and no frozen-identifier guard file.
6. Diff size ≤ 200 changed lines, ≤ 6 files, no new package reference.
7. `log-loop-guard` (leak scan) and every app workflow's `test` job are green.

**Mixed PRs:** a PR containing any change not needed for the fingerprinted bug is **not**
auto-merged — the gate cannot tell a refactor riding along from a fix. The runbook says to split: the
fix goes alone; the rest becomes a separate normal PR.

**Mechanism:** a workflow `log-loop-automerge.yml` (on `pull_request` + `check_suite` completion)
evaluates 1–7 via `Tools/LogLoopReader gate` and, if all pass, calls GitHub's auto-merge (repo
setting "Allow auto-merge" must be on; branch protection on `main` must require the test checks so
auto-merge waits for them). The session itself never calls `merge_pull_request` — the gate is code
in the repo, reviewed like code, not the session's judgment. Tests: `AutoMergeGateTests` (each of
the seven conditions failing alone blocks; all passing allows; mixed PR blocks).

### 6.4 PRs in sensitive code — risks for the owner to accept or reject

Not blocking; the owner decides each.

**FocusPhone lockdown**

| Risk | Why it is specific | Recommended extra check |
|---|---|---|
| A fix that loosens policy | `PolicyPlanBuilder` (`FocusPhone/Codex.Domain/Policy/PolicyPlanBuilder.cs:51`) and `PolicyContext.AllowlistedPackages` (`PolicyContext.cs:109`) decide what runs on a locked phone; "app X failed to open" in a log invites un-suspending X | Put `Codex.Domain/Policy/**`, `Flyleaf.Android/PolicyManager.cs`, `LoginBrowserPolicy` on the **never-auto list**; PR body must state whether the set of runnable packages changes |
| Admin-mode gating | `PolicyContext.AdminModeActive` (`PolicyContext.cs:21`) gates Chrome; a fix that reads it wrong opens the web | A test asserting the runnable set with `AdminModeActive=false` is unchanged by the PR |
| Frozen identifiers | App ids, component names, prefs file names (`Codex.Tests/Guard/FrozenInvariantsTests.cs`) — a rename bricks in-place upgrade silently | Never-auto for any diff touching a guard file or a `[Activity(Name=…)]`/`[Service(Name=…)]`/`[BroadcastReceiver(Name=…)]` attribute |
| Updates channel | `UpdateService`/`SiblingUpdatePlan` installs silently as device owner; a bug there can push a bad build to every head | Never-auto for `Storyworthtelling.Updates/**` and `Flyleaf.Android/UpdateService.cs` |
| No device in the loop | Every lockdown fix is at best Compile-verified (§5) | Already excluded from auto-merge by condition 4 |

**Index money arithmetic**

| Risk | Why | Recommended extra check |
|---|---|---|
| A fix that changes the payoff picture | `SnowballEngine` is checked against an independent Python oracle (`Ledger/Ledger.Tests/Snowball/SnowballSheetTests.cs:28`, `Oracle/sheet_oracle.py`) | Never-auto for `Ledger.Domain/Snowball/**`; any change must leave `SnowballSheetTests` passing **without regenerating** `sheet_expected.json` — a PR that touches the expected file is never auto-merged |
| `decimal` / `CalendarDate` discipline | A "fix" converting to `double` or `DateTime` slides money or a day | Leak-scan-style grep in `log-loop-guard`: no new `double`/`float` in `Ledger.Domain`, no `DateTime` where `CalendarDate` is used |
| Real figures in fixtures | The tempting reproduction is the real balance from the log | Covered by §6.1; plus never-auto for any test file under `Ledger.Tests/Snowball/` |
| Plaid token handling | Institution access tokens live in SQLite and the vault | Never-auto for `AccountService` credential methods |

**Also suggested for the never-auto list:** `Foreword.*/Prayer/**` (isolation), `Storyworthtelling.Voice/Secrets/**`
(the vault), and anything implementing `IPrayerReader`.

---

## 7. Phasing

CI note: branch pushes run only each app's `test` job (CLAUDE.md, "CI minutes are the budget").
Each logfix PR costs one test run per affected workflow; the signed build runs once on merge. The new
`log-loop-guard` job is small (no Android SDK). Budget: at most **5 logfix PRs per run** (Q2), so a
noisy week cannot spend the month's minutes.

### Phase 0 — Spike (no app code)

- Prove the transport: upload a synthetic envelope to Drive by hand, have a cloud session download it
  with the connector, write it to disk and decrypt it with a 20-line tool. Try 15 KB and 60 KB files.
- **Exit:** both decrypt byte-exact, or the fallback (Q4) is chosen.

### Phase 1 — Shared pieces + pilot app: **Post**

Why Post: it already has the richest logs (failure log, texts trail, field notes, a Diagnostics
export), its domain is the most unit-testable (pure MMS parsers, `TimelineMerger`, `DeleteBuffer`),
and its recent bugs were exactly the log-revealed kind (the `MmsNotificationParser` wrapper, the
`DeleteBuffer` race). Its content is sensitive, which tests the leak guards on the hardest case first.

Build (tests first, each named):

- `Storyworthtelling.Diagnostics`: `LogSpool` (`LogSpoolTests`: seal at 64 KB, seal by age, cap
  drops oldest with a `DROPPED` line, survives restart, sequence monotonic), `LogBatch`
  (`LogBatchCodecTests`: round-trip, gzip, header fields), `LogUploadPlan`
  (`LogUploadPlanTests`: no key → nothing; read-back mismatch → keep; existing same-name match → skip
  create and delete local; mismatch → `.r2`; 50-segment budget), `LogKey` (`LogKeyDeriveTests`).
- `Storyworthtelling.Voice`: `IDriveLogFolder` + `DriveLogFolder` (`DriveLogFolderTests` over the fake
  Drive service pattern already used for `DriveTextFile`).
- `Codex.Domain`: `ILogMirror`; `DiagnosticLog` mirrors every `Append` (`DiagnosticLogMirrorTests`:
  roll and clear do not touch the spool).
- `Post.Android`: wire the mirror for both logs and the crash store; `LogUploadWorker`; one status
  sentence on `DiagnosticsActivity`.
- `Tools/LogLoopReader` (`decrypt`, `merge`, `fingerprint`, `sanitize`, `leakscan`, `gate`) with
  `LogLoopReaderTests`, `LogExcerptSanitizerTests`, `LogLoopLeakScanTests`, `OutputOutsideRepoTests`,
  `AutoMergeGateTests`; `Tools/LogKeyDerive`.
- `.claude/log-loop/RUNBOOK.md`; the `log-loop` environment and secret (owner); the trigger.
- **Auto-merge stays OFF** in Phase 1: the gate runs in report-only mode and writes its verdict as a
  PR comment.

**Exit:** four consecutive weekly runs complete; every Post file uploaded is trashed or explained; no
leak-scan hit on any PR; the owner has reviewed every PR the gate *would* have auto-merged and agrees
with ≥ 90% of the verdicts.

### Phase 2 — MAUI heads: Folio, Index, Foreword

- Consolidate the three file loggers into `Storyworthtelling.Diagnostics.FileLogger` (cap, roll,
  category levels), keeping each head's file name (`pipeline.log`, `refresh.log`, `delivery.log`) so
  on-screen Diagnostics pages are unchanged. Gives Foreword's `delivery.log` the cap it lacks.
  Tests: `FileLoggerTests`; `PrayerNeverSpooledTests` (Foreword).
- Folio: spool sink keeps template + args, but `ErrorText.Describe` bodies over 2 KB are truncated in
  the spool (manuscript-quoting risk).
- **Exit:** two clean weekly runs including all three; the prayer guard green; auto-merge turned on
  for Post only (owner's call after Phase 1 review).

### Phase 3 — Flyleaf, Plate, Score, Pulp

- Add `DiagnosticLog` to Flyleaf (`PolicyManager`, `UpdateService`, `CallScreen`, `LockedSessionController`)
  and Plate — replacing logcat-only calls on failure paths. Tests: `FlyleafDiagnosticsTests` (policy
  pass failure lands in the log), plus a guard that every head calls `CrashReporter.Install` *and*
  mirrors to the spool (`EveryHeadSpoolsTests`).
- Score and Pulp: mirror only.
- **Exit:** every head appears in two consecutive reports; auto-merge extended app by app as the owner
  approves.

### Phase 4 — Partner's phone (if Q3 says yes)

---

## 8. Open questions for the owner

| # | Question | Recommended default |
|---|---|---|
| Q1 | Turn on auto-merge when Phase 1 ends, or stay report-only longer? | Stay report-only for four runs; then enable for Post, extend per app. |
| Q2 | Cap on fix PRs per weekly run? | **5**, highest occurrence count first; the rest are reported. |
| Q3 | Should her phone's logs join the loop? (They upload to her Drive, which the owner-account connector cannot see.) | Not in the pilot. Later: her apps write into a `storyworthtelling-logs` folder **shared with** the owner's account, same log key. |
| Q4 | If Phase 0 shows the connector cannot carry the files faithfully, add a Drive OAuth token to the `log-loop` environment for `LogLoopReader`? | Yes, a dedicated OAuth client; the cost is a second secret whose leak exposes Drive ciphertext (which the connector already exposes). |
| Q5 | Accept the never-auto list in §6.4 as written? | Yes. |
| Q6 | Keep trashed log files' 30-day recovery, or also empty them earlier? | Keep 30 days; no permanent delete tool is granted. |
