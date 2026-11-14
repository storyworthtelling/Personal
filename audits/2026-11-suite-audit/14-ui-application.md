# UI application audit: are the shared pieces applied the same way in every app?

Audit W8, asked for by the owner during W8: "make sure icons, frames, colors, button placements,
layouts, etc. are consistent in all apps."

**Scope.** Each app keeps its own colour tiers, and the owner has approved that. This report does
not ask whether a tab is pine or rose. It asks whether the shared pieces are put together the same
way everywhere:

- the binder gutter
- the header and the content well
- the pinned bar
- the dog-ear
- the tab glyphs
- what a page's accents take their colour from

**Apps covered:**

| Kind | Apps |
|---|---|
| Native | Flyleaf, Score, Post, Plate, Pulp, Constitution |
| MAUI | Index, Folio, Foreword |

Report 02 (cross-app behaviour) and report 04 (design fidelity) are not repeated here.

**How this was checked.** Every finding was read from the source, with a file reference. Nothing
here has been seen on a phone. Items marked *verify* depend on how the platform lays out the edges
of the screen, which only a device can show.

**The headline:** the native heads share one frame. The three MAUI heads each built their own copy
of the binder page, and those copies have drifted apart and away from the native frame. Most of the
findings below go away once the kit has one `PaperBinderPage` (already planned as W8 item 3) that
copies the native frame's choices.

---

## A. The binder gutter

### 1. The gutter beside the bottom bar is filled three different ways

| Behaviour | Apps | Where |
|---|---|---|
| The bar spans the full width and the strip stops above it | Score, Post, Plate, every `BinderFrame` secondary screen, Foreword, Index | `ScoreActivity.cs:258`, `PostActivity.cs:312`, `PlateActivity.cs:449`, `Foreword/.../BinderPage.cs:91`, `LedgerBinderPage.cs:236` |
| The strip runs to the bottom of the screen beside the bar | Pulp, Constitution | `Pulp/.../MainActivity.cs:315` (bar inside the page column), `ConstitutionActivity.cs:352` (`PageFrame.SetBar`) |
| **The cell beside the bar is left empty**, so plain paper shows under the tab strip and the gutter has a hole | Folio | `FolioBinderPage.cs:97`: the bar is set to row 1, column 0 with no column span, and nothing fills row 1, column 1 |

**Fix.** Choose one rule and put it in both kits.

- **Recommended:** the bar spans the full width. Six of the nine apps already do this, and
  Foreword's frame gives the reason ("the bar belongs to the page you are on, not to the binder").
- Pulp, Constitution and Folio move to that rule.
- Add a guard that reads each frame's layout.

### 2. The gutter at the very top and bottom of the screen (*verify*)

**Native heads.** The page is inset below the status bar (`PaperUi.WrapForTopInset`), but the strip
is not. The strip runs up behind the status bar, so the gutter's cloth goes to the edge of the
glass (`BinderFrame.cs:76-79` explains why).

**MAUI heads.** They set nothing about safe areas or insets (no `SafeAreaEdges`, no
`IgnoreSafeArea`, no inset listener except Index's keyboard one). MAUI on .NET 10 keeps layouts out
of the system bars by default. That would leave a band of page colour above the strip and below it
on the MAUI apps, where the native apps show cloth.

**Fix.** The shared `PaperBinderPage` lets the strip column reach the edges of the screen and
insets only the page column, as the native frame does. Check on a device.

### 3. Three copies of the MAUI binder page

`LedgerBinderPage` (374 lines), `FolioBinderPage` (332) and Foreword's `BinderPage` (159) each build
the same header, well, strip and bar grid. Findings 1, 4, 5, 6 and 7 are differences between these
copies. W8 item 3 (`PaperBinderPage`) replaces all three.

**The extra slots must survive the merge:**

- Index's overlay slot (an action card's haze over the whole page)
- Index's corner slot
- Index's keyboard listener, which hides the bar while typing

