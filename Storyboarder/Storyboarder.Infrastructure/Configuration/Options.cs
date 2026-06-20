namespace Storyboarder.Infrastructure.Configuration;

// Strongly-typed configuration bound from appsettings / user-secrets / environment variables.
// Secrets (API keys, client ids) must come from user-secrets or env vars, never appsettings.json.

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int MaxTokens { get; set; } = 8_000;
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
    public int ClaudeRequestsPerDay { get; set; } = 2_000;
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
    public int Width { get; set; } = 1_024;
    public int Height { get; set; } = 1_536;
    public int Margin { get; set; } = 40;
    public int Gutter { get; set; } = 24;
}
