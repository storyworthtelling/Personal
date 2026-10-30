# Constitution — P13 design notes

The approved mockups for **Constitution**, the suite's health app (Workouts, Meals and Body for two adults, with family meals shared). Every page below was drawn, revised with the owner and approved one page at a time; the implementation plan builds from them 1:1 and raises anything that cannot be built as drawn rather than substituting.

**The mockups are the specification:** the Constitution Design Book, `https://claude.ai/artifact/8unyM17kLRiTzCP9ipfzsb` (private to the owner). The product spec is the P13 UX design brief; DESIGN.md (Paper & Ink) governs everything this file doesn't override.

**Everything in the mockups is invented** — the household (Sam, Lee, Robin, Juniper), dishes, prices, readings, dates and the sample passphrase. No real routine, meal, age or health number appears in the book or here.

## Identity, cover and icon (pages 1–3)

- **Cover:** a 1950s public-health poster: a towering food pyramid built from real food, a man running up its left face and a woman up its right, in gym clothes and sweatbands, silhouetted on the pyramid's edges (study POS.6). Painted with `gemini-2.5-flash-image`, wordless by prompt. Commit it as `Branding/constitution-cover.jpg` (864 × 1184; invented art).
- **Identity hue: 230° cerulean, taken from the binding cloth** (Post's route), because the poster's own sky is an aged green-blue with no usable hue. 230° is the centre of one of only two windows left on the OKLCH wheel that clear every role and every sibling by 20°: 23.0° from In-progress teal and Reference slate, 51.9° from Score. No recorded departure is needed.

  | Tier | fill | ground · cloth | mark | text | deep |
  |---|---|---|---|---|---|
  | Hex | `#A4C1D0` | `#82ACC1` | `#1B6A89` | `#15546E` | `#143B4C` |
  | vs Paper | 1.78 | 2.29 | 5.70 | 7.83 | 11.24 |

  Lightness steps match Plate's and Post's (0.795 / 0.722 / 0.492 / 0.420 / 0.334). The binding is the **light** ground tier with rule, flourishes and title in ink (5.3:1), Score's and Pulp's construction. Document profile (muted) cerulean: `#476F83`.
- **Launcher icon:** full bleed, cropped to the runners and summit (rows 190–974 of the painting), no keyline, like Pulp's (D12).
- **What IconStudio needs:** `ConstitutionFillHex … ConstitutionDeepHex` and `ConstitutionClothHex` in `Tools/IconStudio/Tokens.cs`, held identical in `Storyworthtelling.Paper/AppIdentityTokens.cs`, `Codex.Domain` `DesignTokens.Identity.Constitution` and DESIGN.md §3's ladder table; an `IconSpecs` entry (`PlateImage: "Branding/constitution-cover.jpg"`, `CoverImage: …/drawable/cover_constitution.png`) with an empty `ConstitutionPlate` glyph; and a **crop rectangle** on the spec, which IconStudio doesn't have yet. Add Constitution to `IdentityTiersStayClearOfEverySemanticRole` and `IdentitySeparationTests`.

## The reMarkable documents (pages 4–10)

- **Page: the tablet's native 3:4, 445 × 594 pt** (D13), so a point on the page is a point on the glass. Type sits on `PaperTypeScale` rungs; the dose and the step times are the only large type.
- **Ink targets** (D14–D18), printed in muted ink so a black pen mark always stands out on the greyscale rM2, each with ≥ 8 pt clear space and its rectangle supplied to the reader from one shared source (Foreword's `JournalRegions` pattern):

  | Target | Size | Reads as |
  |---|---|---|
  | Tick box, large | 28 pt (9.9 mm) | Workout pages; any mark at all is a tick, a scribble included (the tablet has an eraser) |
  | Tick box, standard | 18 pt | Sheets; rows 26 pt apart |
  | Circle a word | 84 pt lanes, hairline dividers (68 pt for four words) | One circle per lane; one touching two lanes → confirm card |
  | Number box | one open box, 96 × 40 pt, unit printed outside | Assessment numbers |
  | "No, because" field | a shaded field opposite YES (D32) | A written reason denies, and wins over a tick on the same row (D17) |
  | Notes margin | 91 pt ruled column | Workout, recipe and digest pages |

- **Workout** (pages 5–6): cover = exercises, doses, total time and the three rules in words, no voice. One page per exercise: the dose at 54 pt, load and rest, the health-poster illustration until done five times (D20; each new figure is checked by a second Claude pass against the exercise description and regenerated, or omitted, on a mismatch, D21), **one** Score setting printed as "Score · 132 bpm · Steady" (D25; band names from `TempoBands`: Easy 90–125, Steady 125–150, Hard 150–180), the one voiced line, **easy / right / hard on every exercise page** (D23) and the Done tick, fixed at the foot. No last page (D24). Pounds (D22). Assessment test pages carry a number box and no rating (D27); the toe reach is circled. Injury day puts "Still sore?" at the top of every page on a light band; blank means better (D28); the once-only doctor suggestion is plain neutral type (D29).
- **Weekly meal sheet** (page 7), five pages, identical on both tablets: dinners (YES box, dish, cook, outlined-word markers SLOW COOKER / EATING OUT / NEW RECIPE, the shaded "No, because" field), breakfast and lunch per person **including the weekend** (D31), lunchboxes, groceries (an ink-framed over-budget flag in words, the Keep preview by store section, staples with blank lines), and the holiday ask-ahead only the week before. Line-by-line approval (D30).
- **Recipe** (pages 8–9): scaled ingredients and equipment; steps on clock times worked back from dinner (Cascadia at the Title rung); a slow-cooker variant, morning first; the kids' meal page; a **notes margin on every recipe page** feeding the taste profile (D35); the rating page with each person's last rating pre-underlined, all four rating the dish they ate (D37), and a **"We didn't cook this tonight"** box, which is how the planner now learns a dinner wasn't cooked (D68).
- **Saturday digest** (page 10), five pages: workouts done against planned; **every Body signal, every week**, over two pages (D39), weekly averages against the owner's own last 12 weeks, neutral ink, dotted gaps; the patterns block headed "not causes" (the only place correlations appear); meals (shared content); next week and "Notes to the planner". No voice.

## The phone (pages 11–18)

- **Base idea (D41–D48):** a family treasure box of scratch paper, messy and in progress. The well's ground is **thin crinkled sheet, ambiguous between scratch paper and doctor's examination-table paper**, procedural (creases and crinkle ticks on the suite's paper colour, one perforated tear line), never in the binder gutter. Each thing is a **scrap held by Scotch tape**, at a slight tilt, one thing per scrap, no overlaps. The material says what it is: **workouts are magazine clippings**, **meals and recipes are index cards** (Index's own `NoteCardHex`, head and body rule tokens, 22 dp pitch), the voice line a torn notepad leaf, dinner out a receipt (D46), **Body's charts scraps torn from a doctor's chart** (D44). No shadows: edge, tint and tilt separate a scrap.
- **Tabs (D40, D45):** Today (cerulean identity) · Workouts (deep red) · Meals (dark stovetop steel) · Body (deep green) · Settings (grey), deep cloths with paper lettering. A light steel would be indistinguishable from Settings' grey (both OKLCH L 0.80, near-zero chroma).
- **Today:** taped voice line, the workout clipping, the dinner card; rows open nothing; the only action is "I ate something else". Rest day swaps the voice line and drops the clipping.
- **Workouts:** today's large clipping, then seven day clippings with one-line reasons for any change. Long-press → action card over a full-screen PaperDeep haze: Skip this day / Swap with…; Swap turns the same card into the other days, a day without room greyed with its reason (D51); a rest day offers "Move a workout here…" (D52). Skip is immediate, struck through, with the pine undo dog-ear for 6 s.
- **Meals:** one tab with an on-page **This week / Recipe box** switcher (D54). The recipe box is **a pile of spread-out index cards, one small pile per cuisine** under a label strip (D53, D56–D58, D60), no tape, titles showing; a tap pulls a card out to show its history and **Cook this soon**, whose success is the card's own words. Search sits **under** the switcher (D59, a recorded departure from §7's order); nothing scrolls behind the action bar.
- **Adding a recipe:** an inline picker from the bar: scan through a Plate errand, or paste an NYT Cooking link (the only NYT route promised until an API spike says otherwise). After a scan, only uncertain words are asked, as choice rows with the best reading pre-chosen; a clean scan still shows its card once (D63); cuisine is assigned silently (D64). Empty state: a Serif-italic invitation, a mono "3 of 10" and ten card places.
- **"I ate something else" (D65, D66):** one photo through Plate, then straight back to the meal's card: "Estimating…", then "You had something else · about 900 kcal". **About the owner's intake only: no faces, no picker**, overriding the brief; the meal is inferred from the time of day.
- **Body:** Month / Quarter / Year switch, the voiced monthly sentence, then one doctor's-chart scrap per signal ordered by what moved most, **worsening first, a rising weight counting as worsening** (D70). A signal's page carries the larger chart, latest value and date, and its voiced sentence. Health Connect is asked per signal on first open. The bed offline: the line stops, "Waiting for the bed", and the error waits behind the dog-ear.
- **Settings:** plain like every app's (D71): pillars, the equipment checkbox rows (D72), household (read-only on the partner's install, D74), connections, the vault passphrase in the clear, diagnostics. **Every connection is loaded from and saved to the vault** (D75).
- **Notices:** one dog-ear, coloured by the most serious thing waiting: Problem (errors), Caution (a low-confidence read, answered on the Waiting page with two big chips), Progress (a scan to check), Reference (the box under ten), Action pine for undo (D77).
- **Voice seats and their neutral fallbacks** (never hidden; errors never voiced):

  | Seat | Neutral fallback (example) |
  |---|---|
  | Today's top line | "Strength at 5:30. Lentil soup tonight." |
  | Rest-day line | "A rest day. Dinner is out tonight." |
  | Body's monthly sentence (with the device reminder the day after) | "Sleep is down this month. The other signals are close to last month." |
  | A signal's sentence | "Sleep: 6.2 hours last night, 40 minutes less than October's average." |
  | Encouragement, each workout page | "25 lb today, up from 20." |
  | The meal sheet's intro | "Dinners for the week of Nov 16. One new recipe, Thursday." |

