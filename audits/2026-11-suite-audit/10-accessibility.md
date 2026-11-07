# Accessibility audit: Paper & Ink suite (read-only)

I didn't edit anything in the repo. The contrast figures come from a small WCAG 2.x script; it and its inputs are at `/tmp/claude-0/-home-user-Personal/b5742c71-73ba-5e61-9ad5-55fc006c45b7/scratchpad/c.py`. "Grain pixel" means Ink at the token `Grain.Alpha` (32/255) over Paper, which works out to `#E1DFD9`. That is the "darkest textured pixel" DESIGN.md §3/§17 says contrast must be measured against.

**Context that changes how severe things are:** DESIGN.md §17 says there is a "single owner", so colour-blind redundancy isn't required. That premise doesn't hold for Pulp (two adults plus children), Constitution (two adults), Index (her install) or Foreword (per-install owner). Treat every "single user" waiver below as invalid for those apps.

**What's fine:** all of these pass:
- Ink on Paper (12.1:1).
- Every role T3, T4 and T5 tier on Paper (≥5.3:1).
- PaperBright on the Action mark and deep tiers (5.65 and 11.7).
- Ink on every fill/cloth tab tier (6.7–7.3).
- InkBlue voice on Paper (10.1).
- Ink on washes (≥7.6).

The problems are concentrated in InkMuted, Hairline, a few identity tiers, and almost no semantics for screen readers.

---

## Critical

**1. Flyleaf's home and tray can't be used with TalkBack or Switch Access at all.**
- **App:** Flyleaf. **Where:** `FocusPhone/Flyleaf.Android/BookshelfView.cs:23` (`class BookshelfView : View`), with touch handled at :102 and :712–728.
- **Evidence:** the whole shelf (six home books, sibling covers, category spines, loose apps, the mantel clock, bookmark ribbons) is one Canvas. It has no `ExploreByTouchHelper` or `AccessibilityNodeProvider`, no `ContentDescription`, and is not focusable. Opening an app is a `GestureDetector` single tap. The only way into Settings is a long-press on the drawn clock (`BookshelfLayout.ClockKey`).
- **Why it's worse than a normal launcher:** `PolicyPlanBuilder` suspends every other home, so there is no fallback launcher.
- **Who it affects:** any blind or switch user of the locked phone. They can't launch any app or reach Settings.
- **Fix:** add an `ExploreByTouchHelper` subclass inside the view. It would make one virtual node per `PlacedItem` (label `art.Label(key)` plus ", has a notification" when the ribbon is lit). Each node gets `ACTION_CLICK` and `ACTION_LONG_CLICK`. Add a node for the clock that reads the time and has an "Open settings" custom action. Bookends become heading nodes. The geometry already exists (pure `ShelfScene`/`BookshelfLayout`), so the node bounds come for free.

**2. Screen-reader semantics are missing across both kits.**
- **Apps:** all nine.
- **Evidence from repo-wide grep (excluding tests and obj):**
  - `AccessibilityLiveRegion`, `AnnounceForAccessibility`, `AccessibilityHeading`, `StateDescription`, `ExploreByTouchHelper`, `AccessibilityDelegate`, `SemanticScreenReader`, `SemanticProperties.HeadingLevel`: **0 hits**.
  - `ContentDescription`: 17 sites, nearly all in Pulp and Constitution. Post, Score, Plate and Flyleaf have one between them (`PaperUi.cs:587` "Open app tray"). In MAUI the only description is on one Index chart.
- **What that means in practice:**
  - Screen headers (`PaperUi.Display`/`PageHeader`/`ScreenHeader`, MAUI `PaperPageHeader`) aren't headings, so heading navigation doesn't work.
  - Status sentences, which replace spinners by design (§6), are silent when they change.
  - Undo and notice dog-ears appear and disappear without being announced.
  - Errors written inline (§7) are never announced.
