# DESIGN.md — the Paper & Ink design bible

This file is the single source of truth for the visual and UX design of every project in this repository: FocusPhone (all four heads), Storyboarder (app and generated PDFs), DailyLoader, and anything built here in the future. Code does not carry design opinions of its own; it carries tokens that mirror this document. **No color or type-size literal may appear outside a project's registered token files** (§16) — leak-guard tests enforce this.

## 1. Principles

1. **Paper & ink.** Every surface is warm paper; everything on it is soft ink. The apps should feel like well-made stationery, not software.
2. **Light only.** There is no dark mode, by decision. Night comfort is the system's job (Android Night Light / Bedtime mode); we build nothing custom.
3. **One accent, one meaning.** Terracotta appears only where something needs attention or action. Never decoration.
4. **Typography does the hierarchy.** Size, weight, and family carry structure; color does not. Serif reads, sans operates, mono is for machine text.
5. **Quiet by default.** Fewer words, sentence case, generous space. Success is silent state change; only problems speak up.
6. **Act, don't ask.** Actions happen immediately with a window to undo — confirmation dialogs are not part of this language.
7. **The books are part of the system.** Generated PDFs follow this bible exactly as the apps do.

## 2. Palette

### Base

| Token | Hex | Role |
|---|---|---|
| `Paper` | `#FAF8F2` | The background of everything: windows, pages, PDF pages |
| `Ink` | `#3A3A33` | Primary text, icons, panel borders |
| `InkMuted` | `#8A8578` | Secondary text, metadata, timestamps, folio titles |
| `Accent` | `#C2542F` | Terracotta. Primary actions, attention, active/error states |

### Extended (derived)

| Token | Hex | Role |
|---|---|---|
| `PaperBright` | `#FFFFFF` | Cards that need lift, dialogue balloons, primary-button text |
| `PaperDeep` | `#F1EBDD` | Caption boxes, inset panels, mono/key input fields, explainer cards |
| `PaperShade` | `#E9E1CF` | Pressed states, letterbox fill behind scaled art |
| `Hairline` | `#E3DCC9` | Borders, dividers, card strokes |
| `InkSoft` | `#6B6759` | Tertiary text where InkMuted is too faint for long runs |
| `AccentDeep` | `#9C4023` | Pressed accent; accent-colored text below 16sp/px |

### Semantic states — no new hues

| State | Recipe |
|---|---|
| Warning / needs setup | Ink text on a `PaperDeep` card with a 3dp `Accent` left rule |
| Error / destructive | `Accent` text (≥ Body size) or `AccentDeep` (smaller); destructive buttons are accent-outlined, never filled |
| Progress / active | `Accent` indicator or tint on paper; status line in `InkMuted` |
| Success | Quiet inline state change — no green exists in this system, and no toasts |

### Contrast (WCAG, computed)

| Pair | Ratio | Verdict |
|---|---|---|
| Ink on Paper | 10.79:1 | AAA body text |
| InkSoft on Paper | 5.33:1 | AA body text |
| InkMuted on Paper | 3.46:1 | Large/secondary text only (≥14sp) |
| Accent on Paper | 4.30:1 | UI components & large text; use AccentDeep for small text |
| AccentDeep on Paper | 6.24:1 | AA at any size |
| PaperBright on Accent | 4.56:1 | AA — primary button text |
| Ink on PaperDeep | 9.64:1 | AAA |

## 3. Typography

Three families, committed as static TTFs in `/Fonts` (OFL; see `Fonts/LICENSES.md`):

- **Source Serif 4** (Regular, SemiBold, Italic) — headings, reading text, PDF body, captions, folios, screen headers. The literary voice.
- **Source Sans 3** (Regular, SemiBold) — UI controls, labels, list rows, comic lettering. The working voice.
- **Cascadia Code** (Regular) — machine text: API keys, SHA-1s, URLs, file paths, countdowns. Anything the user copies or a machine produced.

### Type scale

