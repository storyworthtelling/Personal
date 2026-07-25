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

## Sources

- [Batch API — Gemini API docs](https://ai.google.dev/gemini-api/docs/batch-api)
- [Batch Mode in the Gemini API: Process more for less — Google Developers Blog](https://developers.googleblog.com/en/scale-your-ai-workloads-batch-mode-gemini-api/)
- [Rate limits — Gemini API docs](https://ai.google.dev/gemini-api/docs/rate-limits)
- [Batch processing — google-gemini/cookbook](https://deepwiki.com/google-gemini/cookbook/2.6-batch-processing)
