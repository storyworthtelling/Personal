# Phase 6 — Gemini Batch API: feasibility

Assessment of moving panel rendering onto the Gemini Batch API.

## What the Batch API offers

- **50% discount** on billable tokens versus the synchronous API, image generation included.
- **Up to 24-hour turnaround.** Not a shorter-latency guarantee — an upper bound.
- **Separate quota pools** that do not draw down the real-time limits.
- Submit inline (under 20 MB) or as a JSONL file via the Files API; then poll the job until it reaches a
  terminal state and read the results back.

## Why it fits this app unusually well

The pipeline is already shaped like a batch consumer, which is the main reason this is worth doing:

- Work is **already asynchronous, resumable and DB-backed**, drained by a periodic WorkManager worker.
  "Submit a job, come back later, read results" is how the whole app already behaves — `WorkStatus.NeedsRetry`
  plus `NextAttemptAtUtc` is a polling scheduler in all but name.
- **Panels are independent.** After storyboarding, all panel prompts are known and none depends on another's
  output. That is close to an ideal batch.
- The owner wants this **hands-off and running for days**. A 24-hour turnaround is acceptable to someone
  already waiting weeks, and they explicitly declined notifications and run controls.

## The honest caveat: check the cap before building anything

The estimator added in Phase 1 reports ~3 weeks for a full-length novel, and it derives that from
`RateLimitOptions.GeminiRequestsPerDay`, whose default is **100**. That is *this app's own self-imposed
safety ceiling*, not a limit Google set. A paid Gemini tier allows far more than 100 image calls a day.

So before any batching work: find out what the account's real tier allows and raise that number. It is a
one-line configuration change and it may deliver most of the wall-clock win on its own. Building a batch
pipeline to work around a limit we set ourselves would be the wrong order of operations.

Batching still earns its place after that, for two reasons the cap change does not address: the **50% cost
cut** on the single most expensive part of the pipeline (2000+ image calls per book), and the **separate
quota pool**, which stops bulk rendering from competing with everything else for the same allowance.

## Real complications

1. **Reference images cannot be inlined.** Panels attach character/setting sheets as reference images. Base64
   inlining a few sheets per request across ~2000 requests blows past the 20 MB inline ceiling by orders of
   magnitude. The fix is to upload each sheet **once** to the Files API and reference it by URI in every
   batch request — which is also far less wasteful than repeating the bytes. Files API retention is short
   (~48h), so the upload has to be part of the same submission step as the batch, not a separate stage.
2. **Sheets must precede panels.** Reference sheets are inputs to panel renders, so they cannot share a
   batch with them. Sheets are now recurring-cast only (a dozen or so), so the sensible split is: keep
   sheets synchronous, batch only the panels.
3. **Blocked content gets slow.** The content-block flow (restage the prompt, re-render, park if still
   refused) currently iterates in minutes. Through a batch it iterates in *days*. This is the one place
   batching actively hurts, and it argues for a hybrid: batch the bulk, then handle refusals and retries
   synchronously so a blocked panel doesn't add 24 hours per attempt.
4. **Per-panel aspect ratio** varies (`PanelSlotAspectRatio`), but that is per-request config and carries
   fine in JSONL.
5. **Failure granularity.** A batch returns per-request outcomes, so `ClassifyBlock` can be reused per
   result — but the panel status machine needs a new in-flight state ("submitted, awaiting batch") that
   survives restarts and can be reconciled against a job id.

## Recommendation

Worth doing, in this order:

1. **First**, raise `GeminiRequestsPerDay` to match the account's actual tier. Cheapest possible change,
   possibly most of the speedup.
2. **Then** batch panel renders: keep reference sheets synchronous, upload sheets to the Files API once per
   submission, submit panels as a JSONL batch, persist the job id, poll it from the existing worker, and
   fall back to synchronous rendering for refusals and retries.

Step 2 is a substantial phase — comparable to Phase 2 — because it adds a genuinely new execution mode
(in-flight batch state, job reconciliation, a second code path for rendering) rather than adjusting an
existing one.

## Economics & wall-clock — best estimate (after the cap raise)

