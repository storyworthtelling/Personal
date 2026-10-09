namespace Folio.Infrastructure.Configuration;

// Strongly-typed configuration bound from appsettings / user-secrets / environment variables.
// Secrets (API keys, client ids) must come from user-secrets or env vars, never appsettings.json.

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-5-5";

    // The cheap tier, used for the per-chunk analysis notes: genuinely mechanical note-taking against an
    // explicit JSON contract, run once per chunk (the most calls a novel makes), which is what the small
    // model is good at. Entity extraction was on this tier too, but the cheap model under-extracted —
    // dropping minor characters and described sets, so those panels lost their reference sheets — so it
    // moved back to Model. The other work needing the strong model (adapting prose into panels,
    // synthesising the report) has always stayed on Model.
    public string FastModel { get; set; } = "claude-haiku-5-5";

    public int MaxTokens { get; set; } = 8_000;

    // Entity extraction is EXHAUSTIVE by design — every character and setting in a chunk of up to ~48K
    // characters, each with a description, an approximate scale, and aliases — so its JSON output
    // routinely outgrows the small default MaxTokens. A reply that runs out of budget is not merely
    // trimmed: it is thrown away wholesale (ResponseTruncatedException) and, once the retries are spent,
    // permanently fails the chunk — which used to leave the design bible unable to consolidate at all.
    // So this call gets a budget sized for a full cast-and-set list, like the other heavy calls.
    public int ExtractionMaxTokens { get; set; } = 16_000;

    // The per-chunk analysis "map" call. Its notes now cover craft as well as story (line-edit
    // candidates, prose/voice patterns, dialogue, a scene list) for a chunk of up to ~48K characters,
    // which is more than the small default MaxTokens comfortably holds — and a reply that runs out of
    // budget is thrown away wholesale (ResponseTruncatedException), not merely trimmed.
    public int ChapterNotesMaxTokens { get; set; } = 12_000;

    // The consolidated revision report is long (many sections + several reviews), so its "reduce"
    // call gets a larger output budget than the per-chunk/per-panel calls. This covers the STORY half
    // only — synopsis, character map, plot holes, inconsistencies, pacing, reviews, revision plan.
    public int AnalysisMaxTokens { get; set; } = 16_000;

    // The CRAFT half of the report (line edits, prose/voice, dialogue, the scene-by-scene map) is
    // consolidated by its own call with its own budget. The scene map alone is one entry per scene
    // across a whole novel, so it is the larger of the two halves. Splitting the reduce in two is
    // deliberate: one call producing both halves would need ~40K output tokens, which both risks
    // truncation (the client throws on stop_reason=max_tokens rather than returning a partial report)
    // and streams for long enough that Android's background worker can be killed mid-response. Two
    // calls of this size each stream for about as long as the storyboard calls that already work.
    public int CraftMaxTokens { get; set; } = 20_000;

    // One persona's whole-book pass. Measured (F49): a reader review runs to about 4,000 output
    // tokens and a section to about 3,000, and both stopped at the cap rather than finishing, so the
    // budget is set above what was observed rather than at it. A reply that runs out of budget is
    // thrown away wholesale, not trimmed — a truncated read costs the whole pass.
    public int PassMaxTokens { get; set; } = 8_000;

    // The two models the seventeen passes run on (the finding's reversal 2). Judgement — the seven
    // readers and the two synthesis passes — goes to the strong one, because there the judgement is
    // the product; the eight structural sections go to the standard one.
    //
    // They are their own settings rather than reusing Model above, and that is deliberate: every other
    // call in the pipeline uses Model too, and moving all of them is a separate decision with its own
    // blast radius, not something to do as a side effect of building the reads. Reversal 2 scopes the
    // switch to the passes; this is that scope, written down.
    public string StandardModel { get; set; } = "claude-sonnet-5-5";
    public string StrongModel { get; set; } = "claude-opus-5-5";
}

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash-image";
}

// Browser-side Google credentials surfaced to the status page for the Drive Picker.
public sealed class GoogleOptions
{
    public const string SectionName = "Google";
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    // Deliver the finished report to Google Drive beside the source manuscript, by DEFAULT (Folio's
    // delivery model — the report simply appears in Drive next to the book). Delivery only actually runs
    // when a Drive archive is wired with the full `drive` scope; until that scope is granted the upload
    // fails gracefully and the review still finishes (the report stays on the device). Set false to keep
    // the report on-device only.
    public bool Enabled { get; set; } = true;

    // Delete the local copy of the report once the upload is confirmed. Separate from Enabled because
    // delivering for access and reclaiming phone storage are different decisions.
    public bool FreeLocalCopies { get; set; }

    // Also deliver the finished report to the owner's reMarkable tablet (Phase 4),
    // where it's read and annotated by hand. Off by default (it needs a device-code pairing); best-effort
    // like the Drive upload — a reMarkable failure never blocks the review from finishing.
    public bool DeliverToRemarkable { get; set; }
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string DatabasePath { get; set; } = "folio.db";
    public string OutputRoot { get; set; } = "output";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    public int ClaudeRequestsPerMinute { get; set; } = 5;

    // Safety ceiling, not an expected volume: a full novel needs well under this many Claude calls
    // (roughly two per chunk plus a consolidation), so a day that approaches it means something is
    // looping. Kept comfortably above real usage but low enough that a runaway can't rack up cost.
    public int ClaudeRequestsPerDay { get; set; } = 500;
    // Sized to the owner's real Gemini tier (gemini-2.5-flash-image: 2,000/min, 50,000/day) but kept as a
    // safety ceiling below it, not at it. A single background worker renders one panel per cycle and is
    // latency-bound (~seconds per image), so it can't physically approach these — they exist to remove the
    // old artificial 100/day bottleneck (which stretched a full book to weeks) while still catching a
    // runaway: images are billed each, so an order-of-magnitude margin under the account's 50,000/day caps
    // a bug's worst-case spend instead of letting it drain the whole daily quota.
    public int GeminiRequestsPerMinute { get; set; } = 60;
    public int GeminiRequestsPerDay { get; set; } = 5_000;
    public int MaxAttempts { get; set; } = 6;
    public int BaseDelaySeconds { get; set; } = 4;
    public int MaxDelaySeconds { get; set; } = 600;
    public int IdlePollSeconds { get; set; } = 10;
}
