# Cross-app consistency audit: where similar features look different

I compared all nine apps against DESIGN.md §5–§14 and opened every file cited below. I edited nothing in the repo. The findings are ranked by how much a user would notice. "Native" means Flyleaf, Score, Post, Plate, Pulp and Constitution. "MAUI" means Folio, Index and Foreword.

---

## High impact

### 1. Errors and results appear as modal alerts in the MAUI apps and as toasts in some native ones
§7 rules out toasts and success alerts: failures should show inline, and errors should sit behind the dog-ear.
- **MAUI, using `DisplayAlert`:**
  - Folio, `Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs`: 14 alerts at :222–451 (sign-in failed, registration failed, export failed, vault backup).
  - Folio, `Pages/NewProjectPage.xaml.cs`: 7 alerts at :77–220.
  - Foreword, `Foreword/Foreword.Android/Pages/MainPage.xaml.cs:178`: "Delivery Failed (copied to clipboard)", which shows the whole exception chain in a modal.
  - Index: `Ledger/Ledger.Android/Pages/SetupPage.xaml.cs:402` and `PlaidLinkPage.xaml.cs:538` ("Vault backup didn't complete").
  - Foreword: `SetupPage.xaml.cs:489` and `TaskReadBackPage.xaml.cs:98` ("Saved here only").
- **Native, using `Toast.MakeText`:**
  - Score, `FocusPhone/Score.Android/MusicLibrary.cs:307,320`: shows the raw `e.Message` on sign-in and Drive failures.
  - Score, `ManageSubscriptionsActivity.cs:182,211,219`.
  - Post, `EmailComposeActivity.cs:189`: a **success toast** ("Sent."), plus :195 "Send failed: {e.Message}" and :50, :130.
  - Flyleaf, `HomeActivity.cs:349`: "… isn't installed".
- **What matches DESIGN:** Constitution, Pulp and Plate, plus Post's Mailbox. They use an inline status, the dog-ear, or the Waiting page.
- **Fix:** Route these failures to the page's status line or to `DogEarState.SetNotice` / the native `DogEar.SetNotice`, worded as "what happened + what to do next". Delete the "Sent." toast: the thread updating is the success signal. Add a guard test that bans `DisplayAlert` and `Toast.MakeText` in the heads.

### 2. Clearing a log works four different ways
- **Native shared screen** (`FocusPhone/Codex.Ui.Android/Diagnostics/LoopDiagnosticsActivity.cs:135`), Post (`DiagnosticsActivity.cs`) and Score (`AudioDiagnostics.cs:40`): clear at once, with an undo dog-ear. This matches DESIGN.
- **Foreword `DiagnosticsPage.xaml.cs:115`:** a confirm dialog, "Clear diagnostics?" with Clear / Keep. Its own comment admits this breaks §5.
- **Foreword `MainPage.xaml.cs:357`:** the crash log's "Clear" deletes immediately, with no undo and no confirm. That is the same app taking the opposite approach on another screen.
- **Folio `SetupPage.xaml.cs:295`:** "Clear" in an action sheet deletes immediately, with no undo.
- **Fix:** Give MAUI the native pattern. Back the file up, call `DogEarState.BeginUndo`, and delete only when the window closes. The kit already has `DogEarState`, and `DiagnosticLog` in `Codex.Domain` already has the backup/restore logic to copy.

### 3. Index's Settings tab is not the suite's grey
- `Ledger/Ledger.Android/Pages/LedgerAreas.cs:118` gives Settings `InkMuted` / `PaperDeep`.
- Every other app uses `ResolvedMark` / `ResolvedFill`:
  - Folio: `FolioAreas.cs:68`
  - Foreword: `ForewordAreas.cs:72`
  - Post: `PostShellAreas.cs:27`
  - Score: `ScoreShellAreas.cs:32`
  - Plate: `PlateShellAreas.cs:28`
  - Flyleaf: `LauncherSettingsActivity.cs:140`