The native frame has a pattern for each of these.

## B. The pinned bar

### 4. The order and width of the buttons differ

DESIGN.md §7 places the primary on the **right**.

| Where | Order | Widths |
|---|---|---|
| Native `PaperUi.BottomBar` (`PaperUi.cs:471-479`) | As passed | Every button the same width (`Weight = 1`) |
| Index and Foreword `SetBar` | Primary **last** | The primary takes the remaining width |
| Folio `SetBar` (`FolioBinderPage.cs:186-191`) | Primary **first** (left) | The primary is widest. Its comment says this follows Folio's drawings |

**Fix.**

- One rule in both kits: the primary is last (right) and widest; secondaries are sized to their
  words.
- Native callers that pass the primary first need to be reordered. Many pass
  `(secondary, primary)` already.
- Folio's two drawings are the exception, and that needs your ruling (see Decisions).

### 5. Bar padding differs

| Where | Padding (dp) |
|---|---|
| Native | 24 / 12 / 24 / 12. Every button also carries an 8dp end margin, so the right inset is 32 |
| Foreword | 24 / 12 / 24 / 16 |
| Folio | 24 / 12 / 16 / 16 |
| Index | 24 / 12 (written as literals) |

**Fix.**

- One set of tokens.
- The gap goes *between* buttons only (no margin after the last one).
- The bottom padding is the navigation-bar inset plus 12.

## C. The content well

### 6. The gap between the header and the content differs

