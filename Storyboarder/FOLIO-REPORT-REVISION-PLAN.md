# Folio — revision report: interview findings and plan

The owner was interviewed as product owner about the **review document only** (the PDF Folio delivers to
the reMarkable and opens on the phone), 64 questions across sixteen rounds. This file records what was
said, the rulings it produced, and the plan that follows from them. It is written for the owner to
approve before any release starts; nothing in it is built yet.

## 1. What the interview found

### How the report is used
- Read on the **reMarkable and the phone**, by **skimming, then dipping back in** while revising; the
  **binder tabs** are how a note is found again.
- The **marked manuscript** and the **readers** are the most valuable parts. The **whole-book notes**
  are the most skipped: they repeat the readers, are too abstract to act on, and run too long.
- Report order, look on the glass (type, frame, tabs, colour) and overall trust are all **good**. The
  owner ignores nothing the report says.
- Length is right **depending on the piece**: fine for a novel, too much for a short story in places.

### What works and stays as it is
- The report's **order**, its **look**, the **front matter** (a useful check that the book came across as
  meant) and the **frame page**.
- The **number of margin marks**, their type size, and **nudge never write** (point at the problem; the
  author writes the fix).
- The **scene illustrations** ("love them") and the **before/after** pair on a changed scene.
- The six readers are **genuinely distinct**; none blur together. Reader length is right for a short
  story. The **"Would need"** condition is very useful.
- The **revision plan** works well as the next draft's to-do list.

### What does not work
| Area | Problem | Owner's words / choice |
|---|---|---|
| Margin marks | It is unclear **where in the text** a note points | Notes are fine; the target is unclear |
| Chapter-level notes | Notes pinned to a chapter's opening ("In this chapter") look like notes on the first line | Move them to a list |
| Scene blocks | Scene separation seems muddled | Break scenes on the text's own breaks, not Claude's |
| Whole-book notes | Repeats the readers, too abstract, too long | Shorter summaries (novel); one page at most (short story) |
| Pacing chart | Unlabelled points, unclear axis | Label the points; chapters on the x-axis; novels only |
| Verdict | Redundant by the time it is reached | Replace it with the readers in conversation, which lays the splits out |
| Readers on a novel | Not enough depth at novel length | Each reader gets its own purpose for writing more |
| Readers and line edits | Readers spend words on line-level fixes | No line edits; general terms and real blockers only |

### The finding behind "muddled scenes" (verified in code)
`SceneDetector` already splits scenes on the text (divider lines, two or more blank lines, chapters) but,
for a chapter with no breaks, also **cuts by length** (`TargetSceneChars` 5,000, `MaxSceneChars` 9,000).
Separately, the scene verdicts come from Claude's **own** scene list (`CraftReport.SceneBreakdown`), and
`ReviewProcessor.GatherSceneEvidence` pairs them with the detected scenes **by position**
(`sceneBreakdown[i]`). The two lists are made independently, so whenever their counts differ every
verdict after the first mismatch is printed under the wrong scene, silently. That is a live bug, and it
is why release A comes first.

## 2. Rulings (owner's answers, numbered for reference)

**Scenes**
- R1. Scene breaks come only from the text: **chapter breaks, divider glyph lines, two or more blank
  lines, and horizontal rules**. A single blank line is a paragraph break, not a scene break.
- R2. A chapter with **no breaks is one scene** (one block, one image). No splitting by length, and no
  Claude fallback.
- R3. The scene block stays **after** the scene.
- R4. The scene verdict adds: **what the scene accomplishes**, a **keep / tighten / rewrite / cut** call,
  its **tension level**, and any **inconsistencies in voice, tense, characterisation or setting**.
- R5. Those inconsistencies appear **both** in the scene block and as margin marks at the line.

**Margin marks**
- R6. A **stronger leader line** from each note to its place in the text.
- R7. Notes that cannot find their passage go to a **list at the chapter's start**, not the margin.
- R8. Each note shows its **severity by colour**: Must, Should, Consider.
- R9. **Nudge never write** stays.

**Readers**
- R10. A **seventh reader, the Fellow writer**: "What would I steal, and what would I do differently".
- R11. Readers make **no line-edit notes**. General terms ("the prose is sloppy") and real blockers
  ("tense shifts a line editor should catch") are allowed, even where the line work is costing the score.
