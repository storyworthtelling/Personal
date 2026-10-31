# Log loop — implementation plan

A weekly, log-driven maintenance loop. Every Android app in the repo logs what goes wrong, including
buttons that do nothing, repeated presses, and browser sign-ins that never come back. Once a week each
phone encrypts its logs, uploads them to Drive, confirms the upload, and **clears them from the
phone**. A scheduled cloud Claude Code session then reads them, triages, fixes what is a bug, opens
PRs, and trashes what it has processed.

This is a plan only; no application code is written here. Claims about existing code cite the file
(paths from the repo root, as of `216da1e`).

---

## 0. Decisions

### From the owner

| # | Decision |
|---|---|
| O1 | Logs may carry full content (messages, amounts, names). They are encrypted before upload. |
| O2 | Encryption is the existing `VaultEnvelope` (`Storyworthtelling.Voice/Secrets/VaultEnvelope.cs`). The reader is a scheduled cloud session holding a key as an environment secret. |
| O3 | Auto-merge only for bug fixes derived from log files. Everything else: PR and wait (CLAUDE.md, Git Workflow). |
| O4 | PRs may touch any app, including the FocusPhone lockdown and Index's money code. |
| O5 | **The logs themselves are cleared** on the phone once Drive has a confirmed copy. The in-app Diagnostics screens then show only what happened since the last upload. |
| O6 | **Every app** gets the loop: Flyleaf, Score, Post, Plate, Pulp, **Constitution**, Index, Folio, Foreword. |
| O7 | **Constitution logs body events, never body values.** `BodyStaysHomeTests` stands and is extended to the log path. |
| O8 | **Taps: problems only.** Log a tap when it went wrong (dead, repeated, disabled, threw, wrong outcome), with the last ~20 taps as context. Not a trail of every press. |
| O9 | **Browser sign-in: log the full redirect URL**, including the one-time code. |
| O10 | **Dead-tap detection is automatic in the UI kits, plus declared expectations on key actions.** |
| O11 | **Her phone joins** through a shared Drive folder, after the pilot. |
| O12 | **Upload weekly**, just before the reader runs. |

### Defaults kept by the assistant

| # | Default | Reason |
|---|---|---|
| A1 | Drive deletion = **trash**, never permanent delete. | 30 days to recover from a bad triage. The Drive connector has no permanent delete anyway. |
| A2 | Prayer text never enters any log, **by mechanism** (§4.6). | Today it holds only by care: `ClaudeJournalTranscriber` logs whole exceptions (`Foreword/Foreword.Infrastructure/Prayer/ClaudeJournalTranscriber.cs:80`). |
| A3 | Even with O9, **access tokens, refresh tokens, PKCE verifiers, client secrets and passwords are never logged**. | O9 covers the redirect URL. The URL's code is single-use, short-lived, and for TIDAL useless without the verifier, which never leaves the app. A refresh token is a standing credential. |
| A4 | The session gets a **log key derived from** the vault passphrase, never the passphrase (§6.2). | Revises O2's wording, not its intent. |
| A5 | One new **dependency-free** shared library, `Storyworthtelling.Diagnostics` (net10.0), owns the pure logic for every head. | Native and MAUI heads must clear, seal, judge taps and read sign-in trails identically. Two copies of "when is it safe to delete a log" is the drift that loses logs. `Codex.Domain` takes a project reference to it. The library has no package dependencies, so Domain stays free of third-party code. |

---

## 1. Apps and existing logs

### 1.1 The apps

| App | Head | UI kit | Notes |
|---|---|---|---|
| Flyleaf | `FocusPhone/Flyleaf.Android` | `Codex.Ui.Android` | Device owner; installs the others |
| Score | `FocusPhone/Score.Android` | `Codex.Ui.Android` | |
| Post | `FocusPhone/Post.Android` | `Codex.Ui.Android` | |
| Plate | `FocusPhone/Plate.Android` | `Codex.Ui.Android` | |
| Pulp | `Pulp/Pulp.Android` | `Codex.Ui.Android` (+ `Codex.Domain`) | Both phones |
| Constitution | `Constitution/Constitution.Android` | `Codex.Ui.Android` (+ `Codex.Domain`, `Storyworthtelling.Household`) | Newest (P13); both phones possible |
| Index | `Ledger/Ledger.Android` | `Storyworthtelling.Maui` | Both phones (two roles) |
| Folio | `Storyboarder/Folio.Android` | `Storyworthtelling.Maui` | |
| Foreword | `Foreword/Foreword.Android` | `Storyworthtelling.Maui` | Both phones |

### 1.2 Existing logs

