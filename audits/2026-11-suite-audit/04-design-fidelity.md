# Paper & Ink audit: where the UI code departs from DESIGN.md and the in-repo mockups

I found 32 places where the code does not follow the design. No files were changed. The scratch extractor is `/tmp/claude-0/-home-user-Personal/b5742c71-73ba-5e61-9ad5-55fc006c45b7/scratchpad/txt.py`. All examples below are generic and contain no personal data.

**The three biggest problems:**
1. **Errors show in green in every MAUI app.** In the MAUI kit, `Accent` is now Action pine (green), but every error and problem style still points at it.
2. **Toasts and dialogs are used about 70 times**, although §7 bans them.
3. **Type size has no guard in eight of nine apps.** Only Pulp checks for literal font sizes, so Folio carries about 25 sizes that are not on the type ladder.

**Mockup scope:** only Index (`Ledger/design`, 17 mockups) and Pulp (`Pulp/design`, 15 mockups) have mockups in the repo. Folio's and Foreword's mockups are external artifact links. Constitution has design notes only. The screens I compared were mostly faithful: Pulp's Settings and empty states, Index's Accounts, Snowball and action cards.

---

## A. What the leak-guard tests cover, and what they skip

| Guard | What it scans | What it skips |
|---|---|---|
| `FocusPhone/Codex.Tests/Design/HexLiteralLeakTests.cs` (Flyleaf, Score, Post, Plate, the kit, and Constitution through `HexLiteralScan`) | `*.cs` with a quoted `"#RRGGBB"`. Constitution also gets `*.xaml`. | All `*.xml` resources (area glyphs, colors, animations). Non-hex color construction (`new Color(r,g,b,a)`, `Color.Argb`). **No type-size, spacing, radius or motion check.** |
| `Pulp/Pulp.Tests/Design/HexLiteralLeakTests.cs` | `*.cs` for hex **and** type size. This is the only type-size guard in the suite. | `*.xml`, spacing, radius, motion. |
| `Ledger/Ledger.Tests/Design/HexLiteralLeakTests.cs` | `*.cs`, `*.xaml` | Allowlists `LedgerAreas.cs`, `Ledger.Domain/Design/MaterialTokens.cs`, `Resources/values`, `drawable`, and the whole `Ledger.Tests/Design` folder. No type size, so `FontSize = 22` and `Colors.Black.WithAlpha` both pass. |
| `Storyboarder/Folio.Tests/Design/HexLiteralLeakTests.cs` | `*.cs`, `*.xaml` | No type size and no letterspacing in code. About 25 `FontSize` literals and 2 `CharacterSpacing` values pass. |
| `Foreword/Foreword.Tests/Design/DesignTokenTests.cs` | Hex in `*.cs`/`*.xaml`, plus no `<Style>`/`<Color>`/`<x:Double>` declared in the app's XAML | Sizes or styles set in C# code-behind. |
| `Storyworthtelling.Maui.Tests/PaperKitTests.cs` | Hex in the kit's own views; `NothingIsLetterspaced` | `NothingIsLetterspaced` reads **only `Paper.xaml`**, so letterspacing in a head's code is never checked. |
| `Storyworthtelling.Paper.Tests/HexLiteralLeakTests.cs` | `*.cs` hex | — |

Nothing in the suite checks spacing, corner radius, motion durations, toasts or dialogs, `Colors.Black`/`Colors.White`, or reduced motion.

---

## B. Findings

### Color and role misuse

