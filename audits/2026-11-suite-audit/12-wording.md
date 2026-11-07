# Fixed-copy wording audit: new findings (not in the already-reported list)

I audited this read-only. I pulled the UI string literals (C# and XAML) for all nine apps, plus the shared Codex/kit code, and checked them against DESIGN.md §6–§8, §13, §14 and §15. I also compared the Pulp and Index mockups with the code. All quoted text is UI copy; none of it is personal data. Paths are relative to `/home/user/Personal`. There are 74 items below, grouped by type. Each line gives the location, the current string, what's wrong and a suggested fix.

## 1. British spelling and idiom (§14 says American English)

Pulp's discovery log doesn't record any British-spelling decision, so these all count as violations.

1. `FocusPhone/Flyleaf.Android/UnlockActivity.cs:147`: "…The countdown is visible and can be cancelled at any time." → "canceled".
2. `FocusPhone/Score.Android/TidalPairingActivity.cs:265`: "Sign-in was cancelled." → "Sign-in was canceled. Tap Sign in to TIDAL to try again."
3. `Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs:123` and `:162`: "Backup cancelled." / "Restore cancelled." → "canceled".
4. `FocusPhone/Score.Android/DiscoverArea.cs:262`: "Sign in to TIDAL in Settings to search the music catalogue." → "catalog". DESIGN.md itself uses "catalog".
5. `FocusPhone/Score.Android/WorkoutArea.cs:221`: "Couldn't reach the tempo catalogue: " + e.Message → "catalog". It also shows a raw exception message (see §3 below).
6. `Pulp/Pulp.Domain/Discover/DiscoverPageText.cs:34`: "The catalogue could not be reached just now." → "catalog". It also has no next step: add "Try again in a moment."
7. `Pulp/Pulp.Domain/FirstRun/VaultOutcomes.cs:99`: "Google does not recognise this build yet" → "recognize".
8. `Constitution/Constitution.Domain/Recipes/EmptyBox.cs:6`: "Ten family favourites, and the planning can begin." → "favorites".
9. `Constitution/Constitution.Domain/Workouts/TripsAdapter.cs:35` and `:42`: "Paused: family holiday" / "The plan is paused for the family holiday." → "vacation". Also `Groceries/HolidayAskAhead.cs:79` "The holiday".
10. Pulp uses "tick" where American English says "check". Suggest "Check this…" / "turn it on":
    - `Pulp/Pulp.Domain/Settings/AttestationText.cs:13–17`: "Tick this only if every adult profile…", "services ticked here", "without a tick"
    - `HandOffs/HandOffNotice.cs:22`: "…then tick it in Settings."
    - `Sports/SportsScreenText.cs:84`: "Tick {service} in Settings, under Services…"
    - `Pulp.Android/Settings/ServiceActivity.cs:174`: "Tick it to open titles on {name} from Pulp."
11. `Foreword/Foreword.Android/Pages/RotationsPage.xaml:32`: "These are the lists that come round again." and `RotationEditPage.xaml.cs:67`: "…each comes round every {n} days." → "come around".
12. `Foreword/Foreword.Android/Pages/SetupPage.xaml.cs:375`: "Log into nytimes.com…" → "Log in to nytimes.com". DevTools jargon too (see §4).

## 2. Title case and stray glyphs (§14 says sentence case, no emoji or ⚠-style glyphs)

13. `FocusPhone/Codex.Domain/Music/Track.cs:66–75`: "Unknown Title", "Unknown Artist", "Unknown Album", "Unknown Genre" → "Unknown title", etc. Also `HiddenTrackSplit.cs:14`: "Hidden Track" → "Hidden track".
14. `FocusPhone/Codex.Domain/Apps/CategoryCatalog.cs:46–53`, the built-in shelf names: "Travel & Transit", "Food & Dining", "Health & Fitness", "Utilities & Tools", "Smart Home" → "Travel and transit", "Food and dining", "Health and fitness", "Utilities and tools", "Smart home". Stored data only keys on the ids, so renaming the labels is safe.
15. `Foreword/Foreword.Android/Pages/MainPage.xaml.cs:304–353`: "Delivery Log", "Crash Log", "Delivery Log (copied to clipboard)", "Foreword Delivery Log", "Foreword Crash Log" → "Delivery log", "Crash log". These are separate from the "Delivery Failed" and "View & Copy" items already reported.
16. `Pulp/Pulp.Domain/Actions/ActionCardPlan.cs:115` and `ChildViewText.cs:23`: "Remove from Favorites" capitalizes the area name, but `ActionCardPlan.cs:127` has "Remove from Continue watching". Pick one rule for area names inside sentences.
17. `Pulp/Pulp.Domain/Actions/ActionCardPlan.cs:90`, `ChildViewText.cs:22`: "★ Favorite"; `ChildFavorites.cs:74`: "★ {child}'s star". The star is a glyph in a button label. Draw it as an icon, or drop it.
18. The "✓" glyph is inconsistent across the suite:
    - "Signed in as {email} ✓" (`Folio SetupPage.xaml.cs:304`)
    - "Registered ✓" (`:460`)
    - "Paired ✓" (`FocusPhone/Plate.Android/SettingsArea.cs:74`)
    - while Pulp, Score and Post print "Signed in as {email}." with no glyph.

    Recommend keeping ✓ only in the credential-health "Working ✓" line, or drawing it as an icon.
19. `Constitution/Constitution.Domain/Documents/Recipe/RecipeRatingPage.cs:30` (printed page): "FIRST TIME · PLEASE CIRCLE" → "First time · circle one". This is both "please" and all caps.

## 3. Raw exceptions, HTTP codes and type names shown to the user (§14 says what happened + what to do next)

20. `FocusPhone/Flyleaf.Android/UnlockActivity.cs:426`: "Install failed: {e.Message}". `LauncherSettingsActivity.cs:476`: "GitHub install failed: {e.Message}". `:363`: "Couldn't close {label}: {e.Message}". Use a plain sentence plus a next step, and send the detail to Diagnostics.
21. `FocusPhone/Post.Android/EmailComposeActivity.cs:195`: toast "Send failed: {e.Message}". `SettingsArea.cs:200`: "Sign-in didn't complete. {ex.Message}".
22. `FocusPhone/Plate.Android/ScanArea.cs:450`: "Saved to Documents, but the reMarkable refused it: {e.Message}". `CameraArea.cs:245/271`, `ErrandActivity.cs:318/565/587/613`: "That one did not save: {exception?.Message}", "Could not take that frame: …", "Could not read that: …". `SettingsArea.cs:177`: "That code was refused: {e.Message}".
23. `FocusPhone/Plate.Android/SettingsArea.cs:145–147`: "Could not sign in: {e.Message}. Package: …, SHA-1: … — register these as an OAuth client if they aren't already." This puts developer setup into an error message.
24. `FocusPhone/Score.Android/ImportPlaylistActivity.cs:110`: "Couldn't reach Spotify: {e.Message}". `TidalPairingActivity.cs:334/338`: "TIDAL refused the code exchange: {exchanged.Status}." / "TIDAL wouldn't complete the sign-in: {exchanged.Detail}". "Code exchange" is jargon.
25. `FocusPhone/Score.Android/MusicLibrary.cs:307/320`: `Toast.MakeText(host, e.Message, …)` shows the bare exception text as a toast.
26. `Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs:446`: "The reMarkable cloud returned {(int?)ex.StatusCode}. Check the code and try again." It shows an HTTP number. Also `:139/185` "Backup failed: …" / "Restore failed: …", `:484` "Logs not sent: {result.Error}", and `MainPage.xaml.cs:360` "Couldn't paint a jacket: " + message.
27. `Foreword/Foreword.Android/Pages/SetupPage.xaml.cs:538`: "Pairing refused ({code}) — the code may have expired…" (shows an HTTP number). Also `:544` "Pairing failed — {ex.Message}", `:616` "Test page failed — {ex.Message}", `CalendarsPage.xaml.cs:68` "Could not read your calendars — {ex.Message}".
28. **Foreword's last-delivery line leaks internals.** `DeliveryWorker.cs:106/112` and `MainPage.xaml.cs:160/173` store "Delivered ({newsStatus})" and "Failed: {ex.Message}". The status strings come from `Foreword.Infrastructure/Delivery/CrosswordDeliveryService.cs:94–167`:
    - "puzzle only — news API error: {ex.Message}"
    - "puzzle only — no stories returned by API"
    - "{n} article(s){note} + puzzle"
    - "crossword only — PDF composition error: {ex.Message}"

    That's jargon (API, PDF composition), a "(s)" plural, raw exceptions, and "puzzle" vs "crossword" for the same thing. Suggest "Delivered at 5:40 am · 6 stories and the crossword" or "Delivered the crossword only — the news could not be fetched."
29. `Constitution/Constitution.Domain/Settings/ConnectionFailure.cs:43`: "The tablet did not accept that code ({status})." This shows an `HttpStatusCode` enum name such as "Unauthorized". `:54`: "{act} stopped unexpectedly ({failure.GetType().Name})." shows a .NET type name. `:52`: "Sign-in did not complete. {failure.Message}".
30. `Ledger/Ledger.Android/Pages/PlaidLinkPage.xaml.cs:424/455/550`: "Couldn't start Plaid Link: {ex.Message}", "Couldn't open Chrome: {ex.Message}", "Couldn't finish connecting: {ex.Message}".

## 4. Jargon shown to the user

31. `FocusPhone/Post.Android/SettingsArea.cs:114–121`: "Mail is going through the Gmail API…", "Mail is going through IMAP on the app password…", "…to move mail to the Gmail API". `:99`: "…after the device is locked down (or a whitelisted install)". Use plain words, and replace "whitelisted" with "allowed".
32. `FocusPhone/Post.Android/FiledArea.cs:301–302`: "Matched by words only: Post has no Claude key." There's no next step. Add: "Add the Claude key in Folio's settings for smarter matches."
33. `FocusPhone/Score.Android/SettingsArea.cs:95`: "There is no adb after the cutover, so failures are written here…". "adb" and "cutover" are developer words.
34. `FocusPhone/Score.Android/TidalPairingActivity.cs:191/279`: "Vault has no TIDAL client id/secret." `Codex.Domain/Music/TidalSignInFailure.cs:43/64`: "Check the client id and secret in the vault." / "…redirect URI or scopes in TIDAL's developer portal…".
35. `FocusPhone/Score.Android/PodcastsArea.cs:327`, `ManageSubscriptionsActivity.cs:83/168/182/193/219`: "OPML file", "Import OPML", "RSS feed URL", "Not a valid URL.", "That file couldn't be read as OPML." Suggest "podcast list file", "feed address" and "That isn't a web address."
36. `FocusPhone/Score.Android/MusicArea.cs:111`, `Plate.Android/SettingsArea.cs:65`, `Storyboarder/Folio.Android/Pages/SetupPage.xaml:65/77` and `.xaml.cs:346–349`, `Pulp/Pulp.Domain/FirstRun/VaultOutcomes.cs:100`: "register this app as an Android OAuth client (Drive API enabled)", "SHA-1", "drive.file scope". Move this into one owner-only "Setup notes" block, or into the docs.
37. `FocusPhone/Flyleaf.Android/LauncherSettingsActivity.cs:212`: "Couldn't change Wi-Fi. This needs the device-owner lockdown." This is jargon and gives no next step. Point the user to "Choose a network…" as `:258` already does.
38. `Constitution/Constitution.Domain/Waiting/WaitingPageModel.cs:79`: "…the new token goes to the vault and Constitution resends." → "…the new pairing is saved to the vault…". `Constitution.Android/Settings/HouseholdGroupView.cs:155`: label "Calendar id".
39. `Ledger/Ledger.Android/Pages/PlaidLinkPage.xaml.cs:412`: "Couldn't open Plaid Link. Check your credentials and environment in Settings." Suggest "Couldn't open Plaid. Check the Plaid client id and secret in Settings."
40. `Storyboarder/Folio.Android/Pages/MainPage.xaml.cs:746/753`: "…at your {LimitingFactor} cap", "rate-limited, resumes ~{when}" → "paused by the service, resumes around {when}". Also `SetupPage.xaml.cs:483` "…{n} of {m} batches confirmed in Drive", and `Codex.Ui.Android/Diagnostics/HeadDiagnostics.cs:279` "Uploaded and confirmed {n} of {m} batches."
41. `FocusPhone/Plate.Android/DarkroomArea.cs:171`: "The model will not name anyone in a picture." → "Claude won't name people in photos; write their names yourself."
42. Internal codename leaks: "FocusPhone" is the repo's name, not an app the user sees.
    - `Post.Android/DiagnosticsActivity.cs:153` "FocusPhone diagnostics"
    - `Score.Android/AudioDiagnosticsActivity.cs:97/106` "FocusPhone diagnostics" / "FocusPhone Audio diagnostics"
    - `Codex.Ui.Android/Diagnostics/CrashViewerActivity.cs:72/81` and `Codex.Domain/Diagnostics/CrashLogFormatter.cs:15` "FocusPhone crash report"
    - `Score.Android/Google/DriveMusicClient.cs:30` `ApplicationName = "FocusPhone Audio"`. CLAUDE.md says this label was renamed to "Score", but this second copy was missed.

## 5. Errors and notices missing what happened and/or what to do next

43. `FocusPhone/Post.Android/MailboxArea.cs:264–265`: "Post holds the texting role but the last group send failed. It will need to be sent again." This is passive and contradicts the routing rethink, where Google Messages holds the role. Suggest "A group text did not go out. Open the conversation and send it again."
44. `FocusPhone/Post.Android/MailboxArea.cs:1976`: "The send failed before it could even start." and `:1916`: "…and the phone refused the plain text too." Neither has a next step.
45. `FocusPhone/Post.Android/FiledArea.cs:210`: "Filed could not be searched. The diagnostics log has the details." It needs a next step ("Try again in a moment"). Elsewhere Post says "Diagnostics has the details"; pick one wording.
46. **Wrong next step.** `FocusPhone/Score.Android/MusicArea.cs:323` says "Generating needs the shared Claude key — set it in Settings." Score's Settings has no Claude key field; the key is set in Folio (Index's mockup 10 says "Set in Folio"). Suggest "…add it in Folio's settings."
47. **Stale copy.** `FocusPhone/Plate.Android/SettingsArea.cs:98` says "Plate keeps nothing else. Photos go to your camera roll, scans to Documents." This contradicts the Darkroom spine: nothing leaves Plate until it is sent (`DarkroomArea.cs:77` says "Nothing here has been backed up").
48. `FocusPhone/Score.Android/PodcastsArea.cs:772/849`: "Couldn't start playback — see Manage → Diagnostics." The Settings tab labels the same screen "Recent failures", so the path named in the error doesn't match what the user sees there.
49. `Ledger/Ledger.Android/Pages/MainPage.xaml.cs:1384`: "Couldn't load transactions." Add "Pull down to try again" or the equivalent.
50. **Constitution notifications.**
    - `Constitution/Constitution.Domain/Notices/NoticeNotifications.cs:39–45`: four of the seven bodies are the identical "Open Constitution to see what to do." Each should name the action.
    - **Wrong noun:** the `BoxUnderTen` notification says "The budget box is running low / Worth knowing. Nothing to do." The Waiting page for the same notice (`WaitingPageModel.cs:104–106`) says "The recipe box is under ten" and offers "Scan a card". So the notification both names the wrong box and contradicts the action.