| App | Log | Where | Captures | Cap | Sensitive? |
|---|---|---|---|---|---|
| Shared native | `DiagnosticLog` | `FocusPhone/Codex.Domain/Diagnostics/DiagnosticLog.cs` | Non-fatal failures; rolls to `.1`, **old backup discarded** (`:127-138`) | 128 KB ×2 (`:29`) | Per caller |
| Shared native | `CrashLogStore` | `FocusPhone/Codex.Domain/Diagnostics/CrashLogStore.cs:18` | **Latest crash only**, overwritten | 1 | Low |
| Shared native | `CrashReporter` | `FocusPhone/Codex.Ui.Android/Diagnostics/CrashReporter.cs` | Uncaught handler → crash store; installed by Flyleaf, Score, Post, Plate, Pulp | — | — |
| **Flyleaf** | *none on disk* | `PolicyManager.cs`, `UpdateService.cs`, `CallScreen.cs`, `LockedSessionController.cs`, `RestrictedBrowserActivity.cs` → **logcat only** | Policy, installs, calls, locked sessions, blocked links | logcat | Low |
| Flyleaf | `UpdateStatusStore` | `Flyleaf.Android/UpdateService.cs:342` | One status sentence | 1 | Low |
| **Plate** | *none on disk* | `PlateActivity.cs`, `FaceRecogniser.cs`, `Rolodex.cs`, `EditorActivity.cs` → logcat | | logcat | Contact names |
| **Score** | `DiagnosticLog` | `FocusPhone/Score.Android/AudioDiagnostics.cs:38` | Playback, scan, TIDAL sign-in opened (`TidalPairingActivity.cs:188-190`) | 128 KB ×2 | Low |
| **Post** | `DiagnosticLog` | `FocusPhone/Post.Android/PostDiagnostics.cs:45` | Sync, bridge, MMS parse shapes; crashes appended | 128 KB ×2 | Mail addresses/subjects |
| Post | texts trail | `PostDiagnostics.cs:31-32,46`, lines from `FocusPhone/Codex.Domain/Post/TextTrail.cs` | One line per text, sender as last two digits (`TextTrail.cs:71-75`) | 96 KB ×2 | No (by design) |
| Post | `FieldNotes` | `FocusPhone/Codex.Domain/Post/FieldNotes.cs`; prefs `fieldNotes` (`Post.Android/FieldNotesStore.cs:17`) | Tallies and samples | Bounded | Samples |
| **Pulp** | `DiagnosticLog` | `Pulp/Pulp.Android/App/PulpDiagnostics.cs:18` | Lookups, sports sources, counters | 128 KB ×2 | Titles |
| **Constitution** | five trails | `Constitution.Android/Body/BodyLog.cs:18-20` (`body.log`), `Services/ConstitutionVaultLog.cs:16-18` (`vault.log`), `WorkoutsTab/WorkoutsLog.cs:13-15` (`workouts.log`), `Budgets/BudgetTrail.cs:13-15` (`budget.log`), `ReadBack/ReadBackLog.cs:12` | Phrases per event, each over `DiagnosticLog` | 32 KB ×2 each | Body phrases are value-free by guard |
| Constitution | export | `Constitution.Android/Settings/DiagnosticsExport.cs:22,57-58`; `Constitution.Domain/Diagnostics/*` | Snapshot + last lines of each trail | — | Guarded by `BodyStaysHomeTests` (`Constitution.Tests/Body/BodyStaysHomeTests.cs:578-589`) |
| Constitution | **no crash handler** | no `CrashReporter.Install` or `UnhandledException` anywhere in `Constitution.Android` | — | — | — |
| **Index** | `refresh.log` | `Ledger/Ledger.Android/PipelineFileLogger.cs:13,15` | `ILogger` sink: Plaid, refresh; Connect flow (`Pages/PlaidLinkPage.xaml.cs:170-488`) | 1 MB ×2 | Institution names |
| Index | `crash.log`, `pending-crash.txt` | `Ledger.Android/LaunchFaults.cs:16`; `Ledger.Domain/Diagnostics/PendingCrashStore.cs:11` | Crashes | — | Low |
| **Folio** | `pipeline.log` | `Storyboarder/Folio.Android/PipelineFileLogger.cs:15,17` | Every Claude call, failures with inner chains | 1.5 MB ×2 | Error bodies may quote the manuscript |
| Folio | `crash.log` | `Folio.Android/Pages/SetupPage.xaml.cs:17` | Crashes | — | Low |
| **Foreword** | `delivery.log` | `Foreword/Foreword.Android/DeliveryFileLogger.cs:11` | Morning delivery, calendar, prayer pipeline | **None: grows forever** (`:43-44`) | Calendar titles; prayer-adjacent |
| Foreword | `crash.log` | `Foreword.Android/Pages/DiagnosticsPage.xaml.cs:20` | Crashes | — | Low |

---

## 2. Gaps, and what fills them

What the inventory shows is missing, for the feedback the owner asked for: buttons that don't work,
repeated presses, sign-ins that never come back and why, plus the silent failures that matter on a
locked phone.

| # | Gap | Where | Fill (section) |
|---|---|---|---|
| G1 | No record of a button that did nothing, or of the same control pressed again and again | Every app | Tap watch (§4.1) |
| G2 | Browser sign-ins log at most "opened" and "failed"; a sign-in that **never returns** leaves no trace, and nothing says why | Score TIDAL, Index Plaid, Post Gmail, every Google sign-in, Flyleaf's redirect hop | Sign-in trail (§4.2) |
| G3 | Flyleaf and Plate keep no file log at all | Flyleaf, Plate | `DiagnosticLog` in both (§4.4) |
| G4 | Constitution has no crash handler | Constitution | `CrashReporter.Install` (§4.4) |
| G5 | Only the latest crash is kept, except in Post | Native heads | Crashes append to the log (§4.4) |
| G6 | A process killed by Android, an ANR, or a force-stop leaves nothing. This is the shape of Post's "stopped receiving texts" (CLAUDE.md, the `DeleteBuffer` finding) | Every app | Exit reasons on next launch (§4.3) |
| G7 | Background workers can silently never run | Every app with WorkManager | Worker trail (§4.3) |
| G8 | Rolled logs throw away the older half, which after O5 means a busy week loses history before upload | `DiagnosticLog` roll, both `PipelineFileLogger`s | Roll into the outbox, not the bin (§3.1) |
| G9 | Foreword's `delivery.log` is unbounded | Foreword | Shared capped logger (§4.4) |
| G10 | Per-app holes: Flyleaf's policy passes and install history; Post's SMS role checks and send results; Plate's DCIM writes and scan delivery; Pulp's hand-off results; Index's Plaid item error codes and the Post balance handoff; Constitution's Body sync outcome (`CONSTITUTION-P13-PROGRESS.md`, C-7.1.3: Diagnostics shows "NeverRan") | Per app | Per-app lines (§4.5) |

---

## 3. Upload and clear (device side)

### 3.1 The log becomes the outbox

O5 makes the design simpler than a mirrored copy: the log files themselves move into an outbox and
are deleted once Drive has them.

Types in `Storyworthtelling.Diagnostics` (pure `System.IO`, unit-tested against a temp folder like
`DiagnosticLog` is today):

- **`LogOutbox`**, directory `FilesDir/log-outbox/`. It holds **sealed segments**:
  `NNNNNN.<log>.sealed`, where `NNNNNN` is a monotonic per-install counter persisted in `seq`.
- **Sealing replaces discarding on roll (G8).** When a live log reaches its cap, the full file is
  renamed into the outbox as a sealed segment, not into `.1` to be deleted next time. The Diagnostics
  screens read **live + sealed**, so nothing disappears from the phone until it is in Drive.
- **Sealing at upload.** `LogOutbox.SealAll()` renames every live log into the outbox under its
  lock. A rename is atomic on one filesystem. A writer in the WorkManager process (`DiagnosticLog`
  opens by path on every append, `DiagnosticLog.cs:68`) simply starts a new live file. No line is
  lost or split across the cut.
- **Cap per app: 8 MB of sealed segments.** Past it, the oldest is dropped and the next segment opens
  with `DROPPED n segments (oldest <time>)`, so the reader knows the record has a hole.