- **Fix, at kit level:**
  - `PaperUi.Display`/`Title`/`PageHeader` and `PaperPageHeader` set `AccessibilityHeading = true` / `SemanticProperties.HeadingLevel = Level1`.
  - Add `PaperUi.StatusLine(...)` with `AccessibilityLiveRegion = Polite`. The MAUI equivalent calls `SemanticScreenReader.Announce` from the status-line setter.
  - `DogEar.Show`/`ShowNotice` announces its hint ("Undo available: …").
  - Add a guard test, in the style of the existing `*Tests` guards, that fails on a clickable custom View or GraphicsView with no description.

---

## High

**3. InkMuted fails AA wherever it carries 13–14sp text.**
- **Measured:** InkMuted `#8A8578` is 3.46:1 on Paper, 3.10 on PaperDeep, **2.76 on the grain pixel** and 2.2–2.3 on washes.
- **Where it's used:**
  - Native: `PaperUi.Secondary` (14sp) and `PaperUi.Label` (13sp semibold, field labels and tab labels), `QuietButton`, `EmptyState`, and hint text (`PaperUi.cs:170`, `FilterField.cs:50`).
  - MAUI `Paper.xaml`: `SecondaryLabel` :106, `FieldLabel` :113, `EmptyStateLabel` :123, `QuietButton` :164, `ChipLabel` :300, `SegmentLabel`, `CardRowDescription`, `CardCaption` and `StatusLine` :327, plus `PlaceholderColor` on every Entry and Editor.
- **Why the build doesn't catch it:** neither 13sp nor 14sp regular counts as "large" (large means ≥18.66sp, or ≥14pt ≈ 18.66sp bold). The guard files it at 3.0 anyway: `FocusPhone/Codex.Tests/Design/DesignTokensTests.cs:42` has `[InlineData("InkMuted","Paper",3.0)] // large/secondary text`. It also tests the flat token rather than the textured pixel §3 requires.
- **Also off-spec:** §6 says the MAUI `StatusLine` should be in the In-progress hue, but it uses InkMuted.
- **Who it affects:** low-vision users, anyone outdoors, older readers.
- **Fix:** route secondary, label, placeholder and quiet text to **InkSoft** `#6B6759` (5.33 on Paper). Even InkSoft is 4.25 on grain, so either suppress grain under text or darken InkSoft to about `#5E5A4E`. Change the guard to 4.5 and measure on the grain pixel.

**4. Hairline is the only boundary for fields, checkboxes, chips and tabs, and it's far below 3:1.**
- **Measured:** Hairline `#E3DCC9` is 1.29:1 on Paper and 1.37 on PaperBright. PaperBright field fill vs Paper is 1.05. PaperDeep vs Paper is 1.12.
- **Where:**
  - `PaperUi.StyleField` (`PaperUi.cs:167–172`) and MAUI Entry/HairlineCard (`Paper.xaml:232`).
  - Unchecked `Checkbox` stroke (`Codex.Ui.Android/Checkbox.cs`, `stroke.Color = PaperTheme.Hairline`) and MAUI `PaperCheckbox.cs:38`.
  - `PhaseChip` (:286).
  - Binder tab strokes, including the page-edge line.
  - Index's Settings tab cloth is PaperDeep (1.12).
- **Who it affects:** users who can't find a text field or tell an empty checkbox from blank paper. This fails WCAG 1.4.11.
- **Fix:** add a `HairlineStrong` token at ≥3:1 on Paper (for example InkMuted `#8A8578`, 3.46) for control boundaries only: the field outline at rest, the checkbox outline and chip outlines. Keep the soft Hairline for decorative dividers.

