# Orphan and incomplete-code audit: /home/user/Personal (HEAD 4220126)

I only read the repo; nothing was edited or committed. Scratch scripts are in `/tmp/claude-0/-home-user-Personal/b5742c71-73ba-5e61-9ad5-55fc006c45b7/scratchpad/`.

**How I checked:**
- Declared types and public methods counted against references in production code (tests and `Tools/` counted separately).
- Android drawables, layouts and strings checked against `Resource.*`, `@x/` and string references.
- `const string` keys, preference writes with no matching read, and Options properties.
- `.sln` and workflow paths, NuGet namespaces per project, XAML handlers, and the backticked identifiers in CLAUDE.md.
- Every "zero refs" below was confirmed by a follow-up `git grep`.

The repo has essentially **no TODO, FIXME or NotImplementedException markers**, so most findings are logic that is built and tested but never called.

## The known items in CLAUDE.md, checked

| Item | Status now | Evidence |
|---|---|---|
| "`GmBridgeClient.StartPairing` has no caller" | **Stale.** It is called. | `FocusPhone/Post.Android/MessagesPairingActivity.cs:174` calls `bridge.StartPairing(json)`. Action: fix the RCS bullet in CLAUDE.md, which still says the bridge "has never been paired" for this reason. |
| `BookPage` constructed by nothing | **Fixed.** | `Storyboarder/Folio.Android/Pages/ShelfPage.cs:236` and `MainPage.xaml.cs:563` both push `new BookPage(book)`. |
| `EmbeddedBranding` has no printing consumer | **Confirmed** (high) | Only `Storyworthtelling.Paper.Tests/EmbeddedBrandingTests.cs` uses `EmbeddedBranding.OwnerMedallion`. `Storyworthtelling.Paper.csproj:32` still embeds `../Branding/owner.png` into every app that references Paper. CLAUDE.md says an embedded resource "ships it in every APK". Action: delete the class and the `EmbeddedResource`. |
| Post's four `ROLE_SMS` components | Kept on purpose and guarded. | Not a finding. |
| Plate video refused | **Confirmed, and it contradicts itself** (high) | `FocusPhone/Plate.Android/ErrandActivity.cs:42` declares `[IntentFilter([Errands.VideoCapture])]` and draws a "Record" button. `StartVideo()` at :334 only shows "Plate cannot record for another app yet." Its own comment says "declaring the component without honouring it would be worse than saying so". Action: either drop the VideoCapture filter (check that the `Errands` guard allows it) or build the path. |
| `LockedOpen` | Fully wired. | `LockedOpenReceiver`, `LockedSessionController` → `LockedSessionPlan`/`Rules`/`Codec`, and Pulp's `LockedOpenCourier`. Not a finding. |

---

## Shared libraries

1. **The whole `Storyworthtelling.Google` library is unused in production** (high)
   - `IGoogleAccountBroker`/`GoogleAccountBroker` (`Storyworthtelling.Google/IGoogleAccountBroker.cs:17,42`), `IWeather`/`GoogleWeatherClient`, `GoogleScopes` and `Stubs.cs` (`IHealthData`, `HealthSnapshot`, `IGroceryList`) have no production references. The stubs and `GoogleScopes` have no test references either.
   - Its only consumer is `Constitution/Constitution.Infrastructure/Constitution.Infrastructure.csproj:17`, which uses none of its types. Constitution only gets `Google.Apis.Calendar.v3` through it as a package dependency.
   - Constitution reads Health Connect and shares to Keep its own way, so `IHealthData` and `IGroceryList` are dead seams. Foreword has its own separate `GoogleWeatherClient`.
   - Action: add a direct `Google.Apis.Calendar.v3` PackageReference to Constitution.Infrastructure, then delete the library, its tests, and its entries in `Constitution.sln` and `build-shared-libs.yml`.

2. **`DriveVoiceConfigStore`** (high), `Storyworthtelling.Voice/DriveVoiceConfigStore.cs:9`
   - Only tests use it. CLAUDE.md says `VaultVoiceConfigStore` replaced the old plaintext `storyworthtelling-voice-config.json`.
   - Action: delete it and its tests.

3. **`DeliveryProfilePolicy`** (high), `Storyworthtelling.Paper/DeliveryProfilePolicy.cs:9`
   - `ForRemarkable` and `ForDrive` are referenced only by tests. No renderer picks a colour profile per destination through it.
   - Action: wire it into the reMarkable and Drive delivery paths, or delete it.