- The effect is that Index's inactive Settings tab is a pale cream cloth, not the grey cloth the other apps show. §9 says "Settings is always the neutral gray".
- **Fix:** Switch Index to `ResolvedMark`/`ResolvedFill`, and add Index to `AreaHueTests` (or a kit-level guard).

### 4. The way into diagnostics has seven different labels and controls
| App | Control | Label | Where |
|---|---|---|---|
| Flyleaf | nav row | "Diagnostics" | `LauncherSettingsActivity.cs:561` |
| Foreword | card row with chevron | "Diagnostics" | `SetupPage.xaml:251` |
| Post | quiet button | "Diagnostics" | `Post.Android/SettingsArea.cs:104` |
| Plate | secondary button | "Open diagnostics" | `Plate.Android/SettingsArea.cs:95` |
| Score | secondary button | "Recent failures" | `Score.Android/SettingsArea.cs:96` |
| Pulp | secondary button | "Export the log" (but it opens the diagnostics screen; it does not export) | `Pulp.Domain/Settings/SettingsGroups.cs:19`, `Shell/SettingsArea.cs:246` |
| Constitution | quiet button | "Logs" | `SettingsArea.cs:147` |
| Folio, Index | no screen | inline buttons on Settings | `Folio SetupPage.xaml:118–130`, `Index SetupPage.xaml:194–202` |
- **Fix:** Use one label ("Diagnostics") and one control (a nav row with a chevron) leading to one screen. Folio and Index should get Foreword's `DiagnosticsPage`, or a kit page that copies `LoopDiagnosticsActivity`.

### 5. The diagnostics screen itself differs between native and MAUI
- **Native** (`LoopDiagnosticsActivity.cs:46–49`, `Post DiagnosticsActivity.cs:50–53`, `Score AudioDiagnosticsActivity.cs:50–53`): the bottom bar is **Primary** "Send logs now", Secondary "Share", Quiet "Copy", Quiet "Clear". While sending it shows a status sentence, "Sending the logs to Drive…" (`LoopDiagnosticsActivity.cs:61`).
- **Foreword `DiagnosticsPage.xaml:36–42`:** Copy and Share are Secondary, Clear is Quiet, and Send logs now is Secondary, so there is no primary. The comment claims it "copies Score's AudioDiagnosticsActivity exactly", which it does not. The result of sending is written **into the button's own label** (`DiagnosticsPage.xaml.cs:150`, `SendLogsButton.Text = "Logs sent: …"`).
- **Folio and Index:** "Send logs now" is one Secondary button among many, with no in-flight status and the button never disabled (`Folio SetupPage.xaml.cs:470–485`). A second tap starts a second upload.
- **Score:** "Send logs now" on its own diagnostics screen opens a *second* diagnostics screen, `LoopDiagnosticsActivity`, instead of sending (`AudioDiagnosticsActivity.cs:50`).
- **Fix:** Use one layout everywhere. Send logs now is the primary action; disable it while it runs; show "Sending the logs to Drive…" and then the result on a status line, never on the button.

### 6. Text input pops up a platform dialog in five apps
- **The pattern:** Add, rename and enter-a-code use a modal: `AlertDialog` on native, `DisplayPromptAsync` on MAUI. DESIGN says pickers expand inline, and the dialogs draw platform chrome (shadow, Material buttons in all caps).
- **Native:**
  - Flyleaf: rename shelf at `LauncherSettingsActivity.cs:716` (a raw `EditText`, not `PaperUi.Field`); force-close picker at :348.
  - Flyleaf `UnlockActivity.cs:306,329,471`: the update token **and the vault passphrase** are entered inside a dialog.
  - Score: add feed and OPML import at `ManageSubscriptionsActivity.cs:169,232`.
  - Post `MailboxArea.cs`:
    - :643 name group
    - :663 new contact
    - :699 add to whom
    - :1536 call note
    - :1854 SMS compose, with a raw `EditText`
  - Post `RolodexArea.cs:320`.
- **MAUI:**
  - Index: add and rename category at `SetupPage.xaml.cs:526,559`, plus an action sheet at :555.
  - Foreword: add column, add a name and add a feed at `CalendarColumnsPage.xaml.cs:160`, `RotationEditPage.xaml.cs:185` and `SourcesPage.xaml.cs:187`.
  - Folio: the reMarkable code at `SetupPage.xaml.cs:425`.