| Token | Family/weight | Android sp | MAUI | Analysis PDF px | Use |
|---|---|---|---|---|---|
| `Display` | Serif SemiBold | 34 | 34 | 58 (H1) | Screen headers, page titles |
| `Title` | Serif SemiBold | 24 | 24 | 40 (H2) | Section heads |
| `Heading` | Serif SemiBold | 18 | 18 | 32 (H3) | Card titles, sub-heads |
| `Body` | Sans Regular (UI) / Serif Regular (reading & PDF body) | 16 | 16 | 30 | Default text |
| `Secondary` | Sans Regular | 14 | 14 | 26 | Supporting text, status lines |
| `Label` | Sans SemiBold | 13 | 13 | 22 | Field labels, buttons, chips |
| `Mono` | Cascadia Regular | 14 | 14 | 24 | Technical strings |
| `Countdown` | Cascadia Regular | 40 | — | — | Unlock timer, cutover countdown |

Comic-specific: `Lettering` Sans Regular 17px, `Caption` Serif Italic 16px, `Folio` Serif Regular 18px, `CoverTitle` Serif SemiBold 72px.

Line height: 1.35 for body/reading, 1.2 for headings. Sizes never invented ad hoc.

## 4. Spacing & shape

- **Spacing scale (dp/px):** 4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96.
- **Airy by default:** screen edges are 24dp; list rows may inset 16. Whitespace is the primary separator — hairlines appear only where scanning demands them (list rows, section rules); boxes only where grouping demands them.
- **Corner radius:** soft-cornered stationery, 10–16, never pills. Inputs 12; buttons 12; cards 12; dialogue balloons 14; captions 10.
- **Strokes:** hairline 1dp (UI) / 1.5px (PDF); comic panel border 2px Ink.
- **Touch targets:** minimum 48dp, strictly; list rows 56dp.

## 5. Motion & feedback

- **Two durations only:** `Quick` 150ms (state changes, tint fades, inline status appearing) and `Standard` 200ms (screen transitions, undo bar entry). Decelerate curves (`decelerate` interpolator / `Easing.CubicOut`). Nothing springs, bounces, or overshoots — paper doesn't bounce.
- **Screen transitions are horizontal page-slides**, like turning a page. Native heads implement it (`OverrideActivityTransition` + the `paper_slide_*` animations); MAUI heads accept the platform's navigation transition where custom transitions would mean fighting the framework.
- **No haptics.** Anywhere we control vibration, it is off.
- **Loading is words, not ornaments.** A quiet `InkMuted` Secondary sentence is the primary signal: "Composing page 12 of 40…", "Loading My Drive…". A small accent spinner appears only when duration is genuinely unknown. Never a skeleton screen.

## 6. Materiality — paper grain

- Every window background and every generated PDF page carries a **subtle ink grain**: deterministic seeded monochrome noise, 256px tile, ink at ~2.4% opacity (alpha 6/255), roughly 1 in 6 pixels.
- Grain lives on `Paper` backgrounds only — never on `PaperBright` cards, balloons, buttons, or inputs (their cleanliness is the contrast).
- One algorithm, one seed (see `Grain` tokens); IconStudio emits the app tiles, `PaperGrain` draws it in the Skia renderers. Regenerating is always byte-identical.

## 7. Components

