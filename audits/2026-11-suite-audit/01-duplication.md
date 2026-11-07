# Duplication & divergent-strategy audit (read-only)

I checked every finding below by opening the files, not by guessing from names. Nothing in the repo was changed. Findings are ranked by impact, and **Bug?** marks the ones that look like a live defect rather than a cleanup.

## Tier 1: divergence with a likely live defect

### 1. Log-loop reader cannot parse the MAUI apps' logs (Bug?)
- **Native heads** write `[stamp] LEVEL Source — Message` (`FocusPhone/Codex.Domain/Diagnostics/DiagnosticLogFormatter.cs:18-19`). The timestamp comes from `TimeFormats.Stamp`, e.g. "Nov 6, 3:04 pm" (no year, no seconds).
- **MAUI heads** write `[yyyy-MM-dd HH:mm:ss] [WARN] Category: msg` (`Storyworthtelling.Maui/Diagnostics/FileLogger.cs:140-141`).
- **The reader** recognises an entry only by `^\[[^\]]+\] (?<level>[A-Z]+) (?<source>\S+) — ` (`Tools/LogLoopReader/LogEntries.cs:68`). I ran the regex in Python: a MAUI line does not match.
- **Effect:** in `Parse`, a line that doesn't match the entry start is appended to the current entry. So a whole Index, Folio or Foreword log segment collapses into one `TEXT` entry with no level or source, and the Sunday triage effectively sees nothing from three apps. No test covers a MAUI-format segment.
- **Fix:** put one log-line format (writer and parser) in `Storyworthtelling.Diagnostics`. Have `FileLogger` emit `LEVEL Source — msg`, or teach `LogEntries` both shapes, and add a parity test that writes with each logger and parses the result.

### 2. Three merge policies when publishing the shared secrets vault (Bug?)
- **Folio** (`Storyboarder/Folio.Android/VaultSync.cs:26-34`) writes `Remarkable = new RemarkableSecrets(SettingsStore.RemarkableDeviceToken)` unconditionally. If Folio never paired, or restored nothing before the owner saved Settings, a blank local token **wipes the reMarkable token Foreword published**. It also writes the Claude and Gemini keys unconditionally.
- **Foreword** (`Foreword/Foreword.Android/VaultSync.cs:30-45`) writes its NYT keys, Remarkable and Maps sections unconditionally.
- **Index** (`Ledger/Ledger.Android/VaultSync.cs:51-56`) has an explicit guard: a blank local value never overwrites, with a comment saying why.
- **Constitution** (`Constitution/Constitution.Infrastructure/Vault/ConstitutionVaultSync.cs:171-184, 252-255`) uses `Pick(local, inVault, changed)`: a blank never overwrites, and the vault wins unless the owner edited the field.
- `MergeIds` is copy-pasted into all three MAUI VaultSyncs.
- **Fix:** add shared merge helpers to `Storyworthtelling.Voice.Secrets` (a `Pick` field merge and `MergeDriveFileIds`), plus a `SecretsVaultService.PublishAsync` overload that applies them. Make Folio and Foreword use them.

### 3. The shared Claude key is obtained four different ways, and Foreword's is a separate key
- **Live read from the vault on every use** (`DriveVoiceOptions` / `VaultVoiceConfigStore`): the Codex heads, Index's vision extractor and Pulp.
- **Copied into local prefs once, "fill if empty"**:
  - Folio: `SettingsStore.ClaudeApiKey`, `VaultSync.cs:58`.
  - Constitution: `ConstitutionConnections.ClaudeApiKey`, `ConstitutionVaultSync.cs:74`.
- **A different vault field entirely in Foreword:** `ForewordSecrets.AnthropicApiKey` (`Storyworthtelling.Voice/Secrets/SecretsVault.cs:192`; wired at `Foreword/Foreword.Android/MauiProgram.cs:78,83`). Foreword never reads `Vault.Claude`, and its credential id is `"anthropic"` where every other app uses `"claude"` (`Foreword/Foreword.Domain/Health/CredentialIds.cs:13`).
- **Inconsistent inside Constitution:**
  - `AteEstimatorServices.cs:28` and `ReadBackServices.cs:97` take a snapshot of the key with `Options.Create`.
  - `TasteJudgeServices.cs:36` and `TemplateAuthorServices.cs:30` read it live through a private `LiveKey`.