1. **High — MAUI error, problem and warning styles are pine, not Problem rose.**
   - **Where:** `Storyworthtelling.Maui/Resources/Styles/Paper.xaml`. `Accent` (L30) is `#38734A`, which is Action pine. These styles use it:
     - `ErrorLabel` (L137) uses `AccentDeep`.
     - `GroupLabelProblem` (L226) uses `Accent`.
     - `ErrorChip` (L293) has an `Accent` stroke.
     - `ChipLabelError` (L320) uses `AccentDeep`.
     - `WarningCard` (L277) has a 1dp `Accent` border all round.
   - **Design says:** §8 Error is "Problem-rose border + helper text". §7 says a phase chip with a problem "takes the Problem chip", and a warning card has a "3dp Action left rule".
   - **Effect:** Folio and Index Setup errors, Index PlaidLink errors and Foreword's "Needs you" all show in success-green.
   - **Fix:** add `ProblemText` and `ProblemDeep` to `Paper.xaml` and re-point these styles at them. Draw the warning card as a 3dp left rule. Retire the `Accent` key.

2. **High — The primary button is filled with the mark tier and changes color when pressed** (native and MAUI).
   - **Where:** `FocusPhone/Codex.Ui.Android/PaperUi.cs:84-89` with `PressableFill` at L637 (rest = Accent, pressed = AccentDeep). In `Paper.xaml`, the implicit `Button` style uses `Accent` as its background.
   - **Design says:** §3: "Filled primary buttons use the deep tier." §8: pressed means "no color change".
   - **Fix:** fill with ActionDeep at rest, and show pressed as an inset hairline.

3. **High — A "Destructive" button exists and is used, though §7 says there should be none.**
   - **Where:** the kit's `PaperUi.DestructiveButton` (L105, pine outline) and the `DestructiveButton` style in `Paper.xaml` (L173).
   - **Used in:** `Post/MailboxArea.cs:309,316`, `Post/MailReaderActivity.cs:78`, `Plate/EditorActivity.cs:161`, `Plate/DarkroomArea.cs:274`, `Plate/CameraArea.cs:115`, `Score/ManageSubscriptionsActivity.cs:139`, `Constitution/Settings/PersonSheetView.cs:84`, `Ledger SetupPage.xaml:109`.
   - **Effect:** "Delete" looks exactly like a pine action button.
   - **Fix:** remove both. Use a Secondary or Quiet button followed by an undo dog-ear.

4. **Medium — The MAUI status line is muted ink, not In-progress teal.**
   - **Where:** `Paper.xaml` `StatusLine` (L327) uses `InkMuted`.
   - **Design says:** §6 and §7: "one Secondary sentence in the In-progress hue".
   - **Effect:** every Folio, Foreword and Index status line is grey. Index's PlaidLinkPage hand-codes `ProgressText` instead (L282).
   - **Fix:** set the style's color to ProgressText.

5. **Medium — The kit still ships a spinner style.**
   - **Where:** `Paper.xaml` has `<Style TargetType="ActivityIndicator">` in Accent.
   - **Design says:** §6: "The spinner is retired."
   - **Fix:** delete it, and add a guard against `ActivityIndicator`.

6. **Medium — Index has a second, retyped copy of the role hexes.**
   - **Where:** `Ledger/Ledger.Android/Pages/LedgerAreas.cs:56-75` hand-types `#18717C`, `#9BC7CD`, `#805F00` and others. The guard allowlists this file.
   - **Design says:** §19: "a typed expected value is the failure mode."
   - **Fix:** read them from `PaperColorTokens` and the identity tokens instead.

7. **Medium — Index's Settings tab is a different grey from the rest of the suite.**
   - **Where:** `LedgerAreas.cs:118` uses `InkMuted`/`PaperDeep`, and Index's own `AreaHueTests:69-76` locks that in. Folio, Foreword, Score, Post, Plate, Pulp and Constitution all use `ResolvedMark`/`ResolvedFill`.
   - **Design says:** §9: Settings is "always the neutral gray … so the parts that are the same everywhere look the same."
   - **Fix:** switch to the Resolved tiers and update the test.