4. **`WorkerCadence.Missed`** (medium), `Storyworthtelling.Diagnostics/WorkerCadence.cs`
   - Only `WorkerCadenceTests` calls it, including `Tools/LogLoopReader`, so the digest never reports missed worker runs.
   - Action: call it from the reader's `digest`, or delete it.

5. **`DogEarState.ClearNotices`** (medium), `Storyworthtelling.Maui/State/DogEarState.cs:123`
   - Only tests use it. Action: delete it, or note that it exists for parity with the native dog-ear.

6. **`SuiteVersionBands.All` / `AppVersionBand`** (medium), `Storyworthtelling.Updates/VersionCode.cs:81-90`
   - This is production code read only by the guard tests. Action: move it into the test project, or say in its doc comment that it is test data.

---

## FocusPhone: Flyleaf and the shared kit

7. **`PaperClockView`** (high), `FocusPhone/Codex.Ui.Android/PaperClockView.cs:14`
   - Nothing constructs it; the mantel clock is drawn inside `BookshelfView`. Action: delete it.

8. **`shelf_bookcase_home.png` is unreferenced and 1.7 MB** (high), `FocusPhone/Flyleaf.Android/Resources/drawable/`
   - No `Resource.Drawable.shelf_bookcase_home` or `@drawable/` reference anywhere. It ships in the APK. Action: delete it.

9. **`PaperUi.SegmentedToggle`** (high), `FocusPhone/Codex.Ui.Android/PaperUi.cs:395`
   - Its doc says "kept for the heads that still call it", but there are no callers. Action: delete it.

10. **`MarkerUnderline.SetHue`** (`Codex.Ui.Android/MarkerUnderline.cs:61`) and **`Tile.BuildLaunchIntent`** (`Flyleaf.Android/Tile.cs:35`) have zero callers (medium). Action: delete both.

11. **`PlayRatingClient`** (high), `FocusPhone/Codex.Infrastructure/Play/PlayRatingClient.cs:14`
   - Only tests use it. Action: delete it.

12. **`ReleaseInfo.IsNewerThan`** (medium), `Codex.Infrastructure/Updates/GitHubReleaseClient.cs:20`
   - Unused; `SiblingUpdatePlan` makes this decision now. Action: delete it.

13. **`IShelfGlyphClient`** (low), `Codex.Infrastructure/Apps/ShelfGlyphClient.cs:11`
   - The interface has no consumer; `ShelfGlyphMaker` news up the concrete class. `IDarkroomCaptionClient` (`Plate/DarkroomCaptionClient.cs:11`) is the same pattern. Action: remove the interfaces or inject them.

## FocusPhone: Score

14. **Domain logic that is tested but never wired into the head** (high). Each has zero production references:

   | Type | Location |
   |---|---|
   | `MediaButtonGesture` | `Codex.Domain/Music/MediaButtonGesture.cs:17` |
   | `DeviceAudioProfileBook` and `DeviceAudioProfile` | `Codex.Domain/Music/DeviceAudioProfile.cs:11` |
   | `PlaybackDeviceSettings` | `Codex.Domain/Music/PlaybackDeviceSettings.cs:11` |
   | `PlaybackDeviceSettingsCodec` | `Codex.Infrastructure/Music/PlaybackDeviceSettingsCodec.cs` |
   | `SoundCheckGain.Millibels` | `Codex.Domain/Music/SoundCheckGain.cs:15` |
   | `DriveAudioFileMapper` | `Codex.Infrastructure/Music/DriveAudioFileMapper.cs` |
   | `ShuffleScope` enum (no references even in tests) | `Codex.Domain/Music/ShufflePlanner.cs:4` |
   | `PlaybackTimeline.SeekTarget` | `Codex.Domain/Music/PlaybackTimeline.cs:12` |
   | `PlaybackSettings.Faster`/`Slower` | `Codex.Domain/Audio/PlaybackSettings.cs:21-23` |

   Action: wire them (per-device volume and EQ, sound-check gain, headset gestures) or delete them.

15. **`PodcastLibrary`** (high), `Codex.Domain/Audio/PodcastLibrary.cs:7`
   - `MarkListened`, `GetDefaultView` and the rest are only used by tests. Score uses `PlaybackProgressBook` and `PodcastLibrarySync`. Action: delete it.