51. **Notification vs Waiting page drift for the same notice.**

    | Notification | Waiting page |
    |---|---|
    | "Health Connect was turned off" | "Health Connect was switched off" |
    | "The bed cannot be reached" | "The bed couldn't be reached" |
    | "The week could not be planned" | "This week couldn't be planned" |
    | "The household file will not open" | "The household file won't open" |
    | "A scanned page is waiting" | "A scanned card is waiting to be checked" |

    Use the same headline in both places.
52. `Constitution/Constitution.Domain/Waiting/WaitingPageModel.cs:94`: "…Nothing is wrong on your side that we can see." Every other app speaks as "Constitution" or "Post", never "we".
53. `Pulp/Pulp.Domain/Actions/WaitingNotices.cs:113–114`: "The {name} schedule could not be reached / Games for {name} are shown as they were the last time the feed answered." "Feed" is jargon; add "Pulp tries again on its own."

## 6. Plurals, numbers, units and time

54. Hard-coded plurals that print "1 tracks":
    - `Score.Android/MusicArea.cs:218/353/399/444` "{n} tracks"
    - `WorkoutArea.cs:162` "{n} tracks have no tempo yet"

    `PlaylistEditor.cs:189` and `Codex.Domain/Music/MusicGroups.cs:77` already handle the singular, so reuse that.
