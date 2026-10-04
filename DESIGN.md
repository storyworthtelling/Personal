# DESIGN.md — the Paper & Ink design bible

This file is the single source of truth for the visual and UX design of every project in this repository: FocusPhone / Flyleaf (all heads), Canonizer, Ledger, DailyLoader, and anything built here in the future. Code does not carry design opinions of its own; it carries tokens that mirror this document. **No color or type-size literal may appear outside a project's registered token files** (§19) — leak-guard tests enforce this.

Reorganized in the P4 design-system expansion: **foundations → materials → color → controls → patterns**. American English throughout (color, gray, favor, organize).

---

## 1. Principles

1. **Colored media on paper.** Every surface is warm paper; everything on it is made with real drawing media — ink, colored pencil, marker, highlighter, watercolor. The apps should feel like well-made stationery worked by hand, not software.
2. **Calm lives in the journey.** Restraint is spent on flow, density, and quiet interactions — not on withholding color. Color may be lively as long as it reads as art supplies on paper. Noisy, naggy, cluttered, or input-hungry screens fail the acceptance test; a warm palette does not.
3. **Light only.** One visual world, by decision. There is no dark mode; night comfort is the OS's job (Android Night Light / Bedtime). Every color is defined for light only.
4. **Color carries meaning by role.** Color is assigned to semantic **roles** (action, positive, resolved, reference, caution, problem, in-progress), never decoration. A role resolves to a lively value on screen and a muted, grayscale-safe value in documents (§3). (This supersedes the old "one accent, one meaning" rule; terracotta is retired.)
5. **Typography does the hierarchy.** Size, weight, and family carry structure. Serif reads, sans operates, mono is machine text, and a calligraphy hand is the owner's voice.
6. **Quiet by default.** Fewer words, sentence case, generous space. Success is a silent inline state change; only problems speak up. No toasts, ever.
7. **Act, don't ask.** Actions happen immediately with a window to undo — confirmation dialogs are not part of this language. There is no destructive button; destructive acts are undo-first.
8. **The books are part of the system.** Each app is a book with a painted cover; generated PDFs follow this bible exactly as the apps do.

---

## 2. Materials

Four drawing media, each assigned a **job** so the metaphor is systematic, not decorative. Rendered **procedurally in Skia** by one shared engine in `Storyworthtelling.Paper` (extends the paper-grain approach), consumed identically by the native Android and MAUI heads. Intensity is calibrated **per material**; texture is **always on** (no opt-out) but capped so it never fails contrast (§17). Textures are baked to cached bitmaps/shaders once per size+token and reused — never regenerated per frame; **60fps scroll is a merge gate**.

| Material | Job | Rendering |
|---|---|---|
| **Paper** | The ground of every screen — one consistent warm cream, same color *and* grain everywhere. | Seeded monochrome grain, ~12.5%, on `Paper` grounds only. |
| **Watercolor** | Grounds & covers: book covers, app icons, hero/section-header washes, empty-state and loading grounds. Never behind body text. | Soft multi-stop radial washes; baked for hero surfaces (hybrid with procedural grain). |
| **Highlighter** | Attention / active / selected. A translucent band (25–35%) behind full-strength ink that **adopts the functional hue** of what it marks. May sit over any surface, opacity-tuned so ink over it clears AA. | Translucent wash tier (T1) of the relevant role. |
| **Colored pencil** | Semantic marks: chips, ticks, finding tags, margin notes, in-app icon strokes. The hand-drawn "this means something" material. | Grainy stroke/fill of the role's mark tier (T3). |
| **Marker** | Emphasis: heading underlines (full-width, gently curved, rising from the left), tab labels, tags. Never the heading text itself. | Near-solid streaked stroke of the role's mark tier. |

Every material carries slight texture/imperfection — including functional controls — but texture behind text is capped (§17) and glyph strokes stay legible at 16dp.

---

## 3. Color — the role model

Color is a set of **roles**, each named semantically (so a later re-tint never renames a token). Every role carries **five tiers**:

| Tier | Name | Use |
|---|---|---|
| T1 | wash | highlighter / translucent selection |
| T2 | fill | soft fills, chart areas |
| T3 | mark | pencil strokes, chips, glyph signal |
| T4 | text | text-on-paper (meets AA) |
| T5 | deep | small text / dense contexts (AA at any size) |

Each role resolves through a **profile**: **App** (lively, on-screen) and **Document** (muted, for reMarkable/PDF — the §16 grayscale-safe set). One document surface variant renders grayscale (rM2); the color e-ink (Paper Pro) and screen-PDF variants share the muted hues. The role table below shows the App value; the muted Document set and the separable rM2 gray ramp are generated alongside it.

### Roles (App profile — OKLCH-generated, §19)

The values below are emitted from the OKLCH source spec (`PaletteSpec` + `Oklch`), which places every role on an even hue wheel and every tier on a shared perceptual-lightness ladder — so the family is coherent and the text/deep tiers meet AA by construction. A parity test regenerates and asserts these; to change the palette, change the spec.

| Role | Meaning | mark (T3) | text (T4) | deep (T5) |
|---|---|---|---|---|
| **Action** | pine — primary action / commit, active | `#38734A` | `#295E3A` | `#174025` |
| **Positive** | leaf — good news / success | `#517025` | `#405B18` | `#293E08` |
| **Resolved** | gray — a settled / closed delta | `#68645E` | `#54514B` | `#393632` |
| **Reference** | slate-blue — prior draft / context / structure | `#4C6787` | `#3B536F` | `#26384E` |
| **Caution** | ochre — a "consider" / soft heads-up | `#805F00` | `#694C00` | `#493200` |
| **Problem** | rose — regression / contradiction / blocked | `#9E434B` | `#82333B` | `#5C1F26` |
| **In-progress** | teal — currently working / running | `#18717C` | `#085C65` | `#003F46` |