- **Risk:** rotating the key in Folio reaches only the live readers. The copied keys go stale forever, because restore only fills empty fields; Foreword's key is managed separately.
- **Fix:** one `IOptions<ClaudeOptions>` source in `Storyworthtelling.Voice` that reads `Vault.Claude` live; retire `ForewordSecrets.AnthropicApiKey`.

### 4. Eighteen hand-rolled Claude Messages-API clients with different failure semantics
Clients: `Storyworthtelling.Voice/VoiceClient.cs`, `Storyworthtelling.Handwriting/ClaudeVisionHandwritingExtractor.cs`, `Storyboarder/.../ClaudeAnalysisClient.cs`, `Pulp/.../MoodClient.cs`, three in `Foreword/Foreword.Infrastructure` (ClaudePastoralComposer, ClaudeJournalTranscriber, ClaudeTaskReader), four in `Constitution/Constitution.Infrastructure` (ClaudeInkVision, ClaudeAteEstimator, ClaudeTemplateAuthor, ClaudeTasteJudge), and seven in `FocusPhone/Codex.Infrastructure` (HiddenTrackNamer, TasteProfileClient, DiscoveryClient, ShelfGlyphClient, FiledSearchClient, MailReadingClient, DarkroomCaptionClient).

All 18 re-declare `MessagesUrl`, `AnthropicVersion = "2023-06-01"`, the header setup and text-block concatenation. How they differ:
- **Credential health:** 8 report to `ICredentialHealthSink`; the other 10 report nothing (the seven Codex clients, VoiceClient, and Foreword's pastoral composer and journal transcriber). A revoked key therefore silently degrades Post's mail sorting and Filed search, Score and Plate, with no settings field reappearing.
- **Rejection check:** `ClaudeTaskReader.cs:81` passes `null` as the body to `AuthFailure.IsHttpCredentialRejection`; the others pass the body.
- **Rate limits:** only Folio classifies 429 and overloaded responses (`RateLimitException`); everyone else treats them as generic failures.
- **Transport:** only Folio streams and logs usage per call.
- **Model defaults** are hardcoded in six option classes: VoiceOptions, VoiceConfig, ClaudeVisionOptions, Foreword's `AnthropicOptions`, and Folio's `ClaudeOptions` (two classes in one Options.cs).
- **Fix:** a single `ClaudeMessagesClient` in `Storyworthtelling.Voice` covering request building, prompt caching, health reporting, 429/529 classification and usage logging. Each app keeps only its prompts and parsing.

### 5. Encrypted Drive files: refuse-to-overwrite rules reimplemented about six times, and they disagree
- **Household library:** throws `HouseholdUnreadableException` on any unreadable file (`Storyworthtelling.Household/Store/DriveHouseholdStore.cs:124-140`).
- **Prayer archive:** refuses unless the existing file reads as `Loaded` (`Foreword/.../Prayer/DrivePrayerArchive.cs:74-83`).
- **Index household snapshot:** refuses on wrong passphrase or newer version, but **overwrites a Corrupt file** (`Ledger/Ledger.Infrastructure/Household/HouseholdSnapshotPublisher.cs:76-100`). That contradicts G14 as Pulp and Constitution apply it.
- **Constitution Body backup:** writes, reads back, then rolls back on mismatch (`Constitution/.../Backup/BodyBackup.cs:201-235`).
- **Pulp usage file** (`Pulp/Pulp.Infrastructure/Usage/DriveIndexUsageStore.cs:45-83`), **snowball history** (`Ledger/.../Snowball/SnowballHistorySealer.cs`) and **log uploader** (`Storyworthtelling.Voice/Logs/LogUploader.cs`) each have their own logic.
- **Fix:** a `SealedDriveFile` in `Storyworthtelling.Voice` with one G14 policy (with read-back verification as an option), and route all of these through it.

### 6. Three different answers to "is this the owner's phone or the partner's?"
- **Drive ownership of the vault file:** `VaultOwnership.Decide` (`Storyworthtelling.Voice/Secrets/VaultOwnership.cs`). Used by Pulp, Constitution (`HouseholdRoleReader.cs:36`) and the log loop (`LogPhoneRole`).
- **Index:** compares the signed-in email with the snapshot's `PublishedByAccountEmail` and defaults to Owner (`Ledger/Ledger.Domain/Household/InstallRoleResolver.cs:30-55`).
- **Foreword:** `OwnerColumn` compares the signed-in account with the vault's cached `OwnerAccount`.
- **Risk:** one phone can get different roles in different apps. In particular, Index's Owner default on a fresh partner install, before any snapshot has been read, opens Plaid work that VaultOwnership would refuse.
- **Fix:** make `VaultOwnership` the single source, with a cached `InstallRole` in `Storyworthtelling.Voice.Secrets`; Index and Foreword derive their roles from it.

## Tier 2: unguarded copies and abandoned consolidations

### 7. Index's pipeline engine and Folio's are copies, with no parity guard
- Files copied nearly verbatim, differing only in namespaces, comments and ID names:
  - `PipelineRunner.cs`, `IPipelineStep.cs` and `ProcessResult.cs` in `Ledger/Ledger.Infrastructure/Pipeline/` and `Storyboarder/Folio.Infrastructure/Pipeline/`.
  - `BackoffScheduler.cs` and `RateLimitException.cs` in `Ledger/Ledger.Domain/Pipeline/` and `Storyboarder/Folio.Domain/Pipeline/`.
- The WorkManager shims are also near-identical (`Ledger/Ledger.Android/RefreshWorker.cs` and `Storyboarder/Folio.Android/PipelineWorker.cs`).
- Only Folio has `NetworkFailure`, `ErrorText`, `MemoryFailure` and `PlainError`, so an offline phone burns Index's attempts the way Folio's used to burn its own before that fix.
- **Fix:** a shared `Storyworthtelling.Pipeline` library (net10.0) holding the runner, backoff, failure classification and `RunResult`.

### 8. Google HTTP transport factory: 12 copies, while a shared one sits mostly unused
- `GoogleHttpClientFactory` in `FocusPhone/Codex.Ui.Android/Voice/GoogleHttpClientFactory.cs` says so itself ("copied into five places… left where they are").
- Copies still exist in:
  - Codex.Ui.Android (private, three): `SecretsVaultAccess.cs:64`, `VoiceGreeting.cs:66`, `VoiceOptionsAccess.cs:38`.
  - Score: `Google/AndroidGoogleHttpClientFactory.cs` and `PodcastLibrarySync.cs:125`.
  - Constitution (private, three): `BodySeats.cs:74`, `ConstitutionVaults.cs:76`, `BodyBackups.cs:136`.
  - Index, Folio and Foreword: each has `Google/AndroidGoogleHttpClientFactory.cs`.
- Constitution also rebuilds the `DriveService` factory three times instead of calling `SecretsVaultAccess.DriveFactory`. 19 `new DriveService(` sites in total.
- **Fix:** one factory and one `DriveFactory` (Codex.Ui.Android for native heads, a twin in Storyworthtelling.Maui), or finish `IGoogleAccountBroker` (finding 9).

### 9. `Storyworthtelling.Google` is dead in production
- `GoogleAccountBroker`, `GoogleScopes.ToTokenType` and `GoogleWeatherClient` (`Storyworthtelling.Google/*.cs`) are used only by their own tests.
- Constitution references the project (`Constitution.Infrastructure.csproj:17`) but never uses it.
- Foreword has its own, larger, same-named `GoogleWeatherClient` (`Foreword/Foreword.Infrastructure/Maps/GoogleWeatherClient.cs`, 397 lines against 74). It interpolates the key without escaping it; the shared one escapes it.
- **Fix:** either adopt the broker (finding 10) and move Foreword's weather client into the library, or delete the library.

### 10. Google device-account sign-in and token minting: five near-identical copies
- `DeviceAccountTokenProvider.cs` in `FocusPhone/Score.Android/Google/`, `Foreword/Foreword.Android/Google/`, `Ledger/Ledger.Android/Google/` and `Storyboarder/Folio.Android/Google/`, plus `FocusPhone/Codex.Ui.Android/Voice/VoiceGoogleAccount.cs:64-95`.
  - Same `lock` / `InvalidateAuthToken` / `BlockingGetAuthToken` / 45-minute cache. Only the scope string and error text differ.
- `DeviceAccountSignIn.cs` exists in four heads (102–156 lines).
- `AppSigningInfo.cs` is byte-identical apart from namespace and comments in Score, Plate and Folio.
- Scope strings are hand-typed in each head rather than built with `GoogleScopes`.
- Foreword additionally keeps an unused-in-DI refresh-token provider (`Foreword/Foreword.Infrastructure/Google/GoogleAccessTokenProvider.cs`).
- **Fix:** one `DeviceAccountTokens(scopeSet)` plus sign-in in a shared Android-side helper; `GoogleScopes` builds the scope strings.

### 11. reMarkable delivery: two seams, three ways of building the client, divergent safety limits
- **Through `IPaperDelivery`** (the seam that "every app delivers the same way" per `Storyworthtelling.Paper/IPaperDelivery.cs`): only Folio and Plate.
- **Straight to `IRemarkableClient`:** Foreword (`DailyDeliveryService.cs:221`, `PrayerDeliveryService.cs:196`, `CrosswordDeliveryService.cs:32`), Index (`WorksheetDeliveryService.cs:177-185`, folder upload) and Constitution (`Delivery/DeliveryService.cs:88`, with its own in-memory pending-retry queue).
- **Size cap and out-of-memory handling** exist only in Folio (`ReviewProcessor.cs:1280`, `ArchiveOptions.RemarkableMaxBytes`).
- **HTTP timeout** is 5 minutes in Folio (`Folio.Android/MauiProgram.cs:122`) and the 100-second default for Foreword and Index.
- **Constitution** builds a new `HttpClient` per call, shares it between auth and storage, takes the device token as a snapshot via `Options.Create` (`ReadBackServices.cs:79-88`), and its other client constructions use a static `Http.Value`.
- **Plate** builds two clients per call and gives the auth service no health sink (`PlateRemarkable.cs:68-75`).
- `PaperDelivery.DeliverToRemarkableRoot` drops the `CancellationToken`.
- **Fix:** make `IPaperDelivery` the only door. Move the size cap, OOM classification and timeout into `Storyworthtelling.Remarkable`, add one factory for the auth+storage pair, and pass cancellation through.

### 12. Three handwriting read-back pipelines on three page sizes
- **Extractors:**
  - Shared `ClaudeVisionHandwritingExtractor`: Foreword's daily page and Index's worksheet.
  - Foreword's own `ClaudeJournalTranscriber`.
  - Constitution's `ClaudeInkVision`, which re-sends the same PDF-document request, as its comment at lines 12-16 explains.
- **Page geometry for the same tablet:**
  - US Letter 612×792 (`Foreword/.../Pdf/SkiaPageLayout.cs:51`).
  - A4 595×842 (Folio `PageGeometry.cs:19`, Index `WorksheetRegions`).
  - Native 445×594 (`Constitution/Constitution.Domain/Documents/ConstitutionPage.cs`).
- Each app keeps its own coordinate-regions table for the vision prompt.
- **Fix:** extend `Storyworthtelling.Handwriting` with a per-box result shape so Constitution and the journal transcriber can use it. Register one reMarkable page profile in `Storyworthtelling.Paper`.

### 13. Time helpers: six copies, two unguarded, one already drifted
- Copies of `RelativeTime` / `TimeFormats` live in Codex, Folio, Foreword, Constitution, Index and the MAUI kit.
- `TimeHelperParityTests` (`FocusPhone/Codex.Tests/Time/TimeHelperParityTests.cs:20-25`) checks only four of them; `Storyworthtelling.Maui.Tests/NativeParityTests.cs` checks the MAUI kit's `RelativeTime` against Codex.
- **Index's copies are in neither guard**, and `Ledger/Ledger.Domain/Time/TimeFormats.cs` has already drifted: it lacks `Hour12` and `ClockShort`. A future Index caller re-deriving them would hit exactly the `"h"`→`FormatException` bug those methods document.
- **Pulp rolls its own:**
  - `Pulp/Pulp.Domain/Household/HouseholdSync.cs:72-92` prints "5 min ago" / "3 h ago" where the suite prints "5m ago" / "3h ago".
  - `Pulp/Pulp.Domain/Sports/SportsScreenText.cs:17-22` is a time-zone-aware copy of `Clock12`.
- **Fix:** move both helpers into a BCL-only shared library (`Storyworthtelling.Diagnostics` already proves Codex.Domain can take such a reference), delete the copies, and add a zone-aware overload for Pulp.

### 14. `CredentialHealthStore`: four copies with a lost-update race
- `Foreword/Foreword.Android/`, `Ledger/Ledger.Android/` and `Storyboarder/Folio.Android/` each have a byte-identical `CredentialHealthStore.cs` (MAUI `Preferences`).
- `FocusPhone/Codex.Ui.Android/Voice/CredentialHealthStore.cs` is the same with SharedPreferences.
- All four do an unlocked load-modify-save, while background workers and parallel clients mark health at the same time, so one credential's mark can overwrite another's.
- **Fix:** one implementation over a small storage seam (in `Storyworthtelling.Voice.Health`, or one per UI kit) with a lock.

### 15. Credential ids defined in six places, inconsistently
- `CredentialIds.cs` exists in Index, Folio, Foreword, Constitution and Codex; Constitution also has `Diagnostics/ConnectionId.cs`, and `Storyworthtelling.Remarkable/RemarkableCredentialId.cs` defines another.
- The same shared key is `"claude"` in four places and `"anthropic"` in Foreword. `"remarkable"` is declared three times.
- **Fix:** put the shared ids (`claude`, `gemini`, `remarkable`, `github-update-token`) in `Storyworthtelling.Voice.Health`; keep app-only ids local.

### 16. Undo and dog-ear: three decision engines, two fold painters, four timer styles
- **Decision logic:**
  - `Storyworthtelling.Maui/State/DogEarState.cs` (MAUI).
  - `Constitution/Constitution.Domain/Notices/NoticeBoard.cs`, a re-implementation of the same §13 rules.
  - Inline logic in `FocusPhone/Codex.Ui.Android/DogEar.cs` (389 lines).
- **Painters:** Constitution re-draws the fold in `Waiting/FoldView.cs` and `DogEarHost.cs`, a deliberate fork because the shared view opens the shared Waiting page.
- **Timers:**
  - Index: a dispatcher timer.
  - Constitution: `handler.PostDelayed`.
  - Codex: `PostDelayed`.
  - Folio: `Task.Delay(...).ContinueWith(async …)` (`Storyboarder/Folio.Android/Pages/FolioBinderPage.cs:226-235`). That returns an unobserved `Task<Task>`, so any exception in the deferred hard-delete is swallowed — against the repo rule of not swallowing exceptions.
- **The 6000 ms window is declared five times:** Codex, Folio and Index `DesignTokens.Motion`, plus a private `UndoWindowMs` in `FolioBinderPage.cs:213`, plus Constitution reading Codex's. Index's DesignTokens doc comment says nothing reads it; four pages do.
- **Fix:** move `DogEarState` into a BCL-only library so native heads, Constitution and MAUI share one decision type; read one Motion token; fix Folio's fire-and-forget.

### 17. Drive listing: different escaping and pagination
- `DriveTextFile` and `DriveFileChooser` escape names and page through results at 100 per page.
- `Storyboarder/Folio.Android/Google/DeviceAccountDriveArchive.cs:107` has its own `Escape`.
- `Storyboarder/Folio.Android/Google/DriveDocsLister.cs:38-43` reads one page of 200 with no `PageToken`, so the manuscript picker silently truncates a large folder.
- `FocusPhone/Score.Android/Google/DriveMusicClient.cs:39-43` does the same for its subfolder listing; its file walk does paginate.
- `Tools/LogLoopReader/DriveLogSource.cs:47` interpolates the name with no escaping.
- **Fix:** shared `DriveQuery.Name/InFolder` builders and a paging `ListAll` helper in `Storyworthtelling.Voice`.

## Tier 3: lower impact, worth folding in

### 18. `LiveOptions<T>` copied three times, plus Constitution's `LiveKey`
- Private `LiveOptions<T>` in Index, Folio and Foreword `MauiProgram.cs` (lines 44, 37 and 39).
- Constitution has two private `LiveKey` classes, plus `Options.Create` snapshots elsewhere (see finding 3).
- **Fix:** one `LiveOptions<T>` in `Storyworthtelling.Voice`.

### 19. HttpClient construction is ad hoc in the native heads
- Score creates a new `HttpClient()` per call with the default handler, 21 times (`DiscoverArea.cs:311,600,632,655,699`; `MusicLibrary.cs:191,198,263`; `TidalAccessTokenProvider.cs:39`).
- Post keeps one static shared client (`Post.Android/MailAccess.cs:60`).
- Constitution and Plate pass `AndroidMessageHandler` explicitly, while the MAUI apps use `AddHttpClient` with `ConfigurePrimaryHttpMessageHandler`.
- **Risks:** socket churn, inconsistent timeouts, and reliance on the implicit native-handler default that every other comment in the repo says must be explicit.
- **Fix:** a per-head shared-client helper in Codex.Ui.Android.

### 20. Status-line summaries hand-built per app
- `Foreword/Foreword.Android/CredentialStore.cs:207-210` and `Storyboarder/Folio.Android/SettingsStore.cs:158-161` compose `"{outcome} — {RelativeTime}"` inline.
- Index moved its version into Domain with tests (`RefreshOnOpen.Summary`).
- **Fix:** one tested `RunSummary` helper alongside the time helpers.

### 21. Package and identity constants repeated three to five times each
- For example, `com.storyworthtelling.ledger` appears in `IndexIdentity`, `PolicyContext`, `Storyworthtelling.Updates/VersionCode.cs:103`, Pulp's `IndexUsageContract` and Post's `MoneyMail`.
- Guards cover some pairs (`FrozenInvariantsTests`, the contract tests) but not all of them.
- **Fix:** a source-level guard that the `VersionCode` band table and each `*Identity` / `PolicyContext` constant agree, rather than more copies.

### 22. JSON serializer options declared ad hoc
- 18 `new JsonSerializerOptions` sites, 27 of them setting `PropertyNameCaseInsensitive = true`. Every Codex Claude client declares its own `CaseInsensitive`.
- Vault-backed files consistently reuse `VaultEnvelope.SerializerOptions`; the rest don't.
- Low risk, but fold this into the Claude client consolidation (finding 4).

## Duplication that is deliberate and guarded (leave as is)
- **Time helpers:** the Codex, Folio, Foreword and Constitution copies, and the MAUI `RelativeTime`, are under parity tests. The exceptions are Index's copies and Pulp's (finding 13).
- **MAUI kit `BinderTabLayout` / `DesignTokens`:** source-level parity with Codex.Domain.
- **`PolicyContext.OwnerPackagePrefix`** against `Storyworthtelling.Updates.SuiteApps`: guarded by `FrozenInvariantsTests`.
- **Cross-app contracts** (Index↔Constitution budget, Pulp↔Flyleaf, Plate recipe read): pinned on both sides by content-read guards.
- **Native vs MAUI logger stacks** both seal into `LogOutbox`, which is fine. Only the line format diverged (finding 1).

## Suggested order of work
1. Fix the vault merge, so Folio can no longer blank the reMarkable token (finding 2).
2. Fix the log-format mismatch (finding 1).
3. Unify the Claude key source and client (findings 3 and 4).
4. Write one sealed-Drive-file policy (finding 5).
5. Unify the install role (finding 6).
6. Extract `Storyworthtelling.Pipeline` (finding 7).
7. Consolidate the Google transport and sign-in, or delete `Storyworthtelling.Google` (findings 8–10).
8. Make `IPaperDelivery` the only reMarkable path (finding 11).
9. Then the remaining copies.