**5. Undo dog-ear: small, silent, and on a fixed 6-second clock that deletes real data.**
- **Apps:** all.
- **Where:** `Codex.Domain/Design/DogEarGeometry.cs:18` (`SizeDp = 40`); `Codex.Ui.Android/DogEar.cs:234–263` (`PostDelayed(... UndoWindowMs)`); MAUI `Views/DogEar.cs:119–132` (40×40 `GraphicsView` with no SemanticProperties at all); Constitution `NoticeBoard.cs:29`; Post commits deletes at expiry (`MailboxArea.cs:1388`).
- **Measured:** the target is 40dp against the 48dp spec. The window ignores `AccessibilityManager.GetRecommendedTimeoutMillis`, which users raise under "Time to take action". Nothing announces the fold, and it doesn't pause while it has accessibility focus.
- **Why it matters:** in Post, expiry deletes the message at the source. In Plate (the only copy) and Flyleaf (block), the dog-ear is the *only* recovery, because there are no confirm dialogs by design. This fails WCAG 2.2.1 and 2.5.8.
- **Fix, in the kit:**
  - Keep the 40dp drawing but pad the touch target to 48dp (TouchDelegate, or a 48dp container).
  - Set the window to `max(6s, RecommendedTimeout(6000, FLAG_CONTENT_CONTROLS|FLAG_CONTENT_TEXT))`.
  - Pause the timer while the fold has accessibility focus.
  - Announce the hint.
  - Give the MAUI DogEar `SemanticProperties.Description` and `Hint`.
  - Optionally add an "Undo last" entry on the Waiting page.

**6. Custom controls don't expose their state to TalkBack.**
- **Where:**
  - `Checkbox` (`Checkbox.cs`) is a bare `View` with no checkable info. Callers in Post, Flyleaf and `PaperListAdapter.cs:83` add no text. Only Pulp and Constitution work around it by hand (`SettingsViews.cs:141`).
  - Native `BinderTabs` (`BinderTabs.cs:110–131`) never sets `Selected`. The active tab is shown only by its paper fill and hue ink.
  - Segmented cells (`PaperUi.cs:345–362`) show selection by a wash only.
  - Disabled is applied as `Alpha = 0.45f` plus `Clickable = false`, never `Enabled = false` (`PaperUi.cs:354`, Pulp `SettingsViews.cs:144`, `AddToPicker.cs:157`). TalkBack therefore never says "disabled".
  - MAUI `BinderTabs` builds tabs from `Border` plus `TapGestureRecognizer` (`Views/BinderTabs.cs:158–213`), so they have no button role and no selected state.
- **Fix:**
  - Native `Checkbox`: override `OnInitializeAccessibilityNodeInfo` with `Checkable` and `Checked`, or wrap it in a `CompoundButton`.
  - Tabs and segments: set `Selected`, `ClassName = "android.widget.Tab"`/`RadioButton`, and a `CollectionItemInfo`.
  - Disabled: set `Enabled = false` alongside the alpha.
  - MAUI tabs: `SemanticProperties.Description = "{label} tab{, selected}"`.

**7. Plate's Darkroom shows "chosen" by a teal wash and nothing else, on the only copy of each photo.**
- **Where:** `FocusPhone/Plate.Android/DarkroomArea.cs:107–150`.
- **Evidence:** a chosen cell gets `Wash(ProgressMark)`, a 30% overlay on the photo. There's no check mark, the thumbnails have no `ContentDescription`, and the chosen state isn't exposed. A cull then leads to send or delete.
- **Who it affects:** colour-blind users, and anyone on TalkBack, who can't tell or hear which items are chosen before a bulk delete. The camera preview and editor ImageViews (`EditorActivity.cs:104`, `ErrandActivity.cs:155`) are unlabeled too.
- **Fix:** draw a kit check badge (the `Checkbox` glyph) on chosen cells. Set the description to "Photo, {relative time}{, chosen}" and expose a checkable state.

