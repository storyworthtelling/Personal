namespace Foreword.Infrastructure.Handwriting;

// Configures the Claude vision call used to transcribe hand-written notes.
public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";

    // Notes transcribed below this confidence are not auto-added; they are carried over
    // onto the next document for re-entry.
    public double ConfidenceThreshold { get; set; } = 0.6;
}