55. `FocusPhone/Post.Android/RolodexArea.cs:126`: "{n} contacts have no photo". `ArchiveBackup.cs:108`: "Restored {n} filed messages from Drive."
56. `FocusPhone/Plate.Android/ErrandActivity.cs:461`: "{n} ingredients, {m} steps".
57. `Storyboarder/Folio.Android/Pages/DraftDiffPage.xaml.cs:26`: "{n} scene(s) re-illustrated". `Ledger/Ledger.Domain/Worksheet/WorksheetDeliveryResult.cs:37`: "read back {n} limit(s)". Don't use "(s)".
58. `FocusPhone/Score.Android/EqualizerActivity.cs:186–187`: `$"{hz / 1000} kHz"` uses integer division, so a 3,600 Hz band shows "3 kHz" and 1,500 Hz shows "1 kHz". Use one decimal ("3.6 kHz").
59. `FocusPhone/Post.Android/Attachments.cs:40`: `{SizeBytes / 1024} KB` shows "0 KB" for anything under 1 KB and never switches to MB. Use a shared size formatter.
60. `FocusPhone/Post.Android/MailboxArea.cs:1668`: `{time:h:mm tt}` gives a culture-dependent "7:42 PM", where §14 wants "7:42 pm". Use `TimeFormats.Clock12`. This is separate from the date-order item already reported.
61. Durations and "ago" are written three ways:
    - §14 and `RelativeTime` use "5m ago"; `Pulp/Pulp.Domain/Household/HouseholdSync.cs:82/87` writes "Synced with Drive 5 min ago" / "3 h ago".
    - Remaining time: Score `PlaybackFormatting.cs:15` "{n} min left", Pulp `ContinueRowText.cs:77–79` "{n}m left" / "{h}h {m}m left", Constitution `TodayModel.cs:56` and `WorkoutsTabModel.cs:125` "{n} minutes" but `CardHistory.cs:92` " min hands-on".

    Standardize on §14's "42 min" for durations and "5m ago" for list timestamps.
