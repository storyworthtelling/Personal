# DESIGN.md — the Paper & Ink design bible

This file is the single source of truth for the visual and UX design of every project in this repository: FocusPhone (all four heads), Storyboarder (app and generated PDFs), DailyLoader, and anything built here in the future. Code does not carry design opinions of its own; it carries tokens that mirror this document. **No color or type-size literal may appear outside a project's registered token files** (§12) — leak-guard tests enforce this.

## 1. Principles

1. **Paper & ink.** Every surface is warm paper; everything on it is soft ink. The apps should feel like well-made stationery, not software.
2. **Light only.** There is no dark mode, by decision. The paper is the identity.
3. **One accent, one meaning.** Terracotta appears only where something needs attention or action — a primary button, an active state, an error, a rule under a heading. Never decoration.
4. **Typography does the hierarchy.** Size, weight, and family carry structure; color does not. Serif reads, sans operates, mono is for machine text.
5. **Quiet by default.** Fewer words, sentence case, no emoji or warning glyphs, generous space. Empty states invite rather than apologize.
6. **The books are part of the system.** Generated PDFs (comic volumes, analysis reports) follow this bible exactly as the apps do.

## 2. Palette

### Base (inherited from FocusPhone, now canonical repo-wide)

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
| Success | Quiet confirmation in `InkMuted` copy — no green exists in this system |

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

- **Source Serif 4** (Regular, SemiBold, Italic) — headings, reading text, the launcher clock, PDF body, captions, folios. The literary voice.
- **Source Sans 3** (Regular, SemiBold) — UI controls, labels, list rows, comic lettering. The working voice.
- **Cascadia Code** (Regular) — machine text: API keys, SHA-1s, URLs, file paths, countdown timers. Anything the user copies or a machine produced.

### Type scale

| Token | Family/weight | Android sp | MAUI | Analysis PDF px | Use |
|---|---|---|---|---|---|
| `Clock` | Serif Regular | 64 | — | — | Launcher clock only |
| `Display` | Serif SemiBold | 34 | 34 | 58 (H1) | Screen/page titles |
| `Title` | Serif SemiBold | 24 | 24 | 40 (H2) | Section heads |
| `Heading` | Serif SemiBold | 18 | 18 | 32 (H3) | Card titles, sub-heads |
| `Body` | Sans Regular (UI) / Serif Regular (reading & PDF body) | 16 | 16 | 30 | Default text |
| `Secondary` | Sans Regular | 14 | 14 | 26 | Supporting text, status lines |
| `Label` | Sans SemiBold | 13 | 13 | 22 | Field labels, buttons |
| `Mono` | Cascadia Regular | 14 | 14 | 24 | Technical strings |
| `Countdown` | Cascadia Regular | 40 | — | — | Unlock timer |

Comic-specific: `Lettering` Sans Regular 17px, `Caption` Serif Italic 16px, `Folio` Serif Regular 18px.

Line height: 1.35 for body/reading, 1.2 for headings. Sizes never invented ad hoc — if a size isn't in this table, it isn't in the product.

## 4. Spacing & shape

- **Spacing scale (dp/px):** 4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96. All padding/margins are members of this scale.
- **Corner radius:** inputs 6; buttons and balloons 8; cards 6; captions 4. No large rounding — paper is crisp.
- **Strokes:** hairline 1dp (UI) / 1.5px (PDF); comic panel border 2px Ink.
- **Touch targets:** minimum 48dp; list rows 56dp.

## 5. Components (per-stack recipes)

| Component | Recipe |
|---|---|
| Primary button | `Accent` fill, `PaperBright` text, Label type, radius 8, padding 16×10. One per screen at most. Pressed: `AccentDeep`. |
| Secondary button | `Paper` fill, 1dp `Hairline` border, `Ink` text, Label type, radius 8. |
| Quiet button | No fill, no border, `InkMuted` text; for tertiary actions (e.g. header "Settings"). |
| Destructive button | `Paper` fill, 1dp `Accent` border, `Accent` text. Confirmation dialogs restate the consequence in plain words. |
| Text field | `PaperBright` fill, 1dp `Hairline` border, radius 6, `Ink` text, placeholder `InkMuted`. Mono variant: `PaperDeep` fill, Cascadia. |
| Field group | Label (Label type, `Ink`) above the input; helper text (Secondary, `InkMuted`) below. |
| Warning card | `PaperDeep` fill, 3dp `Accent` left rule, radius 6, Ink body text. Replaces every ⚠-style message. |
| Hairline card | `PaperBright` fill, 1dp `Hairline` border, radius 6, padding 16. List/detail cards. |
| List row | 56dp min height; primary line Sans 16 `Ink`, meta line Sans 14 `InkMuted`; 1dp `Hairline` divider inset 16. |
| Empty state | Centered, Serif Italic, `InkMuted`, an invitation: "No projects yet. Start with a Google Doc." |
| Progress | `Accent`-tinted indicator + Secondary status line in `InkMuted`. Progress copy: "Storyboarding — panels 12 of 40". |
| Dialog | Platform dialog with `Accent` action color (via theme); title Serif where the platform allows. |