Ballpark, not exact. Anchored on the owner's real converted novel: **432K characters → 2,162 panel
renders** (RunEstimator, ~1 panel/200 chars) plus ~a dozen recurring-cast reference sheets. Prices as of
mid-2026: `gemini-2.5-flash-image` bills image output at ~$30/1M tokens ≈ **~$0.039 per image**; the Batch
API is **50% off** everything ≈ **~$0.02 per image**.

### Financial benefit — real, modest, and it scales with usage

| | Sync | Batch (panels) |
|---|---|---|
| Per image | ~$0.039 | ~$0.020 |
| ~2,162 panels | ~$84 | ~$43 |
| ~12 sheets (kept sync either way) | ~$0.5 | ~$0.5 |
| **Image bill / full book** | **~$85** | **~$43** |

- **~$42 saved per full-length book (~50% of the image bill).**
- Image generation is the dominant API cost by far — the Claude text calls for a whole book (design bible,
  ~50 storyboard windows, the map-reduce report) land somewhere around **$5–15**, i.e. well under a fifth of
  the image spend. So halving the image bill cuts **total per-book API cost by ~40–45%**.
- It's linear in volume: every book *and every re-drafted book* saves ~half its image cost. Phase 2's
  incremental re-render already shrinks a redraft's render count; batching halves whatever's left. Over many
  books/drafts the 50% compounds; on any single book it's a modest ~$40.

### Wall-clock benefit — not a smaller headline number, a *robustness* win on the phone

This is the part that's easy to get wrong. There are two regimes:

- **On paper (a worker running flat-out):** post-cap-raise, the synchronous path is latency-bound at
  ~10–15s/image and no longer daily-capped, so ~2,162 panels ≈ **~10 hours** (what the estimator now shows).
  The Batch API's headline is a **24-hour SLA — an upper bound, not a speedup**. Image batches often finish
  much sooner, but the guarantee is 24h. So *idealised*, batching is **not** a wall-clock win; it can even be
  slower.
- **On a real phone (the regime that actually governs this app):** the sync path runs in a background
  **WorkManager** worker that Android throttles hard — Doze, ~15-minute periodic windows, shrinking execution
  windows when backgrounded, and outright kills when the device is busy or asleep (this is *why* the app
  already slices storyboarding into tiny windows). A 2,000-image book almost certainly does **not** get 10
  continuous hours; it grinds in bursts and effectively stretches to **days**, gated by how often the phone
  lets the worker run. The batch path instead **submits one job and the rendering happens on Google's
  servers, in parallel, independent of the phone being awake, charging, or foregrounded** — the phone only
  polls occasionally to collect results.

So the honest verdict: batching's wall-clock value is **decoupling the long render from the phone's
battery/Doze constraints**, so a big book completes reliably server-side instead of depending on hours of
cooperative background execution. Under the 24-hour SLA it very likely beats the *effective* on-device sync
time for a full book, and it removes the "did the worker actually get to run?" fragility.

### The catch: cost of building it, and when it's worth it