8. **Medium — Index and the MAUI views dim with black instead of paper.**
   - `Ledger/Views/LoanActionCard.cs:50` and `RecategorizeActionCard.cs:59` use a `Colors.Black.WithAlpha(0.35f)` scrim. The other Index action card, `ActionCardHaze`, correctly uses a PaperDeep haze.
   - `BudgetPage.xaml.cs:515` draws a hairline as `Colors.Black.WithAlpha(0.08f)`.
   - `PostItView.cs:118` uses black for the adhesive strip.
   - **Design says:** §5: "Paper does not go dark"; §7: action cards sit over a PaperDeep haze.
   - **Fix:** route everything through `ActionCardHaze`, and use the `Hairline` token.

9. **Low — Folio's book cloths use semantic role colors as decoration.**
   - **Where:** `Storyboarder/Folio.Android/Pages/BookCloth.cs:34-44` uses Problem, Caution, Progress and Action tiers as spine colors.
   - **Design says:** §1.4: roles are "never decoration".
   - **Fix:** the file already says it is provisional. Register spine cloths as material tokens, the way the bronze already is.

10. **Low — The kit's `PaperPageHeader`/`MarkerRuleDrawable` default ink is `Colors.Black` (#000), not the `Ink` token.**
    - **Where:** `Storyworthtelling.Maui/Views/PaperPageHeader.cs:13`, `Views/BinderTabs.cs:36`.
    - **Fix:** default to the `Ink` token.

### Typography

11. **High — Flyleaf's voiced hello is not in the voice hand.**
    - **Where:** `FocusPhone/Flyleaf.Android/HomeActivity.cs:94-96` builds it with `PaperUi.Secondary` (Sans, 14sp) in the Paper color.
    - **Design says:** §4 and §14: voiced lines are Pinyon Script in ink-blue.
    - **Fix:** use `PaperUi.Voice`. It needs a paper-colored ground to sit on.

12. **Medium — Post's one voiced seat (the empty Mailbox) is drawn as a plain empty state.**
    - **Where:** `Post.Android/MailboxArea.cs:156` uses `PaperUi.EmptyState`, which is Serif Italic in muted ink, even after the voice has rewritten the line (`Codex.Domain/Post/EmptyMailbox.cs` says "the voice then rewrites whatever was drawn").
    - **Compare:** Pulp's `Components/EmptyState.cs` does this correctly: Pinyon when voiced, italic only for the fallback.
    - **Fix:** follow Pulp's pattern.

13. **High — Folio has about 25 off-ladder font sizes, plus capitals and letterspacing.**
    - **Sizes:** 11, 11.5, 12, 12.5, 15, 17 in:
      - `BookPage.cs` L213-346
      - `DeskPaper.cs` L28, 57-58
      - `ActionCard.cs` L87-91, L128
      - `RenameCard.cs` L26, 61, 68
      - `MainPage.xaml.cs` L473-488
    - **Capitals and letterspacing:** `DeskPaper.cs:26-29` and `BookPage.cs:302-305` apply `ToUpperInvariant()` with `CharacterSpacing` 0.09 and 0.1.
    - **Design says:** §4: "Sizes are never invented ad hoc." §14: sentence case. The kit forbids letterspacing.
    - **Fix:** use `HeadingLabel`, `SecondaryLabel` and `FieldLabel`. Add a FontSize and CharacterSpacing guard to `Folio.Tests`, copied from Pulp's.

14. **Medium — Index's Budget page uses an invented size and capitals.**
    - **Where:** `BudgetPage.xaml.cs:279` sets `FontSize = 22`, which is not on the ladder. L269 renders "MONTH · TRACKING ONLY" in capitals.
    - **Mockup conflict:** `Ledger/design/mockup-07.html` itself draws `text-transform:uppercase` 17 times and `letter-spacing` 20 times. The mockup contradicts DESIGN.md §14 and the kit's no-letterspacing rule.
    - **Fix:** use sentence case and the Title (28) or Heading (18) size. Flag the mockup–bible conflict to the owner.