62. `Constitution/Constitution.Domain/Recipes/Screens/CardHistory.cs:125–139`: "{n} days ago / 1 week ago / {n} weeks ago / months ago". This is a second relative-time scheme that doesn't follow §14's ladder.
63. `Storyboarder/Folio.Domain/Analysis/LengthGap.cs`, `ShelfPage.cs:179`, `AnalysisDocumentRenderer.cs:114/691`, `CoverPageRenderer.cs:126`: `{WordCount:N0}` uses the current culture, so a German-locale phone prints "80.000 words". Pass `CultureInfo.InvariantCulture` or en-US, as Index's `AccountsMoney` does.
64. `Ledger/Ledger.Domain/Usage/PulpUsageLine.cs:37`: "Opened 1 time in Pulp" → "Opened once in Pulp".

## 7. Stale wording left from earlier versions (Folio was Storyboarder)

65. `Storyboarder/Folio.Android/Pages/SetupPage.xaml`:
    - `:28` "Builds the design bible and storyboards the book."
    - `:40` "Renders the panel images."
    - `:58` "…to pick a Doc to convert."
    - `:87` "deletes the panel and page images"
    - `:106` "every conversion and rendered page"
    - `.xaml.cs:181` "Restored. Your conversion will resume shortly."

    Folio now writes a revision report, and the pictures are scene images, not panels. Suggest "Reads the manuscript and writes the revision report", "Paints the scene images", "pick a manuscript", and "Your review picks up where it stopped."
