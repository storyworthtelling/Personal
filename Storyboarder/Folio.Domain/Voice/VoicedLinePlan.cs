using Folio.Domain.Model;

namespace Folio.Domain.Voice;

/// <summary>
/// Whether a book's line on the Work desk needs writing again.
///
/// <para><b>The line is persisted, and that is the whole point.</b> It is composed by a model, so a
/// desk that composed on render would bill a call per book every time the owner looked at the shelf —
/// and the shelf is the screen the app opens on. Persisting it makes the cost one call per book per
/// state change, which is the rate at which the line actually has anything new to say.</para>
///
/// <para>So the question is not "is there a line" but "is the line still about what is happening".
/// A book that has not moved keeps the line it has, however many times the desk is drawn.</para>
/// </summary>
public static class VoicedLinePlan
{
    /// <summary>
    /// True when the stored line no longer describes the stage the book is in — or when there is no
    /// line at all, which is the first paint after a book is added.
    /// </summary>
    public static bool NeedsComposing(string? storedLine, ProjectStage? storedStage, ProjectStage current) =>
        string.IsNullOrWhiteSpace(storedLine) || storedStage != current;
}