15. **Medium — Flyleaf's cover and shelf ignore the type scale.**
    - **Cover:** `CoverView.cs:162-174` caps the title at `44*density` in SemiBold, though CoverTitle is 72. It prints "FLYLEAF" and "STORYWORTHTELLING" in capitals with `LetterSpacing = 0.25f`.
    - **Shelf:** `BookshelfView.cs:434` and `:637` use `13*density` and `12*density`.
    - **Why it passes:** FocusPhone has no type-size guard.
    - **Fix:** use the `TypeSp` tokens. Port Pulp's TypeSize regex to `Codex.Tests`.

16. **Low — Some settings headers and labels use the wrong case or ink.**
    - `Score.Android/SettingsArea.cs:247` puts group headings in capitals with `ToUpperInvariant()`.
    - `Post.Android/SettingsArea.cs:229,246` re-ink field labels to full `Ink`.
    - **Design says:** the Label role is muted (kit comment, §4), and §14 requires sentence case.

### Spacing and shape

17. **Medium — Folio uses near-square corners.**
    - **Where:** `DeskPaper.cs:21,40,51`, `FlatBook.cs:52` and `BookPage.cs:283` use corner radius 2–3. `DraftDiffPage.xaml.cs:85` uses 8.
    - **Design says:** §5: radius 10–16, inputs, buttons and cards at 12.
    - **Fix:** use the 12 token, or register paper-material radii as tokens if the near-square look is meant.

18. **Low — About 105 spacing values in the MAUI apps are off the 4/8/12/16/24 scale.**
    - **Examples:** 9,3; 18,14; 11; 13; 5; 6.
    - **Most affected:** `Ledger SetupPage.xaml` (14), `Folio BookPage.cs` (8), `Ledger MainPage.xaml.cs` (8), Folio `ActionCard`, Ledger `RecategorizeActionCard`.
    - **Design says:** §5 spacing scale.
    - **Fix:** use `DesignTokens.Space.*`, and add a spacing guard.

19. **Low — Button padding differs between the spec and both kits.**
    - §7 says 18×11. Native `PaperUi.ButtonBase` uses 16×12; MAUI `Paper.xaml` uses 16,10.
    - **Fix:** pick one, add it as a token, and match both kits to it.

20. **Low — Tab glyph size is 24 in MAUI but 26 in native and in §9.**
    - **Where:** `Storyworthtelling.Maui/Views/BinderTabs.cs:78` uses `GlyphDp = 24`; native `Codex.Ui.Android/BinderTabs.cs:117` uses a literal 26.
    - **Fix:** one `GlyphDp = 26` token, shared through the parity guard.

### Motion

21. **Medium — There is no Ink (320ms) motion token, and action cards don't use the Ink reveal.**
    - **Where:** `Codex.Domain/Design/DesignTokens.cs` `Motion` and the Folio and Index Motion classes have Quick and Standard only. `Codex.Ui.Android/ActionCard.cs:69` and Constitution `WorkoutCardView.cs:43` fade in at `StandardMs`.
    - **Design says:** §6 has three durations including Ink; §7 says the action card is "animated in with the Ink reveal".
    - **Fix:** add `InkMs = 320`.

22. **Medium — Page transitions run 240ms, which is not one of the three durations.**
    - **Where:** `Codex.Ui.Android/Resources/anim/paper_slide_in.xml` and `paper_slide_out.xml` (the back transitions are 200).
    - **Also:** they fade and scale, with no page-curl (§6).
    - **Also:** Pulp's `FirstRunActivity` and `VaultActivity` never call `PaperTransitions.Apply`.
    - **Fix:** use the Standard duration (200) in both slide files.

23. **Medium — The Flyleaf cover hand-off is a 450ms fade.**
    - **Where:** `Flyleaf.Android/HomeActivity.cs:134`. The delays at L124 and L128 are also literals.
    - **Design says:** §6: the cover hands off with a page-curl at a token duration.
    - **Fix:** use a token duration, and move toward the curl.