66. `Pulp/Pulp.Android/Settings/ServiceActivity.cs:210`: "Off Flyleaf's tray, {name} opens only through Pulp…". Flyleaf no longer has a tray (it's one bookcase now). → "Off Flyleaf's shelves…".

## 8. Toasts and confirm dialogs where §7 says "never a toast"

67. Success toasts:
    - `FocusPhone/Post.Android/EmailComposeActivity.cs:189` "Sent."
    - "Copied to clipboard." in `Post DiagnosticsActivity.cs:154`, `Score AudioDiagnosticsActivity.cs:98`, `Codex.Ui.Android/Diagnostics/CrashViewerActivity.cs:73`
    - `Flyleaf HomeActivity.cs:349` "{app} isn't installed"
    - `Score ManageSubscriptionsActivity.cs:182/211/219`

    Folio and Foreword also put "(copied to clipboard)" into alert titles. Show these as inline status lines instead.

## 9. Pulp mockups vs code

Index's mockups and code match on every string I checked (36 key strings, mockups 2–17). Pulp has two drifts:

68. Mockup 12 (Waiting page): headline "Lanternbox opened on its home page", body "…Find the episode there, or make Northstar+ this title's home." The code (`Pulp.Domain/Actions/WaitingNotices.cs:60` and `HandOffNotice.cs:45`) says "{service} opened, but not on {title}" / "…Look for it in {service}, or mark it watched here." Both the headline and the offered next step have drifted.
69. Mockup 12: "The availability key stopped working / Lookups were refused on Tuesday, so where-to-watch is from then." The code (`WaitingNotices.cs:101–102`) renders "The Streaming Availability key stopped working / Lookups with it were refused, so what Pulp shows is from before then." The date the mockup states is dropped.

## 10. Notification channels (§13)

70. Post's channel is named "Messages" (`Post.Android/SmsNotifications.cs:72`). In Android's settings that reads as Google Messages' own channel, and Post now routes texts through Google Messages. → "Texts".
71. Score's channel "Downloads" (`ScoreDownloadService.cs:60`, from `strings.xml`) has no description. Constitution ("Notices" / "Something Constitution needs you to see.") and Folio ("Reading" / "Shown while Folio is reading a manuscript.") both have one. Add "Shown while episodes and albums download."
72. Folio's notification title is "Folio" (`ReadingNotification.cs:30`), which repeats the app name Android already shows. Use the step instead, e.g. "Reading Tidewater · read 3 of 17".

## 11. Small consistency items

73. Within Post, Google Messages is "Connect to Google Messages" / "Pair with Google Messages again" (`SettingsArea.cs:133`), "Post is connected…" / "Paired with Google Messages…" (`Codex.Domain/Post/MessagesPairing.cs:25/56`). Pick one verb. This is separate from the reMarkable "Register" vs "Pair" item already reported.
74. Within Index, the same act has three names: "Add an account" (Accounts, mockup), "Connect an account" (`Ledger.Android/Pages/SetupPage.xaml:127`) and "Connect a bank". Removing a connected account is "Disconnect {bank}" (`Views/InstitutionActionCard.cs:37`), but Settings shows a "Remove" button, plus a separate "Undo" button (`SetupPage.xaml:109/122`) where §7 calls for the dog-ear.

## Glossary: one word per concept

| Concept | Current words, by app | Recommend |
|---|---|---|
| Vault passphrase field | Flyleaf "Vault passphrase" in Settings but "Secrets vault passphrase" in Unlock; Post "Secrets vault passphrase"; Score "Vault passphrase" in Settings but "Secrets vault passphrase (optional)" in Music; Plate, Pulp, Folio, Foreword, Constitution "Vault passphrase" | **Vault passphrase** |
| Vault group heading | "Secrets vault" (Flyleaf, Folio, Foreword); "Vault" (Pulp, Index); "The vault" (Pulp first run, Foreword home) | **Vault** |
| Shared tablet | "reMarkable", "the reMarkable", "your reMarkable tablet" (Folio), "Your reMarkable" (Foreword), "the tablet" (Constitution, Plate "Straight to the tablet") | **reMarkable** as the name, with "the reMarkable" in sentences |
| Delete | Plate "Throw it away" and "Delete {n}" on one screen; Post "Delete this correspondence"; Score "Deleted {playlist}" / "Removed {title}"; Foreword "Delete"; Pulp "Remove {name}" / "Take off" / "take away the star" | **Delete** for data; **Remove from …** for membership |
| Undo / restore | Post "Put back in the Mailbox"; Pulp "Put … back" / "Undo …"; Index "Undo" button; Score and Pulp "Bring it back" (veto); Constitution "Turn the corner to bring them back" | **Undo** on the dog-ear; **Bring it back** only for vetoes |
| Sync / refresh | Pulp "Sync now" / "Synced with Drive"; Index "Refreshed 2h ago" / "Refreshing balances…"; Folio "Refresh" (Drive list); Post "message sync" / "Email sync"; Score "Scan" / "Rescan"; Flyleaf "Install from GitHub now" | **Refresh** for pulling data, **Scan** for the music library; drop "sync" from UI copy |
| Connect / link a bank | Index "Connect a bank" / "Connect an account" / "Add an account" / "Reconnect" / "Disconnect"; Post "Connect to" / "Pair with" Google Messages | **Connect** / **Disconnect** |
| Admin mode | Flyleaf "Enter admin mode", "Request admin unlock", "Admin access", "Unlock pending", "Admin unlock delay"; Index "Admin mode needed", "Open Admin mode from Flyleaf's settings" | **Admin mode** everywhere (capitalization per §15: "admin mode") |
| Claude key | "Claude key" (Post), "shared Claude key" (Score, Pulp), "Claude (Anthropic) API key" (Folio), "Claude — handwriting" (Foreword), "Claude, for the voice" (Index mockup) | **Claude key** (set in Folio) |
| Diagnostics share title | "FocusPhone diagnostics", "FocusPhone Audio diagnostics", "Post diagnostics", "Folio diagnostics", "FocusPhone crash report" | **{App} diagnostics** / **{App} crash report** |
| Could not vs couldn't | Mixed within single screens in Post, Score, Pulp, Plate and Constitution (Waiting page vs notifications) | **Couldn't** (contractions) everywhere, with the same apostrophe: Index and Constitution use curly ’, the rest straight ' |
| Ellipsis | "…" almost everywhere; ASCII "..." in Folio placeholders "sk-ant-...", "AIza..." (`SetupPage.xaml:26/38`) | **…** |
| Remaining time | "min left" (Score), "m left" (Pulp), "minutes" (Constitution) | **42 min** (§14) |

Nothing turned up for the other checks: no lorem, TODO or "Coming soon" text, and no decision or phase ids (D123, P7) in user-facing strings. Ids appear only in exception and log messages, such as `Pulp AddToPicker.cs:183` "(D257)", which is a log line.
