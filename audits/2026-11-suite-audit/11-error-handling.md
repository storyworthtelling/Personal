# Failure-path audit: what the user sees, and whether their data survives, in all nine apps

Read-only audit; nothing in the repo was changed. Scratch scripts are in /tmp/claude-0/-home-user-Personal/b5742c71-73ba-5e61-9ad5-55fc006c45b7/scratchpad. Items the earlier audits already reported (toasts, swallowed native vault calls, Score Taste/TIDAL, Constitution INFO logging, codec resets, Index snapshot-over-Corrupt, CredentialHealthStore race) are not repeated.

**Two problems put data at risk across several apps:**
1. **Failed Drive downloads are treated as empty files.** `DriveTextFile.ReadAsync` ignores the download result, so a failed download looks like "no file". One retried transient error can then overwrite the shared vault with a single app's section.
2. **Drive backups are overwritten by whatever the phone holds.** If the restore never ran, the first new item replaces the backup.

## Scenario × app matrix
✓ handled well · ~ partial · ✗ bad · n/a

| Scenario | Flyleaf | Score | Post | Plate | Pulp | Constitution | Folio | Index | Foreword |
|---|---|---|---|---|---|---|---|---|---|
| 1 Offline (launch / mid-op) | ~ | ~ | ~ | ~ | ~ (every failure called "offline") | ✓ | **✓ best** (`NetworkFailure.IsOffline`, no attempt spent) | ✗ (offline burns all 5 attempts) | ✗ (no network constraint; failure reported as delivered) |
| 2 Google sign-in expired / account removed | ✗ | ✗ | ✗ | ✗ | ~ | ~ | ~ | ~ | ~ |
| 3 Vault: wrong passphrase / missing / newer version | ✗ | ✗ | ✗ | ✗ | **✓ best** wrong passphrase (`PulpVault` outcomes); ✗ newer version | ~ | ✗ silent | ~ | ~ (prayer side ✓) |
| 4 Revoked key, field reappears | ✓ GitHub | ~ | ✗ | ✗ | ✓ | **✓ best** (reMarkable + Sleep Number) | ~ (reMarkable ✗) | ~ (Plaid ✓, reMarkable ✗) | ~ (reMarkable ✗) |
| 5 Quota / rate limit | n/a | ~ | ✗ | n/a | **✓** (reads 403 reasons) | ~ | **✓ best** (QuotaExhausted, CallGate) | ✓ | ~ |
| 6 Process death / async void / receivers | ✓ | ~ | ✗ | ✗ | ✓ (GoAsync) | ✓ | ~ | ✗ | ~ |
| 7 Permission revoked | ~ | ~ | ✓ | ~ | ~ | **✓** (Health Connect notice) | n/a | n/a | n/a |
| 8 Low storage / OOM | n/a | ~ | ✓ (15 MB cap) | ✗ | n/a | ~ | **✓** (`MemoryFailure`, 100 MB pre-check) / ~ restore | n/a | ~ |
| 9 Vague or raw messages | ~ (7 raw `ex.Message`) | ~ (4) | ~ (3) | ~ (6) | ~ (3) | **✓** (0) | ✓ in-app via `PlainError`; ~ Settings (5) | ~ (5) | ~ (7) |

`async void` handlers, counted as total / with no try (expression-bodied ones included): Flyleaf 6/2, Score 2/1, Post 1/0, Plate 3/0, Pulp 0, Constitution 0, Folio 20/9, Index 37/29, Foreword 33/30. The native handlers without a try call methods that catch internally. The MAUI ones mostly do not. Only Index installs crash hooks (`AppDomain` + `UnobservedTaskException`), and they only record — nothing stops the crash.

Thrown without the inner exception: almost none. Foreword's `PlaywrightPageFetcher.cs:79` and `WebViewPageFetcher.cs:43`, and Constitution's `TasteProfileCodec.cs:162,186`.

## Findings, most severe first