## 6. Iconography

In-app glyphs are single-color `Ink` line drawings, geometric, 2dp stroke, no fills. A glyph lights to `Accent` only as a live signal (e.g. a launcher tile whose app has an active notification). No multicolor icons in UI.

## 7. Generated PDF specifications

### Comic volume pages (1024 × 1536 px)

- Page background `Paper`; letterbox fill behind scaled art `PaperShade`.
- Margins: side 64, top 64, bottom 116 — the bottom margin contains a 52px folio band.
- Panel gutter 24; panel borders 2px `Ink`.
- Dialogue balloons: `PaperBright` fill, 1.5px `Ink` stroke, radius 8, Lettering type (Sans 17), anchored top of panel.
- Caption boxes: `PaperDeep` fill, 1.5px `Ink` stroke, radius 4, Caption type (Serif Italic 16), anchored bottom of panel.
- Folio band: running volume title ("{Novel} · Volume {N}") bottom-left, Folio type in `InkMuted`; page number bottom-right, Folio type in `Ink`.

### Analysis report pages (1240 × 1754 px, ≈A4 @150dpi)

- Background `Paper`; margins side 110, top 100, bottom 130 (footer inside bottom margin).
- Type: body Serif 30 `Ink` (1.35 lh); H1 58 / H2 40 / H3 32 Serif SemiBold `Ink`.
- A 2px `Accent` rule under H1 and H2, 24px wide gap below.
- Bullets: `Accent` glyph, Ink Serif text, 34px indent.
- Subtitles/meta lines: Serif Italic `InkMuted`.
- Footer: running report title left (Sans 22 `InkMuted`), page number right (Serif 22 `Ink`).

## 8. Launcher icon family

Android adaptive icons, generated exclusively by `Tools/IconStudio` (never hand-edited):

- **Background layer:** flat `Paper`.
- **Foreground layer:** one `Ink` line glyph (stroke ≈ 3.5% of the canvas — a 2dp-feeling mark that survives 48px legacy sizes), centered in the 66/108 safe zone, with exactly one `Accent` detail.
- Glyphs: Launcher = clock face w/ terracotta hand · Camera = lens ring w/ terracotta aperture dot · Audio = waveform w/ one terracotta bar · Communication = speech outline w/ terracotta dot · Storyboarder = comic panel grid w/ one terracotta panel · DailyLoader = folded paper w/ terracotta fold.
- Regenerate: `dotnet run --project Tools/IconStudio -- --repo-root .`

## 9. Voice & copy

- Sentence case everywhere, including buttons ("Request unlock", not "REQUEST UNLOCK").
- No emoji, no ⚠/❗ glyphs — semantic cards carry the tone.
- Status lines are short and declarative: "Last run 07:42 — delivered."
- Errors say what happened and what to do next: "Gemini rejected the key. Check it in Settings."
- Empty states are invitations, not absences: "Nothing new to hear."
- Destructive confirmations restate the consequence: "Block Maps? It stays blocked even if reinstalled."

## 10. Future-project defaults

- **New MAUI app:** copy `Storyboarder.Android/Resources/Styles/DesignTokens.xaml` and the `ConfigureFonts` block; register the six fonts with aliases `Serif`, `SerifSemibold`, `SerifItalic`, `Sans`, `SansSemibold`, `Mono`.
- **New native Android head:** reference a `*.Ui.Android`-style library (see `FocusPhone.Ui.Android`) or copy its `Theme` + token pattern.
- **Angular frontend (when one exists):** an SCSS map with these token names, the same scale in rem (16px root: Body 1rem, Secondary .875rem, Label .8125rem, Heading 1.125rem, Title 1.5rem, Display 2.125rem), fonts via `@font-face` from `/Fonts` with `font-display: swap`.
- Every new project adds its token files to §12 and ships a leak-guard test from day one.

## 11. Accessibility baseline

Contrast per §2 table; touch targets ≥48dp; text is selectable where users copy it (keys, SHA-1s); `InkMuted` never used below 14sp; accent-colored text below 16 uses `AccentDeep`.

## 12. Token-file registry (leak-guard allowlist)

Only these files may contain color hex or type-size literals:

| Project | Token files |
|---|---|
| FocusPhone | `FocusPhone.Domain/Design/DesignTokens.cs`; `FocusPhone.Ui.Android/Resources/values/colors.xml`, `themes.xml`; generated icon resources; tile drawables |
| Storyboarder | `Storyboarder.Domain/Design/DesignTokens.cs`; `Storyboarder.Android/Resources/Styles/DesignTokens.xaml`; generated icon resources |
| DailyLoader | `DailyLoader.Android/Resources/Styles/DesignTokens.xaml`; generated icon resources |
| Tools | `Tools/IconStudio/Tokens.cs` (kept in parity with this file by test) |

Parity between C# and XAML/xml token files is asserted by unit tests; a change to this bible is a change to those files and their tests, in the same commit.