**8. The MAUI apps' canvas views are invisible to screen readers.**
- **Folio:** the shelf (`Storyboarder/Folio.Android/Pages/ShelfPage.cs:104–160`) is one `GraphicsView` per row, and a tap position is mapped to a book with `ShelfRow.At(x)`. TalkBack users therefore can't open any shelved book. Series rename is reachable only by tapping a plain `SecondaryLabel` (:117–137). `DeskDrawable` and `ReadinessChartDrawable` have no description either.
- **Index:** `SavingsGrowthChart` and `SnowballSmallMultiplesChart` have no description. Only `SnowballStackedBarsChart.cs:50` has one.
- **Constitution:** `BodyChartView.cs:58` says only "A chart of the readings".
- **Fix:** Folio should render an accessible list of books (one invisible-but-focusable Button per book), or use a CollectionView with the canvas as background. Each chart should get a one-sentence summary through `SemanticProperties.Description`, built from the same domain copy `SnowballHistoryCopy` uses.

**9. Score's tempo Dial can't be operated with assistive tech.**
- **Where:** `Codex.Ui.Android/Dial.cs`.
- **Evidence:** a custom View with touch-only rotation, no `RangeInfo`, no `ACTION_SCROLL_FORWARD/BACKWARD`, and no description of its value. It's used for the workout tempo, which is a "while moving" control.
- **Fix:** report `RangeInfo(RANGE_TYPE_INT, min, max, value)`, handle scroll actions and D-pad keys, and set the state description to "{n} beats per minute".

**10. Binder tab labels clip at 1.3× font scale, which DESIGN.md §17 forbids.**
- **Where:** native `BinderTabs.cs:121` (`SetSingleLine(true)`, MatchParent inside a fixed 66dp width, no ellipsize); MAUI `Views/BinderTabs.cs:179` (`LineBreakMode.NoWrap`, width 66).
- **Estimate:** Source Sans Semibold at 13sp × 1.3 ≈ 16.9sp. An 8-letter label ("Podcasts", "Settings", "Calendar", "Accounts", "Snowball", "Discover") is about 70dp, wider than the 66dp tab. When the strip shrinks to the 48dp minimum height, the glyph plus label overflows vertically at about 1.6×.
- **Fix:** allow two lines with autosize (`TextViewCompat.SetAutoSizeTextTypeUniformWithConfiguration`, 9–13sp), or let the gutter widen with `fontScale`. Add a test at fontScale 1.3 and 2.0.

**11. Two active tab labels fail contrast.**
- **Foreword:** the Today tab's active label is `ForewordSunrise #CA662D` on Paper, **3.63:1**, at 13sp (`Foreword/Foreword.Android/Pages/ForewordAreas.cs:51,67`; MAUI tabs tint the label `area.Hue`, `BinderTabs.cs:165`). The same hue is 2.89 on grain. The 47° Foreword row has no text/deep tier at all (DESIGN.md §3 table).
- **Index:** the active Settings tab is `InkMuted` on Paper, **3.46** (`Ledger/Ledger.Android/Pages/LedgerAreas.cs:118`). Every other app uses `ResolvedMark` (5.53) here.
- **Fix:** give Foreword a text tier of about `#9C4A1C` (≥5:1) and use it for the active ink. Index Settings should use `ResolvedMark` with `ResolvedFill`, like every other app.

**12. Error styling in the MAUI kit is pine, the colour that means "action".**
- **Where:** `Paper.xaml` `ErrorLabel` :137–140 (AccentDeep `#174025`), `GroupLabelProblem` :226 (`Accent`), `ErrorChip` :293 (`Accent` stroke). `Accent` = ActionMark `#38734A` (:30).
- **Why it matters:** §8 says errors use the Problem-rose border. Here an error looks like a commit or link, so colour no longer says "something is wrong" at all. The text is still readable (11.7:1).
- **Fix:** point these styles at `ProblemText`/`ProblemMark`, and keep the "what happened plus what to do" text.

---

## Medium

