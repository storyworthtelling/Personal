namespace Storyworthtelling.Voice;

// Configuration for the Claude-backed voice client. The API key is the same Claude key the apps already
// take on their Settings page; the two models split the work by cost, mirroring the rest of the repo.
public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    public string ApiKey { get; set; } = string.Empty;

    // Refining the profile means reading a whole document and distilling a voice from it — judgement work,
    // so it runs on the strong tier.
    public string Model { get; set; } = "claude-sonnet-4-6";

    // Composing one situational message is a small, high-frequency call against a fixed voice, so it runs
    // on the cheap tier. Every app hits this every time it shows a generated message.
    public string FastModel { get; set; } = "claude-haiku-4-5-20251001";

    // A distilled profile is a short paragraph plus a few lists; a situational message is a sentence or two.
    // Neither needs a large output budget.
    public int RefineMaxTokens { get; set; } = 2_000;
    public int ComposeMaxTokens { get; set; } = 400;
}
