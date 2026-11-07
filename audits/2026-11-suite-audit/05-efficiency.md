# Efficiency audit: Storyworthtelling suite

I read the code for every finding below. All paths are relative to `/home/user/Personal`. Findings are ranked by impact. The biggest costs are mobile data and paid API calls, then UI jank, then CI minutes. Nothing was edited.

---

## Tier 1: high impact

### 1. Post downloads the whole archive backup from Drive on every sync
`FocusPhone/Post.Android/ArchiveBackup.cs` (`BackUpAsync`), called from `PostSync.cs:97-100`
- **Now:**
  - Every 30-minute `EmailSyncWorker` run, every Sync tap and every reconcile after a file/delete does the same three things:
    - deserializes the entire local ghost archive (`Filing.LoadArchive().All` turns every ghost's stored JSON back into a full item, bodies included);
    - downloads the entire `storyworthtelling-post-archive.json` from Drive;
    - compares the two, only to decide whether to write.
  - The archive grows without bound by design.
  - `MailIntelligence.NoteWhatTheDeferredWorkNeeds` (`MailIntelligence.cs:195-212`) runs once per sync and loads the whole archive again just to sum byte sizes. It also queries every contact (`ContactPicker.PhotoCoverage`).
- **Cost:** more than 48 full archive downloads and about 96 full deserializations a day, over mobile data too. This is the largest standing data and battery drain found.
- **Fix:**
  - Keep a local "last backed-up" marker (ghost count + max `filedAtUtc`, or a hash) in prefs and skip both the download and the deserialization when it has not changed. Only read Drive on restore.
  - Compute archive size with `SELECT COUNT(*), SUM(LENGTH(item)) FROM ghosts`.
  - Take the field-note observations once a day, not once per sync.

### 2. Gmail reconcile re-downloads up to 200 full raw messages after every file, delete or Sync tap
`FocusPhone/Codex.Infrastructure/Email/GmailApiClient.cs:33,175-221`, plus `PostSync.cs:66-84` and `Syncs.cs:62` / `MailboxArea.cs:827`
- **Now:**
  - `RunReconcileAsync` lists 30 days of mail (`maxResults=200`). It then fetches each message one at a time with `format=raw`, which includes attachments, and parses the full MIME.
  - `Syncs.After` fires this after **every** file or delete.
  - Incremental runs use the same per-message raw fetch.
  - The IMAP fallback (`GmailClient.cs:59-63`) downloads every full message with no cap.
- **Cost:** potentially tens of MB and about 200 serial HTTPS round trips per action, plus CPU for MIME parsing.
- **Fix:**
  - Reconcile only needs to know which ids still exist. Call `messages.list` alone, diff against cached ids (store the Gmail id next to the RFC id), and fetch only ids not yet cached.
  - Use `users.history.list?startHistoryId=` for incremental runs.
  - Fetch new messages with `format=metadata` or `format=full` (skipping attachment bodies) and download attachments on demand, as `AttachmentPlan` already assumes.
  - Fetch with bounded parallelism (about 4).
  - Debounce or coalesce reconcile after actions.

### 3. Every vault or voice read costs about 6 Drive requests, 2 fresh TLS stacks and a 210k-iteration PBKDF2, with no in-memory cache
- **Where:**
  - `Storyworthtelling.Voice/DriveTextFile.cs:41-66,107-160`
  - `Secrets/DriveSecretsVaultStore.cs:21-46`
  - `Secrets/SecretsCrypto.cs:15`
  - `FocusPhone/Codex.Ui.Android/Voice/SecretsVaultAccess.cs:50-60`
- **Now:** `LoadAsync` makes these calls in order:
  1. `OwnershipAsync` builds a new `DriveService` (new `AndroidMessageHandler`, so a new connection) and calls `FindId`, which does `Files.Get` to check the file still exists. It then does a second `Files.Get` for ownership.
  2. `ReadAsync` builds **another** `DriveService` and calls `FindId` again (another existence `Get`) before the download.
  3. `VaultEnvelope.Read` runs PBKDF2 at 210,000 iterations.

  `DriveVoiceOptions.RefreshAsync` repeats all of this on every call, and the voice profile read adds about 3 more Drive calls. Callers that do this repeatedly:
  - `VoiceGreeting.ComposeAsync`: every Flyleaf cold start, every Score open, each Discover crate pick (`DiscoverArea.cs:458`), each workout voice line (`WorkoutArea.cs:326`).
  - `TidalAccessTokenProvider.RefreshAsync` (`TidalAccessTokenProvider.cs:22-48`): every TIDAL search, every "add pick", and **once per track** in `MusicLibrary.EnrichSavedTrackAsync`. Saving a 12-track album means 12 vault loads plus 12 TIDAL token refreshes.
  - `PostSync.ReadingClientAsync`: every sync.
- **Fix:**
  - Cache the decrypted vault per process with a short TTL. Better, request `headRevisionId` or `md5Checksum` in a single `Files.Get(fields=id,trashed,ownedByMe,headRevisionId)` and skip the download and KDF when the revision is unchanged.
  - Merge the ownership and existence checks into one `Get`.
  - Keep one `DriveService` and handler per head instead of one per operation.
  - Cache the derived key by (passphrase, salt).
  - Cache the TIDAL access token until `expires_in`; refresh only when it has expired or a request returns 401.

### 4. A greeting on the strong model on every Flyleaf cold start, often thrown away
`FocusPhone/Flyleaf.Android/HomeActivity.cs:114-118`, `Codex.Ui.Android/Voice/VoiceGreeting.cs:48-58`, `Score.Android/ScoreActivity.cs:163`
- **Now:**
  - Each cold start of the launcher (which happens whenever the process is killed) starts `ComposeAsync("welcome")` with `HighVisibility=true`, i.e. the strong model, after the Drive reads in #3.
  - The UI waits only 2.5 s and then discards the result. The call still runs to completion and is billed (it is passed `CancellationToken.None`).
  - Score composes on every Activity create as well.
- **Fix:**
  - Cache the line per time-of-day bucket (morning, afternoon, evening) and per day in prefs. Reuse it on the next start and refresh in the background.
  - Use `FastModel` for the launcher hello.
  - Pass a token that is cancelled on timeout.
  - The Discover per-pick and workout lines also deserve a per-day or per-track cache.

### 5. Score's Playing area decodes the artwork bitmap every second on the UI thread
`FocusPhone/Score.Android/PlayingArea.cs:364-471` (`OnTick → Render → RenderArtwork`), driven by `ScoreActivity.cs:397-421` (1 s ticker)
- **Now:** while the Cover panel is showing, every tick does one of:
  - `artworkStore.Resolve` followed by `BitmapFactory.DecodeStream` on the full-size override image from `ContentResolver`; or
  - `ArtworkData.ToArray()` (a Java to managed copy) followed by `DecodeByteArray`.

  The bitmap is never recycled and nothing is cached.
- **Cost:** a full image decode per second on the main thread. That means continuous GC and memory churn, jank, and battery drain during playback.
- **Fix:** decode only when the media id or override URI changes (track `lastArtworkKey`), decode off-thread with `inSampleSize` set for `ArtworkSizeDp`, and keep the bitmap.

### 6. Folio's worker wakes every 15 minutes forever and promotes to a foreground service each time; the desk polls every 5 s with N+1 queries
- **Where:**
  - `Storyboarder/Folio.Android/PipelineWorker.cs:63-91,148-157`
  - `Pages/MainPage.xaml.cs:62-65,75-130`
  - `Folio.Infrastructure/Services/ProjectService.cs:719-736,998–1050`
- **Now:**
  - `Work()` calls `GoToForeground` (foreground service plus a "reading" notification) **before** it checks whether any book is active. So 96 times a day an idle phone starts a foreground service, opens the DB and runs the pipeline loop.
  - With the desk open, `RefreshAsync` runs every 5 s. For each book, `BuildStatus` makes about 8–10 separate queries (novel, chunks, sheets, scenes, analyses, BookAnalyses twice, SourceTexts, PassReads). It then clears and rebuilds every row.
  - On an empty desk with no voice profile yet, `EmptyDeskLinePlan.NeedsComposing` stays true. That causes a `VoicedMessenger.ComposeAsync` (Drive profile read, plus Claude if a profile exists and the call fails) every 5 s.
- **Fix:**
  - Check `HasActiveWorkAsync` first and return without going to the foreground when idle.
  - Better: drop the periodic job when there is no active work and rely on the `RunNow` that import and resume already enqueue.
  - Replace the 5 s poll with an in-process change event from `PipelineRunner`, or at least a single projected query per tick, and diff the rows instead of rebuilding them.
  - Back off the empty-desk compose after a failure (store the attempt day even when no line came back).

---

## Tier 2: medium impact

### 7. Opening one Post thread reads the whole phone, with three extra queries per MMS
`FocusPhone/Post.Android/MailboxArea.cs:1069-1095` (`LoadOpenThread`), `393-416` (`ReadLiveTail`), `SmsLogReader.cs:22-86`
- **Now:**
  - To show one contact's thread, it reads the **entire** call log, the entire SMS and MMS provider, and `Cache.LoadAll()` (every cached item with body and htmlBody). It merges all of it and keeps one thread.
  - `ReadMms` makes three more provider queries per MMS row (`ReadMmsText`, `ReadMmsAllAddresses`, `ReadMmsAttachments`), so a large MMS history costs thousands of queries.
  - `ReloadThreads` repeats the full live-tail read after about 12 different actions.
  - `deleteHandler.PostDelayed(LoadOpenThread, 2500)` repeats it again.
- **Fix:**
  - Filter at the source: SMS/MMS by `thread_id` or address, the call log by number, and add a cache query `WHERE contactKey IN (...) OR groupId = ?`.
  - Read the live tail with a date floor and a `LIMIT`.
  - Join MMS parts and addresses with one query each (`WHERE msg_id IN (...)`) instead of three per row.

### 8. Timeline cache: no index for the list order, OFFSET paging, and a 2,000-row scan on the UI thread
`FocusPhone/Codex.Infrastructure/Storage/TimelineCache.cs:44-52,175-205`, `MailboxArea.cs:1314-1320`, `PostActivity.cs:360-365`
- **Now:**
  - The only key is `PRIMARY KEY(kind, sourceId)`.
  - `LoadRecent` (`ORDER BY timestampUtc DESC LIMIT/OFFSET`) sorts the whole table on every page.
  - `ReconcileWindow` filters on `kind` and `timestampUtc` without an index.
  - `SelectColumns` pulls `body` and `htmlBody` even for list rows.
  - `ApplyReaderAction`, which runs from `OnResume` on the **UI thread**, loads 2,000 full rows to find one `sourceId`.
  - `MailIntelligence` loads 300, 100 and 500 full rows on every sync (`:34,202,234`).
- **Fix:**
  - Add `CREATE INDEX ix_ts ON timelineItems(timestampUtc DESC)` and `(kind, timestampUtc)`.
  - Use keyset paging (`WHERE timestampUtc < ?`).
  - Add a body-free projection for list and ageing queries.
  - Add a `Load(kind, sourceId)` point lookup and move `ApplyReaderAction` off the UI thread.

### 9. Score re-downloads every podcast RSS feed in full on every visit to the area
`FocusPhone/Score.Android/PodcastsArea.cs:168-173,929-944`, `EpisodeCacheStore.cs:21-29`
- **Now:**
  - `Resume()` calls `RefreshFeedsAsync()` every time the area becomes visible.
  - There is no minimum interval and no `If-None-Match` / `If-Modified-Since`. Long-running shows have 1–10 MB feeds.
  - All episodes (with show-notes HTML) are then written as **one SharedPreferences string**. Prefs load whole on first access and rewrite the whole XML on every `Apply`.
- **Fix:**
  - Throttle to about once an hour per feed.
  - Store each feed's ETag and Last-Modified and send conditional GETs (304s cost almost nothing).
  - Move the episode cache to a file or SQLite (the `MusicCatalogCache` pattern), or drop descriptions from the cached copy.

### 10. Score Discovery sends a ~1,500-track catalog to the strong model with no prompt caching
`FocusPhone/Codex.Infrastructure/Music/DiscoveryClient.cs:60-80,164-181`, `Codex.Domain/Music/CatalogDigest.cs:24`
- **Now:**
  - Each "make a playlist" request and the weekly picks send the taste profile and then the JSON catalog (id, title, artist and genre per track, up to 1,500 tracks; probably tens of thousands of tokens) as uncached user text, on `options.Model` (Sonnet).
  - The sample is deterministic, so the content is cacheable, but the profile comes first in the prompt and breaks the shared prefix.
- **Fix:**
  - Put the catalog first, in a `system` block with `cache_control: ephemeral` (as Folio's `ReadingPrompt` and Post's `FiledSearchClient` already do), followed by the profile and the request.
  - Replace long Drive file ids with short indices in the prompt and map them back locally.
  - Consider the fast model for mood-style requests.

### 11. Plate's editor decodes the full-resolution capture on the UI thread
`FocusPhone/Plate.Android/EditorActivity.cs:71`
- **Now:** `BitmapFactory.DecodeFile` in `OnCreate`, with no `inSampleSize`. A Pixel 8a capture (about 12.5 MP) becomes a bitmap of roughly 50 MB, decoded on the main thread.
- **Cost:** risk of ANR on open, plus memory pressure.
- **Fix:** decode bounds first, sample to screen size for preview (the same pattern as `CoverCache`), decode off-thread, and apply edits to the full image only on save.
- `Rolodex.PhotoFor` (`Rolodex.cs:85`, `preferHighres: true`) has the same issue: it should sample down to the embedder's input size.

### 12. Each household sync pass re-downloads and re-derives keys; a heartbeat doubles the pass
`Storyworthtelling.Household/Store/DriveHouseholdStore.cs:81-160`, `Constitution/.../HouseholdFileService.cs:155-170`
- **Now:**
  - A pass is a Drive download plus a PBKDF2 at 210k (plus the #3 `FindId` and `DriveService` overhead).
  - A write adds an encrypt (another KDF), an upload, and a **re-read** (another download and KDF).
  - When a heartbeat is due, `SyncAsync` runs `PassAsync` twice, so a beat costs up to 3 downloads and 4 KDFs. This happens on every app open (`HouseholdSyncSchedule.Opened`) and on each worker run that syncs.
  - Locally, `ReadAsync` re-reads and re-deserializes the whole household file even though `latest` is already held in memory.
- **Fix:**
  - Check `headRevisionId` before downloading.
  - Cache the derived key by salt.
  - Fold the beat into the first pass's write.
  - Serve reads from `latest`, invalidated by file mtime.

### 13. Flyleaf's update channel downloads APKs on any network just to read their version
`FocusPhone/Flyleaf.Android/UpdateService.cs:36-41,120-140`
- **Now:**
  - The 6-hourly worker requires only `NetworkType.Connected`.
  - Each changed asset (every main build re-uploads the APKs of the workflow that built them, for example Flyleaf and Score together) is downloaded **before** its package and versionCode are known.
  - MAUI and native APKs are tens of MB each, and are downloaded even when the version is not newer.
- **Fix:**
  - Have `publish-rolling-release` upload a small `manifest.json` (package, versionCode, asset name) and decide from that before downloading.
  - Require `NetworkType.Unmetered` (or charging) for the download phase.

### 14. Flyleaf bakes the entire bookcase into one full-height ARGB bitmap
`FocusPhone/Flyleaf.Android/BookshelfView.cs:141-157` (`EnsureBaked`), live layer at `:555-640`
- **Now:**
  - `Bitmap.CreateBitmap(Width, layout.TotalHeight, Argb8888)` covers home plus the whole tray.
  - At 1080 px wide and several screens tall that is easily 30–60 MB held for the launcher's lifetime, with a per-frame-draw fallback if it runs out of memory.
  - The live layer (clock, bookmarks) allocates 6–10 `Paint`s per `OnDraw`.
- **Fix:**
  - Bake per shelf, or per screen-height tile, and draw only the visible tiles.
  - Alternatively use `RGB_565` (the wood has no alpha) or record a `Picture` / `RenderNode`.
  - Hoist the live-layer `Paint`s into fields.

### 15. Score loads the music catalog synchronously on the UI thread and reloads it after every save
`FocusPhone/Score.Android/MusicLibrary.cs:92-101,118-160,178-205`, `ScoreActivity.cs:91`
- **Now:**
  - `LoadCatalog()` (SQLite `LoadAll()` over about 1,300 tracks, plus a merge) runs in `OnCreate` and again on every Save, SaveTracks or RemoveSavedTracks, on the UI thread.
  - Enrichment re-authenticates per track (see #3) and creates a new `HttpClient` per call.
- **Fix:** load once off-thread, then apply saved-track deltas in memory without rereading the DB. Get one token per enrichment batch.

---

## Tier 3: lower impact or hygiene

### 16. `HttpClient` created per call (connection and TLS churn)
- `Score.Android/DiscoverArea.cs:311,600,632,655,699` (`new TidalCatalogClient(new HttpClient())`)
- `MusicLibrary.cs:191,198,263`
- `TidalAccessTokenProvider.cs:39`
- `TidalArtworkCache.cs:25` and `PodcastArtworkCache.cs:48` (one per image)
- `PodcastsArea.cs:942`
- Every Drive operation via `SecretsVaultAccess.DriveFactory` and `VoiceGreeting`

**Fix:** keep one static `HttpClient` per head on `AndroidMessageHandler` (Constitution's `Lazy<HttpClient>` services already do this), and one `DriveService` per head.

### 17. Post mail sorting makes one Haiku call per email
`FocusPhone/Codex.Infrastructure/Email/MailReadingClient.cs:33,75-77`, `MailIntelligence.cs:34-60`
- Each unjudged email is its own request carrying the same system instruction.
- **Fix:** batch 10–20 emails per request (numbered, with a JSON array reply). Sorting is not urgent, so the Message Batches API (about half price) also fits.

### 18. reMarkable upload buffers the whole PDF in memory
`Storyworthtelling.Remarkable/RemarkableStorageClient.cs:33` (`ByteArrayContent(pdfBytes)`)
- The 100 MB guard and JPEG pictures shrank the problem, but the API still forces a `byte[]`, and the Android stack keeps a second copy.
- **Fix:** take a `Stream` and use `StreamContent` with a known `Content-Length`.

### 19. CI: Index builds and signs an APK on every branch push
`.github/workflows/build-index.yml:20-40`
- The `build` job (`needs: test`) has **no `if:`**, so every `claude/**` push to Ledger installs the MAUI workload and builds a signed APK. That breaks the repo's own "branch pushes run tests only" rule.
- The workflow also has no `concurrency` group.
- **Fix:** copy the `if:` pair and the concurrency block from `build-storyboarder.yml:36,64` / `build-flyleaf.yml:20`.

### 20. CI: no NuGet or workload cache anywhere
- `setup-dotnet` has no `cache:` and there is no `actions/cache` in any of the 13 workflows.
- Every signed build runs `dotnet workload install android` or `maui-android` from scratch, which takes several minutes per run.
- **Fix:**
  - Cache `~/.nuget/packages`, keyed on a hash of the csproj and `Directory.Packages` files.
  - Cache the workload packs directory.
  - Or share one composite setup action that does both.

### 21. CI: duplicate test runs and wide fan-out
- **Codex.Tests three times:** `FocusPhone/Codex.Tests` runs identically in `build-flyleaf`, `build-plate` and `build-post`, so one change to Codex.Domain or Infrastructure runs it three times.
- **Voice and Remarkable tests twice:** `build-storyboarder` re-runs `Storyworthtelling.Voice.Tests` and `Remarkable.Tests`, which `build-shared-libs` also runs.
- **Fan-out:** a change to `Storyworthtelling.Diagnostics` triggers 9 workflows, which on `main` means up to 8 signed APK builds.
- **Shared libs:** `build-shared-libs` runs all 9 test projects for a change to any one library, and has no concurrency group.
- **Fix:**
  - Run Codex.Tests in a single workflow and have the head workflows only build.
  - Split shared-libs into a per-library matrix filtered with `dorny/paths-filter`.

### 22. CI: the log-loop guard runs twice per push on branches with an open PR
`.github/workflows/log-loop-guard.yml:3-11`
- It triggers on both `push: claude/**` and `pull_request: main`. The concurrency groups differ (branch ref vs `refs/pull/N/merge`), so both runs go ahead.
- It has no path filter and uses `fetch-depth: 0`.
- **Fix:** keep one of the two triggers, or key concurrency on `head_ref || ref_name`. Use a shallow fetch of the merge base.

### 23. Near-duplicate collapse is O(n·m) over the whole timeline
`FocusPhone/Codex.Domain/Timeline/NearDuplicateCollapser.cs:23-33,58,80,92`
- Each notification-sourced text scans every provider or bridge text with `.Any(...)`. With the full SMS provider (thousands) on every reload (#7) this adds up.
- **Fix:** bucket the provider texts by (normalized contact, minute) in a dictionary first.

### 24. Index runs a vault restore from Drive on every launch
`Ledger/Ledger.Android/Platforms/Android/MainActivity.cs:47-68`
- On every launch Index resolves its role (a household read) and restores the vault (the #3 cost) before it schedules work. Folio (`MainActivity.cs:35-46`) and Foreword (`MainActivity.cs:36-50`) restore the vault on every launch as well.
- It runs off the UI thread, so the issue is only data and CPU.
- **Fix:** skip the restore when every local credential is already present (the `FillIfEmpty` targets are all non-empty), or once a day at most.

---

## Biggest wins, quickest first
1. **#1:** a local "last backed-up" marker removes the archive download from every Post sync.
2. **#3 and #4:** an in-process vault and TIDAL-token cache, plus a cached greeting, remove most redundant Drive, KDF and Claude traffic in Flyleaf and Score.
3. **#2:** fetch only new Gmail ids, and use the history API.
4. **#5:** decode artwork once per track instead of once per second.
5. **#6:** check for active work before Folio's worker goes to the foreground, and replace the 5 s desk poll.
6. **#19 and #20:** gate Index's build job to `main` and add NuGet/workload caching across CI.