16. **`PlaybackProgressBook.ImportListened` never runs** (medium), `Codex.Domain/Audio/PlaybackProgressBook.cs:51`
   - It is the "legacy listened ids → progress" migration, and no head calls it. Action: confirm the migration is no longer needed and delete it, or call it once at load.

17. **`EqualizerLibrary.SavePreset` / `ApplyPreset` have no callers** (medium), `Codex.Domain/Music/Equalizer.cs:40,46`
   - `EqualizerActivity` persists the library but never saves or applies a named preset. CLAUDE.md's Drive backup bullet mentions presets. Action: add the preset UI, or drop presets.

18. **TIDAL device-code flow left over after the move to PKCE** (high), `Codex.Infrastructure/Music/TidalDeviceAuthClient.cs:73,111,155`
   - `RequestDeviceCodeAsync`, `PollAsync`, `PollUntilAuthorizedAsync` and `TidalDeviceAuthorization` are only used by tests. Only `ExchangeCodeAsync` and `RefreshAsync` are called.
   - The comment in `Score.Android/TidalAccountStore.cs:15` still names `PollUntilAuthorizedAsync`.
   - Action: delete the device-code half and fix that comment.

19. **`ScoreUpdateStatusStore` is written but never read** (high), `FocusPhone/Score.Android/ScoreUpdateService.cs:266`
   - `Record(...)` is called at :60. `LastSummary` and `LastRunUtc` have no readers, though the doc says "as a sentence for the Settings screen". Action: show it in Score's Settings.

20. **`ScoreIdentity`** (medium), `FocusPhone/Score.Android/ScoreIdentity.cs:15`
   - Zero references. Its doc says it decides whether this install may write the shared vault, but it only wraps one property. Action: delete it, or use it.

## FocusPhone: Post

21. **`Tombstones.ShouldSpeak` is never called, so a failing filing delete never earns its fold** (high), `Codex.Domain/Post/Tombstones.cs:76`
   - `PostSync.cs:133-136` says a delete "that keeps failing eventually earns a fold". What happens instead (:187) is a `PostLogLines.OwedDeleteFailing` log line, and no dog-ear is raised. Action: raise the fold when `ShouldSpeak` is true.

22. **`VoicemailNotificationParser`** (high), `Codex.Domain/Timeline/VoicemailNotificationParser.cs:10`
   - Google Phone voicemail capture is tested but no notification listener calls `TryParse`. Only `FiVoicemail`, the SMS path, is wired. Action: wire it into the listener, or delete it.

23. **`SenderPromotions.Demote`** (low), `Codex.Domain/Post/MailClassification.cs:125`
   - No caller, so a sender correction cannot be undone. That is consistent with "permanent", but then the method is dead. Action: delete it.

24. **`ContactPicker.PhoneContacts` / `EmailContacts`** (medium), `FocusPhone/Post.Android/ContactPicker.cs:238,244`
   - Zero callers. Action: delete them.

25. **`IdentityLinkStore.Unlink`** (medium), `Codex.Infrastructure/Storage/IdentityLinkStore.cs:48`
   - Zero callers, so a wrong contact link cannot be removed. Action: wire it, or delete it.

## FocusPhone: Plate

26. **`FaceBook.Correct` is never called** (high), `Codex.Domain/Plate/FaceBook.cs:147`
   - `EditorActivity.cs:234` only calls `Learn`. CLAUDE.md says "a correction is permanent and takes the face off whoever it was wrongly on", but no UI reaches that path. Action: add a "That's someone else" action in the editor.

27. **`Errands.HasAWaitingCaller`, `CaptureFormat.NextRatio` and `NextFlash`** (medium), `Codex.Domain/Plate/Errands.cs:69` and `CaptureFormat.cs:57,67`
   - Only tests use them. Action: wire them or delete them.

---

## Pulp

28. **The mis-landing check (D122) is computed nowhere** (high)
   - `LandingCheck.Decide` (`Pulp/Pulp.Domain/HandOffs/LandingCheck.cs:92`) and `PulpNotices.AfterLanding` (`Pulp/Pulp.Android/Components/PulpNotices.cs:67`) have zero production callers. The D122 dog-ear after a hand-off can never show.
   - Action: call it from the session-receiver path.