**13. Canvas text is sized in dp, not sp, so it ignores the font-size setting.**
- **Where:** `BookshelfView.cs:434, 493, 637` (`13 * density`, `12 * density`); `CoverView.cs:166, 172`; `BookendGlyphPainter.cs:123`; Pulp `SportsBadgeView.cs:92`; MAUI chart labels (fixed `fontSize` in GraphicsView drawables).
- **Who it affects:** low-vision users who raise font size. Spine titles, cover subtitle and chart axes stay at 12–13dp.
- **Fix:** use `PaperTheme.Sp(context, …)` (which multiplies by `ScaledDensity`), or `TypedValue.ApplyDimension(Sp)`. Cap to the available rect, as is done already.

**14. Book spines are too narrow to hit, and their titles fail contrast on light cloths.**
- **Width:** `Codex.Domain/Apps/ShelfScene.cs:78–84`: `Slim 22dp`, `Regular 30dp`, `Wide 40dp`, with a 2dp gap. Only `Broad` (52) meets 48dp. Spine width follows how often an app is used (`SpineSizing`), so the apps used least get the smallest targets.
- **Contrast:** `DrawVerticalTitle` (:488) always paints `PaperBright`. On the hashed cloth `Flyleaf.Warm #C9803A` that's **3.17:1** at ≤13dp (a halo helps but doesn't count toward AA).
- **Fix:**
  - Set the virtual-node and hit-test width to at least 48dp, centred on the spine; a tap between two slim spines goes to the nearer one.
  - Pick the spine ink with the same luma-threshold rule `BookClothPalette` already uses for covers (`ConstitutionClothInvertsToInk`).

**15. Notification bookmarks (and fold hues) are colour-only.**
- **Where:** `BookshelfView.cs:607–628` draws a 10×~20dp ribbon in the role hue. `Codex.Domain/Apps/NotificationBookmark.cs` maps Problem, Reference, Resolved and Progress.
- **Measured:** Problem mark vs Progress mark luminance contrast is **1.10:1** (fills 1.05), so they are indistinguishable in greyscale and for protan/deutan users. The ribbon isn't announced or tappable. The single dog-ear fold also shows only the most serious hue.
- **Who it affects:** colour-blind users and every TalkBack user (see finding 1). Partner and child phones mean the §17 waiver doesn't apply.
- **Fix:** add a shape cue per role: Problem gets a notched-V tail with a short "!" bar, Progress gets a plain square end, Reference a dot. Put "{role word} notification" into the node description from finding 1.

**16. Pinyon Script copperplate is used in children's seats.**
- **Where:** `Pulp/Pulp.Android/Components/EmptyState.cs:44` (`PaperUi.Voice`) and the child greeting via `ChildVoice`/`ChildWatchingPage.cs:91, 274`. The voice style is 26sp script in all apps (`PaperUi.cs:56`, `Paper.xaml` `VoiceLabel`).
- **Who it affects:** early readers can't read copperplate. It's also hard for people with dyslexia or low vision, and connected script degrades badly at large font scales.
- **Fix:** for any seat whose reader is a child profile, or when fontScale ≥ 1.3, set the voice in Serif Italic (the §4 "invitation" face) in InkBlue. Expose the line as plain text to TalkBack. Record this as a §14 exception.

**17. The ActionCard overlay isn't modal for assistive tech.**
- **Where:** `Codex.Ui.Android/ActionCard.cs:33–70`. Folio has its own copy in `Folio.Android/Pages/ActionCard.cs`.
- **Evidence:**
  - The card is added as a child of the page's `FrameLayout`. Focus isn't moved into it, the content behind stays reachable, and no pane title is set.
  - The full-screen scrim is clickable with no label ("Button, double-tap to activate").
  - `card.Click += {}` makes the whole card announce as a button.
  - Its heading and subtitle are single-line and truncate (:79–87), so long email subjects and titles are cut off.
- **Fix:**
  - Set `AccessibilityPaneTitle` and `ImportantForAccessibility = NoHideDescendants` on the host's other children while the card is open.
  - Move focus to the heading and set the scrim's description to "Dismiss".
  - Use a touch interceptor instead of the empty click handler.
  - Let headings wrap to 2 lines.