- **Already inline:** Pulp, Constitution and Plate. Foreword's reMarkable code entry is an inline field (`SetupPage.xaml:197–205`).
- **Fix:** Use an inline expanding field (the accordion pattern) or a pushed page with a pinned bar. `AccordionPicker.cs:17` already says it exists to replace these dialogs.

---

## Medium impact

### 7. The "Working ✓ / Replace" credential pattern is done five ways
- **Folio, Index, Post and Pulp:** "Working ✓" with a **Quiet** Replace button. This is the rule in CLAUDE.md.
  - `Folio SetupPage.xaml:23,35`
  - `Index SetupPage.xaml:34`
  - `Post SettingsArea.cs:255`
  - `Pulp VaultActivity.cs:188`
- **Flyleaf `UnlockActivity.cs:447`:** a **Secondary** Replace button, inside a dialog, labelled "Update token: Working ✓".
- **Constitution `Constitution.Domain/Settings/SettingsModel.cs:154`:** "Working ✓" with **no Replace at all** (`ButtonLabel` is null).
- **Foreword `SetupPage.xaml:15`:** the comment says "A WORKING CREDENTIAL DOES NOT APPEAR", yet the Maps key still shows a "Working ✓ + Replace" row (:150–160).
- **Pulp's group meta** (`SettingsGroups.cs:44`): "Working", with no check mark.
- **Fix:** Pick one rule (Working ✓ plus a Quiet Replace) and apply it in all nine apps. Constitution needs a Replace. Foreword's comment needs fixing, or its Maps row removed.

### 8. How settings get saved differs from app to app
- **A pinned Primary "Save":** Post (`SettingsArea.cs:162`), Plate (`SettingsArea.cs:116`) and Flyleaf (`LauncherSettingsActivity.cs:169`).
- **A pinned "Save settings"** with the default button style: Folio (`SetupPage.xaml:149`).
- **Saves as you type, no Save button:** Foreword (`SetupPage.xaml:20`, "THERE IS NO SAVE … Score's SettingsArea refuses those on principle") and Score.
- **Saves when the field loses focus:** Constitution's passphrase (`SettingsArea.cs:619`).
- The same vault passphrase field therefore needs a Save tap in three apps and none in the others.
- **Fix:** Use the Foreword/Score rule (commit on change or blur) everywhere and remove the Save bars.

### 9. Settings screens are built five different ways, in different orders
- **The five layouts:**
  - **Grouped cards with chevrons:** Foreword (`GroupLabel`/`GroupCard`) and Flyleaf (`StartGroup`/`AddCardRow`, `LauncherSettingsActivity.cs:761`).
  - **Flat `Group()` headings:** Score (`SettingsArea.cs:50–93`).
  - **Flat field groups:** Post (`AddFieldGroup`).
  - **Accordion groups with a summary meta:** Pulp (`SettingsGroups.Order`) and Constitution (`AddGroup` / `SettingsGroup`).
  - **Flat `TitleLabel` sections:** Folio and Index.
- **Where the vault sits:** first in Plate, second in Score, Folio and Index, fifth in Flyleaf, ninth in Foreword, and last in Post. Its label is "Vault", "Secrets vault" or "Secrets vault passphrase".
- **Fix:** Agree one settings anatomy (Foreword/Flyleaf grouped cards is the most complete) and one group order: Connections → Vault → app-specific → Diagnostics → build line.

### 10. Copy-to-clipboard feedback varies
- **Toast** "Copied to clipboard.": `CrashViewerActivity.cs:73`, `Post DiagnosticsActivity.cs:154`, `Score AudioDiagnosticsActivity.cs:98`.
- **No feedback at all:** `LoopDiagnosticsActivity.cs:127` (the screen six apps share).
- **The button label changes to "Copied":** Foreword (`DiagnosticsPage.xaml.cs:86`).
- **A modal whose title says "(copied to clipboard)":** Folio (`SetupPage.xaml.cs:231,289`) and Foreword (`MainPage.xaml.cs:320,347`).
- **An inline status label:** Folio's "Copy package + SHA-1" (`SetupPage.xaml:72`).
- **Fix:** Use one inline change everywhere; Foreword's "Copied" label is the one that fits §7. Put it in the shared native screen too.

