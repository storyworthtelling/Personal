# ROADMAP.md — the suite, planned

This is the product/UX roadmap for the Storyworthtelling personal-app suite, written
out of a discovery session (Aug 2026). It is the plan to build *before* code — read it
first when starting any new app or cross-app work. It records **how the owner actually
lives with their two devices**, what the suite should become, and in what order to build
it. `DESIGN.md` remains the design bible; this file is the product bible.

> **The headline from discovery:** the original brief ("five new apps") *consolidated*
> rather than expanded. The owner does not want clutter, and the honest answer to their
> life is **one flagship app (Vitality), one evolved hub (the morning ritual), and two
> pieces of shared infrastructure** — plus a small, honestly-gated backlog. Fewer, deeper,
> quieter is the correct outcome, not a shortfall.

---

## 1. Operating principles (the owner's real life — obey these)

These are not preferences; they are constraints every app in the suite must respect. They
came directly from the owner and override generic UX instincts.

### Device division of labor
- **Phone (FocusPhone): quick in-out only.** Minimize time on it. *Anything long or deep
  goes to the reMarkable or Google Drive, never the phone.* This is the single most
  load-bearing principle in the suite.
- **reMarkable: the sit-and-think surface.** Reading, work notes, thinking/planning, and —
  ideally — the **calendar/task command surface**. The morning ritual lives here. Deep
  decisions (approving a meal plan, journaling, reviewing a digest) happen here.
- **Pixel Watch: glance + comms only.** Worn to bed as the alarm and the *first*
  notification surface. Ideally the phone and reMarkable are **out of the bedroom**.

### Attention
- **Strict hard blocks**, not gentle friction. Stop the owner; no easy escape hatch.
- **The phone home screen stays blank.** No glances, no cards, no numbers on the phone
  home — the empty screen is the point. *Glances live on the watch and the reMarkable, never
  the phone home.* (This kills the earlier "balance-glance on the home screen" idea.)
- **The suite pushes almost nothing.** The only thing that earns a buzz on the wrist is
  **new communication (texts/calls)**. Everything else is pull, or arrives quietly on the
  reMarkable. No scheduled nudges, no streak guilt, no "you spent X hours."

### Data & privacy
- **Google is the sync backbone** (Calendar, Tasks, Drive, Health Connect). "Google is
  fine." **No custom backend.** On-device + Google, following the existing
  `Storyworthtelling.Voice` Drive-config pattern.
- **The family calendar (Google) is the shared surface.** The partner is on a normal phone
  and wants *only* to see/add via shared Google lists/calendar — **no app to learn, ever.**

### Voice & character
- Static labels use the existing **Paper & Ink** system, neutral. **Authorial voice**
  (`Storyworthtelling.Voice`) is used only for **situational/customized messaging**, exactly
  the static-vs-situational split already in `DESIGN.md`.
- **Faith is woven into the voice**, not bolted on as features. The owner is Christian; the
  suite's tone may reflect that where it fits. It is not sprinkled everywhere as widgets.

### Dealbreakers (any one = instant delete)
Noisy/naggy · slow or unreliable · cluttered UI · demands too much input. In other words:
**the entire Paper & Ink ethos is the acceptance test.** An app that betrays calm fails.

### The owner's daily budget
Near-zero phone time. A real sit-down happens on the **reMarkable in the morning**. The
owner will **approve** things (a meal plan) and **record** things (workout difficulty,
prayers) but will not maintain databases or log obsessively.

---

## 2. The morning, as the owner lives it

> alarm (watch) → shower → **Scripture** (their own practice) → **glance at the day ahead**
> (reMarkable) → off to work.

Scripture reading is the owner's *own* practice — the suite does not deliver a devotional or
a reading plan. What the suite owns is the **"glance at the day ahead"** on the reMarkable
and the **prayer journal** that sits inside that ritual. Everything the suite wants to *say*
to the owner should try to land in this one calm reMarkable moment, not as interruptions
through the day.

---

## 3. Portfolio map

| # | Thing | Type | Status | Phase |
|---|---|---|---|---|
| — | **Storyworthtelling.Paper** | Shared infra | **New** | 1 |
| — | **Storyworthtelling.Google** | Shared infra | **New** | 1 |
| 1 | **Vitality** (health: workouts · meals · body) | **New app** | **New** | 2–4 |
| 2 | **Morning ritual hub** (evolves DailyLoader) | Enhancement | Existing → grown | 2–4 |
| — | Prayer journal | Mode inside the hub | **New (mode)** | 3 |
| — | Pixel Watch, Paper & Ink | Cross-cutting | **Deferred** | 5 |
| — | Backlog candidates (§9) | TBD | Gated | later |