1. **Critical — shared (every app that reads Drive) — `Storyworthtelling.Voice/DriveTextFile.cs:50`.** The read calls `await service.Files.Get(id).DownloadAsync(stream)` and ignores the progress it returns. Google's downloader does not throw on a 401/403/429/5xx or a dropped connection; it reports the failure in `progress.Exception`. So a failed read comes back as `""`, and `VaultEnvelope.Read` (lines 36–38) turns that into **Empty**.
   - **Vault:** `SecretsVaultService.PublishAsync` (line 39) then merges this app's section into `SecretsVault.Empty` and **overwrites the shared vault**. The ownership check just before it succeeded, so nothing stops the write. Every other app's section is wiped: Plaid tokens, reMarkable token, Gmail, calendar mapping, Claude key.
   - **Other files:** the owner's household file can be rewritten from the local copy only. The Post archive and Score library treat it as "no backup". Folio's `GoogleDocsClient.cs:93` has the same bug, so a failed download is reported as a corrupt manuscript, or an RTF is read as empty.
   - **Fix:** do what `Storyworthtelling.Voice/Logs/DriveLogFolder.cs:78-85` already does — check `progress.Exception` and throw. Also treat "file exists but read back empty" as Corrupt, not Empty.

2. **Critical — Post — `PostActivity.cs:189-236`, `Codex.Domain/Post/ArchiveSnapshot.cs:135-152`, `PostSync.cs:97-101`.** The ghost-archive restore runs only inside the Gmail vault restore. That restore stops early when mail is already configured, or when the vault is not Loaded (no passphrase, or Drive unreachable). After that the archive restore never runs again.
   - `ArchiveSync.NeedsWriting` writes local over Drive whenever the two differ.
   - **What is lost:** after a factory reset where the owner typed the Gmail details by hand (as the code comment invites) or was offline at first launch, the first message filed replaces the only copy of every filed message with one ghost.
   - **Fix:** run the restore on its own, retried each sync until one Drive read succeeds, and keep a "restore confirmed" flag. Back up as a union merge keyed by `SourceId`, never as a replacement.

3. **High — shared vault — `SecretsVault.cs`, `VaultEnvelope.cs:23-27`.** System.Text.Json drops unknown properties, and neither the vault nor its sections has `[JsonExtensionData]`. An app that has not updated yet loads a vault written by a newer app, merges, and writes it back without the newer sections or fields. The envelope's `Version` is never checked either, so a future format reads as "WrongPassphrase".
   - **Fix:** add extension data, and refuse a newer format the way the household file does with `HouseholdFormatException.IsNewerFormat`.

4. **High — native heads (Flyleaf, Score, Post, Plate, Constitution, Pulp), plus the MAUI token providers — `Codex.Ui.Android/Voice/VoiceGoogleAccount.cs:86-87`.** `BlockingGetAuthToken(..., notifyAuthFailure: true)` posts Google's "sign in again" notification. The lockdown has disabled the status bar, so it is never seen. The call returns null and the app throws "Sign in again" into a log.
   - `IsSignedIn` stays true. Vault, archive, library, household and log upload all stop with no prompt anywhere.
   - The cached token is not invalidated on a 401 (45-minute cache).
   - Foreword's status line still says signed in by email presence while its last outcome says "sign in again".
   - **Fix:** a shared `google-account` credential-health entry, written when the token comes back null or with `AuthenticatorException`, shown through `CredentialFieldState` as a "Sign in again" row. Re-consent from the foreground using `KEY_INTENT`.

5. **High — Index — `Ledger.Infrastructure/Pipeline/BalanceRefreshProcessor.cs:112-129`, `Configuration/Options.cs:36-39`.** Network failures count toward `MaxAttempts = 5` with a 30 s base backoff. A connection drop lasting about 8 minutes (DNS failure or captive portal while "connected") marks the bank **Failed** with `NextAttemptAtUtc = null`. It is never refreshed again until the owner taps Refresh now.
   - **Fix:** Folio's `NetworkFailure.IsOffline` (no attempt spent), and bring Failed banks back after 24 h.

6. **High — Foreword — `Foreword.Infrastructure/Delivery/DailyDeliveryService.cs:58-61, 217-230`.** `TryUpload` swallows a failed upload of The Day. `DeliveryWorker.cs:104-107` then records **"Delivered (…)"** and does not retry. The owner wakes up with no calendar page while Settings says it was delivered.
   - **Fix:** return the outcome per document, fail or retry the run, and show which part did not arrive.