- It's a **substantial phase** (comparable to Phase 2): a genuinely new execution mode — in-flight
  "submitted, awaiting batch" panel state that survives restarts, job-id persistence and reconciliation,
  once-per-submission Files-API sheet upload (48h retention), JSONL submission, per-result classification —
  **plus** a synchronous fallback for refusals and retries (a blocked panel through a batch costs +24h per
  attempt) and for small runs (a 20-panel quick draft shouldn't wait up to a day).
- **Break-even is dominated by robustness, not dollars.** The ~$40/book saving alone pays back a
  Phase-2-sized build only after a good number of books; if the owner converts a handful, the cost case is
  weak on its own. The compelling case is the mobile-execution decoupling — *if* the raised-cap sync path
  proves too flaky/slow on-device for a 2,000-image book.

### Recommendation

The cap raise turned "unusable (weeks)" into "usable (hours-ish)", so batching is now an **optimization, not
a rescue**. **Measure the raised-cap sync path on one real full book first.** Then:
- If the on-device sync path completes a full book acceptably (a day or two of background running), batching
  buys ~40–45% cost and some robustness — worth doing **if** the owner converts books often, otherwise
  deprioritize against its build/maintenance cost.
- If the on-device sync path proves flaky or drags for a 2,000-image book (the likely outcome under Doze),
  batching becomes clearly worth it — as much for *reliable server-side completion* as for the 50% cost cut.

## How rejected prompts are handled under batching

The current block flow (`HandleBlockedPanel`) is a synchronous escalation ladder mixing **automatic model
round-trips** with **two human gates**, keyed on the block reason (`ClassifyBlock` → Safety / Prohibited /
Filtered):

1. **Safety, first hit** → park the panel `AwaitingApproval` with the flagged harm categories; the owner
   *approves-and-relaxes-once* or *skips* in the app (human gate). The rest of the book keeps rendering.
2. **Approved, or Filtered, or first Prohibited** → ask Claude to **restage the prompt to imply** the beat,
   then re-render.
3. **Restaged and still blocked** → Safety/Filtered fall back to a **neutral placeholder**; Prohibited parks
   `AwaitingApproval` for the owner to *rework the art direction* or *skip* (human gate).

Each of those Gemini attempts is currently **minutes**. Naively routing every attempt through a batch makes
each one **up to 24h**, so a single blocked panel could burn 2–3 days walking the ladder. That's the trap.

**The design that avoids it — batch the first attempt only; escalate synchronously.**

- **Batch = one render attempt per panel, the first pass.** Submit each panel's prompt once, through a batch.
  The 50% discount and server-side parallelism apply to the ~99% that render clean on the first try.
- **Batch per scene, not per book.** Claude tags scene boundaries at storyboard time (`Panel.NewScene`), and
  the pipeline submits **one job per scene** (a scene past `BatchMaxPanelsPerJob` splits across jobs), running
  up to `MaxConcurrentBatchJobs` at once. This replaced the original single-book-job design after the owner's
  first real run: a single 664-panel job returned wrong/mismatched art and then wedged (repeated 503s) for
  days, freezing the whole book. Scene-sized jobs keep each submission well under the size ceiling, bound a
  bad job's blast radius to one scene, and — with a **give-up-by-age** guard (`BatchGiveUpHours`) — mean no
  single stuck job can freeze the book: a job that outlives the window has its panels fall back to synchronous
  rendering. The reconcile also logs result-to-panel **correlation gaps** (a result key matching no submitted
  panel, or a submitted panel with no result) so a recurrence of the wrong-art mis-correlation is diagnosable.
- **Rejections fall *out* of the batch, they aren't retried *in* it.** A batch returns per-request outcomes,
  so `ClassifyBlock` runs unchanged on each result. Split the returned results: successes → save + Completed;
  blocked → hand to the **existing** `HandleBlockedPanel` ladder.
- **Every attempt after the first is synchronous.** The relax-and-re-render, the Claude restage-and-
  re-render, the post-approval re-render, the post-rework re-render — all run as ordinary sync Gemini calls
  (minutes), on a small set (a handful of panels out of ~2,000). So the batch's 24h exposure is capped at
  exactly **one** attempt per panel, and everything iterative stays fast.
- **The human gates are untouched.** "Retry raised in the app" (the Safety approve/skip sheet and the
  Prohibited rework/skip flow) doesn't care whether the first render came from a batch or a sync call — a
  parked panel is just `AwaitingApproval`. After the owner acts, the re-render is synchronous so their
  decision takes effect in minutes, not a next-batch day.

Rule of thumb: **first attempt batched, every escalation and human-gate re-render synchronous.** That keeps
the cost/parallelism win on the bulk while the block-handling UX and timing stay exactly as they are today.

The one genuinely new piece this needs (already noted under *Real complications #5*): an in-flight panel
state — "submitted, awaiting batch," carrying the job id — that survives a restart and reconciles each
returned result into either Completed or the block ladder. Transient (non-content) failures in a batch are
handled the same way: pull them out and re-render synchronously (or roll into the next batch), rather than
re-batching one at a time.

## Implementation status — built, off by default, needs live verification

The batch renderer is built end to end and tested with a fake. It is now **on by default**
(`GeminiOptions.UseBatchRendering = true`); the first real run doubles as the live verification because every
failure path degrades to synchronous rendering (see below). Pieces:

- `IGeminiBatchClient` + models (`Domain/External/GeminiBatch.cs`); `GeminiBatchImageClient` (the real HTTP
  client, `Infrastructure/External`), registered in DI but dormant while the flag is off.
- In-flight state (now **per-panel**, so several scene jobs run at once): `Panel.BatchAttempted`,
  `Panel.NewScene`, `Panel.BatchJobId`/`Panel.BatchSubmittedAtUtc`, `WorkStatus.SubmittedToBatch` (+
  migration `AddPanelSceneBatchState`). `Novel.BatchJobId`/`BatchSubmittedAtUtc` are the legacy single-job
  columns — retained for schema stability, no longer read or written.
- `GraphicNovelProcessor.RunBatchStep`: group never-batched Pending panels into scenes (at the `NewScene`
  markers, capped at `BatchMaxPanelsPerJob`), submit one job per scene up to `MaxConcurrentBatchJobs`
  concurrently, then each cycle poll/reconcile every in-flight job — success completes the panel; a
  failure/block returns it to Pending for the **synchronous** render + `HandleBlockedPanel` ladder (first
  attempt batched, every escalation synchronous). A job older than `BatchGiveUpHours` is abandoned to the
  synchronous path; a panel stranded in `SubmittedToBatch` with no job id is recovered. E2E-tested with a
  fake batch client: one-job-per-scene, oversized-scene split, give-up-and-render-synchronously, and
  stranded recovery.

**What's settled, and the one thing the first live run confirms:**

1. **Reference sheets at scale** ✅ — sheets now go through the **Files API**: `SubmitAsync` uploads each
   DISTINCT sheet once (deduped by content hash) and references it by `fileData` URI in every request, so a
   full book no longer blows the ~20 MB inline ceiling. Unit-tested (dedup + fileData shape, no inlined
   base64). The Files API *upload protocol itself* (single-request `X-Goog-Upload-Protocol: raw`) is
   reconstructed from docs — if the live API needs the resumable handshake, `UploadReferenceAsync` is the one
   method to adjust, and a failed upload fails the submit into the synchronous fallback, so it's safe.
2. **The Batch REST envelope** ⏳ first-run check. `SubmitAsync`/`PollAsync` assume the
   `models/{model}:batchGenerateContent` request wrapper (`batch.inputConfig.requests.requests[]` with a
   `metadata.key` per request) and the polled Operation's result nesting (`response.inlinedResponses…`,
   correlated by `metadata.key`). If the real JSON differs, adjust the builder/parser (the 6 parse tests
   localize the change). Parsing already degrades to "no image → synchronous fallback" on a mismatch, so a
   wrong shape is safe-but-useless, not crashy — which is why the default could be flipped on without a
   sandbox-impossible live check first.
3. **Exposure** ✅ — flipped the `UseBatchRendering` default on (no Settings toggle; the safe-fallback design
   makes a footgun toggle unnecessary — set the option false to force synchronous).

Still worth measuring the raised-cap synchronous path on a real book (per the economics section): batching is
an optimization, and if the synchronous path is already fast enough, the up-to-24h batch turnaround may not
be worth it — set `UseBatchRendering = false` if so.

## Sources

- [Batch API — Gemini API docs](https://ai.google.dev/gemini-api/docs/batch-api)
- [Batch Mode in the Gemini API: Process more for less — Google Developers Blog](https://developers.googleblog.com/en/scale-your-ai-workloads-batch-mode-gemini-api/)
- [Gemini Developer API pricing](https://ai.google.dev/gemini-api/docs/pricing) — image output ~$30/1M tokens (~$0.039/image); Batch API 50% off
- [Rate limits — Gemini API docs](https://ai.google.dev/gemini-api/docs/rate-limits)
- [Batch processing — google-gemini/cookbook](https://deepwiki.com/google-gemini/cookbook/2.6-batch-processing)