Existing apps **FocusPhone**, **Storyboarder**, and **Ledger** are largely left alone
(§8). The five "new apps" from the first pass resolve to **Vitality** plus enhancements —
Correspondent and Marginalia are **dropped** (owner saw no value), Almanac is **renamed and
absorbed** into Vitality, and Chronicle **becomes the prayer-journal mode** in the morning
hub rather than a standalone app.

---

## 4. Flagship — **Vitality** (working name)

*One app, tabbed: **Workouts · Meals · Body**. Longevity-oriented, beginner-friendly, calm.
The name should carry **life/vitality + steady daily practice** (not "program", not
"fitness tracker"). "Vitality" is a placeholder — see naming note below.*

The unifying idea in the owner's own words: **"gradually feeling more healthy and more in
control of my health journey."** In control means: **trend, not numbers · following a plan ·
seeing the whole picture.** Vitality is the "dream app" — the thing the owner most wishes
existed.

### 4.1 Workouts tab
- **Claude-generated, adaptive** programming. Setup + adjustments happen **in the app**
  (equipment, days available, the beginner starting point).
- Constraints: **home gym + minimal/bodyweight**, beginner, **no major limitations**.
  Emphasis: **keep it varied** across strength basics, mobility/joints, and zone-2/cardio —
  the longevity foundation, not a hypertrophy grind.
- **Delivery:** on days a workout is scheduled, the session card is **delivered to the
  reMarkable** (via the shared Paper publisher). The owner trains from the paper card.
- **The adaptive loop:** the card asks the owner to **mark difficulty** (an RPE-style read,
  handwritten/checked on the reMarkable). That feedback flows back and **tunes the next
  session.** Feedback is captured on the reMarkable — *not* the phone.
- No live rep-logging burden: the card tells the owner what to do; the owner reports how
  hard it was. That's the whole interaction.

### 4.2 Meals tab — an adaptive family meal planner
This is the sleeper feature the owner got most animated about. It is a **taste-profile
engine for dinner**, modeled on the same map-reduce-durable-profile pattern as FocusPhone's
Listening Genius and `DesignBibleMerger`.

- **Plans for the whole family** — dominant constraints are **picky eaters + weeknight
  time pressure**, one cook (the owner).
- **Reads the family Google Calendar ahead** (the "meal planner ↔ calendar" synergy the
  owner explicitly wants): busy nights get quick meals; open evenings can take real cooking.
- **Optimizes for health within real constraints** — dietary preferences + time available —
  aimed at the longevity goal.
- **Approval-gated learning:** the planner proposes a week; the owner **approves the plan on
  the reMarkable before grocery shopping**; it **learns from approval/disapproval and
  retries.** No thumbs-rating chore — the approve/reject *is* the training signal.
- **Scan-in recipes** via the **phone camera** (Claude reads a cookbook page / card into a
  recipe); scanned recipes join the **family-approved pool** and bias future plans.