- R12. At **novel length** each reader gets **its own purpose for writing more**, distinct per reader.
  Short-story reads stay their current length.
- R13. Scores stay where they are (under the name) and gain a **score history chart** across drafts.
- R14. "Would need" stays as it is.

**Plan, notes, chart, frame**
- R15. Each revision-plan item gets a **link to its mark** in the manuscript and a **checkbox** for the pen.
- R16. Whole-book notes: **shorter summaries** for a novel; **one page at most** for a short story.
- R17. The pacing chart: **labelled points**, **chapters on the x-axis**, **novels only**.
- R18. Front matter and the frame page stay.

**Between drafts**
- R19. A later draft shows what changed **spread through the report**:
  - **Readers:** score movement (done), plus the history chart from R13.
  - **Plan:** each item marked **fixed, still open or regressed** against the last draft.
  - **Manuscript:** **changed scenes** with their before and after verdicts.

**Series**
- R20. Folio keeps a **series bible**: settings and characters **mapped over time**, because a new book may
  be a sequel, a prequel, an interquel, or set thousands of years apart.
- R21. Folio **infers where a new book sits** in the series' chronology, and the owner **confirms**.
- R22. The continuity read catches **contradicted facts, character drift, dropped threads and timeline
  errors**.
- R23. **The earlier-written book is canon**; the new book is flagged.
- R24. Checks run against the **bible, quoting the earlier book's passage** (the bible stores the quote),
  not against the earlier books' full texts.
- R25. Series findings appear **both** as marks in the text (citing the earlier book) and in a **Series**
  section.
- R26. The bible is **printed and editable**: delivered as its own document, and correctable in the app.
- R27. **The readers get the series bible** when the book is part of a series.

**The readers in conversation**
- R28. A **second round** after the independent reads; the first-round reviews and scores are unchanged.
- R29. **The seven readers and a neutral moderator** take part; the sections do not.
- R30. It contains a **roundtable**, a **debate on each split**, and **rebuttals** wherever two readers'
  scores differ by **three or more**: in each such pair, each reader answers the other once.
- R31. A reader may **concede a point in words**; its score stays the independent one.
- R32. It **lays the splits out** and does not rule on them. It **replaces the verdict**.
- R33. Its length **scales with the manuscript**, and it runs on **every draft**.

**Not wanted:** a per-piece goal setting; a summary page before the frame; a sensitivity or
target-age reader (for YA, the genre fan already is one); the line editor back.

## 3. The plan

Eight releases. A, B and C are the owner's top three (scene breaks, readers, mark anchors), with A first
because it carries a live bug. Every release is test-first, keeps the existing guards green, and
compiles the Android head before it is called done. None of it is verified on a device until the owner
runs a real draft through.

### Release A — Scenes from the text (R1–R5)
1. **`SceneDetector`**:
   - Breaks on chapter starts, divider lines, two or more blank lines, and horizontal rules.
   - Remove the length split (`TargetSceneChars`/`MaxSceneChars`), so a chapter with no breaks is one scene.
   - Tests: a break-free 30,000-character chapter is one scene, and a single blank line splits nothing.
2. **Horizontal rules survive extraction.**
   - Check what Google Docs' text export, `.docx`, ePub and PDF each turn a horizontal rule into.
   - Have each extractor emit a divider line, with a fixture per source format.
   - The Docs export is the one most likely to drop it silently.
3. **Fix the pairing bug.**
   - Scene verdicts are asked for the detected scenes by id, never zipped by position.
   - The craft call is given the scene list (id, chapter, opening and closing words) and returns one
     verdict per id.
   - A missing id is reported as "no verdict", never shifted.
   - Guard test: Claude's list running one scene short leaves every other verdict under its own scene.
4. **The richer verdict** (R4):
   - New fields: `Accomplishes`, `Call` (Keep/Tighten/Rewrite/Cut), `Tension` (0–1, the pacing curve's
     scale) and `Inconsistencies` (kind + excerpt).
   - The scene block prints all four.
   - Each inconsistency also becomes a mark through `MarkSource` (R5).
   - `NudgeNeverWriteTests` still holds: the call is a word, not prose.
5. **Cost check.**
   - Fewer, longer scenes means fewer images and longer image prompts.
   - Log the first real run's scene count and image cost against the last.