24. **Medium — The MAUI apps have no page transitions, and the MAUI dog-ear has no fold-in.**
    - **Tabs:** `FolioBinderPage.cs:290-294`, `LedgerBinderPage.cs:329-333` and Foreword's `BinderPage.cs:144-148` all switch areas with `animated:false`.
    - **Dog-ear:** `Storyworthtelling.Maui/Views/DogEar.cs` has no fade-in or fold-in. The native one fades in over 200ms.
    - **Design says:** §6: "every navigation is a page turn"; §13: the dog-ear folds in.

25. **Medium — Reduced motion is not supported anywhere.**
    - No code reads `AnimatorDurationScale` or any accessibility setting.
    - **Design says:** §6: reduced motion drops the slides, curl and bloom.

### Components drawn ad hoc

26. **High — Toasts and dialogs are used about 70 times**, against §5–§7: "Toasts and success alerts are banned", "never a confirm dialog", and pickers expand inline.
    - **Success toasts:**
      - `Post/EmailComposeActivity.cs:189` shows "Sent." as a toast.
      - `Codex.Ui.Android/Diagnostics/CrashViewerActivity.cs:73`, `Post/DiagnosticsActivity.cs:154` and `Score/AudioDiagnosticsActivity.cs:98` show "Copied to clipboard."
    - **Error toasts:** `Score/MusicLibrary.cs:307,320` (raw `e.Message`); `Score/ManageSubscriptionsActivity.cs:182,211,219`; `Flyleaf/HomeActivity.cs:349`.
    - **AlertDialogs (native):**
      - Post `MailboxArea.cs` (5) and `RolodexArea.cs:320`
      - Flyleaf `UnlockActivity.cs:306,329,471` and `LauncherSettingsActivity.cs:348,716` (the rename uses a raw `EditText`)
      - Score `ManageSubscriptionsActivity.cs:169,232`
    - **MAUI DisplayAlert / DisplayActionSheet / DisplayPromptAsync:**
      - Folio `SetupPage.xaml.cs` (14) and `NewProjectPage.xaml.cs` (7)
      - Foreword `MainPage.xaml.cs` (8, with title-case "View & Copy" and "Delivery Log")
      - Index `SetupPage.xaml.cs:402,526,555,559` and `PlaidLinkPage.xaml.cs:538`
    - **Fix:** use inline status lines, `ActionCard`, `AccordionPicker` and the dog-ear. Add a guard that forbids these APIs.

27. **Medium — Action cards are re-implemented per app; the MAUI kit has none.**
    - **Separate copies:** Folio `Pages/ActionCard.cs`; Index `ActionCardHaze`, `LoanActionCard`, `RecategorizeActionCard` (and the latter two differ from each other); Constitution `WorkoutCardView.cs` and `CuisineCardView.cs`, each with its own `HazeAlpha` and a local `BinderGutterDp = 66` (L22).
    - **Not using the kit:** Flyleaf `HomeActivity.cs:509-` "ShowPaperMenu" opens a `Dialog`, though CLAUDE.md describes it as a "Paper action card".
    - **Fix:** add an `ActionCard` to `Storyworthtelling.Maui`. Make Constitution and Flyleaf call `Codex.Ui.ActionCard`.

28. **Medium — The binder-page frame is copied three times.**
    - **Where:** `FolioBinderPage.cs` (305 lines), `LedgerBinderPage.cs` (371) and Foreword's `BinderPage.cs` (159). Each builds the header, tabs, pinned bar and overlay separately.
    - **Design says:** §18: "adopt … the pinned-header + binder-tab frame".
    - **Fix:** move a `PaperBinderPage` into the kit.