- **Output:** a **grocery list** for in-store shopping (delivered to the reMarkable / kept
  as a shared Google list so it's on hand at the store).

### 4.3 Body tab — passive, trends only
- **Passive only.** No manual logging. Sources:
  - **Pixel Watch** → activity + sleep (via **Health Connect**).
  - **Sleep Number** bed → sleep (SleepIQ; see risk §7).
  - **Smart scale** → **weight as a gentle trend, never a daily number.** Weight is
    explicitly a potential stressor — soft direction only.
- **A composite health picture** — "overall health scores and category breakdowns **à la
  Google Health**." Trend, not numbers, everywhere.

### 4.4 The weekly digest (Vitality → reMarkable)
Once a week the shared Paper publisher composes a calm Paper & Ink PDF to the tablet:
- **Body trends** (sleep, activity, weight direction — gently)
- **Training recap** (what was done, how it felt, what's next)
- **A voiced reflection** — one paragraph in the authorial voice
- **Next-week preview** — workouts + meals
- **Overall health scores + category breakdowns** (the Google-Health-style read)

### 4.5 Architecture (fits the house style)
- MAUI Android head, app id `com.storyworthtelling.vitality`, `net10.0-android`.
- Domain (`net10.0`) + Infrastructure (`net10.0;net10.0-android`) + Tests, one `.sln`.
- **EF Core + SQLite** on device; **AndroidX WorkManager** for the weekly planner run,
  digest composition, and body-data pulls. No backend.
- References `Storyworthtelling.Voice` (reflections), `Storyworthtelling.Paper` (digests +
  workout cards + grocery list), `Storyworthtelling.Google` (Calendar read + Health
  Connect + Drive).
- Claude for programming, meal planning, recipe OCR-to-structure. Reuses the shared Voice
  Claude key from Drive — **no new key screen.**
- Design tokens registered in `DESIGN.md §16`; leak-guard + parity tests as every app has.

---

## 5. The morning-ritual hub (evolving DailyLoader)

DailyLoader already delivers the crossword + brief to the reMarkable (**reMarkable cloud
delivery works**) and reads handwritten notes back into Google Calendar/Tasks. It becomes
the **morning ritual hub** — the single calm reMarkable surface where the day is met.

Grows to:
- **The reMarkable as the true calendar/task command surface** (the owner: *"already built
  into DailyLoader but can use some tweaking"*). Tighten the handwrite→Google-Tasks/Calendar
  loop; make the day-ahead page the reliable "glance at the day ahead."
- **Prayer-journal mode** (this replaces the standalone Chronicle idea). Handwritten on the
  reMarkable as part of the morning flow. Holds **gratitude, prayer requests & answers, and
  freeform prayers.** It **accumulates daily to propagate as appropriate** over time (the
  same durable-profile instinct as the taste/voice engines) — kept self-contained; note the
  owner did **not** ask to feed it into the shared authorial voice.
- **Becomes the delivery target** for Vitality's workout card, meal-plan approval page, and
  weekly digest — one place, one ritual, not five apps buzzing.

> The morning hub is where the suite consolidates: brief + calendar/tasks + prayer + today's
> workout + (weekly) meal approval + (weekly) health digest, all on paper, all quiet.

---

## 6. Shared infrastructure (build FIRST — the owner chose "shared infra first")

Two libraries, each mirroring the proven `Storyworthtelling.Voice` pattern (a repo-root
`.csproj` + `.Tests`, referenced by every consumer). The owner explicitly values two of the
three original synergy ideas here.

### 6.1 `Storyworthtelling.Paper` — the publisher + reMarkable delivery seam
The single highest-leverage structural move. Today Storyboarder owns Skia→PDF page
composition and DailyLoader owns reMarkable-cloud delivery; Vitality, the hub, and any
future app all want "emit a Paper & Ink page to the tablet."
- Extract **Paper & Ink Skia page composition** (from Storyboarder's composer) + an
  **`IPaperDelivery`** seam (reMarkable cloud / Drive / print) into one library.
- Every app gains workout cards, grocery lists, digests, and reports **for free**, all
  design-bible-correct.

### 6.2 `Storyworthtelling.Google` — sign in once
The repeated friction in `CLAUDE.md` is per-app OAuth-client registration + scope
re-consent. `Storyworthtelling.Voice` already centralized the *Claude key* in one Drive
file; do the same for **Google identity**.
- One **account + scope broker** across Drive/Docs/**Calendar**/**Tasks**/**Health
  Connect**. The owner authenticates once; each app narrows from the shared grant.
- Directly enables the **family-calendar-shared-with-partner** model (partner routes through
  Google, no app) and the **meal-planner-reads-calendar** synergy.

> The third original synergy — **prayer → voice** — the owner did **not** pick. Do not build
> it. The two the owner wants are Paper and Google; the meal↔calendar synergy is delivered
> inside Vitality.

---

## 7. Risks & prerequisites (verify on hardware before building deep)

The owner has **all the hardware ready**: reMarkable, Pixel Watch, FocusPhone device, Sleep
Number + scale. That is the opening to close the pile of "not yet live-verified" items the
repo already carries. Order the work so risky assumptions are proven *before* they're built
upon.

| Risk | Why it matters | Action |
|---|---|---|
| **Handwriting-back is untested on device** | Workouts (difficulty), prayers, *and* tasks all ride on reMarkable→Google recognition. This is the #1 risk. | Verify on the real tablet **first**. Add a **review-before-commit** step so a misread never silently writes to Calendar/Tasks. |
| **Health Connect data flow** | Body tab depends on Pixel Watch data actually arriving via Health Connect (Fitbit/Google path). | Prove the read on-device before building the Body tab. |
| **Sleep Number (SleepIQ)** | Unofficial API; reliability/longevity unknown. | Spike it early; **degrade gracefully** to Pixel-Watch-only sleep if it's flaky. Never block the app on it. |
| **Smart scale brand unknown** | May or may not feed Health Connect. | Identify the scale; if it doesn't integrate, weight-trend is manual-optional, not core. |
| **Recipe document scanner untested** | Camera + QR work; the document *scanner* isn't proven. Recipe scan-in depends on it. | Verify scan-to-text on device; fall back to photo→Claude OCR if the scanner is weak. |
| **Existing unverified loops** | Voice, batch rendering, Listening Genius are all "pending live verification." | Use the hardware-ready window to verify these too, since the suite now depends on the Voice + profile patterns. |

---

## 8. Existing apps — what changes (mostly nothing)

- **Ledger** — **left alone.** Balances-only answers "can I afford this?" for now; the owner
  explicitly does not want it deepened. No safe-to-spend, no glance on the phone home.
- **FocusPhone** — strict blocking is right; home stays blank. **Camera + QR work; document
  scanner untested** (gates recipe scan). No structural change requested.
- **Storyboarder** — not flagged for change.
- **DailyLoader** — evolves into the morning-ritual hub (§5); the owner asked for the
  calendar/task-loop **tweaks**.
- **Pixel Watch** — the owner wants it eventually brought into **Paper & Ink** (unify the
  look), but the build is **deferred to last.** Design it into the plan; don't build it yet.

---

## 9. Backlog — honestly gated future candidates

The owner asked for the **full portfolio plan including distant ideas**, but also despises
clutter. So the backlog is short and each item carries an **earn-it bar** — build only when
that bar is met. Do **not** pad the suite to hit an app count.

- **A weekly-rest / Sabbath mode (cross-cutting, not an app).** A system-wide quiet window
  the whole suite honors — faith-aligned, fits "strict blocks" + calm. *Earn-it: only if the
  owner wants rest *enforced*, not merely observed.* Likely a FocusPhone/DPC feature, not a
  new app.
- **A light "read-it-later → reMarkable" delivery.** Rides entirely on
  `Storyworthtelling.Paper`; the owner already reads on the tablet. *Earn-it: a concrete
  "I keep meaning to read X and it never gets to the tablet" pain. (Note: full Marginalia
  reading-tracker was rejected — this is delivery only, no tracking.)*
- **A household layer (maintenance/admin).** The owner called household admin **"not a
  priority"** and routes shared family logistics through Google. *Earn-it: only if
  maintenance/recurring-bill chaos becomes a felt pain; the meal side is already covered by
  Vitality and the shared calendar by Google.*

Explicitly **dropped** and not to be revived without new signal: **Correspondent** (no
value), **Marginalia** (no value), standalone **Chronicle** (folded into the prayer-journal
mode).

---

## 10. Build sequence

1. **Phase 1 — Shared infra.** `Storyworthtelling.Paper` (publisher + reMarkable seam) and
   `Storyworthtelling.Google` (sign-in-once broker). Migrate Storyboarder's composer and
   DailyLoader's delivery onto Paper as the first consumers (proves the extraction).
2. **Phase 2 — Verify the risky loops on hardware** (§7): handwriting-back with a
   review-before-commit step, Health Connect read, a Sleep Number spike. Ship the DailyLoader
   calendar/task **tweaks** here since they share the handwriting work.
3. **Phase 3 — Vitality vertical slice: the workout loop.** reMarkable-delivered adaptive
   session card + difficulty-feedback-back + next-session tuning. Add the **prayer-journal
   mode** to the morning hub in parallel (same handwriting + Paper plumbing). Thin, real,
   end-to-end.
4. **Phase 4 — Vitality breadth.** Meals tab (calendar-aware family planner, approval-gated
   learning, camera recipe scan, grocery list) → Body tab (passive Health Connect + Sleep
   Number + weight-trend, Google-Health-style scores) → the weekly digest.
5. **Phase 5 — Pixel Watch, Paper & Ink** (deferred): unify the wrist's look; comms-only
   notifications remain the rule.
6. **Backlog (§9)** — only as items clear their earn-it bar.

Throughout: TDD (tests before implementation), Paper & Ink leak-guards, no new dependencies
outside the house rules, no backend, and the calm-first dealbreakers as the acceptance test.

---

## 11. Open decisions for the owner

Small things left deliberately un-decided, to settle at the top of the build:
- **Naming.** "Vitality" is a placeholder for the health app; the desired feeling is
  *life/vitality + steady daily practice*. Confirm the name before the head is created (app
  id follows it).
- **Sleep Number scope.** Wire SleepIQ in Phase 4, or ship Body on Pixel-Watch sleep alone
  and add the bed only if the spike (§7) is clean?
- **Grocery list home.** Delivered to the reMarkable, kept as a shared Google list, or both?
  (Shared Google list also lets the partner add to it — consistent with the family model.)
