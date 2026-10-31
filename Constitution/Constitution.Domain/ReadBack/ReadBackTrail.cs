namespace Constitution.Domain.ReadBack;

/// <summary>
/// The trail's words for a read-back: fixed phrases, a few counts, an exception's type name, never a title, a name,
/// a mark or an exception's message. Each goes through the export's filter whole (<c>TrailLines</c>), so every word
/// here is in its vocabulary and a new one is a decision (<c>TrailVocabularyTests</c> reads this file).
/// </summary>
public static class ReadBackTrail
{
    public static string LogPhrase(DocumentReadBack outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome)
        {
            case DocumentReadBack.Settled settled:
                return $"ink read back settled, {settled.PagesRead} pages read, {settled.Questions} questions asked";
            case DocumentReadBack.DigestTrashed:
                return "ink read back digest trashed";
            case DocumentReadBack.DigestKept:
                return "ink read back digest kept";
            case DocumentReadBack.NoMarksInCloud:
                return "ink read back document found, nothing read, page kept";
            case DocumentReadBack.NotOnTablet:
                return "ink read back document not on the tablet";
            case DocumentReadBack.Unfinished:
                return "ink read back page not read, document kept";
            case DocumentReadBack.GaveUp:
                return "ink read back gave up, document kept";
            case DocumentReadBack.Failed failed:
                return $"ink read back failed at {failed.Stage.ToString().ToLowerInvariant()}: {failed.Failure.GetType().Name}, document kept";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This outcome has no trail phrase; add one here.");
        }
    }
}