**18. Tiny or unlabeled glyph buttons.**
- **FilterField clear:** `FilterField.cs:56` is a `TextView` "×" in InkMuted. Padding is 8/8/0/8, so it's about 18×37dp, and TalkBack reads "multiplication sign".
- **Reorder buttons:** "↑"/"↓" `QuietButton`s at `Score.Android/PodcastsArea.cs:545, 550`, `ManageSubscriptionsActivity.cs:131, 136` and `Flyleaf.Android/LauncherSettingsActivity.cs:614, 618`. `ButtonBase` clears minWidth (`PaperUi.cs:131–137`), so they come out about 40dp wide. They're read as "up arrow" with no item named, and their text is InkMuted.
- **Fix:** add a kit `PaperUi.IconButton(glyph, label)` that is 48×48 minimum with `ContentDescription` "Move {item} up" and "Clear search". Pulp's `StackPileView.Small` (:218) already does this correctly and can serve as the model.

**19. Index's Plaid setup fields have a fixed 44dp height and no labels.**
- **Where:** `Ledger/Ledger.Android/Pages/SetupPage.xaml:40, 46, 77`.
- **Evidence:** each Entry sits in a Border with `HeightRequest="44"`, below 48, and it clips at large font scales. The visible "Client ID"/"Secret" Labels aren't linked, so TalkBack reads the placeholder ("6f1a…").
- **Fix:** use `MinimumHeightRequest=48` and set `SemanticProperties.Description` to the label text. Better still, add a kit `PaperField(label, entry)` that does both.

**20. Long-press is the only route to item actions (§9 "no kebab").**
- **Native list rows:** use `LongClick`, which TalkBack exposes as double-tap-and-hold, but it can't be discovered.
- **Canvas and MAUI:** the gesture isn't exposed at all (findings 1 and 8).
- **Fix:** add a named `AccessibilityAction` per row ("Actions for {item}") in `PaperListAdapter` and the row factories. In MAUI, use `SemanticProperties.Hint` plus a custom action.

**21. The lockdown removes accessibility entry points.**
- **What's fine:** I found no explicit suspension of `com.google.android.marvin.talkback`, the Accessibility Menu or Live Transcribe, and `SetPermittedAccessibilityServices` is never called, so all services stay permitted. Suspension targets browsers, other homes and never-visible packages (`PolicyPlanBuilder.cs:30–37`).
- **Status bar:** `DisableStatusBar: true` removes Quick Settings, so the Color inversion, Extra dim, Live Caption, Accessibility shortcut and Live Transcribe tiles are gone. Flyleaf's Connectivity panel deep-links Wi-Fi, Bluetooth and hotspot but not `Settings.ActionAccessibilitySettings` (`LauncherSettingsActivity.cs:218–297`).
- **Locked hand-off:** `LockTaskFeatureFlags.Session = Home | Keyguard` (`Codex.Domain/Policy/LockedSession.cs:25`) leaves `GlobalActions` off (no power menu) and `SystemInfo`/`Notifications` off. It isn't verified whether the nav-bar accessibility button survives lock-task mode.
- **Install watcher:** any app that handles http VIEW intents, or has an Unknown rating, can be blocked or quarantined. Nothing pins accessibility apps as safe.
- **Fix:**
  - Add the accessibility packages (`com.google.android.marvin.talkback`, `com.google.audio.hearing.visualization.accessibility.scribe`, and the Accessibility Menu, which ships in the same suite) to `PolicyContext.AllowlistedPackages`.
  - Add an "Accessibility settings" row to Flyleaf Settings → Connectivity.
  - Test that the accessibility shortcut (both volume keys) works in a locked session. Consider adding `GlobalActions` to `Session`.

