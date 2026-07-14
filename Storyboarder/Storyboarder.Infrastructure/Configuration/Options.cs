namespace Storyboarder.Infrastructure.Configuration;

// Strongly-typed configuration bound from appsettings / user-secrets / environment variables.
// Secrets (API keys, client ids) must come from user-secrets or env vars, never appsettings.json.

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int MaxTokens { get; set; } = 8_000;

    // A single storyboard call must emit the full panel-by-panel breakdown of an entire chunk
    // window (many pages, each with several panels of prose visual prompts) as one JSON object —
    // by far the heaviest output the pipeline produces. The default MaxTokens is too small: dense
    // chunks overflow it, the response is truncated mid-object, and the JSON fails to parse, so the
    // novel stalls in "needs attention". Give storyboarding its own generous budget.
    public int StoryboardMaxTokens { get; set; } = 32_000;

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
