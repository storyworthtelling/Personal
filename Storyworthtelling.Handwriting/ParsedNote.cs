namespace Storyworthtelling.Handwriting;

// A hand-written note transcribed by a vision call. What it becomes — a calendar event, a task, a
// budget line — is entirely up to the caller.
public class ParsedNote
{
    public required string Text { get; init; }

    // The date/time the note refers to, when one could be determined.
    public DateTime? When { get; init; }

    // Which of the caller's own regions/columns/categories the writer indicated, exactly as the
    // model answered. This library does not know what values are valid here — that is an app
    // concern (Foreword's PersonColumn enum, for instance, is not a concept this library shares),
    // so the caller parses and validates this string itself.
    public string? Column { get; init; }

    // True when the note is a to-do item rather than a calendar event.
    public bool IsTask { get; init; }

    // Model confidence 0..1 that the transcription is accurate. What counts as "low enough to not
    // auto-add" is entirely the caller's own decision.
    public double Confidence { get; init; }
}