**22. The generated PDFs aren't accessible documents.**
- **Apps:** Foreword, Folio, Index worksheet, Constitution digest, Plate scans.
- **Where:** `Storyworthtelling.Paper/VectorPdfSink.cs:43`.
- **Evidence:**
  - `SKDocumentPdfMetadata` sets only `EncodingQuality`: no Title, no Language, no structure tree. SkiaSharp exposes no tagging, so there's no reading order and Folio's scene illustrations, Foreword's maker's mark and the charts have no alt text.
  - Plate scans are image-only PDFs even though OCR runs on the device.
  - Smallest printed type is `PaperTypeScale.Label = 9.75pt` on A4. On a 10.3" reMarkable an A4 page renders at about 0.71×, so roughly 6.9pt physical; margin notes in Folio go down to that size.
- **Impact:** low on the tablet itself, which has no screen reader. It matters when the same files go to Drive or a desktop reader, and for low-vision readers on e-ink.
- **Fix:**
  - Set Title, Author and Language in metadata.
  - Keep the vector text layer, which already makes the text extractable.
  - Add an invisible OCR text layer to Plate scans.
  - Raise the document Label and Mono floor to about 11pt, or a print ratio of about 0.85 for the smallest rungs.

---

## Low

**23. Reduced motion relies entirely on system animator scales.** There's no check of `ANIMATOR_DURATION_SCALE` or `AreAnimatorsEnabled` anywhere (0 grep hits).
- **What's fine:** the XML page-slides (`PaperTransitions.cs`) and `ViewPropertyAnimator` fades follow "Remove animations" automatically, and MAUI's ticker does too. No page-curl or bloom is implemented yet.
- **Gap:** §6 promises the big movers are dropped while small fades stay. The platform setting zeroes everything, and anything later drawn frame-by-frame on a Canvas won't follow it at all. Flyleaf's cover hello is hard-coded at 700 ms dwell plus a 450 ms fade (`HomeActivity.cs:128, 134`). That isn't long enough to read the voiced line, and it's also a raw literal outside the tokens.
- **Fix:** add a `PaperMotion.Reduced(context)` helper (checking `ValueAnimator.AreAnimatorsEnabled()` and `TRANSITION_ANIMATION_SCALE`) that every future curl or bloom must consult. Hold the voiced cover line until tapped, or for `RecommendedTimeout`.

**24. Disabled states are very faint.** Ink at 45% on Paper is 2.50:1 and a disabled primary button is 1.97:1. WCAG exempts disabled controls, but combined with the missing `Enabled=false` (finding 6) a user can't tell inert from faint. **Fix:** dim to about 60%, keep Ink text, and set `Enabled`.

**25. Flyleaf's clock isn't described.** `PaperClockView.cs` and the mantel clock are the home's only time display, since the status bar is disabled. There's no `ContentDescription` with the time and no live update. **Fix:** set a description like "7:42 pm", updated each minute, as part of the finding 1 node set.

**26. Hit-test regions don't scale with font size.** `BinderTabLayout` shrinks tabs to the 48dp minimum at fixed geometry (good). Waiting-page role bars (4dp, `WaitingView.cs:224`) and colour bars are decorative and correctly carry text, so no action is needed there. Generally, add the fontScale 1.3 and 2.0 snapshot tests §17 requires for ActionCard, AccordionPicker (single-line value, `AccordionPicker.cs:70`), `CoverGrid` titles (:98, :109) and `NowPlayingBar` (:40). All of them truncate rather than wrap.

---

### Highest-leverage kit fixes, in order
1. An `ExploreByTouchHelper` for `BookshelfView`, plus an accessible book list for Folio's shelf.
2. Headings, live-region status lines and announced dog-ears in `PaperUi`/`DogEar` and `Paper.xaml`/`PaperPageHeader`.
3. Retoken InkMuted text and the Hairline control boundary, and fix the contrast guard (4.5:1, measured on the grain pixel).
4. A dog-ear with a 48dp target, `GetRecommendedTimeoutMillis` and pause-on-focus.
5. Expose checked/selected/disabled state in `Checkbox`, `BinderTabs` and the segmented control.
6. Wrapping or autosizing binder tab labels.
7. An accessibility allowlist and an Accessibility-settings deep link on the locked phone.
