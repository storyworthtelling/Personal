namespace Storyboarder.Infrastructure.Configuration;

// Strongly-typed configuration bound from appsettings / user-secrets / environment variables.
// Secrets (API keys, client ids) must come from user-secrets or env vars, never appsettings.json.

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";

    // The cheap tier, used for the two high-volume mechanical calls: pulling entities out of a chunk and
    // taking per-chunk analysis notes. Both run once per chunk — by far the most calls a novel makes —
    // and both are extraction against an explicit JSON contract rather than judgement, which is what the
    // small model is good at. The work that actually needs the strong model (adapting prose into panels,
    // and synthesising the whole manuscript into a report) stays on Model.
    public string FastModel { get; set; } = "claude-haiku-4-5-20251001";

    public int MaxTokens { get; set; } = 8_000;

    // Storyboarding is the heaviest output the pipeline produces (a panel-by-panel breakdown as one
    // JSON object), so it gets its own budget rather than the small default MaxTokens. Storyboarding now
    // runs on small text sub-windows (see RateLimitOptions.StoryboardWindowChars), so this doubles as a
    // per-call duration cap: bounding the output also bounds how long one call streams, keeping it
    // comfortably inside the background worker's time budget.
    public int StoryboardMaxTokens { get; set; } = 16_000;

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
}

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash-image";

    // Render the first pass of panels through the Gemini Batch API (~50% cheaper, separate quota pool,
    // up-to-24h turnaround, work runs server-side rather than grinding on the phone). On by default now
    // that reference sheets go through the Files API (so a real book stays under the inline size ceiling).
    // Anything a batch fails or blocks falls back to the synchronous render + block ladder, and a wrong
    // REST shape degrades to that same fallback — so the first live run verifies the envelope safely
    // rather than crashing (see BATCHING.md). Set false to force the fully-synchronous path.
    public bool UseBatchRendering { get; set; } = true;

    // How long to wait between polls of an in-flight batch job. The job can take hours, so polling every
    // few minutes is plenty and keeps the worker cheap.
    public int BatchPollSeconds { get; set; } = 300;

    // Panels are batched per SCENE (Claude tags scene boundaries), so a book runs several small jobs
    // instead of one giant one — that keeps every job well under the inline/Files-API size ceiling and
    // bounds the blast radius of a bad job to one scene. At most this many scene jobs are in flight at
    // once (more scenes queue until a slot frees), and a single job never carries more than
    // BatchMaxPanelsPerJob panels — a very long scene is split across several jobs.
    public int MaxConcurrentBatchJobs { get; set; } = 4;
    public int BatchMaxPanelsPerJob { get; set; } = 50;

    // A batch job polled for longer than this without finishing is given up on: its panels go back to
    // Pending and render synchronously (they're marked BatchAttempted, so they're never re-batched). This
    // is the guard against the failure mode where a provider job wedges (e.g. repeated 503s) and the book
    // would otherwise stay frozen forever waiting on a job that never completes.
    public int BatchGiveUpHours { get; set; } = 24;
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

    // Off by default, and it must stay that way until the Drive WRITE scope (drive.file) is registered on
    // the OAuth consent screen and granted on the device. Defaulting this on would send every finished
    // book into a stage whose only possible outcome is a permission error.
    public bool Enabled { get; set; }

    // Delete the local copies once the upload is confirmed. Separate from Enabled because archiving for
    // safekeeping and reclaiming phone storage are different decisions: a book's rasters and PDFs run to
    // hundreds of megabytes, but a user may still want them on the device.
    public bool FreeLocalCopies { get; set; }
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string DatabasePath { get; set; } = "storyboarder.db";
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

    // Storyboarding slices each design-bible chunk into smaller windows and adapts one per Claude call.
    // A whole ~48K-char chunk in a single call generates for many minutes — long enough that Android's
    // background worker is killed mid-stream ("Software caused connection abort"). These bound each
    // window (packing toward Chars, splitting oversized paragraphs at Max) so every call stays short and
    // finishes well inside the worker's time budget; progress is persisted per window so it's resumable.
    public int StoryboardWindowChars { get; set; } = 8_000;
    public int StoryboardWindowMaxChars { get; set; } = 10_000;
}

public sealed class PageLayoutOptions
{
    public const string SectionName = "PageLayout";
    public int Width { get; set; } = Domain.Design.DesignTokens.Comic.PageWidth;
    public int Height { get; set; } = Domain.Design.DesignTokens.Comic.PageHeight;
    public int TopMargin { get; set; } = Domain.Design.DesignTokens.Comic.TopMargin;
    public int SideMargin { get; set; } = Domain.Design.DesignTokens.Comic.SideMargin;

    // Deeper than the sides: contains the folio band (see DESIGN.md §7).
    public int BottomMargin { get; set; } = Domain.Design.DesignTokens.Comic.BottomMargin;
    public int Gutter { get; set; } = Domain.Design.DesignTokens.Comic.Gutter;

    // The hard ceiling on panels drawn on one physical page. The storyboard model is steered toward
    // this, but a page it returns with more panels is split across several pages at storyboard time so
    // panels never get squished into a dense grid.
    public int MaxPanelsPerPage { get; set; } = 6;

    // The most pages one bound volume may hold. Volumes are decomposed one-per-chapter, but a chapter
    // (or a chapterless book) larger than this is split into parts at scene boundaries so no single
    // volume PDF grows large enough to blow the phone's memory or the worker's time budget while binding.
    // Sized well under the 338-page volume that used to wedge assembly for days; a chapter-sized volume
    // is typically far smaller, so this only ever trips on unusually long chapters or unchaptered books.
    public int MaxPagesPerVolume { get; set; } = 40;
}
