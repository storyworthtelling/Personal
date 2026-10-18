namespace Constitution.Domain.Ink;

/// <summary>
/// What the vision read saw at one target on one page (the typed input to <see cref="InkResolver"/>;
/// the call that produces it is C-2.2.3). It says whether the target carries ink, what was written in
/// it if anything, and how sure the read is of all of that together.
///
/// <para><b>Confidence is the read's confidence in this whole answer</b>, "empty" included: a
/// scribble the read is sure is a mark is <c>Marked: true</c> at 0.9, and a box it cannot tell from a
/// smudge is low whatever it guessed. A target that was not reported at all is <i>not</i> an empty
/// read; <see cref="InkPageRead.For"/> returns null for it and the resolver will not call it silence.</para>
///
/// <para>A circled word is read per lane: a circle that touches two lanes is two lanes that are
/// <c>Marked</c>, which is exactly what <see cref="InkResolver"/> will not guess between.</para>
/// </summary>
public sealed record InkTargetRead
{
    /// <param name="targetId">The target's id on the page's <see cref="Documents.InkMap"/>.</param>
    /// <param name="marked">Whether there is any ink in the target.</param>
    /// <param name="confidence">From 0 to 1, how sure the read is of this answer.</param>
    /// <param name="text">What was written in the target, if anything was; blank means nothing legible.</param>
    /// <param name="runnerUpText">The next most likely reading of the writing, offered as the second chip.</param>
    public InkTargetRead(string targetId, bool marked, double confidence, string? text = null, string? runnerUpText = null)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("A read names the target it is about, or the resolver cannot tell which box it was.", nameof(targetId));
        }

        TargetId = targetId;
        Marked = marked;
        Confidence = InkConfidence.Check(confidence, nameof(confidence));
        Text = Clean(text);
        RunnerUpText = Clean(runnerUpText);
    }

    public string TargetId { get; }

    public bool Marked { get; }

    public double Confidence { get; }

    /// <summary>The writing, trimmed; null when none was legible.</summary>
    public string? Text { get; }

    public string? RunnerUpText { get; }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