### 11. Undo looks different even within one Index screen
- `Ledger/Ledger.Android/Pages/SetupPage.xaml:117–124` and `SetupPage.xaml.cs:440–488`: removing a bank shows a hand-built inline **"UndoBar"** card with an "Undo" button.
- Removing a budget category **on the same page** uses the dog-ear (`UndoRemoveCategoryAsync`, :465).
- CLAUDE.md says UndoBar was retired from the suite. The "Remove" button also uses `DestructiveButton` (:109).
- **Fix:** Route the bank removal through `DogEarState.BeginUndo`.

### 12. Destructive actions on long-press cards are red in some apps and not others
- **Red (`ProblemMark`):** Delete / Block / Spam / Throw away in Post (`MailboxArea.cs:583,590,1513,1520`), Score (`MusicArea.cs:489`) and Plate (`DarkroomArea.cs:240`, `ScanArea.cs:150`), via `ActionCard.cs:98`.
- **Plain ink by decision:** Pulp's card (`PulpActionCard.cs` doc comment: "nothing is Destructive red, because a removal here is undone from the corner").
- **No destructive weight at all:** Folio's card (`Folio.Android/Pages/ActionCard.cs`: Normal / Primary / Quiet).
- **Also:** `PaperUi.DestructiveButton` and the kit's `DestructiveButton` style exist even though §7 says "Destructive: *None*". They are used in Plate, Post, Score, Constitution, Index and Foreword. Foreword even styles "View crash log" as destructive (`MainPage.xaml:67`).
- **Fix:** Decide once. Pulp's reasoning follows §7 (undo replaces warning), so drop the red and retire `DestructiveButton`, or record the exception in DESIGN.md.

