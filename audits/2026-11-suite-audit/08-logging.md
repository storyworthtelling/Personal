# Log-loop audit: can the logs alone diagnose real failures?

**Short answer:** not yet. The device side is well built for **Post, Pulp and Folio**, and partly for Flyleaf. Six things undercut the whole loop:

1. **The reader cannot parse MAUI logs.** The known regex bug is confirmed.
2. **Missed workers are never detected.** `WorkerCadence.Missed` is called by no production code.
3. **The digest drops each line's build.** It cannot tell whether a fault is "still present after the fix" or "new since the merge".
4. **Every vault restore or publish on the native heads is silently swallowed.**
5. **Constitution's workers record crashes as an exception type name only, at INFO.**
6. **Score has a large logcat-only and swallowed surface:** the Taste/Discovery calls, TIDAL, player errors and its update service.

**Known issue, confirmed.** `FileLogger.Line` (`Storyworthtelling.Maui/Diagnostics/FileLogger.cs:142-146`) writes `[yyyy-MM-dd HH:mm:ss] [WARN] Short: msg`. `LogEntries.Header` (`Tools/LogLoopReader/LogEntries.cs:69`) wants `[..] LEVEL Source — msg`. No MAUI line matches, so a whole segment becomes **one** `TEXT` entry. Index, Folio and Foreword therefore currently produce one digest group per batch, and crash, tap, sign-in and exception keying are all lost for them.

## Per-app matrix

| | Uploaded file log | Launch | Exit reasons | Tap watch | Worker trail | Crash in uploaded log | Unobserved-task hook | Sign-in trail | Main gap |
|---|---|---|---|---|---|---|---|---|---|
| Flyleaf | `HeadLog` → diagnostic.log (128 KB) | ✓ | ✓ | ✓ | ✓ | ✓ (`CrashReporter`) | ✓ | ✓ (redirect hop) | Vault restore/publish swallowed; receivers log to logcat only |
| Score | `AudioDiagnostics` + ~40 logcat-only calls | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ (TIDAL) | Taste/Discovery swallowed; no player-error listener; update service and TIDAL token on logcat only |
| Post | `PostDiagnostics` + texts.log (96 KB) | ✓ | ✓ | ✓ | ✓ (own `LogUploadWorker`) | ✓ | ✓ | ✓ | Best covered; vault restore swallowed; Index courier logcat-only |
| Plate | `HeadLog`/`PlateLogLines` | ✓ | ✓ | ✓ | – (no workers) | ✓ | ✓ | ✓ | Vault and caption catches swallowed |
| Pulp | `PulpDiagnostics` (lowercase area sources) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | Per-title warnings can flood; couriers logcat-only |
| Constitution | diagnostic.log + body/vault/workouts/budget (32 KB each) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | Worker failures are a type name at INFO; every trail is INFO |
| Index | refresh.log (1 MB) via `ILogger` | ✓ | ✓ | ✓ | RefreshWorker only (WorksheetDeliveryWorker missing) | Only through `AppDomain`; Java-thread and unobserved faults go to crash.log, which is not uploaded | → crash.log only | ✓ (Plaid) | Reader can't parse; balance and lender text in the log |
| Folio | pipeline.log (1.5 MB, 2 KB message cut) | ✓ | ✓ | ✓ | ✓ | `AppDomain` only | **none** | ✓ | Reader can't parse; vault restore and heal swallowed |
| Foreword | delivery.log (1 MB, every category at INFO) | ✓ | ✓ | ✓ | ✓ | `AppDomain` only | **none** | ✓ | Reader can't parse; HttpClient chatter; vault restore swallowed |

## Findings, most diagnostic value first

1. **The reader cannot fingerprint MAUI logs** (Index, Folio, Foreword). The known regex bug, confirmed. *Fix:* accept both header shapes in `LogEntries.Header`, or have `FileLogger` write `[stamp] WARN Category — msg`. Add a reader test that feeds it real `FileLogger` output.