### Release B — Readers (R10–R14, R27)
1. **Fellow writer.**
   - A seventh persona on the strong model: "What would I steal, and what would I do differently".
   - Anchors to draft and confirm with the owner (open question 1).
   - `ReadingPlan`, `PersonaCoverageTests`, the score tests and the readers' contents grow to seven.
   - Cost: one more strong pass, about 65 seconds, reading the manuscript from cache.
2. **No line edits** (R11).
   - One sentence in every reader's instruction says what is out (word choice, grammar, punctuation,
     single sentences) and what is in (general terms, blockers, by example).
   - A live check runs the bad-prose fixture and confirms no reader lists line fixes.
3. **Novel depth** (R12). At novel length each reader is given its own extra purpose, which the owner
   approves (open question 2):
   - Developmental editor: an act-by-act map of what holds and what sags.
   - Agent: the pitch, comparable titles, and what a query letter would lead with.
   - Acquiring editor: list fit and the edit letter's three priorities.
   - Genre fan: where engagement rose and fell, chapter by chapter.
   - Harsh critic: the three biggest failures, each with the chapters that show it.
   - Literature professor: themes and motifs traced through the book.
   - Fellow writer: techniques worth stealing, each with the chapter it is in.

   `PassMaxTokens` rises for novels only, and short-story reads are unchanged.
4. **Score history chart** (R13): each reader's score across drafts.
   - On the phone's book page, beside the readiness chart.
   - On each reader's report page as a small trend line (open question 3).
   - Data: the `PassRead` rows already kept per draft.
5. **The bible in the readers' prompt** (R27), when the book is in a series.
   - It is the same for every pass of a draft, so it goes inside the cached prefix, after the manuscript.
   - `ReadingPromptTests` keeps the prefix byte-identical across personas.
   - The independence guard is updated: the bible is canon, not another reader's notes.
   - Lands with release G; until then, the existing prior-book canon is passed the same way.

### Release C — Clear mark anchors (R6–R8)
1. **Leaders.** A heavier leader drawn to the start of the anchored phrase, not the line.
   - Tested by geometry: the leader ends inside the phrase's box.
   - Check the rM2 in greys.
2. **Chapter notes as a list** (R7).
   - The chapter-fallback rung no longer places a query in the margin.
   - The notes print as "Notes on this chapter" at the chapter's head.
   - Update `MarkSource`'s ladder and its tests.
3. **Severity colour** (R8).
   - Every finding carries a Must/Should/Consider severity, asked for in each prompt that produces findings.
   - The note and its leader take Problem rose, Caution ochre or muted ink.
   - Colours come from `ReportPalette` only.
   - A contrast test checks the three stay distinguishable in greyscale.