### 13. There are three action-card implementations, and the MAUI cards use a different ground
- **The three:** native `Codex.Ui.Android/ActionCard.cs`, Folio `Pages/ActionCard.cs`, and Index `Views/ActionCardHaze.cs` plus four card classes. The MAUI kit has no action card.
- **The ground:** native cards use `PaperBright` (#FFFFFF). The MAUI cards use `NoteCardHex` (#FFFEF8) (Folio :70, Index :24).
- **The haze:** the same in all three (PaperDeep at about 86%).
- **Fix:** Lift Folio's card into `Storyworthtelling.Maui/Views`, and pick one card ground for both kits.

### 14. Dates and times bypass the shared formatters and come out in British order
§14 asks for American English, 12-hour time and "Jun 30" style dates.
- **Post `MailboxArea.cs:1668`:** `{StartsOn:ddd d MMM} at {time:h:mm tt}` gives "Tue 4 Nov at 3:00 PM" (day before month, capital PM, current culture).
- **Folio `BookPage.cs:46`:** draft rows show `"d MMM"` ("4 Nov"). This is a list timestamp, which should be relative.
- **Foreword:**
  - `TasksPage.xaml.cs:42`: `d MMMM`.
  - `CalendarSplitPage.xaml.cs:240`: `dddd h:mm`, with no am/pm.
  - `SetupPage.xaml.cs:47`: `h:mm tt`, culture-dependent.
- **Pulp `CalendarArea.cs:268`:** `"ddd, MMM d"` with the current culture.
- **Correct helper:** `TimeFormats` (Codex, Folio, Foreword, Constitution, Index) gives "7:42 pm".
- **Fix:** Route all of these through `TimeFormats`/`RelativeTime` with the invariant culture, and make Folio's draft rows relative.

### 15. Pulp has no copy of `RelativeTime` or `TimeFormats`
- There are six identical copies elsewhere (I checked by md5): Codex, Ledger, Folio, Foreword, Constitution and the Maui kit.
- Pulp writes its own instead:
  - `Pulp.Domain/Sports/SportsScreenText.cs:21` (`Clock12`)
  - `Library/RecentDayLabel.cs`
  - `Calendar/CalendarScreenDayLabel.cs`
- Its labels are capitalised ("Yesterday", "Tonight"), while the shared rule uses lower case ("yesterday").
- **Fix:** Add `Pulp.Domain/Time/{RelativeTime,TimeFormats}.cs` and include them in the parity test. Wrap day headings rather than re-implement them.

### 16. Google sign-in status, errors and button labels differ
- **Status wording:**
  - "Signed in as X ✓": Folio `SetupPage.xaml.cs:304`. The same page says "Signed in as X." without the tick at :338.
  - "Signed in as X.": Index, Post, Pulp and Constitution.
  - "Signed in as X" (no full stop): Score, and Folio's `NewProjectPage`.
  - The bare email: Foreword `SetupPage.xaml.cs:63` and Plate.
  - "Signed in with Google as X — AI glyphs enabled.": Flyleaf :585.
  - "Not signed in" against "Not signed in.": Plate uses the full stop, Foreword and Folio do not.
- **Button labels:**
  - "Sign in with Google": Folio, Index, Constitution.
  - "Sign in": Foreword, as a quiet button.
  - "Sign in again": Score.
  - "Use another account": Plate.
- **The "no active window" error:**
  - Foreword: an inline status line (`SetupPage.xaml.cs:553`).
  - Folio: a `DisplayAlert` (`SetupPage.xaml.cs:329`, `NewProjectPage.xaml.cs:77`), while the same Folio page reports the same problem for restore inline (:148).
  - Score: a raw toast.
- **Fix:** Put shared copy constants in `Storyworthtelling.Voice` (for example, a `GoogleAccountCopy` class) and show errors inline everywhere.

### 17. reMarkable pairing uses different words and flows
- **Folio:**
  - Button: "Register reMarkable".
  - Code entry: a prompt dialog.
  - Status: "Registered ✓" / "Not registered yet."
  - Errors: a "Registration failed" modal (`SetupPage.xaml:95`, `.cs:425–460`).
- **Foreword:**
  - Buttons: "Pair" on the row, then "Register reMarkable device" inside the expanded fields.
  - Status: "Paired".
  - Errors: inline, "Pairing refused…" (`SetupPage.xaml:194–205`, `.cs:530–544`).
- **Constitution:** "Pair" / "Paired and saved on this phone…", all inline (`SettingsArea.cs:482,589`).
- **Fix:** One verb (Pair / Paired), Foreword's inline field, and one shared status string.

### 18. Retry buttons use different words
- "Retry now": Folio (`MainPage.xaml.cs:570`, `DeskStatus.cs:94`).
- "Try again": Post (`MessagesPairingActivity.cs:240`) and Plate (`DarkroomArea.cs:232`).
- "Pair again" / "Sign in again": Constitution's Waiting page (`WaitingPageModel.cs:80,90`).
- **Fix:** Use "Try again" for a generic retry and "<verb> again" for a specific one.

### 19. Foreword breaks sentence case
- §14 asks for sentence case, including buttons.
- `Foreword/Foreword.Android/Pages/MainPage.xaml.cs`: "Delivery Failed (copied to clipboard)" :178, "Delivery Log" :304/315, "Crash Log" :336/342, and the actions "View & Copy" :315/342.
- Folio's copy of the same flow already says "View & copy" (`SetupPage.xaml.cs:284`).
- **Fix:** Correct the casing. Better, remove the action sheets and point to `DiagnosticsPage`.

### 20. Post's voiced empty Mailbox is not in the calligraphy hand
- `Post.Android/MailboxArea.cs:156` draws the voiced pool line (`EmptyMailboxStore`) with `PaperUi.EmptyState`, which is muted Serif italic.
- Plate's empty Darkroom (`DarkroomArea.cs:50`), Pulp (`Components/EmptyState.cs:44`), Score, Folio (`MainPage.xaml.cs:499`) and Foreword (`MainPage.xaml:22`) all set voiced lines in Pinyon Script / InkBlue (`PaperUi.Voice`, `VoiceLabel`), as §14 requires.
- **Fix:** Draw the voiced line with `PaperUi.Voice`, and keep `EmptyState` for neutral copy.

### 21. There are three dog-ear implementations
- Native: `Codex.Ui.Android/DogEar.cs`.
- MAUI kit: `Storyworthtelling.Maui/Views/DogEar.cs` with `DogEarState`.
- Constitution: `Constitution.Android/Waiting/FoldView.cs` and `DogEarHost.cs`. These redraw the fold because the shared one always opens the shared Waiting page.
- There are also three Waiting pages: `Codex.Ui.Android/WaitingActivity.cs`, the kit's `WaitingPage`, and Constitution's `WaitingPageView`.
- **Fix:** Give the native `DogEar` an `onTurned` callback so Constitution can reuse it, and add a geometry parity test across all three.

---

## Lower impact

### 22. The MAUI kit has no pinned bottom bar, so each page builds its own
- Folio (`SetupPage.xaml:144–150`), Foreword (`DiagnosticsPage.xaml:33`) and Index each lay out their own `BoxView` and `Grid`. Native has `PaperUi.BottomBar`.
- **Placement differs too:** §7 says the primary goes right and a quiet Cancel goes left.
  - Primary on the **left**: `LoopDiagnosticsActivity`, the Score diagnostics screen, and Plate `EditorActivity.cs:160` (Send, then Delete).
  - Cancel on the **right**: Plate `ErrandActivity.cs:198–213`.
  - Cancel on the left, as specified: Score's `PlaylistEditor` (Cancel, then Save).
  - Score `EqualizerActivity.cs:101,132` has a **"Back"** button, which §9 forbids.
  - `LoopDiagnosticsActivity` and Score's diagnostics put four buttons in one bar; §7 allows at most two plus Cancel.
- **Fix:** Add a kit `PaperBottomBar`. Have the native `BottomBar` order its buttons by role rather than by argument order.

### 23. The build-number line has three formats
- **Native:** `PaperUi.BuildNumber` (owner medallion plus `BuildLine.Format`), in all six native apps.
- **Index and Foreword:** `Storyworthtelling.Updates.BuildLine.Format` (`Ledger SetupPage.xaml.cs:83`, `Foreword SetupPage.xaml.cs:35`).
- **Folio:** its own format, `"{Name} {Version} ({Build}) · {Package}"` (`SetupPage.xaml.cs:316`).
- **Fix:** Have Folio call `BuildLine.Format`.

### 24. Two native screens have no marker underline under the header
- `Flyleaf.Android/UnlockActivity.cs` and `Codex.Ui.Android/Diagnostics/CrashViewerActivity.cs` use `PaperUi.PageHeader`, a bare title. Every other screen uses `ScreenHeader`, which adds the underline §7 asks for.
- **Fix:** Switch both to `ScreenHeader(…, ResolvedMark)`.

### 25. Leftover components that DESIGN.md has retired
- The kit still styles `ActivityIndicator` (`Paper.xaml:334`), though no screen uses one. A spinner can quietly come back.
- `PaperUi.DestructiveButton` exists (`PaperUi.cs:105`) even though §7 says there is no destructive button.
- Folio's toggles name a verb ("Archive finished books", "Deliver reports to reMarkable", `SetupPage.xaml:80,99`), and Post has "Enable message sync (notification access)" (`SettingsArea.cs:91`). §14 says toggles name the thing.
- Foreword's test-page button shows "Sending…" inside the button (`SetupPage.xaml.cs:604`). §7 says no in-button status.
- **Fix:** Delete the spinner style, rename the toggles, and move in-button status onto the caption.

---

**Most consistent overall:** Constitution, Pulp and Plate. **Most divergent:** Folio's and Foreword's settings and diagnostics flows, which lean on `DisplayAlert`/`DisplayActionSheet`, and Post's older modal dialogs and toasts. Many of these fixes would be cheaper as shared pieces — a MAUI action card, bottom bar and diagnostics page, Pulp's time helpers, and shared sign-in and pairing copy — than as nine separate patches.
