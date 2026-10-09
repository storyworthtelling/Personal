namespace Storyworthtelling.Handwriting;

// Configures the Claude vision call ClaudeVisionHandwritingExtractor makes. A caller that already
// has a broader Anthropic-key config for its other Claude callers (Foreword's own
// AnthropicOptions, shared with its task-reading and prayer-transcription clients) maps its key
// and model into this at DI registration time rather than this library owning that shape.
public class ClaudeVisionOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-5-5";
}