Fills (T2): action `#A5C8AD` · positive `#B1C69A` · resolved `#C0BDB9` · reference `#AEC0D5` · caution `#D0BB91` · problem `#E8ACAD` · in-progress `#9BC7CD`. Washes (T1) are the mark hue at the highlighter alpha (~30%). Filled primary buttons use the **deep** tier so `PaperBright` text clears AA.

Terracotta is **retired everywhere** — including documents, where the old "active" accent becomes a **muted pine**; report rules, bullets, and covers regenerate accordingly.

### Neutrals & the two inks

| Token | Hex | Role |
|---|---|---|
| `Paper` | `#FAF8F2` | The ground of everything |
| `PaperBright` | `#FFFFFF` | Cards that lift, primary-button text |
| `PaperDeep` | `#F1EBDD` | Inset panels, explainer cards, mono fields, overlay haze |
| `PaperShade` | `#E9E1CF` | Slider tracks, letterbox fill |
| `Hairline` | `#E3DCC9` | Borders, dividers |
| `Ink` | `#33322B` | Body & UI text, glyphs |
| `InkSoft` | `#6B6759` | Tertiary text for long runs |
| `InkMuted` | `#8A8578` | Secondary text, metadata (≥14sp) |
| `InkBlue` | `#2C3E5E` | "The pen": signatures, editorial marks, and the color of the voice |

**Ink-black** reads the body and UI. **Ink-blue** is the pen. The owner's voice is set in a **calligraphy hand** (Pinyon Script, §4) colored ink-blue.

### Identity hues

Each app (book) has a **signature identity hue**, kept as a **separate set** from the semantic roles so a tab or accent never reads as "caution." Each app's identity is **sampled from its painted cover art** during its phase, not fixed here. Identity hues color the binder tabs (§10) and per-app accents.

Sampling is not the whole rule: a sampled hue is then **checked against the semantic roles it will sit near**, and moved if it crowds one. The binding is **≥ 20° of OKLCH hue separation from every chromatic role**, and it is enforced, not asserted — `IdentityTiersStayClearOfEverySemanticRole` reads each committed tier back through `Oklch.FromHex` and measures it against `PaletteSpec.RoleWheel` itself, so a role moving is caught too. The near-neutral *resolved* role is excluded: at 0.010 chroma it is a warm gray, and an angle measured off almost no chroma is not a hue anyone can see.

*Score* was rotated off its as-painted hue for exactly this reason, by **rotating the painting** rather than re-prompting it, so the paper texture and pigment blooms are as painted and only the hue moved. **What shipped sits at 178°, 29° clear of the in-progress teal at 207°.** (Earlier revisions of this section described that move as 174° → 165° for 21° of separation. None of those three figures matches the committed ladder or the role wheel; the outcome was always sound, the narrative was not. Nothing caught it because the per-app identity ladders are hand-committed rather than generated by `PaletteSpec`, so they sit outside the generated-vs-committed parity test — bringing them under it is open work.)

*Plate* is the case that shows the rule working. Twelve cover studies were generated from
their subjects with **no colour named in any prompt**; eleven landed between 47° and 102° —
the warm band the caution ochre and Flyleaf's amber already hold — because photography is
sepia however you render it. The twelfth, the same lens-and-tree subject as the chosen cover
regenerated under identical wording, measured **326.5°**: free wheel space, unprompted,
because its coating flare came out magenta where the chosen cover's dust is warm. So Plate's
identity is sampled from its own art and needs no departure at all. Its **binding** does
depart from Post's construction, though not from this section: it is the light `ground` tier
with the rule, brackets and lettering inverted to ink, because §12 warns that a binding
harmonising with its art makes the book hardest to pick out of the row and Plate's cover art
is a near-black lens. That is Score's construction, taken for Score's reason.

*Post* is the one deliberate departure from "sample the art". Its mailbox painting samples near **65°**, which is 14° off the resolved gray and 24° off Flyleaf's amber — an identity that would read as a status *and* look like the launcher on the shelf. §12 already allows a binding to contrast the art it wraps, so **Post's hue is sampled from the binding cloth, at 285°**, where the nearest role is the slate-blue reference a comfortable 32° away.

Registered ladders (per-app `Design/DesignTokens.cs`, §19):

| App | fill | ground | mark | text | deep | cloth | hue | nearest role |
|---|---|---|---|---|---|---|---|---|
| Flyleaf | — | `#C9803A` | `#B4552F` | — | `#8A4A22` | — | 41° | problem, 24° |
| Score | `#AFC0BC` | `#93ABA5` | `#436A61` | `#35544D` | `#263B36` | — | 178° | action, 26° |
| Post | `#B9B9D4` | `#A1A1C7` | `#5C5894` | `#494676` | `#333252` | — | 285° | reference, 32° |
| Plate | `#C8B5C8` | `#B69BB6` | `#7A4F7B` | `#613F62` | `#442D44` | — | 326° | problem, 50° |
| Folio | `#C1C197` | `#919059` | `#6B681E` | `#575410` | `#3B3902` | `#B32738` | 107° | positive, 21° |
| Index | `#B6C3A9` | `#839372` | `#596C45` | `#475735` | `#2F3C21` | `#4E5F3A` | 129° | positive, 0° |
| Pulp | `#CEB7A3` | `#BF9E82` | `#86541F` | `#6B4219` | `#4B3015` | `#F6A24E` | 63° | caution, 22° |

Flyleaf's three tiers predate the five-tier ladder and are **provisional** pending a resample.

