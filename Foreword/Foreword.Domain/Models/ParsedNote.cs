namespace Foreword.Domain.Models;

// A hand-written note transcribed from the bottom region of a delivered document.
// Becomes either a calendar event or a task depending on IsTask.
public class ParsedNote
{
    public required string Text { get; init; }

    // The date/time the note refers to, when one could be determined.
    public DateTime? When { get; init; }

    // The column the note should be filed under, when the writer indicated one.
    public PersonColumn? Column { get; init; }

    // True when the note is a to-do item rather than a calendar event.
    public bool IsTask { get; init; }

    // Model confidence 0..1 that the transcription is accurate. Low-confidence notes
    // are not auto-added; they are carried over onto the next document for re-entry.
    public double Confidence { get; init; }
}