2. **Missed or never-run workers are invisible** (all apps). `Storyworthtelling.Diagnostics/WorkerCadence.cs:15` `Missed()` has no production caller, and `Digest.cs` does no cadence analysis. G7 from the plan is therefore unimplemented on the reader side. A worker stranded by a moved class name or a cancelled schedule produces *no lines*, which the digest cannot show. *Fix:*
   - Parse the `WORKER x started` lines in the digest.
   - Run `WorkerCadence.Missed` against a per-app table of expected cadences (Constitution's five workers, Index, Folio, Foreword, Score, Flyleaf, log-upload).
   - Print a "Workers that did not run" section.

3. **The digest loses which build a line came from** (`Tools/LogLoopReader/Digest.cs:142-158`). `Accumulator.Add` ignores `header.AppVersion`/`VersionCode`. First/last seen come from the *batch* header range, not the entry's own time. The runbook's step 3 (Gone / Still present / New since merge) needs exactly this. *Fix:* carry a set of version codes per group and parse the entry timestamps.

4. **The digest is ordered by count, not severity** (`Digest.cs:57-62`). Chatty INFO lines (`WORKER … started/finished`, `LAUNCH`, Flyleaf's "Policy applied") sort above a single `CRASH` or `EXIT ANR`. *Fix:* order sections as CRASH / exception / Exit-problem / Tap / SignIn / Worker failure / WARN / INFO, and fold INFO into counts.

5. **No signal when an app or install uploads nothing** (reader). The loop only ever sees files that arrived. A phone whose upload is refused (no passphrase, not signed in) or failing for weeks is silent. `UploadHistory.FailedWeeks` lives only in the phone's preferences. *Fix:*
   - Keep a list of expected (app, phone) pairs in the reader and report what is absent.
   - Have the upload write a tiny unencrypted-free status marker, or report the refusal in the next batch it does manage to send.

6. **Native vault restore and publish are swallowed with no trace** (Flyleaf `HomeActivity.cs:591-604`, `UnlockActivity.cs:488-500`; Post `PostActivity.cs:200-235`; Score `MusicLibrary.cs:453-474, 650-660`; Plate `PlateRemarkable.cs:94-135`). Two problems:
   - `catch { }` swallows any exception.
   - `if (result.Status != Loaded) return;` discards `WrongPassphrase` and `Corrupt`. Every `PublishAsync` result (`SecretsVaultPublishResult`) is ignored.

   After the factory reset — the vault's whole reason to exist — a mistyped passphrase or an unreadable vault looks exactly like "nothing restored". *Fix:* one shared `VaultTrail.Restored/Published(app, status, exceptionType)` line, at WARN for anything not Loaded or not Written.

7. **Folio and Foreword vault restore is swallowed too** (`Storyboarder/Folio.Android/Platforms/Android/MainActivity.cs:36-47`, also `:54-67` for `VerifyAndHealOutputsAsync`; `Foreword/Foreword.Android/Platforms/Android/MainActivity.cs:38-49`). Index does this correctly (`Ledger/.../MainActivity.cs:49,70`). *Fix:* log the way Index does.

8. **The personal vault turns every failure into "Empty"** (`Storyworthtelling.Voice/Secrets/PersonalVaultAccess.cs:41-48`). Its `PublishAsync` returns false on any exception (`:75-78`) and the exception is never named. Score's `TidalAccountSync.RestoreAsync` therefore can't tell "no backup" from "Drive refused". *Fix:* return the reason or log it; add an `Unreachable` status.

9. **Constitution's workers lose the stack and log failures at INFO** (`Body/BodySyncWorker.cs:52-56`, `MealWeek/MealWeekWorker.cs:42-46`, `WorkoutWeek/WorkoutWeekWorker.cs:47-51,95-98`, `SaturdayDigest/DigestWorker.cs:44-48,75-78`, `ReadBack/ReadBackWorker.cs:47-51`). Each catch records `"… worker failed: {Type}"` through `BodyLog.Outcome`, which is always INFO (`BodyLog.cs:47`), and returns `Failure`. `HeadDiagnostics.Worker` never sees the exception. `MealWeekWorker`'s own doc comment says it "fails the pass with the original exception (stack and all)". It does not. *Fix:*
   - Log at WARN in diagnostic.log with the type chain and the frames, no message (a stack carries no values).
   - Or route through a quiet variant of `HeadDiagnostics.Worker`.

10. **Every Constitution trail line is INFO** (`ConstitutionVaultLog.cs:47`, `WorkoutsLog.cs:41`, `BudgetTrail.cs:41`, `BodyLog.cs:47`). Vault refusals, unreadable household files, Health Connect revoked and calendar failures are indistinguishable by level from "ok, 2 readings". *Fix:* an `Outcome(phrase, problem: bool)` overload.

11. **Constitution's meals, read-back and the bed all write into the 32 KB body.log** (`ReadBack/ReadBackLog.cs:12-17`, `MealWeek/MealWeekServices.cs:96,125,151,268,353`). The meal pass runs every 30 minutes. It rolls quickly and buries Body sync lines under meal-pass lines, and the file name misleads triage. *Fix:* give meals their own trail, or raise the cap.

12. **Score's Listening Genius swallows everything** (`Score.Android/Google/MusicTasteService.cs:65,78,95,124,151`; `HiddenTrackPass.cs:83`). Every Claude and Drive failure (rejected key, 400, quota, Drive scope not re-registered) returns `Empty` or `[]` with no line. A broken Discovery or Friday Picks reads as "no taste learned yet". `Codex.Infrastructure/Music/TasteProfileRefresher.cs:49`, `DiscoveryClient.cs:108,143` and `TidalCatalogClient.cs:55,83,138,173` swallow too. *Fix:* one WARN per call failure with the HTTP status, request id and call name, mirroring Folio's `ClaudeAnalysisClient` logging.

13. **Score has no player-error listener.** No `Player.Listener`/`OnPlayerError` exists anywhere in `FocusPhone/Score.Android`. Drive-stream 401/403, codec failures and podcast redirect failures leave no trace. *Fix:* attach a listener to both sessions in `ScorePlayback`. Log the error code, the source kind (Drive, TIDAL or podcast) and the host only.

14. **Score's TIDAL token refusal goes to logcat only** (`PlaybackService.cs:151`). "Refresh token did not exchange (status=…)" means TIDAL silently vanishes from the merged library. `TidalPlaybackBridge.cs:142,170`, `TidalKotlinBridge.cs:319` and `TidalArtworkCache.cs:34` are logcat-only too. *Fix:* send them through `AudioDiagnostics.LogWarning`.

15. **Score's update service goes to logcat only** (`ScoreUpdateService.cs:70,88,126,205,237`). Release fetch, per-APK install failures (signature mismatch) and token read failures reach only the status store sentence. *Fix:* use `HeadLog`, as Flyleaf's `UpdateService` does.

16. **Flyleaf doesn't log a rejected update token** (`UpdateService.cs:104-109`). The 401/403 path marks credential health and returns with no file line. The run summary (`:64`) also goes only to `UpdateStatusStore`. *Fix:* `HeadLog.Warn` the status, and `HeadLog.Info(result.Summarize())` on every run.

17. **Cross-app contracts are logcat-only on both ends:**
    - Post → Index courier: `IndexCourier.cs:40`.
    - Pulp → Index: `IndexUsageCourier.cs:47`.
    - Pulp → Flyleaf: `HiddenAppsCourier.cs:43`.
    - Flyleaf → Pulp session relay: `PlaybackSessionWatcher.cs:192`.
    - Flyleaf receivers rejecting malformed payloads: `LockedOpenReceiver.cs:44`, `HiddenAppsReceiver.cs:37`.

    Successful receipt is not logged by Flyleaf either. A permission or signature mismatch between separately installed apps — the plan's top unverified item — can't be seen. *Fix:* INFO on send and receive (contract name, result code, never the payload), WARN on refusal, through `HeadLog`.

18. **Index's WorksheetDeliveryWorker has no worker trail** (`Ledger/Ledger.Android/WorksheetDeliveryWorker.cs:39`, `DoWork` not wrapped in `MauiLogLoop.Worker`). It is missed because `WorkerTrailCoverageTests`/`LogLoopHeads` (`FocusPhone/Codex.Tests/Guard/LogLoopHeads.cs`) cover only the six native heads. *Fix:* wrap it, and extend the guard to the MAUI heads.

19. **MAUI crash paths miss the uploaded log:**
    - Index sends `UnobservedTaskException` and Java-thread uncaught exceptions only to `crash.log`/`pending-crash.txt` (`Ledger/.../MainApplication.cs:49-53`, `CrashLogger` at `:95`). The upload carries only `log.LivePath` (`MauiLogLoop.cs:117`).
    - Folio and Foreword have **no** `UnobservedTaskException` hook. That covers 20+ and 33 `async void` handlers plus discarded tasks, e.g. `Folio MainPage.xaml.cs:60`.

    *Fix:* write every crash route to `MauiLog.Current`, add the unobserved hook to Folio and Foreword, and include `crash.log` in the upload paths.

20. **A worker that is stopped mid-run leaves only a "started" line** (all apps). No worker overrides `OnStopped`, and nothing reads `StopReason`. The reader doesn't pair start lines with finish lines. A run killed at WorkManager's 10-minute cap looks like a process death with no cause. *Fix:* override `OnStopped` in a shared base worker that logs `WORKER x stopped (reason n)`, and flag unmatched starts in the digest.

21. **The log upload rides the head's worker body** (`Ledger/.../RefreshWorker.cs:46-51`, `Folio/PipelineWorker.cs:40-45`). If `Work()` throws, `UploadLogs()` never runs, so the logs that would explain the crash are never uploaded. *Fix:* `try/finally`.

22. **Voice failures are logged at Debug, which is dropped everywhere** (`Storyworthtelling.Voice/VoicedMessenger.cs:41`; native `Codex.Ui.Android/Voice/VoiceGreeting.cs:58-61` has no log at all). A rejected key or 400 on every voiced seat in every app is invisible. *Fix:* WARN with a status code, once per seat per day.

23. **Codex.Infrastructure HTTP clients have no logging seam.** None of its clients take a log seam, unlike Folio's `ClaudeAnalysisClient` and Pulp's per-adapter callbacks. `MailReadingClient.cs:103` (`catch (Exception)`), `ShelfGlyphClient.cs:81-87`, `DarkroomCaptionClient.cs:137` and `PodcastSearchClient.cs:60` return "nothing". Post's aggregate "MAIL SORTING asked n, placed m" line mitigates only the first. *Fix:* an optional `Action<string, Exception?>` sink in the constructors, logging call, status and request id.

24. **Android permission denials are swallowed in Post** (`MailboxArea.cs:400-414, 1076-1087`). An `Java.Lang.SecurityException` for READ_SMS or READ_CALL_LOG leaves an empty Mailbox with no line. That is the exact failure if Flyleaf's policy grant stops landing after the role went back to Messages. *Fix:* one line per process per permission.

25. **Native timestamps are minute-only, with no year or zone** (`FocusPhone/Codex.Domain/Time/TimeFormats.cs:43-44`, used by `DiagnosticLogFormatter.cs:18`). Sequences inside a minute (tap → action → crash, retry storms) can't be ordered, and they can't be matched against Exit lines, which carry full stamps. *Fix:* write an ISO-8601 stamp to the file; keep the friendly format for the screen only.

26. **Plaid failures lack the request id and endpoint** (`Ledger.Infrastructure/External/PlaidClient.cs:340-365`). The exception carries the status, `error_code` and message, but not `request_id` or which call failed. *Fix:* parse `request_id` and name the endpoint.

27. **Score's podcast feed failures are toast-only** (`PodcastsArea.cs:1036-1052`). Only the host goes to a toast, and nothing is logged. A feed that has been dead for weeks is invisible. *Fix:* INFO with host and exception type per refresh, summarised.

28. **Other native vault and caption catches are swallowed:** Flyleaf `UnlockActivity.cs:497` (GitHub token publish), Plate `PlateActivity.cs:357` (caption), Score `MusicLibrary.cs:471,657`. *Fix:* as for #6.

29. **A corrupt local store silently resets to empty** (Codex.Infrastructure codecs: `PlaylistCodec.cs:41`, `ListeningEventCodec.cs:29`, `SavedTidalTracksCodec.cs:30`, `PodcastCacheCodec.cs:27`; Score `FridayPicksStore.cs:60,88`). On the next save, data loss happens with no trace. Constitution's stores do this right (`SetAside` + `.unreadable`). *Fix:* the same set-aside pattern, plus a WARN line.

30. **Flyleaf's media-session listener failures are silent** (`PlaybackSessionWatcher.cs:54,69,246`). Missing listener access means Pulp never receives sessions and its watched evidence never lands. *Fix:* log once per process.

31. **Source naming is inconsistent** (all apps). Pulp uses lowercase areas (`"calendar"`, `"sports"`); Post uses `"Post"`, `"FocusPhoneComm"` and `"FocusPhoneEmail"` for the same app's logcat tags; MAUI uses the bare class name (`FileLogger.ShortCategory`, which can collide). This hurts grouping a fault across apps. *Fix:* a shared vocabulary (`Vault`, `Drive`, `Http:<service>`, `Worker`, `Contract:<name>`, `Permission`).

## Volume and caps

32. **Foreword logs every category at INFO** (`Foreword/Foreword.Android/MauiProgram.cs:294-295`, `_ => true`). HttpClientFactory writes about 4 INFO lines per request across about 10 typed clients. That buries delivery lines and fills the 1 MB file. URLs carry `key=`/`api-key=` and home lat/long in the query; Microsoft.Extensions.Http 9.0 should redact query strings, but that is unverified on this build. *Fix:* treat only `Foreword.*` as own, like Index and Folio, and filter `System.Net.Http` to WARN.

33. **Pulp catalogue warnings fire once per title, with a stack each** (`Pulp/Pulp.Android/App/PulpServices.cs:1000-1004, 1019-1023`). A missing OMDb key or exhausted quota produces a multi-KB entry for every title in every refresh. That rolls diagnostic.log and eats the 8 MB outbox (`LogOutbox.DefaultCapBytes`), forcing `DROPPED`. *Fix:* count per failure kind per pass, with one line and one stack.

34. **The 8 MB outbox is shared by every log of an app.** Folio seals at 1.5 MB, so only about 5 segments survive a retry-storm week before `DROPPED`. The digest shows `DROPPED` as an ordinary message group. *Fix:* surface `DROPPED` at the top of the digest, and consider per-file quotas.

## Reader gaps not covered above

35. **The exception key drops non-own frames and can garble the type.** `Digest.KeyOf` keys on own-namespace frames only, so all Java-only exceptions of one type merge into one group (`"Type @ "`). Messages that embed `{e}` (Score's `AudioDiagnostics.LogWarning(…, $"… threw: {e}")`) put the exception type in the header line, so `lines[0]` of the detail is a frame and the "type" is wrong. *Fix:* fall back to the top 3 frames of any kind, and pass exceptions as `exception`, not interpolated text.

36. **ANR traces are not captured** (`FocusPhone/Codex.Ui.Android/Diagnostics/ExitTrail.cs:30`, `MauiLogLoop.cs:139`). The trail reads reason and description only. `ApplicationExitInfo.GetTraceInputStream()` (the ANR thread dump) is never read. *Fix:* attach the main-thread section of the trace, capped at about 4 KB, to Exit ANR lines.

37. **Skips and no-ops look like work done.** `LoopUploadWorker` (`LoopUploadWorker.cs:42-48`) and the Constitution workers return `Success` both when nothing was due and when they acted, so the trail can't tell a skip from a run. The MAUI workers state their skip reasons; the native ones don't. *Fix:* return or log a reason word.

## Personal data already reaching logs

- **Index:** `BalanceReceiver.cs:91,115` logs the balance amount and the sender/lender name to refresh.log. Plan O1 permits the figure, but the lender name is a real institution name in an uploaded log.
- **Shared handwriting extractor:** `ClaudeVisionHandwritingExtractor.cs:138` logs the raw model reply `{Text}` on a parse failure. That reply is a transcription of Foreword's handwritten Day-page notes and of Index worksheet figures. Prayer is separate and appears safe.
- **Tap watch:** `TapWatch.cs:183-205` (`FirstText`) takes the first visible text of a tapped row as the control label. On Post and Rolodex rows that is a contact name or message preview; on Index rows it can be account names or amounts. These go into tap verdicts, their 20-tap context, and the digest's fingerprints. *Fix:* prefer the view id or a declared label, and never read text from list rows.
- **Post:** the logcat lines `MailboxArea.cs:1580` ("Could not block {number}") and `:1744` (source ids) carry a phone number and source ids. If those same strings reach the file log, they are a leak; the paired `PostDiagnostics` call sits right beside them.