**Cloth** is the binding (§12), and it is registered only where a ladder commits one as a hex: Folio's deep red, deliberately not its olive identity, and Index's `#4E5F3A`, which is simply the identity hue deepened. Index's is held identical in `Tools/IconStudio/Tokens.cs` (`IndexClothHex`) and in Flyleaf's shelf (`Codex.Domain` `DesignTokens.Identity.Index.Cloth`). The other bindings are described in §12 (Score's cream linen, Post's deep indigo, Plate's light ground tier) rather than registered as a hex here.

*Folio* is the **tightest ladder in the suite** at 21.4° from the positive olive — inside the floor,
but only just, and worth knowing before anything on that side of the wheel moves. The window it was
committed at the centre of (105–109°) is the only one left that clears every role and every sibling;
the wheel is nearly full. Its tiers were registered here and brought under a guard late, in P10
Release I: they lived in `Tools/IconStudio/Tokens.cs` and in no table, so the suite's hue-separation
check — which enumerates its apps by hand — simply did not know about them. `Folio.Tests`'
`IdentityLadderTests` now covers them, and adds the half the suite's copy never did: distance from
every *other* app's committed identity, which is a different failure from crowding a role.

*Index* is the suite's one departure the 20° floor itself does not survive. Its identity hue is
the bookplate cover's own green, sampled at **129°** — the same hue the Positive role sits at, a
**0° separation** where the floor asks for 20°. It is recorded here rather than resampled or
rotated because D116 makes the collision **inert rather than ambiguous**: Index draws no Positive
role anywhere on its own pages — a paid-off loan is Resolved (settled, crossed out per D12)
rather than shown Positive, and an under-limit budget line takes no colour at all — so there is
nothing on an Index page a Positive-role green could be mistaken for. It is a single, named
exception, for Index against Positive only; every other role, and every other app's identity,
still has to clear 20° from Index. **`FocusPhone/Codex.Tests/Design/DesignTokensTests.cs` carries
no exception mechanism today** — `IdentityTiersStayClearOfEverySemanticRole` is a flat assertion
against every non-neutral role with no per-app or per-role exception list at all, so adding
Index's named exception (guards G9/G10) means building that mechanism, not just adding an entry
to one that already exists. That is Release B's work, not this one's.

*Pulp* is chosen rather than sampled, and the sampling is why. The den painting on its cover
measures warm, near **75°** — inside the band the caution ochre and Flyleaf's amber already hold.
The owner first chose a contrast blue sampled from the painting's projected sky (D201), then asked
for yellow-orange instead (D202). **63°** is the only yellow-orange that clears §3's floor against
both neighbours at every tier after hex rounding: **22° from Flyleaf's amber mark (41°) and 22° from
the caution ochre (85°)**, on the suite's shared lightness ladder at a mark chroma of 0.095. A
yellow-orange cannot carry text on paper at its own brightness, so the text-bearing tiers are browns
of that hue and the bright **marigold lives on the cloth** (`#F6A24E`) alone — a light binding with
the rule, flourishes and title in ink (6.25:1), which is Score's and Plate's construction. The full
ladder is held identical in `Tools/IconStudio/Tokens.cs`, `Storyworthtelling.Paper/AppIdentityTokens.cs`
and `Codex.Domain` `DesignTokens.Identity.Pulp`. **Open, for the owner:** the 22° figures are against
Flyleaf and the roles; against Foreword's sunrise mark (47°) the mark sits **16.3°** apart, which
`IdentitySeparationTests` (every pair of app identities ≥ 20°) fails on. It is not recorded as an
exception here; whether to accept it as a second recorded pair or move the hue is a decision.

### Contrast (binding)

Body text ≥ **AA (4.5:1)**; UI/large text ≥ **3:1** — computed against the **darkest textured pixel** and **behind highlighter washes**, not the flat token. A role tier that fails is darkened to its `deep` tier. The contrast table is **generated and build-failing** (§19). Colorblind redundancy is not required on screen (single known user); documents keep their non-color cues (§16) regardless.

---

## 4. Typography

Four families, committed as static fonts in `/Fonts` (OFL; see `Fonts/LICENSES.md`):

- **Source Serif 4** (Regular, SemiBold, **Black**) — headings, reading text, PDF body, covers. The literary voice. (Black is new: it anchors the reworked display sizes.)
- **Source Sans 3** (Regular, SemiBold) — UI controls, labels, list rows. The working voice.
- **Cascadia Code** (Regular) — machine text: keys, SHA-1s, URLs, paths, figures, countdowns.
- **Pinyon Script** (Regular) — a copperplate **calligraphy** hand, colored ink-blue, used **only** for the owner's voice messages.

**Serif Italic has exactly one role**, and it is the empty state's invitation (§7). It was put to the
owner a second time when Foreword's prayer page drew its past-entry quotations in it, and the answer
was to keep italic where it is — so a printed quotation carries its quotation marks and the muted ink
and nothing else. That is a deliberately weak signal, which makes both of those load-bearing rather
than decorative wherever a page quotes something.

### Type scale (reworked — bigger, more contrast at the top)

| Token | Family/weight | Android sp | Analysis PDF px | Use |
|---|---|---|---|---|
| `Display` | Serif **Black** | 44 | 72 | Screen headers, chapter openings |
| `Title` | Serif SemiBold | 28 | 46 | Section heads |
| `Heading` | Serif SemiBold | 18 | 32 | Card titles, sub-heads |
| `Body` | Sans Regular (UI) / Serif Regular (reading) | 16 | 30 | Default text |
| `Secondary` | Sans Regular | 14 | 26 | Supporting text, status lines |
| `Label` | Sans SemiBold | 13 | 22 | Field labels, buttons, chips, tab labels |
| `Mono` | Cascadia Regular | 14 | 24 | Technical strings, figures |
| `Voice` | Pinyon Script | ~26 | — | The owner's voice, in ink-blue |
| `Countdown` | Cascadia Regular | 40 | — | Cutover / unlock timer |
| `CoverTitle` | Serif SemiBold | 72 | — | Book/report covers |

Headings are ink-black Serif with a **full-width marker underline** (a gently curved stroke rising from the left, in the section's functional or identity hue). Body reading stays at 16. Line height 1.35 for reading, 1.2 for headings. Sizes are never invented ad hoc.

---

## 5. Spacing & shape

- **Spacing scale (dp/px):** 4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96.
- **Airy by default, just fuller.** Screen edges 24dp; whitespace is the primary separator. Working screens may carry more content than the Flyleaf home, but the 24dp edges and whitespace-first rule hold. Hairlines only where scanning demands (list rows, tables); boxes only where grouping demands.
- **Corner radius:** soft-cornered stationery, 10–16, **never pills**. Inputs/buttons/cards 12; the switch is a **rounded rectangle** track, not a stadium.
- **Strokes:** hairline 1dp (UI) / 1.5px (PDF).
- **Touch targets:** minimum 48dp, strictly; list rows 56dp. Refined controls (switch, checkbox) keep small visuals with a padded ≥48dp hit-area.
- **No shadows, ever.** Paper does not cast digital shadow. Overlays separate by a dimmed **PaperDeep haze** + hairline (§7), not elevation.

---

## 6. Motion & feedback

- **Three durations:** `Quick` 150ms (state changes, tint fades, toggle slide, inline status), `Standard` 200ms (screen transitions, accordion expand, dog-ear entry), `Ink` 320ms (watercolor bloom / ink-spread on hero moments & overlays only). Decelerate curves; nothing springs or bounces.
- **Screen transitions** are a horizontal page-slide with a **subtle page-curl** at the leading edge. Switching binder-tab areas also uses the page-curl (every navigation is a page turn). The frameless cover-art load screen hands off to content with a page-curl.
- **Reduced-motion** drops the big movers (slides, curl, watercolor bloom, ambient drift) and keeps only small opacity fades and instant state change. Animated texture freezes.
- **No haptics, no sound**, anywhere we control them — including all new controls.
- **Loading is words.** A quiet `Secondary` status sentence in the **In-progress (teal)** hue is the primary signal ("Composing page 12 of 40…"); a determinate meter shows a known count. The spinner is **retired** — unknown duration is a sentence, not an ornament.

---

## 7. Components

| Component | Recipe |
|---|---|
| **Primary button** | Marker-textured **Action (pine)** fill, `PaperBright` text, Label type, radius 12, padding 18×11. One per screen. In-flight: the button disables and a status line (In-progress hue) reports; no in-button spinner. |
| **Secondary button** | Pencil **outline** on paper, `Ink` text, radius 12. |
| **Quiet button** | No fill/border, `InkMuted` text; tertiary actions. |
| **Destructive** | *None.* Destructive acts happen immediately with an undo dog-ear (§13); truly irreversible acts use the inline countdown. |
| **Pinned bottom action bar** | Appears only when a screen has a primary action. Up to two — primary (marker-pine, right) + optional secondary (outline); a quiet Cancel may sit left. Never two primaries. 1dp hairline top rule, paper ground. |
| **Switch** | Rounded-rect track: `PaperShade` off, **functional-hue** on; ink-line paper knob, no shadow. |
| **Checkbox / radio** | Hairline at rest; functional-hue on. Indeterminate renders as a **circle** (vs the square check). ≥48dp hit-area. |
| **Segmented control** | Within-area view switch; selected shows the **highlighter wash** of the functional hue. |
| **Chips** | Four defined types: **filter/choice** (toggle, highlighter wash on), **input/removable** (× to remove), **status/read-only** (no tap target, exempt from 48dp), **assist/action**. |
| **Dial** | A circular control for a single value operated **while moving or not looking**: 270° hairline track, colored-**pencil** filled arc, paper knob, the figure in Cascadia at `Countdown` size in the centre. No shadow, no gloss. Use only where a big imprecise target genuinely beats a small precise one — a slider is the default and this is the exception. Added in P6 for Score's workout tempo. |
| **Stepper** | ±, disabled at bounds, long-press to repeat; value in mono. |
| **Slider** | Range (two-thumb) supported; filled portion is a colored-**pencil** track; value bubble on the thumb while dragging. |
| **Text field** | `PaperBright` fill, 1dp `Hairline`, radius 12. Focus = 2dp **Action** border, no glow. Mono variant: `PaperDeep` + Cascadia. Required fields carry a **faint identity-tinted fill**. Multiline auto-grows. Validation on blur, then live. Two secret variants: **masked** (dots + reveal) and **clear-secret** (mono, always visible, no clipboard block — the vault passphrase). |
| **Picker** | Inline-**accordion**: the field expands in place (no overlay, no dialog). |
| **Flip date picker** | Single-date, split-flap style: three flip columns (Month / Day / Year), flip up/down. |
| **Search field** | Leading lens glyph, clear affordance, empty-state invitation. It **filters in place** — it narrows the list it sits above; it never navigates to a results screen. **Pinned directly under the screen header**, above any sort/segment control, and it does not scroll away. It appears on **any list that cannot fit one screen** and is omitted where the content already does. The field takes the area's **identity border** while it holds a query, matches take the **highlighter wash** behind full-strength ink (§8 Selected), and the count line reports **matches of total**, not a total. Where a list spans more than one source, the filter spans them too — one field, one list, no seam. |
| **Artwork slot** | Externally-supplied imagery — album and podcast art — is **content, not palette**: it keeps whatever colour it arrives with. What the system governs is the **window** it sits in. Every piece of art is framed by a hairline keyline and **never bled to the edge** — the ex-libris plate anatomy (§12) applied to someone else's picture, which is what lets a saturated cover sit on paper without either one losing. Art earns a slot only where it **distinguishes** something: a flat list of many items leads each row with it; a list already scoped to one work (an album's tracks) carries none, because every row would repeat the same picture. Missing art falls back to the miniature bookplate, so a ragged library reads as plain bindings rather than broken images. |
| **Cards** | Five types: **warning** (`PaperDeep`, 3dp Action left rule), **explainer** (`PaperDeep`), **hairline** (`PaperBright` + 1dp hairline), **cover** (watercolor ground + title), **stat** (a single mono figure + label). |
| **List rows & tables** | 56dp rows (Sans 16 `Ink` + Sans 14 `InkMuted`), hairline dividers, no zebra. Real tables where truly tabular (header row, aligned columns, horizontal scroll). Figures in Cascadia mono, right-aligned, tabular; negatives/deltas take the functional hue. Rows expand via the one inline-accordion pattern. |
| **Action card** | Long-press → a **centered** paper card over a **PaperDeep haze** scrim (tap-out dismisses), animated in with the Ink reveal. Item actions only; never a confirm dialog. |
| **Undo dog-ear** | The confirmation replacement: the act happens, then a **dog-ear folds into the top-right corner** carrying a **back-turn arrow** (functional hue, §13). It is not a bar and it turns to nothing — **tapping it reverses the act**. It holds **6s**, then flattens. While it shows it **overrides any notice fold**, so the corner never means two things at once. Never a dialog, never a toast. |
| **Inline countdown** | For the rare irreversible act: a Cascadia countdown + Cancel; executes only when the visible countdown expires; leaving cancels it. |
| **Status line** | The loading/progress voice: one `Secondary` sentence in the In-progress hue, updated in place. |
| **Empty state** | Centered `Serif` italic invitation on plain paper + the primary action. (The frameless cover art is the *loading* screen, not the empty one.) |
| **Screen header** | Every screen opens with an in-page `Display` Serif title + full-width marker underline; **stays pinned**. No standing top app bar. |
| **Phase chip** | Hairline pill (radius 10), Label type: "scenes 12 of 40". Active `Ink`, finished `InkMuted`, problems take the Problem chip. |

**Success is an inline state change.** The screen shows the new truth. Toasts and success alerts are banned; failures surface inline where they happen, worded per §14.

---

## 8. The state matrix

One rule set for every interactive element.

| State | Treatment |
|---|---|
| Rest | Hairline border, paper fill, ink text |
| Pressed *(hover folds in on pointer surfaces)* | Inset-paper: subtle inner hairline, texture lifts briefly — no color change (Quick 150) |
| Selected / active | Highlighter wash of the functional hue, behind full-strength ink |
| On / checked | Functional-hue fill (default Action pine) |
| Indeterminate | A circle mark (vs the square check), functional hue |
| Read-only | Shown, not editable; no caret, not dimmed |
| Disabled | Desaturate + dim (~45%); never silently removed from the DOM |
| Loading | Status sentence in the In-progress hue; determinate meter when known (spinner retired) |
| Error | Problem-rose border + helper text that states the fix |
| Success | Inline state change — the row updates; no toast, no banner |

The **custom focus ring is dropped**; the platform/TalkBack focus indicator is retained for switch-access.

---

## 9. Navigation & the app frame

- **Binder tabs.** In-app navigation lives on **right-edge divider tabs**, like a school notebook. A **26dp glyph above a horizontal label** — the label is never rotated — in a **66dp gutter**. A **page-edge line** runs down the right; inactive tabs are clipped at that line and point out toward the screen edge; the **active tab is page-colored and open on the page side (no left border)**, blending to the page at the seam without crossing into content. Tabs never overlap content controls (reserved gutter); the strip scrolls if there are many. **Aim ≤5 areas; six fits the gutter without scrolling** and has been drawn — Score runs six (Music · Podcasts · Playing · Discover · Workout · Settings). Past six the strip scrolls, which costs a tab its at-a-glance position, so six is the practical ceiling rather than a second aim.
- **The tab carries the color, not the glyph.** Tabs are **color-coded per area**, and an inactive tab is cut from **its own colored cloth** — the area's `fill` tier — so the strip reads as coded dividers at rest, the way a real binder does. The **active** tab stays page-colored per the rule above and takes its area's hue **in the ink** (glyph and label) instead. Tinting an inactive glyph on a neutral tab is the inversion of this and reads as a row of disabled buttons.
- **Two areas are fixed across every app**, so the parts that are the same everywhere look the same everywhere: the **home area takes the app's identity hue** (§3), and **Settings is always the neutral gray** — never an identity or role hue, in any app.
- **The middle areas may draw on the semantic roles**, and in Post they do — filed runs to the caution ochre, rolodex to the positive leaf. This is in tension with §3's rule that an area tint must never read as a status; it was raised once and the owner chose by color feel. Recorded as a decision rather than a precedent: an area color that happens to be a role hue is a tab, not a state, and nothing on the page inherits it.
- **The strip is top-aligned** under the header, not centered in the well. A centered strip moves every tab whenever an area is added or removed; a top-aligned one keeps each tab where the hand last found it.
- **The frame.** Pinned Serif header (top) · binder tabs (right, always) · action bar (bottom, only when a screen has a primary action). Content scrolls in the well between; the frame stays thin (hairlines, no fills) so it reads airy.
- **Working landing.** Apps open onto a working "now" view (not a blank hub). The shared *pattern* (not a fixed component) always carries: the in-page Serif header, a "now" status block (display-only), the binder tabs, and a reachable primary action.
- **Back** = system back + OS edge-swipe (reads as sliding the page back); **going to an area's root** = **tapping the binder tab you are already on**, from any depth, exactly as you grab a tab in a real binder rather than paging backwards. Navigation therefore adds **no chrome** — no back button, no home button. The one exception is a screen that claims horizontal swipe for itself (a transport that swipes between tracks): edge-swipe back is ambiguous there, so *that screen* carries a back affordance, not the whole app. Where a filter field is on screen, back stacks — the first press clears the query and dismisses the keyboard, the second leaves the screen. **Overflow** is inline-only — no kebab menu; item actions come from the long-press action card.

---

## 10. Iconography

In-app glyphs are single-style **colored-pencil line icons**: pencil-textured 2dp strokes, geometric, no fills, capped for legibility at 16dp. Three sizes — **16 / 20 / 24** — inside ≥48dp targets. Ink by default; a **live/active** glyph takes the relevant **functional hue** (no single fixed accent). One shared set, generated and governed by `Tools/IconStudio`.

---

## 11. Generated PDF & e-ink documents

Documents render through the **Document (muted)** profile and carry **grain only** — no pencil/marker/watercolor texture in report bodies (print legibility + e-ink limits). The old terracotta rules/bullets/covers regenerate to **muted pine**. Non-color cues (dash/dot/fill/marker per role) are retained so meaning survives grayscale. All page specs (margins, folios, cover, contents) are otherwise unchanged from the prior bible; only the palette and the accent color move.

---

## 12. Launcher icons & covers

Each app is a **book with a painted cover**. The launcher icon follows the approved **Flyleaf hero template**: a watercolor ground, the ex-libris composition, real-paper texture — **no single accent detail** (color comes from the ground and glyph). Each app gets **bespoke painted cover art**; generation is **hybrid** (baked watercolor wash + procedural grain + vector glyph via IconStudio). Until an app's art is dropped in, IconStudio produces a **plain signature wash + glyph** fallback so a build is never icon-less.

**The ex-libris plate — the anatomy, in order.** The art is a **window set into the binding**, never a full-bleed image:

1. **Binding cloth** — the ground the plate sits on. Chosen per app (§3 identity hues).
2. **Fine rule** — a hairline frame inset from the edge, **all four sides**.
3. **Corner flourishes** — the quarter-curve motif at each corner, **bilaterally symmetrical**: each corner is a *reflection* of the top-left, not a rotation of it. Rotating one asymmetric curl through 90° produces a pinwheel with no mirror line and reads wrong on a plate. The window's inset must clear the flourishes or it paints over them.
4. **Illustration window** — the painted art, framed by a keyline. Rectangular by default; a shaped window (Flyleaf's arch) is a per-app choice, and dropping the shape does **not** drop the frame.
5. **Title band** — beneath the window, on the cloth.

**Two variants, one plate.** The **shelf cover** (cover-out on the Flyleaf home, portrait at `CoverPortraitRatio`) carries the title — a book with no title cannot be identified on a shelf. The **launcher icon** (square) omits it: Android already prints the app name beneath the icon.

**Binding cloth is chosen per app and may contrast its own art.** It is not required to match the identity hue: a binding that harmonises with the cover art makes the book the hardest of six to pick out of the row. *Score's binding is **cream linen** with the rule, flourishes and lettering inverted to ink* — chosen because it is the only light book on the shelf and so is findable at a glance. *Post's binding is **deep indigo** against a warm mid-century mailbox painting* — and because that contrast is where the book's only cool color lives, Post's identity hue is sampled from the **cloth** rather than the art (§3).

**A corner motif need not be a curve.** The quarter-curve is the default, not the requirement — the rule that matters is the reflection. Post's corners are a **mitred bracket** (an L with an inner rule), chosen because the art it frames is flat and geometric, where a curl fights it, and because a bracket is the variant that stays crisp at 48dp.

**The maker's mark is not on the launcher icon or the shelf cover.** It appears on PDF/report covers, settings screens, and on the cover art **when that art is expanded into the app-open (frameless cover) load screen** — nowhere else.

---

## 13. Notifications

Two markers, taking their cue from the Mockup 01 icon direction:

- **Bookmark (out-of-app / launcher).** A ribbon bound at the spine that sticks out the **top** of the book near the left spine, **squared** end, in the functional hue — the launcher's "this app has a notification" marker.
- **Dog-ear (in-app).** A real **folded page corner** (top-right): a cream flap folded down revealing a **functional-hue** notice triangle behind it, with a crease line and a highlight along the fold. **One dog-ear, no count** (presence, not quantity) — so when several things wait at once, one fold carries them all and turning it lists them. It appears with a quiet fold-in (Quick 150) and never buzzes.

  The fold has **two jobs, and never both at once**. As a **notice** it marks something waiting and you *turn* to it when ready, with no clock running; it is **scoped to its page**, so on a list it holds what waits for the app and inside an item it holds what waits for that item. As an **undo** it carries a **back-turn arrow**, turns to nothing, and *reverses the act* on tap; it holds 6s (§7) and **overrides any notice fold** for that window, because an undo is a closing window rather than something that waits. Nothing else in the app announces itself: errors and pending decisions live behind the fold, never as banners or cards on the page.

  **Turning a notice fold turns the page to a Waiting page** (mockup 17, D162) — not an action card over the page: a Serif header, the page rule, a status line ("For Accounts · newest first"), and what waits written straight onto the graph paper, in the app's own binder frame with the area's tab still lit. Each notice is what happened and what to do next (§14), static and neutral, with a thin bar in its role colour, a **date** beside its headline in the relative-time rule (§14) and at most one secondary-button action. The page is **newest first by date**: dated notices run latest to earliest, undated ones keep a stable order of their own, and a notice an owner states again unchanged keeps its place. A notice goes when its cause does, and the page is live: if the last one resolves while it is open it says "Nothing waiting."

  The fold carries **six hues**, one per role it can report: Problem (a broken connection), Caution (something going stale), Action (the undo), Resolved, **Reference** (slate: something only worth knowing, such as how much of a plan's allowance is used) and **Progress** (teal: waiting, not wrong, the colour Score's Playing area and Index's Snowball already wear). Reference and Progress joined the kit's `DogEarHue` and the native `DogEar.Hues` together, so the component stays one across the MAUI and native heads. The ear is 40dp, in the page's top-right corner flush to the binder gutter, with the flap's loose tip lifted off the page and the back-turn arrow drawn as a stroked arc and head, not a glyph.

Push stays almost silent (§1): only new comms earn a buzz; everything else waits as a quiet dog-ear/bookmark or arrives on the reMarkable.

---

## 14. Voice, copy & formats

- **American English**, sentence case everywhere (including buttons). No emoji, no ⚠ glyphs.
- **Time is 12-hour** with an analog sensibility ("7:42 pm"); the home shows a drawn analog face.
- **List timestamps are relative** ("just now" → "5m ago" → "2h ago" → "yesterday" → "Tuesday" → "Jun 30").
- **Error template (binding):** *what happened* + *what to do next*.
- **Numbers & units:** counts "12 of 40"; durations "42 min"; interpuncts (·) join metadata; em-dashes (—) join clause-level status.
- **Toggles name the thing, not the verb** ("Listening Genius", not "Enable Listening Genius").
- **Owner's voice.** Functional labels/buttons/status/errors stay **static-neutral** (never sent to a model). **Situational messaging** — welcome, empty-state invitations, book-complete notes, and the one-line summaries an app names — may be **voiced** in the owner's voice, set in the calligraphy hand, but must **degrade silently to the neutral fallback** (or hide the line). **Errors are never voiced**: they stay in the template above, and each app keeps its voiced occasions as a closed list. A voiced line is built only from facts the app hands it, and never from a raw amount or an account's identity. See `Storyworthtelling.Voice`.

---

## 15. Terminology glossary

One noun per concept, everywhere:

| Term | Meaning |
|---|---|
| project | A manuscript being reviewed in Canonizer |
| scene · evidence image · reference sheet · report | (unchanged Canonizer nouns) |
| admin mode · block/unblock · cutover · deliver · thread · tile/tray · feed/episode | (unchanged) |
| **binder tab** | The right-edge divider navigation to an app area |
| **dog-ear** | The in-app folded-corner marker: a waiting item / undo |
| **bookmark** | The launcher ribbon marker: an app has a notification |
| **cover** | An app's painted book cover (icon + hero art) |
| **wash** | A watercolor ground |
| **maker's mark** | The owner's signature emblem |
| **flip picker** | The split-flap single-date control |
| **identity hue** | An app's signature color, sampled from its cover (never a semantic role) |
| **correspondence** | Post's word for a thread: one person, every channel, in time order. *This narrows the suite-wide "thread", which stays the general noun everywhere else — inside Post, say correspondence.* |
| **mailbox** | Post's home area: everything that has arrived and is not yet dealt with |
| **filed** | Post's done area: dealt with, deleted at the source, kept as a ghost |
| **rolodex** | Post's contacts area, keypad included |
| **circulars** | Machine mail — marketing, newsletters, bills, transactional — banded below the human mail |
| **dispatch** | Post's send verb |

---

## 16. Device-adaptive color (Document profile)

The role model resolves per profile at render time. **App** = the lively §3 set (screens). **Document** = the muted set for reMarkable Paper Pro (color e-ink), rM2 (grayscale), and the screen/Drive PDF. The muted set keeps the deliberate, separable **rM2 gray ramp** so roles stay distinguishable after desaturation, and **every role carries a non-color cue** (weight, dash, fill, marker) so no document meaning depends on hue alone. Grounds shift per device. `PaperPalette.Resolve(role, tier, profile)` in `Storyworthtelling.Paper` is the only way above the tokens to get a color.

---

## 17. Accessibility (binding)

- **Contrast:** AA per §3, verified against the worst-case textured/washed pixel; the table is generated and build-failing.
- **Font-scale resilience:** every screen and **every new component** (binder tabs, tables, accordions, picker, flip calendar) must survive Android font scaling at **1.3×** with no clipping; texture fades if it would muddy at scale.
- **Touch targets:** 48dp minimum, strictly (refined controls use a padded hit-area).
- **Texture never muddies text or fine marks** beyond the contrast cap; glyphs stay legible at 16dp.
- **Single owner:** colorblind redundancy is not required on screen; documents keep their non-color cues regardless. **LTR only** (English) — documented assumption; the binder tabs, page-slide, and back-swipe are not mirrored.
- Technical strings are selectable (system long-press); sanctioned ones offer explicit copy.

---

## 18. Future-project defaults

- **New MAUI app / native head:** adopt the shared `Storyworthtelling.Paper` tokens + the material engine, the pinned-header + binder-tab frame, and the pinned bottom action bar.
- **Angular (when it exists):** an SCSS role-token map mirroring these names — deferred until Angular actually lands.
- Every new project references the shared tokens (thin re-export, §19) and ships a leak-guard test from day one.

---

## 19. Token-file registry (leak-guard allowlist) & generation

The palette is generated from **one OKLCH source spec** (roles × tiers × profiles) that emits the C#/XAML tokens, the WCAG contrast table, and the rM2 gray ramp. `Storyworthtelling.Paper` is **the design library**: the UI runtime sources color from it, not from per-app literals.

Only these may contain color hex or type-size literals:

| Project | Token files |
|---|---|
| Shared: Paper | The OKLCH **source spec** (`Storyworthtelling.Paper/PaletteSpec.cs` + `Oklch.cs`) and the generated `PaperColorTokens.cs` it emits (roles × tiers × App/Document profiles + gray ramp) |
| Shared: app identity | `Storyworthtelling.Paper/AppIdentityTokens.cs` — each app's **identity** hue for what a printed page signs with it (a document rule, the app's name in the owner's hand, a tab's cloth). Not the role palette: a role says what something *means*, an identity says *whose* it is, and nothing here may stand in for a role. Held to `Tools/IconStudio/Tokens.cs` — the source, where the hue was chosen — by parity test, because a console application is not something a library can reference |
| Shared: materials | `Storyworthtelling.Paper/MaterialTokens.cs` — the **materials** a drawn piece of furniture is made of: the desk's wood and grain, the shelf's ledge and bronze, and the note card and post-it a label lies on. Neither a role nor an identity — a role says what something *means*, an identity says *whose* it is, and a plank of wood is neither. It is shared rather than per-app because §19 leaves an adopted MAUI head no token file of its own, and because the second app to draw furniture should find a wood rather than invent one. Two of its combinations are **deliberately** under §17 — muted ink on wood at 1.40:1, cream on bronze at 2.80:1 — which is why every label lies on paper and no bookend is lettered; `MaterialContrastTests` records both so a screen that forgets fails the build |
| Shared: printed type | `Storyworthtelling.Paper/PaperTypeScale.cs` — the **printed** ladder, in points, derived from the screen ladder by one registered ratio (D30). Not a second scale: a guard multiplies the native sp ladder, read from its own source, and asserts these are the answers |
| Shared: MAUI kit | `Storyworthtelling.Maui/Resources/Styles/Paper.xaml` — the role table and component styles for the MAUI heads, the counterpart of `Codex.Ui.Android`'s `PaperUi`/`PaperTheme`. Checked against `PaperColorTokens` **by reflection**, not against a second typed copy |
| Per-app | `*/Design/DesignTokens.cs` + `Resources/Styles/DesignTokens.xaml` — **thin generated re-exports** of the shared tokens (still leak-guard-scanned). **A MAUI head that has adopted the kit above has no token file at all** and is scanned for hexes everywhere; Ledger and Canonizer still carry their own and adopt the kit at their own phases |
| Tools | `Tools/IconStudio/Tokens.cs` (kept in parity by test) |
| Fonts | `/Fonts` — Source Serif 4 (incl. **Black**), Source Sans 3, Cascadia Code, **Pinyon Script** (all OFL). All eight are embedded in `Storyworthtelling.Paper` for the Skia renderers, held by a guard: a face the printing library cannot load is not an error — Skia substitutes one and says nothing, and the page is already printed by the time anyone looks |
| Branding | `/Branding/owner.png` (owner-supplied) |

**Tests (build-failing):** the **contrast** table (all role×tier×profile ≥ AA vs the worst textured/washed pixel); **screen↔document role parity** (every role resolves in both profiles); **generated↔committed parity** (a fresh generator run equals the committed tokens); the **material-legibility** cap; and C#↔XAML parity. A change to this bible is a change to the source spec and the tests, in the same commit.

**Printed type is the screen ladder times one ratio (D30), never its own scale.** The sheets used to choose a size at each call site — 8, 9, 10, 11, 12, 16, 20 and 22 points scattered through the composers, none derived from anything. That is a second, unregistered ladder with the same disease as a second copy of the palette: nothing compares it to the system it belongs to. The ratio is **0.75** — print type runs smaller than screen type for the same apparent size, and three quarters is both the conventional figure and the one that lands the body role on the 12pt the sheets had already reached by eye, so the derived ladder agrees with the best of the literals rather than overruling all of them. Re-fitting is expected to move pages: it moved one, the calendar timeline, whose event strips were fixed at heights chosen for an 8pt face and would have clipped every title silently at 9.75. Strip heights are now measured from the font.

**A private copy of the palette per app was how the MAUI side drifted, and the drift was invisible.** Each MAUI head kept its own `DesignTokens.xaml` and its own leak-guard test holding a *second* hand-typed copy of the same values to check it against, so both could be wrong in the same way — and were: the accent was still the retired terracotta long after §4 retired it, Display drew in Semibold where §4 gives every screen header Serif **Black**, the Label role took the full ink, and the owner's voice had no style anywhere. Nothing failed, because nothing compared either copy to the generated source. The kit's guards therefore check the palette against `PaperColorTokens` by reflection and the type ladder against the native tokens' own source file; a typed expected value is the failure mode, not the test.

**The per-app identity ladders are the exception, and are covered differently.** They are hand-committed rather than emitted by `PaletteSpec`, so generated↔committed parity does not reach them — which is how §3's account of Score's hue drifted from the hex it describes without anything failing. The compensating control is the **hue-separation guard** (§3): every committed identity tier is read back through `Oklch.FromHex` and measured against `PaletteSpec.RoleWheel`, so a ladder that crowds a role, or a role that moves onto a ladder, fails the build. Bringing the ladders under the generator proper is open work.

---

## 20. Governance

Every real screen, icon, chart, and cover is **mocked up, shown, adjusted, and approved** before it is built (the §03 gate). Approving this bible locks the **vocabulary** — materials, color, controls, patterns — not the individual screens. Each existing app adopts the kit during **its own rebrand phase**, with its cover art and identity hue, so apps never half-change; **Flyleaf (P5)** is the pilot that proves the kit before the rest. The onboarding-guide artifacts are refreshed at each phase boundary.