## Flyleaf (page 19)

Constitution joins the sibling apps on the cover-out suite shelf (D79). At 96 × 120 dp, three covers fit a row, so the fifth sibling wraps onto a second row, and **every row stands on its own full ledge** (`BookshelfLayout` already places one per wrapped row; the painter must draw it at full weight). The bookmark is Flyleaf's existing ribbon at the spine edge, in the notification's role colour, never a count.

## Open items carried into the plan

- IconStudio has no crop-rectangle parameter (page 3).
- The scrapbook materials (crinkle ground, clipping, index card, doctor's-chart grid, tape) need registered tokens and leak-guard coverage; the mockups' CSS values are the starting point.
- Known risks unchanged: the NYT Cooking API, the reMarkable "opened" signal (3-day fallback), the Sleep Number bed's unofficial access, ink read only at the morning run, the partner's calendar share.

## Decisions log

Append new rulings below rather than renumbering.

| # | Decision |
|---|---|
| D1 | **The cover comes first, and the hue is derived from it** rather than chosen off the wheel first. |
| D2 | **Five subjects, all in oil:** the morning walk, a plate of pasta, a men's church softball game, a "Go" traffic light, and a man running up the food pyramid. |
| D3 | **Other art styles are tried after a subject is picked,** on that subject only. |
| D4 | **The food pyramid run goes forward,** with a much taller pyramid relative to the runner and a woman partner running up the other side. |
| D5 | **Make the workout unmistakable:** gym clothes, sweatbands, sneakers. |
| D6 | **The style round paints eight manners:** oil, watercolor wash, linocut, vintage health poster, dark Victorian gothic, stained glass, impressionist, and clay. |
| D7 | **Gothic means both** dark Victorian gothic and stained glass; **sculpture means clay and plasticine.** |
| D8 | **The cover is a vintage health poster,** with the runners smaller and clearly running up the pyramid. |
| D9 | **Page 1 approved: the cover is POS.6,** the vintage health poster with the runners silhouetted on the pyramid's outer edges. |
| D10 | **Page 2 approved: the binding is Cerulean light cloth,** `#82ACC1`, and the identity hue is 230°, sampled from the cloth (Post's route). Ladder: fill #A4C1D0 · ground #82ACC1 · mark #1B6A89 · text #15546E · deep #143B4C. |
| D11 | **The summit stays as painted.** The crossed chocolate bars are not noticeable as a cross. |
| D12 | **Page 3 approved:** launcher icon B (full bleed, close crop, no keyline) and the shelf cover as drawn. |
| D13 | **Documents use the tablet's native 3:4 page,** 445 × 594 pt, so nothing is scaled on the glass. |
| D14 | **A choice is marked by circling a printed word** (easy / right / hard; Loved / Fine / Never again). |
| D15 | **A written number goes in one open box,** with the unit printed outside it. |
| D16 | **Page 4 approved:** the ink targets as drawn, printed in muted ink, from one shared set of coordinates. |
| D17 | **A tick and a reason on the same meal row is a denial.** The written reason wins. |
| D18 | **Any mark in a box is a tick,** however obscure, including a scribbled-out one. The reMarkable has an eraser, so a mark left on the page was meant. |
| D19 | **Page 5 approved:** the workout cover and exercise pages. |
| D20 | **Exercise illustrations use the health-poster style** of the cover. |
| D21 | **Each new illustration is checked by a second Claude pass** against the exercise description; a mismatch is regenerated, and a repeat failure prints no illustration. |
| D22 | **Weights print in pounds.** |
| D23 | **Each exercise page carries its own easy / right / hard,** not the workout as a whole. |
| D24 | **The workout's last page is dropped;** the stopped-early rule moves to the cover. |
| D25 | **One recommended tempo per exercise,** printed as "Score · 132 bpm · Steady"; no options. |
| D26 | **Page 6 approved:** assessment week and injury day as drawn. |
| D27 | **Assessment test pages carry no easy / right / hard;** the number is the answer. |
| D28 | **"Still sore?" left blank means better;** the question stops. |
| D29 | **The once-only doctor note prints in plain neutral type,** not the owner's voice. |
| D30 | **Breakfasts, lunches and lunchbox parts are approved line by line.** |
| D31 | **Weekend breakfast and lunch are planned too,** on the meal sheet with the weekdays. |
| D32 | **The "no" end of a row is a shaded "No, because" field** opposite YES (treatment B). |
| D33 | **Page 7 approved:** the five-page weekly meal sheet. |
| D34 | **Page 8 approved:** scaled ingredients and equipment, clock-timed steps, the slow-cooker variant. |
| D35 | **Recipe pages carry a notes margin;** the cook's notes go to the taste profile. |
| D36 | **Page 9 approved,** with page 8's notes margin. |
| D37 | **On a family-meal night all four rating rows rate the same dish;** each row names the dish that person ate. |
| D38 | **Page 10 approved:** the Saturday digest. |
| D39 | **The digest's Body section charts every signal, every week;** it runs to two pages. |
| D40 | **Tab colours:** Workouts red (blood pumping), Meals stovetop steel/black, Body a calming green; Today cerulean, Settings grey. |
| D41 | **The screens are a family treasure box of scratch paper,** messy and in progress: scraps held by paper clips, pins and Scotch tape. The box is a metaphor only, never drawn. |
| D42 | **Recipes and meals are index cards; workouts are magazine clippings.** |
| D43 | **The ground is thin crinkled sheet,** ambiguous between scratch paper and doctor's examination-table paper, drawn procedurally. |
| D44 | **Body's graphs and charts look torn out of a doctor's chart** (for page 16). |
| D45 | **The tab strip uses deep cloths** for Workouts (red), Meals (steel) and Body (green), with paper lettering. |
| D46 | **Dinner out is a restaurant receipt,** the one meal that isn't an index card. |
| D47 | **Scraps stay calm:** one thing per scrap, slight tilts, no overlaps. |
| D48 | **Every scrap is held by Scotch tape;** no paper clips or pins. |
| D49 | **Page 11 approved:** Today and the rest day, all scraps taped. |
| D50 | **Page 12 approved:** Workouts, Skip / Swap and its undo. |
| D51 | **In Swap with…, a day without room shows greyed with its reason** ("no 40-minute gap"), never hidden. |
| D52 | **A rest day can be long-pressed** to "Move a workout here…". |
| D53 | **Recipe-box dividers are by cuisine.** |
| D54 | **Meals keeps one tab with an on-page switcher,** This week / Recipe box. The tab strip stays at five. |
| D55 | ~~A recipe card expands in place on a tap.~~ Superseded by D56. |
| D56 | **The recipe box is a box of overlapping cards;** you see titles, and pick one out to see more. Supersedes D55. |
| D57 | **No tape in the recipe box;** the cards stand loose. |
| D58 | **Not an actual box:** a pile of spread-out recipe cards. |
| D59 | **On the recipe box, search sits under the switcher** (departure from §7's search-first order). Content never runs behind the action button. |
| D60 | **Cuisine shows as a small pile per cuisine** under a label strip. |
| D61 | **Page 13 approved:** Meals, This week and the recipe pile. |
| D62 | **Page 14 approved:** adding a recipe and the empty box. |
| D63 | **A cleanly read scan still shows its card once,** with only "Looks right". |
| D64 | **Claude assigns a scanned card's cuisine silently;** it can be moved by long-pressing the card. |
| D65 | **"I ate something else" is about the owner's own intake only:** no faces, no person picker. Overrides the brief's "tap the faces". |
| D66 | **The meal it replaced is read from the time of day** (before 11 am breakfast, before 4 pm lunch, then dinner). |
| D67 | **Page 15 approved:** "I ate something else", one photo, about you only. |
| D68 | **A dinner that wasn't cooked is told by ink:** a "We didn't cook this tonight" box on the recipe's rating page, read the next morning. |
| D69 | **Page 16 approved:** Body, the signal page, permissions and the bed offline. |
| D70 | **A rising weight counts as worsening** when Body orders its charts. |
| D71 | **Settings is plain,** like every app's: no crinkle, no scraps. |
| D72 | **The equipment checklist is §7 checkbox rows.** |
| D73 | **Page 17 approved:** Settings. |
| D74 | **Household is read-only on the partner's install;** the owner's install is the single writer. |
| D75 | **Every connection is loaded from and saved to the vault** (reMarkable, Sleep Number, NYT Cooking, Health Connect choices). |
| D76 | **Page 18 approved:** the dog-ear, the Waiting page and the voice fallbacks. |
| D77 | **The fold's colours stay as drawn:** errors rose, a low-confidence read ochre, a waiting scan teal, the 3-of-10 count slate, undo pine. |
| D78 | **Page 19 approved:** the second suite row on its own ledge, and Constitution's book. |
| D79 | **Constitution stands cover-out** on the suite shelf like every sibling; the bookmark is bound at its spine edge. |
| D80 | **Constitution is a native .NET 10 Android head over `Codex.Ui.Android`,** on Pulp's four-project template (finding §1.1). |
| D81 | **Groceries go to a new Google Keep note each week, through Keep's own share intent; nothing is read back** (finding Q1). The Keep API is enterprise-only and cannot edit items, so the pantry learns from the meal sheet's staples checklist alone. Overrides the brief's "Keep ticks read back" and "adds and removes only its own items". |
| D82 | **NYT Cooking is never scraped; only an official API or NYT's written permission** (finding Q2). A discovery spike asks first. Until it succeeds there is no NYT route: the weekly stretch dish is a household card not cooked lately, and page 14's "Paste an NYT Cooking link" row waits on the spike rather than being built against the terms. |
| D83 | **The partner doesn't use Flyleaf, so on her phone the brief's short list of notices also posts as ordinary Android notifications** (finding Q3). On the owner's phone they light the Flyleaf bookmark. Narrows the brief's "nothing is ever pushed" to the owner's install. |
| D84 | **Tab cloths may share a hue with each other or with a role colour** (finding Q4). A fold wearing its own tab's hue on Workouts or Body is allowed; `AreaHueTests` and DESIGN.md §9 are amended to say so. |
| D85 | **The reMarkable is paired the same way as Foreword:** Constitution uses the pairing that phone's Foreword uses (the vault's `RemarkableSecrets.DeviceToken`, restored into either app) (finding Q5). |
| D86 | **The partner's phone uses the system camera,** one photo per page for a recipe card and one photo for "I ate something else". **The owner's phone scans recipes through Plate's multi-page scan tray** as page 14 draws, which needs a new `READ_RECIPE` request in Plate (finding Q6). |
| D87 | **Constitution's own morning worker reads the ink,** scheduled after Foreword's delivery window, not Foreword (finding Q7). |
| D88 | **Releases:** A identity, shell and Settings · B Body · C Workouts · D Meals · E digest and notices · F the device checks (brief: Body first). |
| D89 | **One phase PR:** releases land as labelled commits on one branch, and the PR opens when CI is green (Index's and Pulp's way). |
| D90 | **The weekly Keep hand-off is a Reference notice on the Waiting page,** "This week's groceries are ready", with one button, Send to Keep, which opens Keep's share sheet (plan OQ1, PC10). |
| D91 | **A waiting scan's Flyleaf bookmark is Reference slate, as page 19 draws it;** its dog-ear stays In-progress teal (D77). The bookmark and the fold differ on purpose (plan OQ2). |
| D92 | **Plan round 1 approved:** overview, releases A–F, architecture and plan choices PC1–PC10. |
| D93 | **Plan round 2 approved:** Epics E1–E4, with an agent level and effort on every PBI. |
| D94 | **Plan round 3 approved:** Epics E5–E9. Rounds 4 and 5 are merged into one final round. |
| D95 | **The P13 plan is approved in full** (final round: Epics E10–E13, the cross-cutting sections and traceability). |
| D96 | **NYT spike outcome (C-11.4.1): no official API covers recipes, and NYT's terms bar storing recipes or feeding them to AI without written permission,** so Constitution builds as if the answer were NO: page 14's paste row and C-6.3.4 are not built, and the change goes back to the owner as a book revision. `NoScrapingTests` (G25) still ships. Evidence was second-hand (the research tool could not read nytimes.com), so the owner confirms at developer.nytimes.com/apis that no Cooking entry exists before sending the drafted permission request. |
| D97 | **The Release A device spikes (C-11.4.2 Health Connect, C-11.4.3 SleepIQ, C-11.4.4 ink round-trip) are not run beforehand; the owner tests after the app is ready.** The orchestrator builds best-guess implementations that log each platform and service outcome (shapes and outcomes only, never readings or logins) into the Diagnostics export and field notes, and the owner's results reopen PBIs. Accepted risk: if the cloud PDF carries no pen marks (R3), the ink design in Release C is rebuilt, not just tuned. |
| D98 | **Body readings may go to Claude, but only as the facts for the six voice lines** (Release A review note, owner's answer). Nothing else about Body leaves the phone except its own encrypted backup. Plan guard G13 reads: Body data reaches no outbound path but its own backup and the voice-seat facts; `BodyStaysHomeTests` allows exactly that one path and the seat facts carry only what the six seats need. Voice lines never name a person or a medical conclusion. |
| D99 | **Plate's recipe read returns text, not images** (owner's answer, Release D). Title, ingredients and steps are read on the phone by ML Kit and cross as three string extras; no photo ever leaves Plate and the frame is deleted after recognition. The plan's page-image payload for a Claude vision read (C-11.2.1, TF §1.8, C-6.5.3) is superseded; p.14's 'two alternatives' for uncertain words will come from a second Claude read of the text, if built. |
| D100 | **An install whose role is not yet known has the owner's rights over the household roster**, as in Pulp (owner's answer). A fresh phone can set the household up; such a phone can never create the shared file; the role becomes known once the vault passphrase restores it. |
| D101 | **A blank 'Still sore?' always means better** (literal D28; owner's answer), including on a day the workout was skipped. Only a blank that was actually read counts: an unread page or a box the read could not decide still keeps the note. |
| D102 | **A skip ledger with an entry the app cannot read keeps recording, and carries the unreadable entry through unchanged** (owner's answer). A ledger file that is unreadable as a whole is still never overwritten. |
| D103 | **A meal-sheet line that no tablet was read for by the deadline is approved at the deadline, like any untouched line** (owner's answer). The decision still records that nothing was read, so a notice can say 'approved without a read'. Accepted risk: a failed read-back approves everything on the sheet. |
| D104 | **The planning week runs Sunday through Saturday everywhere** (owner's instruction): workouts, dinners, groceries, the planning lease, event keys. The meal sheet's silence-approves deadline is Sunday 8:00 am on the owner's clock, after the 7:15 read-back. The Meals 'This week' view lists Sunday to Saturday. The meal sheet shows the cook's start time on every cooked row. |
| D105 | **The partner's phone scans recipe cards in the app, with Google's ML Kit text recognition and a CAMERA permission** (owner's answer). Reading happens on the phone; no image leaves it and the photo file is deleted after recognition. This adds one dependency (the ML Kit text-recognition package Plate already uses) and Constitution's second Android runtime permission after the Body reads, so the manifest and CI permission checks are widened deliberately for CAMERA only. |
| D106 | **Short of ten recipes the pinned bar shows 'Type it in' beside 'Scan a recipe card'** (owner's answer); typing is always available. |
| D107 | **The 'I ate something else' estimate shows only as the dinner card, exactly as book page 15 draws it** (owner's answers): 'You had something else · about 900 kcal' with the figure in mono, no detail line beneath it (the range, what the model saw and how sure it is stay in the stored record, undrawn), and no breakfast or lunch cards. A breakfast or lunch photo is still recorded (the meal comes from the time, D66) and the page says one plain inline sentence, 'Noted for breakfast.' or 'Noted for lunch.'. Nothing is removable after the undo window: a wrong estimate is replaced by taking another photo for the same meal that day. |