29. **Medium — The MAUI kit has no text-field component, and native fields have no focus state.**
    - **MAUI:** Folio's `SetupPage.xaml:26,38,49` and `NewProjectPage.xaml:18` use bare `Entry`, which shows Android's underline with no hairline or radius. Index built its own `Views/FieldBox.cs`.
    - **Native:** `PaperUi.StyleField` (L167) has no focus state.
    - **Design says:** §7: `PaperBright` fill, 1dp Hairline, radius 12, and a 2dp Action border on focus.
    - **Fix:** add a field component to the kit, and add a focused state to the native field.

30. **Medium — Settings layout differs between apps.**
    - **Where:** Foreword's `SetupPage.xaml` and Flyleaf use the kit's settings layout (`GroupLabel` + `GroupCard`, 9 and 10 uses). Folio's and Index's `SetupPage.xaml` use `TitleLabel` headings with hand-placed `BoxView` dividers (7 and 6) and no cards.
    - **Design intent:** `Paper.xaml` L210-215 says these styles exist so "the two screens are the same screen".
    - **Fix:** move Folio and Index to `GroupLabel`/`GroupCard`.

### Mockup vs implementation

31. **Medium — Index Settings vs mockup 10** (`Ledger/Ledger.Android/Pages/SetupPage.xaml`):
    - The heading is "Plaid credentials" with an explanatory paragraph. The mockup shows "Plaid" with a one-line "Working ✓ Replace".
    - There is a "Connected accounts" section with `Remove` buttons and "Connect an account" (L97-129). The mockup says "Banks are managed on their cards in Accounts." The code comment admits this is kept as a fallback.
    - That section's undo is a hand-built **UndoBar** card (L115-124). The undo bar was retired in favor of the dog-ear (§7, CLAUDE.md).
    - Category rows end with a "⋮" kebab glyph (L178) and open a `DisplayActionSheet`. §9 bans the kebab, and the mockup says tap a row for rename, move or remove, with undo.
    - The foot shows `owner_medallion.png`, a photo. Foreword and Folio now draw the owner's drawn mark (`Storyworthtelling.Paper/OwnersMark`). Flyleaf's `CoverView.cs:177-187` also draws an ad-hoc circled "M" instead of `OwnersMark`.

32. **Low — Index Snowball vs mockup 6:** the caption "Added to the focus loan's payment." under "Extra each week" is missing from `SnowballPage.xaml.cs`; it appears nowhere in the code. The other labels match: Debt-free, In payoff order, As the focus, Every loan week by week, Each loan.

### Icons

33. **Medium — The in-app tab glyphs are hand-made and drawn in two different styles.**
    - **Not generated:** none comes from `Tools/IconStudio` (it has no area or tab glyph spec).
    - **Native drawables:** Score's `area_*.xml`, Plate's `area_*.xml`, `Codex.Ui.Android/.../area_settings.xml` and Flyleaf's `tile_*` and `folder_*` are **solid filled** silhouettes. Post's `area_*` and Constitution's `area*.xml` are **stroked**, at 1.6 units.
    - **MAUI:** glyphs are hand-typed fill-path strings in `LedgerAreas.cs:79-105`, `FolioAreas.cs` and `ForewordAreas.cs`.
    - **Design says:** §10: "colored-pencil line icons … 2dp strokes … no fills … generated and governed by Tools/IconStudio".
    - **Fix:** add a glyph set to IconStudio that emits stroked vectors for both platforms.

---

## C. Already correct (checked)

- All eight fonts are registered in the MAUI apps through `PaperFonts.Register`. Native `PaperTheme` loads all eight, including Black and Pinyon.
- `PaperUi.Display`/`ScreenHeader` and the kit's `DisplayLabel` both use Serif Black.
- No shadows outside the Flyleaf bookcase exception.
- No spinners in use.
- Pulp's palette has no Caution arm, as decided (D275).
- Index draws no Positive role on its screens, as decided (D116).
- Pulp's Settings and empty-state strings match mockups 9 and 13 word for word.
- Native views mostly use `PaperUi` rather than raw `TextView`/`Button`.