| Where | Content padding |
|---|---|
| Native (`BinderFrame.cs:66`, every shell) | 24 at the sides, **0 at the top**, so content sits directly under the header's rule |
| MAUI pages using `ScreenPadding` (most of Foreword, Folio's Setup, New project and Draft diff) | 24 all round |
| Foreword Today | `24,12` |
| Folio desk | `24,4,16,24`, with a 16 right side |
| Index Plaid | `24,16,16,24` |
| Index Accounts and the plan pages | Their own `WellPaddingLeft`/`WellPaddingRight` constants |

**Effect:** the same header sits at a different distance from the first line of content in every
MAUI app, and further away than in any native app.

**Fix.** The binder page owns the well's padding (24 at the sides, 0 at the top), as the header
already owns its own spacing. Pages stop setting it.

### 7. The dog-ear sits in a different place in each MAUI app

| App | Where the dog-ear sits |
|---|---|
| Native (all) | The page's own top corner, beside the binder, from the shell's overlay host |
| Folio | The frame builds one into the corner of every page |
| Index | Four pages pass theirs to the frame's `SetCorner` |
| Foreword | Inline in the page's content, below the status line (`MainPage.xaml:19`, `NoticeCorner`), so it scrolls with the page and is not in the corner |

**Fix.** The binder page owns one corner slot, as Folio's does, and Foreword's Today moves its fold
there.

### 8. Foreword's Waiting page has no binder

Index opens its Waiting page as a `LedgerWaitingPage`, a binder page with tabs
(`MainPage.xaml.cs:1685`). Foreword opens the kit's bare `WaitingPage` (`MainPage.xaml.cs:80`),
which is a `ContentPage` with its own header and no strip.

**Fix.** The Waiting page is framed like every other screen.

## D. Tabs and glyphs

### 9. The tab glyph size differs

DESIGN.md §9 sets a "26dp glyph above a horizontal label".

| Kit | Glyph size |
|---|---|
| Native `BinderTabs.cs:117` | 26 |
| MAUI kit `BinderTabs.GlyphDp` | **24** |

**Fix.** 26 in both. This is a token: add it to the shared `DesignTokens` so the parity guard holds
it.

### 10. Three Settings glyphs and two glyph styles

**Settings has three different glyphs** although it is the one area every app shares:

| Apps | Settings glyph |
|---|---|
| Score, Post, Plate and Constitution (`area_settings` in `Codex.Ui.Android`) | Filled "tune" sliders |
| Index, Folio and Foreword (path strings in each head) | A second, different sliders drawing, identical across the three |
| Pulp | Its own stroked gear (`tabsettings.xml`) |

**Styles are mixed:**

- **Filled:** Score, Plate and Settings.
- **Stroked:** Pulp, Constitution and part of Post's set.
- DESIGN.md §10 asks for single-style stroked line icons from IconStudio.

**Resource names follow three patterns:** `area_x`, `areax` and `tabx`.

**Fix.** W8 item 8: IconStudio emits one stroked set for both platforms, including one Settings
glyph, under one naming pattern.

## E. What a page's accents take their colour from

### 11. Filter fields and in-page accents follow the tab now

Owner's ruling at the start of W8: a page's accents wear their tab's colour, and it is one approach
for the whole suite. That reverses the restraint rule in the "Score refit" section of CLAUDE.md, and
the `AreaHueTests` restraint guard (which lists Post's Filed and Rolodex as exceptions).

**Already on their tab's colour:** Post's Mailbox, Filed and Rolodex, and Score's Music.

**Still on the identity hue while sitting behind another tab:**

| Page | Tab | Where |
|---|---|---|
| Score Podcasts filter | Reference | `PodcastsArea.cs:297` |
| Pulp Discover filter | Discover's tab | `DiscoverArea.cs:117`, and the child version `ChildDiscoverPage.cs:54` |
| Pulp Sports team finder | Sports' tab | `SportsScreen.cs:269` |
| Score's Dial, tempo bands and budget meter | The Workout and Playing tabs | Named in CLAUDE.md as deliberately kept on the identity hue |

**Fix.**

- Every in-page accent asks its area for the colour (`Tab.Hue`) instead of naming a token.
- Reverse the guard: a page's accent must equal its tab's hue.
- Record the ruling in DESIGN.md §9.

## F. Checked and consistent

- **The binder gutter's cloth.** Both kits fill the whole gutter with the active area's cloth, so
  there are no paper gaps between tabs (native `UpdateGutterColor`, MAUI
  `strip.BackgroundColor`).
- **Strip width.** 66dp in both kits; the top-alignment rule holds.
- **Header.** Every native shell and secondary screen uses `PaperUi.ScreenHeader` with the area's
  rule. Every MAUI screen uses `PaperPageHeader` with 24/32/24/8 spacing. Report 02 §24 covers the
  two headers missing their underline.
- **Binder on every screen.** Every navigable native activity wears the binder, through the shell,
  `BinderFrame` or `SettingsViews.Frame`. The screens that do not are deliberate:
  - Plate's errand and read activities
  - Post's share and send-to activities
  - Pulp's trailer and first run
  - Flyleaf, which has no areas
- **Launcher icons.** All nine come from IconStudio specs.
- **Constitution's tab lettering** (`TabLettering`) is the approved D45 exception.
- **Square corners.** Plate's viewfinder frames and Pulp's posters are square because they hold
  pictures. That is consistent and not a finding.

---

## Owner's rulings

1. **The gutter beside the bar:** the binder strip always runs to the foot of the screen, and the
   bar sits under the page only. Pulp and Constitution already do this. Score, Post, Plate, the
   `BinderFrame` secondary screens, Foreword, Index and Folio change.
2. **Folio's bar order:** Folio follows the suite, with the primary on the right and widest.

## Where each finding lands in W8

| W8 item | Findings |
|---|---|
| 3 (`PaperBinderPage`) | 1, 2, 3, 6, 7 and 8 for the MAUI heads, plus 9's token |
| New 3a (one bar) | 4 and 5, in both kits |
| 8 (IconStudio glyphs) | 10 |
| New 10 (tab-hue accents) | 11 |
| Native frame fix | 1 for Score, Post, Plate and `BinderFrame` (move the bar under the page column, as Pulp and Constitution do) |
