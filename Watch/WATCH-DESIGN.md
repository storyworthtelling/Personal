# WATCH-DESIGN.md — the watch design language (draft for approval)

**Status:** draft, round 1. Nothing here is approved until the owner says so. Items marked **O#** are
open questions for the owner, and every one of them is listed in §14.

This is the single source of truth for how the four watch apps look, move, buzz and speak: **Point**
(the face and the lockdown, Flyleaf's counterpart), **Dash** (Post's), **Wave** (Score's) and **Beat**
(Constitution's). It shares **nothing** with Paper & Ink (`DESIGN.md`) by the owner's decision (A33):
not its palette, fonts, materials or voice rules. The two languages meet only at the repository's
general rules (no hex or type-size literals outside registered token files, invented data in every
mockup) and at the one standing interaction rule that suits both: destructive acts are undo-first.

Answers cited as **A#** are in [`P14-OWNER-ANSWERS.md`](P14-OWNER-ANSWERS.md).

---

## 1. Principles

1. **Calm, then eyes up.** A glance takes under two seconds and leaves nothing to come back for. The
   watch exists so the phone can stay home and the owner can look at the world (A1, A36).
2. **Abstract.** Shapes, light and colour carry meaning; words are the last resort (A46).
3. **Nothing anxious.** No counts, no unread marks, no badges, no streaks, goals or scores, no red
   dot. The face shows time and date only (A34). Something arriving is told by touch, not by a mark.
4. **Glass with light through it.** The one material: shapes are lit glass on black (A36).
5. **Touch before sight.** Four buzz patterns, each with one meaning, so the wrist can be read
   without looking (A35).
6. **One short sentence, at most.** When words are needed, there is one sentence and no more (A37).
7. **Undo, never confirm.** A destructive act happens at once and can be taken back for a moment.

---

## 2. Ground and light

- **Ground:** true black everywhere (an OLED pixel that is off). Nothing on the watch has a coloured or
  grey background, apart from the system's own cards.
- **Glass:** a lit shape has a soft radial highlight high on its left, its full colour through the
  middle and a deeper tone at the edge, with a thin bright rim. Glyphs carry a faint glow of their
  own colour.
- **Always-on:** glass becomes outline. No fills, no glow, no motion, lightness kept at about a third
  of the lit value, and under 5% of pixels lit. Point's always-on face is the hour shape in outline
  and its date is hidden (round-2 studies).

## 3. Shape

- **The polygon vocabulary** belongs to the face: the hour is a shape with as many sides as the hour
  on a 12-hour clock (1 = a dot, 2 = a line, 3 = a triangle … 12), each standing on a point at twelve
  o'clock. After noon, the same shape returns inside an outline of itself (A46).
- **Glyphs** (family A, A50): each app is its name drawn once, on a round black icon.

  | App | Glyph |
  |---|---|
  | Point | a single point of glass light |
  | Dash | one short horizontal dash with round ends |
  | Wave | one full sine wave, one period |
  | Beat | one heartbeat trace: flat, a peak, a trough, flat |

  The glyph is the only mark of an app. The app's name appears only in the watch's system settings
  and in the repository (A48).
- **Stroke weights** are proportional to the icon: the dash at 14% of the icon's width, the wave at 8%,
  the beat at 7.5%, and the face's rim lines at 1.8% of the face's radius.

## 4. Colour

### 4.1 The face's minute colour

The face's hour shape takes a hue from the minute, going round the full colour wheel once an hour:
red at the top of the hour, through yellow, green, blue and violet, and back to red (A34). Its
lightness and saturation stay fixed, so a shape is equally bright at every minute.

### 4.2 Each app's day colour

Each app has its own colour range and drifts across it **once a day and back**, not with the minute
(A51). Its position follows the sun's rhythm: at one end at midnight, at the other end at noon, and
easing between them, never jumping. Every Dash glyph, tile and accent shows the same colour at the
same moment, because the colour depends only on the time of day.

| App | Midnight | Noon | Proposed mapping (O1) |
|---|---|---|---|
| Point | violet | seafoam | night violet, day seafoam |
| Dash | navy | maroon | night navy, day maroon |
| Wave | slate | amber | night slate, day amber |
| Beat | velvet | bright red | night velvet, day bright red |

- The colour between the two ends is mixed in a perceptual colour space (OKLCH), along the shorter
  way round the hue wheel, so Dash passes through plum rather than through green.
- **Legibility on black (O2):** maroon, navy and velvet are dark colours, and a dark glyph on black
  fades away. The proposal keeps each colour's hue and character but holds its lightness at or above
  a floor where it is drawn as a glyph, giving at least 3:1 contrast against black, and lets the glow
  carry the depth. The tokens record both the named colour and its on-black form.
- Colour is never the only cue: every app is also its own shape.

### 4.3 Neutrals and states

- **Text:** a cool near-white for primary words and a mid grey for secondary words. No coloured text
  other than an app's own accent.
- **There are no good/bad colours.** A trend is never green or red (the household rule since P13). The
  one exception is safety: **Ease off** (§7) is a buzz, not a colour.
- **Errors** are a quiet line in the app, in the secondary grey, and never a buzz (A37).

## 5. Type

- One family for words and numerals, with tabular figures. **Proposed (O3): Atkinson Hyperlegible
  Next** (OFL, drawn for low-vision legibility, which suits a 30 mm screen). It is not one of the
  Paper & Ink faces, by decision (A33).
- **Three sizes only:** *Glance* (a name or a time: the largest), *Line* (the one short sentence) and
  *Small* (relative times, the date). Values are set in the token file, never in code.
- Sentence case, no letter-spacing on words, and no all-caps except the weekday on the face's date.
- Times are relative ("5 min ago") in lists and 12-hour clock times elsewhere (A37).

## 6. Words

- One short sentence at most (A37). No voice seats on the watch: every line is the neutral sentence.
- **Loading** is a sentence that says what is happening ("Finding the song."), not a spinner.
- **Success** is silent: the thing simply changes state. There is never a "Sent!".
- **Errors** say what went wrong in plain words ("Can't reach TIDAL."), as a quiet line, with no buzz.
- No exclamation marks.

## 7. Touch (haptics)

Four patterns, each with one meaning and no other use (A35):

| Pattern | Meaning | Proposed shape (O4) |
|---|---|---|
| **Call** | a call is ringing | long, gap, long, repeating while it rings |
| **Text** | a text arrived | two short taps |
| **Rest end** | a workout rest is over | one long press |
| **Ease off** | worrying exertion: above the maximum zone, or the heart not recovering in rest (A24) | three slow strong pulses, with the words "Ease off" |

- Email never buzzes (A11). Text buzzes are held during a workout set and released in the rest (A26).
- Overnight only favourites (starred in the Rolodex) and repeat callers buzz (A12).
- All four shapes are checked on the real watch, since a pattern that cannot be told apart on the
  wrist fails silently (device check).

## 8. Motion

- Things ease in and out and never bounce. Fades take about 250 ms.
- **The face does not animate** apart from the minute colour drifting and the shape changing on the
  hour, which is a crossfade over a second.
- Wave's tempo changes crossfade over 5 to 6 seconds (A21).
- Reduced-motion and always-on both remove all motion.

## 9. Interaction on the wrist

- **Getting in:** tiles (Wave's Now playing, Dash's People waiting), voice, and the system app list
  (A35). If Wear OS refuses gestures on the face, tiles and the crown take their place.
- **Big targets:** a workout set is marked done by tapping anywhere (A23).
- **The crown** is volume in Wave and scroll everywhere else.
- **Undo (O5):** a filed or deleted message leaves at once, and an **Undo** line stays for six
  seconds, drawn as the app's glyph with a thin ring that empties as the time runs out. Then the act
  is final. There are no confirmation dialogs.
- **Choosing where Wave plays:** whoever started playback owns it. A single **Play on phone / Play
  here** line switches it (A20).

## 10. Point (the face)

- The hour shape at about half the face's radius, in its minute colour; after noon, the second ring at
  0.62 of the radius in the same colour, thinner and quieter.
- **Date:** weekday and day ("SAT 31") under the shape, small, in the shape's own colour at low
  saturation.
- **Nothing else**, ever: no complications drawn, no notification state (A34). Texts and calls arrive as
  cards over the face, and the face is unchanged after them.
- Always-on: the shape in outline, at about a third of its lit lightness; no date.

## 11. The apps

- **Dash:** opens on *People waiting*, newest first. A person is their name and one line, nothing
  else; no counts. Accent: Dash's day colour.
- **Wave:** opens on *Now playing*: the track, the artist and the controls, with the crown for volume.
  No lyrics, no album art beyond a small square (O6). Accent: Wave's day colour.
- **Beat:** during a workout shows what A23 lists (cadence, live HR, distance when running; the set
  counter and rest timer for strength). Live heart rate is the only Body value the watch ever shows
  (A29). Accent: Beat's day colour.
- **Point** has no screens of its own besides the face, apart from the vault passphrase entry on
  first run (A38), which shows the passphrase in the clear as the phone apps do.

## 12. Accessibility

- Colour is never the only cue: shape always differs.
- Text keeps 4.5:1 contrast against black, glyphs at least 3:1 (§4.2).
- Every touch target is at least 48 dp.
- The four haptic patterns differ in rhythm, not only in strength.

## 13. Tokens and guards

- **Registered token files (proposed):** `Watch/Watch.Domain/Design/WatchTokens.cs` holds the colour
  ranges, the type sizes, the haptic shapes and the motion durations; `WatchTokens` is the only place
  any of them appears. A leak guard fails on a colour hex or a type size anywhere else in the watch
  projects, as the phone projects' guards do.
- **Day-colour function:** `DayColour.At(app, timeOfDay)` is pure and unit-tested (midnight is one end,
  noon the other, and it eases between; it never jumps), and is the only way a watch screen gets an
  app's colour.
- **Face function:** `HourShape.For(time)` (sides, after-noon ring, minute hue) is pure and tested. The
  Watch Face Format XML (A40) is generated from it rather than hand-written, so the face and the
  tests cannot disagree.
- A parity guard keeps the four glyph definitions identical between the icon generator and the apps.

## 14. Open questions for the owner

| # | Question | Proposal |
|---|---|---|
| O1 | Which end of each range falls at noon | the table in §4.2 |
| O2 | The dark colours on black | keep the hue, hold lightness to a 3:1 floor, and let the glow carry depth |
| O3 | The typeface | Atkinson Hyperlegible Next |
| O4 | The four buzz shapes | the table in §7, then checked on the wrist |
| O5 | Undo on the watch | a six-second Undo line with an emptying ring |
| O6 | Album art in Wave | a small square, or none |