7. **High — Foreword — `DeliveryWorker.cs:108-118, 155`.** The worker has no network constraint, and `MaxRetries = 3` on WorkManager's default 30 s backoff, so the morning is lost if Wi-Fi comes up more than ~3.5 minutes late. `RemarkableStorageClient.Upload` (lines 22–37) always creates a new document, so a retry after the brief was uploaded, or after a timeout the server had accepted, puts a **duplicate "Morning Brief"** on the tablet.
   - **Fix:** a network constraint, longer backoff, and a "delivered today" mark per document.

8. **High — Plate — `ScanArea.cs:62, 330-366`.**
   - The page tray, gathered over several scanner runs, lives only in memory. If Android kills Plate while the ML Kit scanner is in front, every page is lost.
   - Composing runs on the UI thread with every page plus the PDF in memory and no try/catch, so an OOM or IOException crashes the app.
   - The MediaStore row is inserted without `IS_PENDING`, so a failed write leaves an empty "Scan" document.
   - **Fix:** persist the tray, compose off the UI thread, use IS_PENDING, and catch out-of-memory like Folio's `MemoryFailure`.

9. **High — Index — `Ledger.Infrastructure/Services/AccountService.cs:507-518`, `PlaidItemRelease.cs`.** A disconnect while offline: `/item/remove` fails, which is logged and then forgotten. The local row holding the only access token is already deleted, so the Item stays live — and billed — at Plaid forever, and the owner is told nothing.
   - **Fix:** a pending-release outbox that keeps the token until Plaid confirms, retried by `RefreshWorker`, plus a Waiting notice.

10. **High — Post — `Post.Android/MailIntelligence.cs:34-75`, `Codex.Infrastructure/Email/MailReadingClient.cs:89-107`.** The mail sorter makes up to 300 Claude calls per sync. A 401, 429, 529 or empty credit balance each return "no idea", so the same mail is asked again every sync (every 30 minutes, and after every file or delete). There is no backoff, no circuit breaker, and nothing reported to credential health.
    - **Fix:** stop the loop at the first non-success answer, report the `claude` health entry, and classify the failure as Folio's `ClaudeAnalysisClient` does.

11. **Medium — Foreword, Folio, Plate, Index — reMarkable token revoked.**
    - Foreword says "Paired" based only on the token being present (`SetupPage.xaml.cs:55-60`), even though its health entry is written.
    - Folio says "Registered ✓" (`SetupPage.xaml.cs:460`).
    - Plate builds `RemarkableAuthService` without a health sink (`PlateRemarkable.cs:72-73`).
    - Index has no field at all.
    - Only Constitution surfaces it (`Constitution.Domain/Settings/SettingsModel.cs:126`). Fix: run `CredentialFieldState` over `RemarkableCredentialId` everywhere.

12. **Medium — Post — `GmailApiClient.cs:236` vs `SettingsArea.cs:79-81`.** A Gmail API rejection is recorded under `gmail-account`, but Settings reads only `gmail-app-password`. An account the Gmail API refuses is never shown.

13. **Medium — cross-app — shared Claude key and reMarkable token.**
    - Only Folio has the Claude key field, and only Folio's own per-app health store feeds it. A 401 seen by Post, Plate, Flyleaf, Score or Constitution never reaches it.
    - Restore is "local wins" (`Folio.Android/VaultSync.cs:44-63`, Plate's and Post's restores too). Once the owner rotates a key or re-pairs, the other apps keep the dead local copy forever.
    - **Fix:** let a vault value replace a local value whose health is Failing, and share health through the vault.

14. **Medium — Index — `Platforms/Android/BalanceReceiver.cs:96-125`.** The receiver does its work in an `async void` after `OnReceive` returns, without `GoAsync`. On a cold start the process can be killed first. Post remembers its verdict and never hands the balance over again, so it is lost.
    - **Fix:** `GoAsync`, as `Pulp/Sessions/SessionReceiver.cs` and Post's `MmsDownloader` do, or a one-time work request.