29. **`WaitingNotices.SportsFeedUnreachable` (D208) is never raised** (high), `Pulp/Pulp.Domain/Actions/WaitingNotices.cs:107`
   - Only tests use it. An unreachable league feed is silent. Action: raise it from `SportsFeeds` on failure.

30. **Frozen worker names with no worker behind them** (medium), `Pulp/Pulp.Domain/PulpIdentity.cs:37-49`
   - `FridayStackWorkerName`/`WorkName` and `WeeklyRefreshWorkerName`/`WorkName` exist, but `CalendarSyncWorker` is the only Worker in Pulp.Android.
   - The Friday stack is dealt when the page opens (`DiscoverPageWeek`); the plan's weekly availability refresh worker was never built.
   - Action: build them, or mark the names as reserved.

31. **Smaller unused members** (medium)
   - `HouseholdView.LeaguesAllowedFor` (`Household/Records/HouseholdView.cs:122`), `ServiceCatalogue.ByMerchant` (`Services/ServiceCatalogue.cs:68`), the `ChildViewText.WatchAsAChild` constant, and `LookupTally` (`Catalogue/LookupCounter.cs`, a production class only tests construct).
   - Action: delete them, or move `LookupTally` into tests.

## Index (Ledger)

32. **`BudgetService.ApproveMonthAsync` is never called, so D66 retention (guard G5) never purges** (high), `Ledger/Ledger.Infrastructure/Services/BudgetService.cs:999`
   - Approval actually happens through `MarkBalancedAsync` (:919, called from `WorksheetDeliveryService:258` and `WorksheetReadBackService:282`). That sets `Approved` without purging the previous month's `Transaction` rows, so they are never deleted.
   - Action: route approval through `ApproveMonthAsync`, or move the purge into `MarkBalancedAsync`.

33. **"Refresh now" does not do what its comment says** (medium), `AccountService.cs:522`
   - The comment says `MarkAllDueAsync` is "used by Refresh now", but it has no callers. `MainPage` → `RefreshWorker.RunNow` drains only institutions that are already due, so a failed or not-yet-due bank is skipped.
   - Action: call `MarkAllDueAsync` before `RunNow`.

34. **`AccountService.HasInstitutionsAsync`** (`:39`), **`ConnectBrowserPolicy.IsRestrictedViewer`** (`Ledger.Domain/Connect/ConnectBrowserPolicy.cs:47`, whose doc says "no longer gates the connect page"), and **`WorksheetRegions.NewCategoryRowTop` / `ContinuationAddCategoryHeaderTop`** (`Ledger.Domain/Worksheet/WorksheetRegions.cs:111,148`) have no production callers (medium). Action: delete them, or wire the worksheet regions into `FollowUpPainter`.

## Folio (Storyboarder)

35. **`OnSettingsClicked` is a handler with no binding** (high), `Storyboarder/Folio.Android/Pages/MainPage.xaml.cs:713`
   - Not referenced in `MainPage.xaml` or code. Action: delete it.

36. **`ProjectRow.SameAs`** (medium), `MainPage.xaml.cs:727`
   - Never called, so the row-diff it was written for is not used. Action: use it to skip repaints, or delete it.

37. **`ReadingPrompt.CachedPrefix`** (medium), `Folio.Domain/Reading/ReadingPrompt.cs:43`
   - No caller anywhere. The test named `EveryPersonaSharesByteIdenticalCachedPrefix` does not call it. Action: have the test use it, or delete it.

38. **`PipelineOptions.IdlePollSeconds`** (high), `Folio.Infrastructure/Configuration/Options.cs:153`
   - The property is never read. Action: delete it.

39. **`Folio.Infrastructure/Resources/owner.png`** (high)
   - Embedded by `Folio.Infrastructure.csproj:31` (148 KB). There is no `GetManifestResourceStream` anywhere in Storyboarder, since `OwnersMark` replaced the photo. `Tools/IconStudio/BrandAssets.cs:24` still writes this target.
   - Action: delete the file, the `EmbeddedResource` and the IconStudio target.

## Constitution

40. **`SeatCacheEncouragement`** (high), `Constitution/Constitution.Infrastructure/Workouts/SeatCacheEncouragement.cs:11`
   - Only tests use it; the head wires `SeatLineEncouragement` (`WorkoutWeekDeliverySeams.cs:69`). Action: delete it.