| Component | Recipe |
|---|---|
| Primary button | `Accent` fill, `PaperBright` text, Label type, radius 12, padding 16×10. One per screen at most. Pressed: `AccentDeep`. |
| Secondary button | `Paper` fill, 1dp `Hairline` border, `Ink` text, radius 12. |
| Quiet button | No fill, no border, `InkMuted` text; tertiary actions. |
| Destructive button | `Paper` fill, 1dp `Accent` border, `Accent` text. |
| **Pinned bottom bar** | Primary actions live in a bar fixed to the screen bottom regardless of scroll: 1dp `Hairline` top rule, paper/grain background, 24×12 padding. Content scrolls above it. |
| Text field | `PaperBright` fill, 1dp `Hairline` border, radius 12. Mono variant: `PaperDeep` fill, Cascadia. |
| Field group | Label above the input; helper text (Secondary, InkMuted) below. |
| Warning card | `PaperDeep` fill, 3dp `Accent` left rule, radius 12. |
| Hairline card | `PaperBright` fill, 1dp `Hairline` border, radius 12, padding 16. |
| List row | 56dp min height; Sans 16 `Ink` + Sans 14 `InkMuted`; hairline divider. |
| **Undo row** | The confirmation replacement: the action happens immediately, then an inline bar appears — Secondary `Ink` message ("Blocked Maps.") + Quiet "Undo" — and dismisses itself after 6 s. Never a dialog. |
| **Inline countdown** | For the rare irreversible act (finalize cutover): a Cascadia countdown ("0:10…") with a Secondary "Cancel" beside it; executes only when a visible countdown expires. Leaving the screen cancels it. |
| **Status line** | The loading/progress voice: one Secondary `InkMuted` sentence, updated in place. |
| **Gesture hint** | Icon-only affordance for hidden gestures: a small `InkMuted` line glyph (chevron/handle for swipe-up; the clock's hub dot marks the long-press). No text. |
| Empty state | Centered, Serif Italic, `InkMuted`, an invitation. |
| Screen header | Every screen opens with an in-page `Display` Serif title, like a chapter heading. Nav bars stay minimal. |
| Phase chip | Hairline-bordered pill (radius 10), Label type: "panels 12 of 40". Active phase `Ink`, finished phases `InkMuted`, problems get the error chip (accent border + `AccentDeep` text). |

**Success is an inline state change.** The screen shows the new truth (status line updates, the row disappears, the list gains the item). Success toasts and success alerts are banned; failures may still surface visibly, worded per §11.

## 8. Iconography

In-app glyphs are single-style **ink line icons**: 2dp stroke, geometric, no fills. `Accent` appears only as a live signal (a tile with an active notification, the one terracotta detail in each launcher icon). No second icon style, ever.

## 9. Generated PDF specifications

All pages (including front matter) carry the §6 grain over `Paper`.

### Comic volumes (1024 × 1536 px)

- **Cover page (new, page 0):** novel title in `CoverTitle` Serif, short terracotta rule, "Volume N" + volume title in Serif, the owner's medallion (§13) near the foot. No folio.
- Content pages: margins side 64, top 64, bottom 116 (52px folio band); gutter 24; 2px `Ink` panel borders; `PaperShade` letterbox behind off-ratio art.
- Dialogue balloons: `PaperBright`, 1.5px Ink stroke, radius 14, Sans 17. Captions: `PaperDeep`, radius 10, Serif Italic 16.
- Folio band: running volume title bottom-left `InkMuted`, page number bottom-right `Ink`, Serif 18.

### Analysis reports (1240 × 1754 px)

- **Front matter (new, unnumbered):** a cover (title, "Revision report" in Serif Italic muted, terracotta rule, medallion) and a **Contents** page listing each section with its page number. Front matter carries no footer; content pages keep folios 1..N, so printed numbers match the TOC with no bookkeeping.
- Content: margins 110/100/130; body Serif 30 `Ink`; H1 58/H2 40/H3 32 Serif SemiBold; 2px terracotta rule under H1/H2; terracotta bullets; footer = running title left (Sans 22 `InkMuted`), page number right (Serif 22 `Ink`).

## 10. Launcher icon family

Android adaptive icons, generated exclusively by `Tools/IconStudio` (never hand-edited): flat `Paper` background layer; one `Ink` line glyph (stroke ≈ 3.5% of the canvas) centered in the 66/108 safe zone with exactly one `Accent` detail. Glyphs: clock (Launcher) · lens (Camera) · waveform (Audio) · speech (Communication) · panel grid (Storyboarder) · folded sheet (DailyLoader). Regenerate: `dotnet run --project Tools/IconStudio -- --repo-root .`

## 11. Voice, copy & formats

- Sentence case everywhere, including buttons. No emoji, no ⚠ glyphs.
- **Time is 12-hour with an analog sensibility**: "7:42 pm" (lowercase, no leading zero) wherever digital time must appear; the FocusPhone home shows a drawn analog face and no digital time at all.
- **List timestamps are relative**: "just now" → "5m ago" → "2h ago" → "yesterday" → "Tuesday" → "Jun 30" → "Jun 30, 2025" (`RelativeTime` helpers).
- **Error template (binding):** *what happened* + *what to do next*. "Gemini rejected the key. Check it in Settings." Never a bare "failed".
- **Numbers & units:** counts as "12 of 40"; durations as "42 min"; interpuncts (·) join metadata; em-dashes (—) join clause-level status ("Delivered — 2h ago").
- **Notifications:** title = the outcome ("Crossword delivered"), body = next step if any; one line; sentence case; never the app name as a prefix.
- Empty states are invitations; destructive copy states the consequence plainly.
- **Owner's voice — pending.** The long-term intent is for copy to carry the owner's own wit, learned from their self-authored Google Drive documents. That corpus is not yet accessible (the Google Drive connector must be authorized in claude.ai). Until that review happens, all copy stays quiet-neutral per the rules above; this section will then gain the voice profile and per-surface guidance.

## 12. Terminology glossary

One noun per concept, everywhere (screens, PDFs, notifications, code comments):

| Term | Meaning (never a synonym) |
|---|---|
| project | A novel being processed in Storyboarder (UI word; "novel" acceptable in prose) |
| volume | One output comic PDF |
| page / panel | Comic page; one framed image on it |
| reference sheet | Character/location visual reference |
| design bible | Claude's style guide for a novel (in-app sense) |
| analysis report | The revision-notes PDF |
| admin mode | FocusPhone's unlocked maintenance state (never "settings" or "escape hatch") |
| block / unblock | App suppression on FocusPhone (never "ban"/"disable") |
| cutover | The irreversible device lockdown step |
| deliver | DailyLoader's daily push to the reMarkable |
| thread | One contact's merged conversation |
| tile / tray | Home-screen app icon; the swipe-up app list |
| feed / episode | Podcast RSS source; one item |

## 13. Branding

The owner's mark is a **circular medallion**: the owner's Google profile image, circle-masked with a 1dp `Hairline` ring. Until the real image is provided, IconStudio generates a placeholder — Serif monogram on paper with one terracotta dot.

- Appears on: generated PDF covers (volume + analysis) and the settings screens of all three apps. Nowhere else; the design is the brand.
- **Swap path:** replace `/Branding/owner.png` with the real photo (square, ≥512px), run `dotnet run --project Tools/IconStudio -- --repo-root .`, rebuild. IconStudio circle-masks and rings whatever it finds there; it never overwrites an existing `Branding/owner.png`.

## 14. Accessibility (binding)

- **Font-scale resilience:** every screen must survive Android font scaling at 1.3× with no clipped text or broken layout (sp/scalable units everywhere).
- **Touch targets:** 48dp minimum, strictly — including gesture hints and chips' tap areas (chips that don't tap are exempt).
- Contrast per §2; `InkMuted` never below 14sp; technical strings selectable (plus explicit copy actions for the sanctioned ones).