### Release D — Plan links and checkboxes (R15)
1. The synthesis that writes the plan returns, per item, the finding thread ids it draws on.
2. Each item gets an internal PDF link to its first mark (or to its chapter's notes list) and a pen
   checkbox drawn on the line.
   - Links: test that the destination page carries the mark.
   - Checkboxes: drawn so the tablet's pen can tick them; nothing reads them back.

### Release E — Leaner notes and a labelled pacing chart (R16–R17)
1. Whole-book notes: one short paragraph per section at novel length. A short story gets one page, with
   only what is true of the whole piece.
   - Enforced by a page-count test on a short-story fixture.
2. Pacing chart:
   - Novels only (open question 4 covers novellas).
   - Chapter numbers on the x-axis.
   - Each point labelled with a few words on what drives or drains its tension, a new field on
     `ChapterTension`.
   - Labels placed so they do not collide.

### Release F — What changed since the last draft (R19)
1. **Plan:** each item is marked fixed, still open or regressed against the last draft, from the finding
   threads Folio already tracks (`New`/`Carried`/`Regressed`; fixed is a thread's absence).
2. **Manuscript:** a changed scene's block shows its previous verdict beside the new one, next to the
   before/after images already there.
3. **Readers:** the score movement already shipped, plus B4's history chart.

### Release G — The series bible (R20–R26)
1. **Model.**
   - A `SeriesBible` per series, holding characters, settings, world rules, timeline events and open
     threads.
   - Each entry keeps states over story time ("as of" a book and an era), the book that established it,
     and the quoted passage.
   - Stored in the app's database, with a sealed copy in the owner's Drive. Never in the repository: it is
     manuscript-derived.
2. **Building it.**
   - After each series book's review, a pass updates the bible from that book.
   - The first bible is built from the canon already stored for earlier books (`SeriesCanon`), so existing
     series do not need re-reading.
3. **Placement** (R21). Folio infers where the new book sits:
   - It can be before, after or between books, or in a stated era.
   - The owner confirms on a paper card when adding the book.
   - Until confirmed, continuity findings say what they assumed.
4. **The continuity read** (R22–R24) checks four things against the bible, with the earlier-written book
   as canon:
   - contradicted facts;
   - character drift;
   - dropped threads;
   - timeline errors.

   Each finding quotes the earlier book's passage from the bible.
5. **In the report** (R25): findings appear as marks citing the earlier book, and in a **Series** section.
6. **Printed and editable** (R26):
   - The bible is delivered as its own document to the tablet and Drive.
   - An app screen lets the owner correct an entry, and a correction wins over the books.

### Release H — The readers in conversation (R28–R33)
1. **Second round.** After all seven reads, a conversation pass over the reviews and scores, not the
   manuscript, on the strong model.
2. **Shape.**
   - The moderator frames each split.
   - Each split gets a debate between the readers on either side.
   - A roundtable covers what all seven see differently.
   - Every pair with scores three or more apart gets rebuttals, each answering the other once.
   - Readers may concede in words; scores do not change.
3. **Distinct voices.** To keep the seven as distinct as they are now:
   - Each reader speaks in its own call, given the others' reviews.
   - The moderator's call plans the splits and assembles the transcript.
   - Cost: a handful of strong calls over about 20k tokens of reviews, small next to the reads.

   Open question 5 offers a single call instead.
4. **In the report.**
   - It replaces the verdict section and its tab, laying the splits out without ruling.
   - Its length is scaled by `ManuscriptLength`.
   - The revision plan stays.
   - The verdict's "standing" sentence is checked for other users (the desk and the book page) before
     the verdict is removed.
5. **Status.** `ReadingPlan`, the desk's "read N of M" status and the run estimate are updated for the
   new passes.

### Release R — Regenerate the books already reviewed (owner's request)
When releases A–H have landed, every book Folio has already reviewed gets a fresh report in the new
form. This is done once, at the end, so no book is paid for twice.

1. **The latest draft of each project only.**
   - Earlier drafts keep their old reports.
   - A series is regenerated **in story order** once release G's placement is confirmed, so each book's
     bible exists before the next book is read against it.
2. **The owner's voice profile is not touched.**
   - Regeneration does not run the voice refinement (`ReviewProcessor`'s `RefineAsync` step, guarded per
     draft by `Novel.VoiceAnalyzed`).
   - So nothing is written to the authorial-voice document in Drive (`storyworthtelling-voice-sealed.json`).
   - The manuscripts already shaped the profile once; re-reading them would count each book twice.
   - Guard test: a regeneration run calls no voice client and never saves the profile store.
3. **A deliberate exception to "a finished book never re-renders".**
   - Regeneration is started by the owner (one action in Settings, listing the books and the estimated
     cost before it starts), never by the healer.
   - `CompletedBooksDoNotHealTests` still holds for everything else.
4. **What is re-run and what is reused.**

   | Step | Regenerated | Why |
   |---|---|---|
   | Scene detection | Re-run (release A) | The new rules split scenes differently |
   | Scene images | Reused where the new scene matches an old one (`SceneMatcher`); otherwise generated | |
   | The reads | Re-run | |
   | Conversation, plan and series checks | Re-run | |
   | Chapter notes and reductions | Re-run only where a release changed their prompt | |
   | Imported source text | Not re-imported | The draft's stored source text is the manuscript |

5. **It stays the same draft.**
   - The regenerated report replaces that draft's report in place; no new draft number.
   - A later draft still compares against it.
   - Findings keep their thread ids where the new read finds the same issue.
6. **Batched** (see below). A backfill nobody is waiting on is exactly what the batch tier is for, so the
   whole regeneration runs at the batch discount.
7. **Delivery.** The new report goes to Drive (replacing the old file) and to the reMarkable.
   - The tablet's old copy is left in place unless the owner says otherwise (open question 8).
   - The diagnostics export lists each regenerated book and where it went.

### Cross-cutting — batching the AI calls (owner's request)
Folio's pipeline runs unattended: nobody watches a reduction or a scene prompt finish. The Message
Batches API takes **50% off every token in a request, including cache reads and writes** (the two
discounts stack).

The trade-offs:
- **Latency.** Results come back asynchronously, and the 24-hour window is an expiry, not a promise.
- **One shot per request.** A request cannot hold a mid-request back-and-forth.
- **Best-effort caching.** Cache hits between requests in the same batch are not guaranteed.

Batch where the calls are independent and nobody is waiting; keep the rest as they are.

**Batch candidates:**

| Calls | Why they batch | Release |
|---|---|---|
| Scene image prompts (`DescribeSceneImage`, one per scene) | Independent of each other; the largest share of input in the first logged book | A |
| Scene verdicts | Once the craft call takes the detected scene list (A3), the verdicts can be asked per chapter in one batch, keyed by scene id | A |
| The sections and the seven readers (`ReadSection`, `ReadAsReader`) | Each reads the same manuscript and none depends on another | B |
| The two synthesis passes (plan and verdict, later the conversation's moderator plan) | Independent of each other | D, H |
| The conversation's per-reader turns, rebuttals included | Independent once the moderator has planned the splits | H |
| Updating the series bible from a finished book | Nobody is waiting | G |

**Not batched:**
- **Chapter notes** (`AnalyzeChapter`). Each carries the story-so-far from the one before, so they run in order.
- **Anything the owner is waiting on in the app.**

**How it works:**
1. **Submission.** The worker submits a batch and stores its id on the manuscript, so a restarted phone
   resumes rather than resubmitting.
2. **Polling.** It polls on later worker cycles, so no foreground service is needed. The fifteen-minute
   foreground read exists only to keep the cache warm in one session.
3. **Results.** Each result is read **by its custom id, never by position**: results arrive in any order,
   which is the same lesson as the scene-pairing bug.
4. **Failures.**
   - A request that errors or expires is retried on its own, through the existing attempt and park rules
     (`CallGate`).
   - A refusal or a truncated reply is handled per request, as now.
5. **Logging.** Every result is logged with its tokens as today, and the batch id is added to the line.

**Caching inside a batch.** The reads share one cached manuscript.
- Send the first read on its own to write the cache.
- Then batch the rest.
- Measure the first batched draft's cache reads and cost against the **$3.38 a draft** baseline before
  making it the default.
- If cache hits inside the batch are poor, the 1-hour cache lifetime is the fallback to try.

**What the owner gives up:** a report that arrives in about fifteen minutes. A batched read usually comes
back well inside the hour, but may take up to a day (open question 7).

**Gemini's image generation** is a separate provider. Whether it has an equivalent batch mode is to be
checked, not assumed.

## 4. Open questions for the owner
1. The **Fellow writer's anchors** for 2, 4, 6, 8 and 10. A first draft will be written for approval in
   release B.
2. The **novel-length purposes** listed in B3: approve, change or replace each.
3. Should the **score history** show on each reader's page in the report, or only on the phone?
4. Should a **novella** get the pacing chart (it usually has chapters), or only a novel?
5. Should the conversation be **one call writing every voice** (cheaper, and risks the voices blurring) or
   **one call per voice** (recommended)?
6. Should the conversation **section** be called "The conversation" or "The panel"? Its tab replaces
   *Verdict*.
7. **Batching trades speed for half the cost.** Should every unattended call be batched? A report may
   then take hours (rarely up to a day) instead of about fifteen minutes for the reads. Or should only the
   scene prompts and verdicts be batched, keeping the reads quick?
8. When a book is regenerated, should the **old report on the reMarkable** be moved to the trash (Folio can
   trash a document it delivered), or left beside the new one?

## 5. Order and size
- **A, Scenes:** medium. A live bug, so it goes first.
- **B, Readers:** medium.
- **C, Mark anchors:** medium.
- **D, Plan links:** small.
- **E, Notes and chart:** small.
- **F, Between drafts:** small. Needs A and D.
- **G, Series bible:** large. Feeds B5.
- **H, Conversation:** medium. Best after B, so the Fellow writer takes part.
- **R, Regenerate reviewed books:** small to build, and the largest single API spend in the plan.
  - Last, after A–H.
  - Its cost is shown and approved in the app before it runs.
- **Batching:** medium, cross-cutting.
  - It needs a batch submit/poll client beside the existing ones, and the pipeline's stage state to hold
    a batch id.
  - It lands with A, for the scene prompts and verdicts (the safest first use), then extends to each
    later release's calls as they are built.