41. **Domain helpers only exercised by tests** (medium). The pipeline probably inlines these, so check before deleting:
   - `BodyBackupNote` (Infrastructure/Backup)
   - `DueExpectedDocuments`
   - `WorkoutWeekNotices` (so the "week not planned" notice is applied by nothing)
   - `TasteProfileCodec`, `TasteSeed`, `PlannedHistory`, `ReplanVeto`
   - `ScrapGeometry`, `RosterLabel`, `BusyReasons`

   Of these, **`WorkoutWeekNotices` and `BodyBackupNote` look like the user-visible gaps**: notices and sentences that never reach the screen.

42. The null objects `NoMealTaste`, `UnavailableAteEstimator`, `UnavailableTasteJudge`, `NoTemplateAuthor` and `NoRecipeTextReader` are only used by tests (low). This is fine, but they could move into the test project.

## Foreword

43. **`PuzzleType` enum** (high), `Foreword/Foreword.Domain/Models/PuzzleType.cs`
   - Zero references, including tests. Action: delete it.

44. **`GoogleAccessTokenProvider` and `GoogleClientFactory.Initialize`/`InterceptAsync`** (medium), `Foreword.Infrastructure/Google/GoogleAccessTokenProvider.cs`
   - The provider (including `ExchangeCodeForRefreshToken`) is only used by tests; the app uses device-account tokens. Action: delete it after checking DI.

---

## Constants and Options

45. **Unused constants** (medium)
   - `AndroidUserRestrictions.NoModifyAccounts` (`Codex.Domain/Policy/AndroidUserRestrictions.cs:16`). Check whether the policy plan meant to apply it.
   - `WebPermissionPlan.AudioCapture` (`Codex.Domain/Web/WebPermissionPlan.cs`).
   - `GoogleScopes.DocumentsReadonly`.
   - Action: delete them, or apply `NoModifyAccounts`.

## Build, CI and dependencies

46. **`Tools/IconStudio.Tests` and `Tools/IndexHistorySeal.Tests` are run by no workflow** (high)
   - Only `LogLoopReader.Tests` is run, from `log-loop-guard.yml`. The icon generators run in CI without their tests. Action: add them to a workflow.

47. **`Moq` is referenced but never used** (high, no `using Moq`) in:
   - `Storyworthtelling.Google.Tests`
   - `Storyworthtelling.Updates.Tests`
   - `Storyworthtelling.Remarkable.Tests`
   - `Storyworthtelling.Diagnostics.Tests`
   - `Pulp/Pulp.Tests`

   Action: remove the PackageReference.

48. **`Microsoft.Extensions.Logging.Abstractions` in `Storyworthtelling.Maui.Tests`** has no `ILogger`/`NullLogger` use (medium). Action: remove it.

49. **`Microsoft.Extensions.Http` in `Folio.Infrastructure` and `Ledger.Infrastructure`** (medium)
   - Neither project calls `AddHttpClient` or uses `IHttpClientFactory`; the heads do, and get the package through these projects.
   - Action: move the reference to the heads.

The AndroidX pins (`Concurrent.Futures`, `LiveData.Core`, `Dagger`, `OkHttp3`) look unused by namespace but are documented NU1107 or runtime-class pins, so leave them.

## Docs and privacy

50. **CLAUDE.md drift** (medium)
   - The Post RCS bullet's `StartPairing` claim (item 1 of the table).
   - The "Plate video refused" line does not mention that the component is declared anyway.
   - `TidalAccountStore`'s comment (item 18).
   - Action: update them.

51. **Check `Branding/owner.png`** (medium)
   - It is committed (135 KB) and, through `Storyworthtelling.Paper.csproj:32`, embedded in every app that references Paper. I did not open it.
   - Under CLAUDE.md's public-repo rule, confirm it is IconStudio's placeholder monogram and not a real photo. With item 1 of the table, the embed can simply be removed.

**Totals:** about 45 high/medium findings. The most consequential:
- Index retention never purging old transactions (#32).
- Pulp's mis-landing check and sports-feed notice never wired (#28, #29).
- Post's tombstone fold never raised (#21).
- Plate's face correction never reachable (#26).
- Score's update status never shown (#19).
- The entire `Storyworthtelling.Google` library being dead weight (#1).