## 15. Future-project defaults

- **New MAUI app:** copy `Storyboarder.Android/Resources/Styles/DesignTokens.xaml` + the `ConfigureFonts` block; adopt the pinned-bottom-bar Grid pattern and in-page headers.
- **New native Android head:** reference `FocusPhone.Ui.Android` or copy its pattern.
- **Angular (when it exists):** SCSS token map, same names, rem scale (Body 1rem … Display 2.125rem), fonts via `@font-face`, `font-display: swap`.
- Every new project registers its token files in §16 and ships a leak-guard test from day one.

## 16. Token-file registry (leak-guard allowlist)

Only these files may contain color hex or type-size literals:

| Project | Token files |
|---|---|
| FocusPhone | `FocusPhone.Domain/Design/DesignTokens.cs`; `FocusPhone.Ui.Android/Resources/values/colors.xml`, `themes.xml`; generated icon/grain/medallion resources; tile drawables; anim XMLs (durations only) |
| Storyboarder | `Storyboarder.Domain/Design/DesignTokens.cs`; `Storyboarder.Android/Resources/Styles/DesignTokens.xaml`; generated icon/grain/medallion resources |
| DailyLoader | `DailyLoader.Android/Resources/Styles/DesignTokens.xaml`; generated icon/grain/medallion resources |
| Tools | `Tools/IconStudio/Tokens.cs` (kept in parity by test) |
| Branding | `/Branding/owner.png` (image, owner-supplied) |

Parity between C# and XAML/xml token files is asserted by unit tests; a change to this bible is a change to those files and their tests, in the same commit.