- **Every log adopts it**: `DiagnosticLog` (so every native trail, Constitution's five included), the
  crash store, and the MAUI file logger (§4.4).

### 3.2 Payload

`LogBatch` is one Drive file. It carries a header (`schema`, `app`, `installId`, `phone` =
`owner`|`partner`, `appVersion`, `versionCode`, `logKeyEpoch`, `firstUtc`, `lastUtc`, `segments`)
and the lines of up to **256 KB of plaintext**. It is serialized to JSON, gzip-compressed, base64'd,
and sealed with `VaultEnvelope.Write`. `installId` is random per install, never a serial number or
an address. `phone` comes from the role the app already derives (Index `InstallRoleService`,
Foreword `OwnerColumn`, the vault's `OwnerAccount`).

### 3.3 The weekly upload worker

O12: weekly, just before the reader. `LogUploadWorker` per head runs as **daily** WorkManager work
(`log-upload`, constraint `NetworkType.Connected`). Each day it asks the pure
`UploadWindow.Due(lastConfirmedUpload, now)` whether to act: **yes from Saturday 18:00 local** until
a confirmed upload lands that week. A phone offline on Saturday uploads at its first connection;
whatever lands after the Sunday run waits for the next one. Each Diagnostics screen also gets a
**Send logs now** button. It counts as a key action (§4.1).

The pure `LogUploadPlan.Decide(segments, keyState, existingRemote, now)` decides; the worker only
carries it out. For each batch, oldest first:

1. **No key, stop.** If the vault passphrase is empty (the post-reset state; `VaultPassphraseStore`
   for native heads, the `VaultPassphrase` setting for MAUI), upload and clear nothing. One status
   sentence on Diagnostics: *"Logs are waiting for the vault passphrase."* Never plaintext, never an
   empty key. This is the refusal `DrivePrayerArchive.SaveAsync` already makes
   (`Foreword/Foreword.Infrastructure/Prayer/DrivePrayerArchive.cs:65-70`).
2. **Name**: `swt-log.<app>.<installId>.<firstSegment>-<lastSegment>.json` in the folder
   `storyworthtelling-logs`.
3. **Idempotency.** If that name already exists and decrypts to the same SHA-256, skip to step 6.
   This covers a crash between upload and delete. If it exists and differs, write `….r2.json`.
   Never overwrite.
4. **Create** the file.
5. **Read back** by the returned id, decrypt with the same key, gunzip, compare SHA-256.
6. **Clear.** Delete exactly the sealed segments in that batch, only after step 5 (or 3) matched.
   Failures leave everything in place. Three weeks in a row of failures show a status sentence.

Budget: at most 40 batches per run (~10 MB plaintext), so a phone back from a long trip drains over
a few days rather than an hour on the radio.

### 3.4 Drive seam

`IDriveTextFile` (`Storyworthtelling.Voice/IDriveTextFile.cs`) is one named file with overwrite. Add,
beside it:

```csharp
public interface IDriveLogFolder
{
    Task<string?> FindAsync(string name, CancellationToken cancellationToken);
    Task<string> CreateAsync(string name, string content, CancellationToken cancellationToken);
    Task<string?> ReadAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ProcessedNamesAsync(CancellationToken cancellationToken); // §5.6
    Task TrashOwnAsync(string name, CancellationToken cancellationToken);                 // §5.6
}
```

`DriveLogFolder` builds on the same `DriveService` factory shape as `DriveTextFile`
(`Storyworthtelling.Voice/DriveTextFile.cs:30-40`), and on `IGoogleAccountBroker.CreateDriveServiceAsync`
(`Storyworthtelling.Google/IGoogleAccountBroker.cs:27`) where a head already uses the broker. Every
uploading head already holds a full-`drive` token for the vault, so no new scope is needed.

---

## 4. Filling the gaps (what gets logged)

### 4.1 Tap watch: dead, repeated and failing controls (G1)

**The judge (pure, shared).** `TapJudge` in `Storyworthtelling.Diagnostics` takes timestamped events
and returns verdicts:

| Event in | Source |
|---|---|
| `Tap(screen, control, point, enabled, isControl)` | kit hook |
| `ScreenChanged(screen)` | activity/page/area change |
| `ContentChanged(screen)` | a layout pass that changed the visible tree's fingerprint (view count, text, visibility) |
| `StatusChanged(screen)` | status line / dog-ear update |
| `ActionStarted(actionId)`, `ActionSucceeded(actionId)`, `ActionFailed(actionId, reason)` | key actions |
| `Threw(control, exception)` | the kit's handler wrapper |

| Verdict | Rule | Logged as |
|---|---|---|
| **Dead** | No screen, content or status change within **1.5 s** of a tap on an enabled control, and no key action running | `DEAD TAP` |
| **Repeated** | **3+ taps** on the same control, or within 48 px of one spot, inside **4 s**, whatever happened | `REPEATED TAP ×n` |
| **Disabled** | Tap on a disabled control | logged only when repeated (3+) |
| **Not a control** | Repeated taps (3+) on something that is not clickable (it looks tappable but isn't) | `TAPS ON NON-CONTROL` |
| **Threw** | The click handler threw | `TAP THREW` + exception |
| **Wrong outcome** | A key action reported failure, or did not report within its declared window | `ACTION FAILED` / `ACTION NO RESULT` + reason |

Each problem line carries the screen, the control (its label text and view id or XAML name), the
verdict, and the **last 20 taps** from an in-memory ring (screen, control, ms since). The ring
itself is never written except as context to a problem (O8).

**Key actions (O10).** An action that starts background work (send, connect, sign in, install,
save, sync now, scan, play, hand off, Send logs now) declares what it should do:
`TapWatch.Action("post.send", within: 30 s)`. The code reports `Succeeded` or `Failed(reason)`. A
declared action is **never judged dead**, which removes the main false positive: a button that works
silently in the background. The guard `KeyActionsDeclaredTests` (per app) lists each app's key
actions by id, so a new one is a deliberate edit.

Initial key actions per app: Flyleaf (install from GitHub now, install APK from Downloads, admin
unlock, move to shelf, feature, block, uninstall, connectivity toggles, answer/return to call);
Score (TIDAL sign in, rescan, play, mark played, Discovery playlist); Post (send SMS, send MMS, file,
delete, request SMS role, Gmail sign in, open attachment); Plate (shutter, send, scan, deliver,
recognise); Pulp (hand off, locked open, plan, calendar write); Constitution (Google sign in, Sleep
Number sign in, Health Connect permission, scan recipe, plan); Index (connect bank, refresh, print
worksheet); Folio (add manuscript, retry, open report); Foreword (deliver now, pair reMarkable, save
mapping).

**Native hook** (`Codex.Ui.Android/Diagnostics/TapWatch`), used by Flyleaf, Score, Post, Plate,
Pulp and Constitution:

- `PaperUi.PrimaryButton/SecondaryButton/QuietButton/DestructiveButton` (`Codex.Ui.Android/PaperUi.cs:84-105`)
  wrap their `onClick` in `TapWatch.Wrap` (exceptions, timing).
- `TapWatch.Attach(activity)` wraps the window's callback. It sees every touch, hit-tests the view
  under the finger (catching non-controls and raw `Click +=` handlers: 40 in Pulp, 18 in
  Constitution, 5 in Post and Plate, 2 in Flyleaf and Score), and hooks
  `ViewTreeObserver.GlobalLayout` for `ContentChanged`.
- Guard `EveryActivityIsWatchedTests` (`Codex.Tests/Guard`) scans every `Activity` subclass in the
  six heads for `TapWatch.Attach`. It reads source by content, like the existing guards.

**MAUI hook** (`Storyworthtelling.Maui/Diagnostics/TapWatch`), used by Index, Folio and Foreword:

- `ButtonHandler.Mapper.AppendToMapping` and the `TapGestureRecognizer` path wire every `Button`
  and tap gesture to the judge.
- `MainActivity.DispatchTouchEvent` feeds raw touches for the not-a-control case.
- `Shell`/`NavigationPage` navigation events give `ScreenChanged`, and `LayoutChanged` on the
  current page gives `ContentChanged`.
- Guard `MauiTapWatchInstalledTests` (in `Storyworthtelling.Maui.Tests`, reading each head's
  `MauiProgram.cs` and `MainActivity.cs`).

### 4.2 Sign-in trail: did a token come back, and why not (G2)

**The trail (pure, shared).** `SignInTrail` records ordered steps under a **flow id**. The flow id is
the OAuth `state` where there is one (it appears in the redirect URL, so Flyleaf's hop and the
app's trail join on it), else a random id.

| Step | Recorded |
|---|---|
| `Started` | flow kind, client id, redirect URI, scopes, which browser path (Custom Tab, Chrome by package, Flyleaf's restricted browser, account picker) |
| `NoBrowser` / `BrowserRefused` | the exception or the policy decision (`ConnectBrowserPolicy`, `LoginBrowserPolicy`, Admin mode on or off) |
| `LeftApp` | time |
| `Hop` (Flyleaf) | `RestrictedBrowserActivity` saw the redirect: handed back to package X via `SuiteRedirect`, or **blocked** and why (`FocusPhone/Codex.Domain/Web/SuiteRedirect.cs`, `LinkPolicy.cs`) |
| `Returned` | **the full redirect URL** (O9) |
| `ProviderError` | `error`, `error_description` from the URL or page |
| `StateMismatch` / `StaleRedirect` | expected vs received state (TIDAL already distinguishes these: `TidalPairingActivity.cs:212-219`) |
| `ExchangeFailed` | HTTP status and full error body of the code-for-token exchange |
| `TokenReceived` | granted scopes, expiry, token **type**. Never the token (A3) |
| `NoReturn` | the app was resumed (back button, task switch) or **10 minutes** passed with no redirect |
| `AccountPicker` / `ConsentDenied` / `RecoverableAuth` | for device-account sign-ins: picker cancelled, consent screen shown/declined, `UserRecoverableAuthException` |

**The why.** `SignInDiagnosis.Explain(trail)` is pure and tested. It turns a trail into one reason
sentence, written into the log beside the trail. Examples:

- *"Opened the TIDAL page; no redirect ever reached Flyleaf or Score; came back by Back after 40 s.
  The provider page most likely refused the request before redirecting (the Error 1002 class: scope
  or redirect URI not registered)."*
- *"Redirect reached Flyleaf's browser and was blocked: scheme not in SuiteRedirect's list."*
- *"Plaid returned but `/link/token/get` had no public_token after 6 polls."*
- *"Google account picker cancelled."*

**Flows covered** (each gets a trail; a guard lists them, `SignInFlowsCoveredTests`):

| Flow | Where |
|---|---|
| TIDAL PKCE | `FocusPhone/Score.Android/TidalPairingActivity.cs` (+ `Codex.Domain/Music/TidalSignIn.cs`) |
| TIDAL device flow | `FocusPhone/Codex.Infrastructure/Music/TidalDeviceAuthClient.cs` |
| Plaid Hosted Link | `Ledger/Ledger.Android/Pages/PlaidLinkPage.xaml.cs` (Chrome + `public_token` polling) |
| Gmail API (Post) | `FocusPhone/Post.Android/MailAccess.cs` |
| Google device account | `Storyworthtelling.Google/IGoogleAccountBroker.cs`; `DeviceAccountSignIn`/`DeviceAccountTokenProvider` in Index, Folio, Foreword, Score; `Codex.Ui.Android/Voice/VoiceGoogleAccount.cs`; `Pulp.Android/FirstRun/FirstRunActivity.cs` (+ `Pulp.Domain/FirstRun/GoogleSignInFailure.cs`); `Constitution.Android/FirstRun/FirstRunPage.cs` |
| reMarkable device-code pairing | `Storyworthtelling.Remarkable` (`RemarkableRegistrationClient`) via Foreword/Folio |
| Sleep Number login | `Constitution.Infrastructure/SleepNumber` (status and error body only; **never the password**) |
| Flyleaf redirect hop | `FocusPhone/Flyleaf.Android/RestrictedBrowserActivity.cs` |
| Chrome under Admin mode | `PolicyManager` / `LoginBrowserPolicy` decisions at sign-in time |

### 4.3 Silent failures every app shares (G6, G7)

- **Exit reasons.** On each launch, `ExitTrail` reads `ActivityManager.GetHistoricalProcessExitReasons`
  (API 30+) since the last read and logs each one: ANR, native crash, low memory, user/force stop,
  killed by the system, with its description. The pure `ExitReasonPhrases` maps codes to sentences.
  This is the record that would have shown Post being force-stopped.
- **Worker trail.** Every `Worker`/`ListenableWorker` in the suite logs start, end, result and
  duration through `WorkerTrail`. `WorkerTrailCoverageTests` scans for worker classes, the same way
  `FrozenInvariantsTests` already reads them. A worker that never ran shows up as a gap in its
  expected cadence (`WorkerCadence.Missed`, pure).
- **Launch line.** Each process start logs app version, versionCode and OS version once, so every
  line can be tied to a build.

### 4.4 Structural fixes (G3, G4, G5, G9)

- **Flyleaf and Plate** get a `DiagnosticLog` (`FlyleafDiagnostics`, `PlateDiagnostics`, shaped like
  `PostDiagnostics`). The logcat calls on **failure and decision paths** become log lines; debug
  chatter stays on logcat.
- **Constitution** installs `CrashReporter` in its `Application`, and its five trails adopt the
  outbox automatically through `DiagnosticLog`.
- **Crashes append to the rolling log in every head.** `CrashReporter` does what Post does today, so
  a second crash no longer overwrites the first.
- **One MAUI file logger.** `Storyworthtelling.Maui/Diagnostics/FileLogger` replaces the two
  `PipelineFileLogger`s and `DeliveryFileLogger`, keeping each file name (`refresh.log`,
  `pipeline.log`, `delivery.log`) and giving Foreword the cap it lacks. The MAUI heads' own
  `UnhandledException` handlers (`Ledger.Android/Platforms/Android/MainApplication.cs:39`, Folio
  `:43`, Foreword `:36`) write through it.

### 4.5 Per-app lines (G10)

| App | Add |
|---|---|
| Flyleaf | Each policy pass: what was suspended, released, hidden; Admin mode state; failures. Each update run: per-app `AppInstallResult` (not just the last sentence). Call surfacing decisions (`CallNotice`, `IncomingCallSurfacer`). Locked session start/end/reason. Every blocked link or redirect in the restricted browser. |
| Score | TIDAL trail (§4.2). Scan summary (files read, changed, failed). Stream/player errors with the track's source. |
| Post | `SmsRole.Decide` result on every launch. SMS send result codes. MMS send/download completion status. Gmail route chosen (`MailRoute.Choose`). Filing deletes that keep failing. |
| Plate | DCIM insert at send time (success or failure). Scan delivery route and outcome. Recognition: match distance and decision (number only, never the image). Errand results returned. |
| Pulp | Every hand-off: target package, link or intent shape, locked or plain, result. Flyleaf refusing a locked open. Sports source fetch failures. Calendar write results. |
| Constitution | Body sync outcome per run (fixes C-7.1.3 "NeverRan"). Health Connect permission results. Sleep Number trail. Vault restore and publish outcomes (already in `vault.log`). **Body events only (O7).** |
| Index | Plaid item error codes per institution. Each refresh run's outcome. The balance handoff received from Post (that it arrived and parsed; the figure is allowed by O1). |
| Folio | Unchanged, except error bodies over 2 KB are cut in the log (manuscript quoting). |
| Foreword | One outcome line per morning document (Day, Brief, Prayer) and per reMarkable upload. |

### 4.6 Prayer and body stay out, by mechanism

- **Prayer (A2).** The MAUI file logger keeps only the **template**, level, exception type and stack
  for categories under `Foreword.Infrastructure.Prayer`, `Foreword.Infrastructure.Pdf.PrayerPages`,
  and the five `IPrayerReader` types. It drops formatted arguments and exception messages. A
  sign-in or tap line never carries page text either. Guard `PrayerNeverLoggedTests` (Foreword.Tests)
  drives each with a sentinel phrase in a `PrayerText` and in an exception message, and asserts the
  log never contains it.
- **Body (O7).** Constitution's log types join `BodyOutbound.PayloadNamespaces`
  (`Constitution.Tests/Body/BodyStaysHomeTests.cs:584-589`), so the existing scanner fails on any
  body type reaching a log line. Guard `BodyLogsCarryNoValuesTests` adds the runtime half: a body
  sync with sentinel values (an invented weight, a heart rate) produces log lines that contain none
  of them. The tap watch logs a body screen's control label, never the text of a body value view.

---

## 5. Reader session

### 5.1 Schedule and grants

- `create_trigger`, `create_new_session_on_fire = true`,
  `cron_expression = "CRON_TZ=<owner's zone> 47 2 * * 0"`: Sunday 02:47, after Saturday's uploads,
  off the hour.
- `connectors = ["Google Drive"]` only. `notifications = { push: true }`.
- A **dedicated environment** `log-loop`, used by no interactive session, holding the secrets (§6.2).
- The prompt points at the committed runbook `.claude/log-loop/RUNBOOK.md`, so the procedure is
  reviewed like code.

### 5.2 Finding and decrypting

Weekly batching means one run may face 100+ files. Relaying every encrypted file through the model
as base64 (`download_file_content`) puts megabytes of ciphertext through the context for no benefit.
So:

- **`Tools/LogLoopReader fetch`** downloads and decrypts directly with a **read-only Drive token**
  (`SWT_DRIVE_READ_TOKEN`, a refresh token for a dedicated OAuth client, `drive.readonly`). It writes
  plaintext under `$TMPDIR/log-loop/plain/`, **outside the repo**. The tool refuses a path under
  the repo root.
- **`LogLoopReader digest`** groups lines by fingerprint (app, exception type, top three own frames,
  or the message template; tap verdicts by screen and control; sign-in trails by flow kind and
  diagnosis). It counts occurrences and phones and keeps first and last seen. The session reads the
  digest first and opens raw lines only for what it triages.
- The connector is used for `search_files` (listing, a cross-check of the tool's view) and
  `trash_file`.
- A corrupted or wrong-key file fails the GCM tag and is reported as `Corrupt` or `WrongKey`. It is
  never misread.

Fallback if the owner declines the second secret (Q2): connector-only transport, with batches capped
at 64 KB plaintext so each file relays as ~15 KB of base64.

### 5.3 Branches

One branch per fix: `claude/logfix-<yyyy-MM-dd>-<app>-<slug>`. It matches CI's `claude/**` filter,
so branch pushes run tests only. No report branch: the report is an issue (§5.5).

### 5.4 Trash, and failing partway

The session keeps a manifest in `$TMPDIR/log-loop/manifest.json`: file → `fetched` / `decrypted` /
`triaged` / `done`. A file is `done` when every fingerprint in it has an outcome: fix PR opened,
reported, or no action.

- **Trash is the last step**, in one pass, after the report issue is posted. A crash leaves the
  untrashed files in Drive for next week. They are still in the folder.
- Next week's run finds open `logfix` PRs by fingerprint (§7.2) and does not duplicate them.
- Files that fail to decrypt are never trashed. After four weeks they are listed for the owner.

### 5.5 Report

One GitHub issue per run, titled `Log loop — week of <date>`. Body: counts per app and class; the
top dead or repeated taps by screen and control **label only, sanitized**; sign-in diagnoses by flow
kind; PR links; the verification mark of each (§8); untrashed files and why. **No raw log line**
appears, only sanitized shapes (§6.1). Ends with the attribution footer. The trigger's push carries
a one-sentence summary.

### 5.6 Her phone (O11)

- The folder `storyworthtelling-logs` lives in the **owner's** Drive and is shared with her account
  as editor. Her installs create their files in it.
- Her files are **owned by her**, and in My Drive only the owner of a file can trash it. So the
  reader cannot trash them. Instead the reader writes `swt-log-processed.json` (names only, no
  content) into the folder. On its next weekly upload, her phone reads that list and trashes its own
  processed files (`IDriveLogFolder.ProcessedNamesAsync`/`TrashOwnAsync`). The owner's phone files
  are trashed directly by the reader.
- Her logs use the same derived key: the shared vault has one passphrase.

---

## 6. Conflicts and how they are resolved

### 6.1 Full content vs. the public-repo rule

**Rule:** CLAUDE.md, "This repository is public": no real balances, messages, names, or anything from
Drive, Gmail or the phone in commits, fixtures, PR text or logs. **Conflict:** the reader sees exactly
that. Tap lines now add button labels and screen content, and sign-in lines add full redirect URLs.
**Mechanism, four layers:**

1. **Plaintext never enters the working tree.** It lives under `$TMPDIR/log-loop/`. `LogLoopReader`
   refuses repo paths (`OutputOutsideRepoTests`), and `.gitignore` adds `log-loop/`.
2. **Synthetic reproductions only.** A test is written from the failure's **shape**, described in
   the session's own words first, then built with invented values (`example.com`, `555-01xx`,
   fictional lenders). It is never edited from a copy of the real input.
3. **Sanitizer.** `LogExcerptSanitizer` is the only path from a log line to a PR or issue. It keeps
   templates, exception types and control ids. It replaces runs of 3+ digits, emails, phone shapes,
   amounts, URLs (**including every OAuth `code`, `state` and `public_token` parameter**), and
   capitalized-name positions with typed placeholders. Tested by `LogExcerptSanitizerTests`.
4. **Leak scan before push.** `LogLoopLeakScan` builds a salted hash set of every distinct token of
   4+ characters in the week's plaintext. The session scans the diff, PR title and body against it
   before pushing; any hit blocks. CI job `log-loop-guard` runs a content-free subset (card-like
   numbers, non-`example.com` addresses, non-`555-01xx` numbers, OAuth-code-shaped strings, `$`
   amounts in tests) on every `claude/**` push. Tested by `LogLoopLeakScanTests`.

Residual risk: a paraphrase ("the owner's Chase card") passes the scan. That is why logging-defect
fixes and anything touching personal-data fixtures never auto-merge (§7.3). If personal data lands
in a commit anyway, the runbook says: stop, no covering commit, say so in the report (CLAUDE.md).

### 6.2 The passphrase as an environment secret

**Rule:** the vault passphrase is never stored outside the phones (CLAUDE.md, Secrets vault).
**Conflict:** an environment secret is a stored copy, readable by every session in that environment.
**Mechanism:**

- `LogKey.Derive(passphrase, epoch)` = PBKDF2-SHA256(passphrase,
  salt `"storyworthtelling-log-key/" + epoch`, 210,000 iterations, the same count as
  `SecretsCrypto.DefaultIterations`, `Storyworthtelling.Voice/Secrets/SecretsCrypto.cs:15`) → 32 bytes →
  hex. Phones derive it in memory. The environment holds **`SWT_LOG_KEY`** only.
- **What a leaked session could decrypt:** the log files. **Not** the vault, the prayer archive,
  the snowball history, the household file or Constitution's body backup: those are sealed under the
  passphrase, and PBKDF2 is one-way.
- **Who can read it:** whoever can open the `log-loop` environment in the owner's Claude account,
  and every session started in it.
- **Rotation:** `LogKeyEpoch` (a constant, starts at 1) is in every batch header. Bump it in a
  reviewed PR, ship, derive the new key locally with `Tools/LogKeyDerive` (reads stdin, prints hex,
  logs nothing), replace the secret. Old-epoch files are reported, not trashed. Changing the vault
  passphrase rotates the key too.
- **The second secret** (`SWT_DRIVE_READ_TOKEN`, §5.2) is read-only. A leak exposes what the Drive
  connector already exposes to the session: plaintext Drive documents and the ciphertext of the
  sealed files. Revoke it from the Google account's third-party access page.
- **Full redirect URLs (O9) and the key.** Anyone holding `SWT_LOG_KEY` and the files can read
  auth codes. The exposure is small: Google device sign-ins carry no URL; a TIDAL code is useless
  without the PKCE verifier, which is never logged (A3); a Plaid `public_token` needs the client
  secret to exchange; and every code expires within minutes, long before a weekly reader sees it.
  Accepted by O9.

Tests: `LogKeyDeriveTests` (fixed passphrase and epoch give fixed hex; a different epoch gives a
different key).

### 6.3 Auto-merge

**Rule:** never push to `main`; PR and wait. O3 makes one exception. **A PR is a log-derived bug fix
only when all of these hold:**

1. Branch `claude/logfix-*`.
2. The body has a `log-loop` block (§7.2) whose class is C1–C4 or C13 (§7.1), and the fingerprint
   is in that week's report issue.
3. The **first commit adds a test that CI shows red**, and the PR head shows it green. This is the
   TDD evidence the gate reads.
4. Mark **Test-verified** (§8).
5. No path on the never-auto list (§6.4), no frozen-identifier guard file, no change to what is
   logged (C10).
6. ≤ 200 changed lines, ≤ 6 files, no new package reference.
7. `log-loop-guard` and every affected app's `test` job are green.

**Mixed PRs** (anything not needed for the fingerprinted bug) are never auto-merged; the runbook
splits them. **Mechanism:** workflow `log-loop-automerge.yml` runs `LogLoopReader gate` and enables
GitHub auto-merge only when all seven pass. This needs the repo setting "Allow auto-merge" on, and
`main` protection requiring the test checks. The session never calls merge itself. Tests:
`AutoMergeGateTests` (each condition failing alone blocks; mixed blocks; all passing allows).

### 6.4 PRs in sensitive code: risks for the owner to accept or reject

Not blocking.

**FocusPhone lockdown**

| Risk | Recommended check |
|---|---|
| A "button does nothing" or "app won't open" finding invites loosening policy: `PolicyPlanBuilder` (`FocusPhone/Codex.Domain/Policy/PolicyPlanBuilder.cs:51`), `PolicyContext.AllowlistedPackages` (`PolicyContext.cs:109`) | Never-auto for `Codex.Domain/Policy/**`, `Flyleaf.Android/PolicyManager.cs`, `LoginBrowserPolicy`; PR states whether the runnable set changes |
| A sign-in "fix" that widens Chrome's allowlist or `SuiteRedirect` | Never-auto for `Codex.Domain/Web/**`; a test asserts the runnable set with `AdminModeActive=false` (`PolicyContext.cs:21`) is unchanged |
| Frozen identifiers (`Codex.Tests/Guard/FrozenInvariantsTests.cs`, Constitution and Pulp invariants) | Never-auto for guard files and any `[Activity/Service/BroadcastReceiver(Name=…)]` edit |
| Update channel installs silently to every head | Never-auto for `Storyworthtelling.Updates/**`, `Flyleaf.Android/UpdateService.cs` |
| No device in the loop | Already blocked by condition 4 |

**Index money**

| Risk | Recommended check |
|---|---|
| Payoff picture changes (`SnowballEngine` vs the Python oracle, `Ledger/Ledger.Tests/Snowball/SnowballSheetTests.cs:28`) | Never-auto for `Ledger.Domain/Snowball/**`; a PR touching `sheet_expected.json` never auto-merges |
| `decimal`/`CalendarDate` erosion | `log-loop-guard` flags new `double`/`float` in `Ledger.Domain` and `DateTime` where `CalendarDate` is used |
| Real figures in fixtures (logs now carry them, O1) | §6.1; never-auto for `Ledger.Tests/Snowball/**` |
| Plaid tokens | Never-auto for `AccountService` credential methods |

**Also proposed for never-auto:** `Foreword.*/Prayer/**`, `Storyworthtelling.Voice/Secrets/**`,
`Constitution.*/Body/**` and `Constitution.*/Backup/**`, and anything implementing `IPrayerReader`.

---

## 7. Triage

### 7.1 Rubric

| Class | Evidence | Outcome |
|---|---|---|
| **C1 Crash** in our code | Crash entry, ANR, exit reason `CRASH`/`ANR` with our frames | **Fix** (auto-merge eligible) |
| **C2 Logic error** | Broken own contract, impossible state, own-format parse failure | **Fix** (eligible) |
| **C3 Unexpected third-party input** | Feed or PDU shape we don't handle | **Fix** with a synthetic reproduction (eligible); else **report** asking for a capture |
| **C4 Retry or backoff wrong** | Re-sent with no wait, budget unspent, re-billing | **Fix** (eligible) |
| **C5 Transient network** | DNS, timeout, offline, reset | **No action**, unless ≥ 20% of a source's attempts that week fail, then **report** |
| **C6 Upstream outage or rate limit** | 429, 5xx, Plaid `INSTITUTION_DOWN` | **No action**; > 3 days running, **report** |
| **C7 Carrier or platform rejection** | MMS refused, role lost, system kill | **Report**; fix only if our handling is wrong (then C2) |
| **C8 Credential rejected** | 401/403, `ITEM_LOGIN_REQUIRED` | **Report** (owner action) |
| **C9 Owner action pending** | No passphrase, no profile | **No action**, listed once |
| **C10 Logging defect** | Missing context, or sensitive data where it shouldn't be | **Fix**, never auto-merge |
| **C11 Cost or performance** | Token counts, retry storms | **Report** with numbers |
| **C12 Unclassifiable** | | **Report** |
| **C13 Dead or failing control** | `DEAD TAP` / `ACTION FAILED` / `TAP THREW` with a code cause: handler not wired, exception, disabled state wrong, navigation dropped | **Fix** (eligible if test-verified); if the cause can't be found from code, **report** with screen + control |
| **C14 Repeated presses on a working control** | `REPEATED TAP` where each press did act | **Report** as UX (slow feedback, ambiguous state). The fix is a design change: normal PR |
| **C15 Presses on non-controls** | `TAPS ON NON-CONTROL` | **Report** as UX (looks tappable). Normal PR, DESIGN.md applies |
| **C16 Sign-in did not return a token** | `NoReturn`, `ProviderError`, `StateMismatch`, `ExchangeFailed`, blocked `Hop` | By diagnosis: our bug (wrong scope, redirect, state handling, blocked scheme) → **fix**, never auto-merge (sign-in and policy are never-auto); provider or portal configuration → **report** with the exact owner checklist; user cancelled → **no action** |

### 7.2 Linking a PR to its log

Every fix PR body carries:

```
<!-- log-loop
fingerprint: <sha256 of app + exception type + top 3 own frames, or template, or screen+control+verdict, or flow kind+diagnosis>
class: C13
source: swt-log.post.<installId>.<range>.json
occurrences: 14
phones: owner
first-seen: 2026-10-24
-->
```

The fingerprint comes from templates, stacks and control ids, never from content, so it is safe to
publish.

---

## 8. Verification limits

Each finding and PR carries one mark:

| Mark | Meaning | Examples |
|---|---|---|
| **Test-verified** | A synthetic test reproduced the bug, red then green | Domain parsers, `TimelineMerger`, `DeleteBuffer`, `ReadingPlan`, `CallGate`, `SnowballEngine`, `PolicyPlanBuilder`, `SiblingUpdatePlan`, `ChildFilter`, `EventLanes`, `TapJudge`, `SignInDiagnosis`, `OAuthRedirect.Read`, `SuiteRedirect` |
| **Compile-verified** | Head code builds locally (`setup-android-build.sh`), behaviour needs a phone | Click wiring in an Activity or XAML page, receivers, WorkManager, `PolicyManager`, `TidalPairingActivity` |
| **Device-pending** | Matches a CLAUDE.md "pending on-device" item, or depends on a carrier, provider page or Health Connect | Role consent under lockdown, MMSC, TIDAL portal settings, Plaid OAuth banks, lock-task |

Most dead-tap fixes will be **Compile-verified**: the handler lives in a head. Only those whose cause
sits in pure code are auto-merge eligible. The report says so per item.

---

## 9. Phasing

**CI.** Branch pushes run only each app's `test` job (CLAUDE.md, "CI minutes are the budget"). Cap
fix PRs at 5 per weekly run (Q3). `log-loop-guard` is small (no Android SDK). Implementation PRs
for this plan touch shared libraries that many workflows watch, so batch them: one PR per phase, not
per type.

### Phase 0: spike (no app code)

- Create the dedicated OAuth client and `drive.readonly` token. A cloud session in `log-loop` lists,
  downloads and decrypts a synthetic envelope with a 20-line tool, and trashes it via the connector.
- **Exit:** byte-exact decrypt of 15 KB and 250 KB files; trash works; the shared-folder ownership
  rule (§5.6) is confirmed with a second account.

### Phase 1: shared core + Post pilot

Why Post: the richest logs, the most testable domain, a browser sign-in (Gmail), the hardest privacy
case, and its recent bugs were exactly the log-revealed kind.

Tests first:

- `Storyworthtelling.Diagnostics.Tests`: `LogOutboxTests` (roll seals instead of discarding; seal-all
  is atomic; cap drops oldest with a `DROPPED` line; survives restart), `LogBatchCodecTests`,
  `LogUploadPlanTests` (no key → nothing; read-back mismatch → keep; same-name match → skip create,
  then clear; mismatch → `.r2`; batch budget; clears only the confirmed segments), `UploadWindowTests`
  (Saturday 18:00 local; missed Saturday catches up; one per week), `LogKeyDeriveTests`,
  `TapJudgeTests` (each verdict; declared action never dead; ring of 20), `SignInTrailTests`,
  `SignInDiagnosisTests` (one case per diagnosis sentence), `ExitReasonPhrasesTests`,
  `WorkerCadenceTests`.
- `Storyworthtelling.Voice.Tests`: `DriveLogFolderTests`.
- `Codex.Tests`: `DiagnosticLogOutboxTests` (the existing class adopts the outbox; the Diagnostics
  screen reads live + sealed), `CrashAppendsToLogTests`, `TapWatchWrapTests` (pure part of the
  wrapper), `EveryActivityIsWatchedTests` (Post only at first, then all native heads),
  `KeyActionsDeclaredTests` (Post), `SignInFlowsCoveredTests` (Gmail).
- `Post.Android`: outbox, upload worker, Send logs now, tap watch, Gmail sign-in trail, exit reasons,
  worker trail, the §4.5 lines.
- `Tools/LogLoopReader` (`fetch`, `digest`, `sanitize`, `leakscan`, `gate`) with
  `LogLoopReaderTests`, `LogExcerptSanitizerTests`, `LogLoopLeakScanTests`,
  `OutputOutsideRepoTests`, `AutoMergeGateTests`; `Tools/LogKeyDerive`.
- `.claude/log-loop/RUNBOOK.md`; the environment, secrets and trigger (owner steps).
- **Auto-merge off**: the gate runs report-only and posts its verdict as a PR comment.

**Exit:** four weekly runs complete. Post's logs are uploaded, cleared on the phone and trashed in
Drive. At least one deliberately broken button on a test build shows up as `DEAD TAP`, and one
cancelled Gmail sign-in as `NoReturn` with a diagnosis. No leak-scan hit. The owner agrees with
≥ 90% of the gate's would-merge verdicts.

### Phase 2: the other native heads (Flyleaf, Score, Plate, Pulp, Constitution)

- Flyleaf and Plate get their file logs. Constitution gets `CrashReporter`. All five get tap watch,
  key actions, exit reasons, worker trail, upload and the §4.5 lines.
- Sign-in trails: TIDAL (both flows), Flyleaf's redirect hop, `VoiceGoogleAccount`, Pulp and
  Constitution first-run, Sleep Number.
- Tests: `EveryActivityIsWatchedTests` covers all six heads; `KeyActionsDeclaredTests` per app;
  `SignInFlowsCoveredTests` complete for native; `BodyLogsCarryNoValuesTests` and the extended
  `BodyStaysHomeTests`; `FlyleafDiagnosticsTests` (a policy-pass failure lands in the log);
  `RedirectHopTrailTests` (`SuiteRedirect` hand-back and block both logged with the flow id).
- **Exit:** every native head in two consecutive reports. One TIDAL sign-in that never returns is
  diagnosed end to end through Flyleaf's hop. Auto-merge on for Post if the owner agrees (Q1).

### Phase 3: MAUI heads (Index, Folio, Foreword)

- Shared `FileLogger` (Foreword gets its cap), MAUI tap watch, Plaid and Google sign-in trails,
  reMarkable pairing trail, exit reasons, upload.
- Tests: `FileLoggerTests`, `MauiTapWatchInstalledTests`, `PrayerNeverLoggedTests`,
  `PlaidLinkTrailTests` (no `public_token` after polling → diagnosis), `KeyActionsDeclaredTests` per
  app.
- **Exit:** all nine apps in two consecutive reports; the prayer guard green.

### Phase 4: her phone

- Share the folder with her account. Her installs upload, then trash what the processed list names.
- Tests: `ProcessedListTests` (her phone trashes only its own files, only those listed).
- **Exit:** two runs where her files are read, processed and trashed by her phone.

---

## 10. Open questions

| # | Question | Recommended default |
|---|---|---|
| Q1 | When to turn on auto-merge | After Phase 1's four runs, Post only; then app by app |
| Q2 | Add the read-only Drive token (`SWT_DRIVE_READ_TOKEN`) so the reader fetches files itself? | **Yes.** It adds no exposure the connector doesn't already have, and weekly batches are too big to relay through the model |
| Q3 | Fix PRs per run | **5**, by occurrence count; the rest are reported |
| Q4 | Accept the never-auto list (§6.4) | Yes, as written |
| Q5 | Dead-tap window 1.5 s, repeat threshold 3 taps in 4 s | Keep; both are constants in `TapJudge`, tuned after the pilot from the logs themselves |
