namespace Storyboarder.Infrastructure.Configuration;

// Strongly-typed configuration bound from appsettings / user-secrets / environment variables.
// Secrets (API keys, client ids) must come from user-secrets or env vars, never appsettings.json.

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int MaxTokens { get; set; } = 8_000;

    // Storyboarding is the heaviest output the pipeline produces (a panel-by-panel breakdown as one
    // JSON object), so it gets its own budget rather than the small default MaxTokens. Storyboarding now
    // runs on small text sub-windows (see RateLimitOptions.StoryboardWindowChars), so this doubles as a
    // per-call duration cap: bounding the output also bounds how long one call streams, keeping it
    // comfortably inside the background worker's time budget.
    public int StoryboardMaxTokens { get; set; } = 16_000;

    // The consolidated revision report is long (many sections + several reviews), so its single
    // "reduce" call gets a larger output budget than the per-chunk/per-panel calls.
    public int AnalysisMaxTokens { get; set; } = 16_000;
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
    public int GeminiRequestsPerMinute { get; set; } = 5;
    public int GeminiRequestsPerDay { get; set; } = 100;
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
}