15. **Medium — MAUI heads — `async void` with no try.** Examples: Index `MainPage.xaml.cs:1303` (RemoveManualAccount), `:1435` (DisconnectInstitution), `SavingsPage.xaml.cs:632`; Foreword `RotationEditPage.xaml.cs:194`, `TaskEditPage.xaml.cs:172`. A database or navigation exception crashes the app, and inside an undo window the undo is lost with it.
    - **Fix:** one shared helper in `Storyworthtelling.Maui` that awaits the work and routes any failure to the dog-ear — the pattern Flyleaf and Plate already follow by hand.

16. **Medium — Folio — `ProjectBackupService.cs:70-108`, `SetupPage.xaml.cs:143-186`.** Restore overwrites the database, then deletes and re-copies the output folder. Low storage partway through leaves the database pointing at PDFs and images that no longer exist. The worker is cancelled and not rescheduled when the restore fails, and the screen shows the raw `ex.Message`.
    - **Fix:** check free space, copy then swap, keep a database snapshot first, and reschedule in `finally`.

17. **Medium — Score — `Codex.Domain/Audio/AudioLibrarySync.cs:26-42`.** The same overwrite as finding 2. If the restore failed at first launch (offline or not signed in), the first subscription backs up over the full library: listened episodes, resume positions and EQ.

18. **Medium — Plate — `DarkroomStore.cs:37`, `CameraArea.cs:255-262`.** The Darkroom holds the only copy of each photo, and its row is saved with `Apply()` after the file is written. A crash before the write is flushed leaves the photo in the private folder where nothing shows it, and nothing sweeps for such files.
    - **Fix:** `Commit()`, plus a startup sweep for unlisted files.

19. **Medium — Constitution — `Infrastructure/Calendar/CalendarErrors.cs:12`.** Every Google 403, including `rateLimitExceeded`, is told to the owner as "refused access; it may not be shared" — the wrong action. Pulp's `GoogleCalendarClient.cs:306` reads the reason code and gets it right.

20. **Medium — Pulp — `Pulp.Android/App/PulpServices.cs:666-672`.** Every non-household failure, including a revoked token, becomes "Drive couldn't be reached", so there is no sign-in prompt. It also passes `exception.Message` into the status.
    - **Fix:** classify with `AuthFailure.IsHttpCredentialRejection`.

21. **Medium — Plate — `ScanArea.cs:444-451`.** An offline send reads "the reMarkable refused it: {raw message}". It blames the tablet, offers no retry, and has no 100 MB pre-check (Folio has one).

22. **Low — all apps — raw `ex.Message` on screen.** Examples: Foreword `CalendarsPage.xaml.cs:68`, `SetupPage.xaml.cs:544`, and the delivery outcome line; Index `SetupPage.xaml.cs:642` (`LastError`); Folio's Backup/Restore lines; Plate's capture line (`CameraArea.cs:270`).
    - **Fix:** move Folio's `PlainError.Describe` into a shared library and use it in every app.

23. **Low — Plate — `CameraArea.cs:148`, `PlateActivity.cs:114-127`.** A refused camera permission shows "The camera is not ready yet." It does not say what to do, and the permission is only asked for in `OnCreate`.

24. **Low — Folio — `MainPage.xaml.cs:97-101`.** A bare catch around the desk refresh means a failing database (for example after a failed migration) leaves the desk stale with no notice.

25. **Low — Foreword — `PrayerSettings.cs:55`.** Every save failure returns `false`, so the rotations screen cannot tell offline from a locked archive.

## Patterns to share — the app that does each best
- **Offline isn't failure:** Folio's `NetworkFailure.IsOffline` plus `CallGate` (waits stored on the item, a cap, a parked state). Apply it to Index refresh, Foreword delivery and Plate.
- **Credential health:** Constitution's connection list and Pulp's vault screen. Extend to `google-account`, `remarkable` and `gmail-account`.
- **Vault outcomes:** Pulp's `PulpVault` restore results.
- **Rate limits:** Pulp's 403 reason codes and Folio's QuotaExhausted.
- **Receivers:** Pulp and Post use `GoAsync`.
- **Drive reads:** `DriveLogFolder.ReadAsync` checks `progress.Exception`.
- **Refuse to overwrite:** the household store's G14 refusal; the vault, the Post archive and the Score library should adopt it.
