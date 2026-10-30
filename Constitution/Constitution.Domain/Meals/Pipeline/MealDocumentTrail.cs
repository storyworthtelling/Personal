namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// The documents step's words in the Body trail: fixed phrases and counts, never a title, a name or a date. Every word is in the trail
/// vocabulary (<c>TrailVocabularyTests</c> reads the literals returned below), all lower case because the export's filter reads a capital as a
/// possible name. A count stands beside "documents" or "records", the words the filter lets a count stand beside.
/// </summary>
public static class MealDocumentTrail
{
    /// <summary>One line per kind of outcome that happened, then the pantry notes; or the one line for a stop.</summary>
    public static IReadOnlyList<string> Lines(MealDocumentsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result)
        {
            case MealDocumentsResult.Stopped stopped:
                return [LogPhrase(stopped.Because)];
            case MealDocumentsResult.Done done:
                var lines = new List<string>();
                foreach (var verdict in Enum.GetValues<MealDocumentVerdict>())
                {
                    var count = done.Documents.Count(item => item.Verdict == verdict);
                    if (count > 0)
                    {
                        lines.Add(LogPhrase(verdict, count));
                    }
                }

                if (done.PantrySentence is not null)
                {
                    lines.Add(LogPhrase());
                }

                if (done.UnreadableStaples > 0)
                {
                    lines.Add($"meal documents: {done.UnreadableStaples} records unreadable");
                }

                return lines;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, "This result has no trail lines; add them here.");
        }
    }

    private static string LogPhrase() => "meal documents: pantry empty";

    private static string LogPhrase(MealDocumentsStoppedBecause because)
    {
        switch (because)
        {
            case MealDocumentsStoppedBecause.NoTablet:
                return "meal documents not delivered, no tablet here";
            case MealDocumentsStoppedBecause.RosterUnavailable:
                return "meal documents not delivered, roster unreadable";
            case MealDocumentsStoppedBecause.PantryUnreadable:
                return "meal documents not delivered, pantry unreadable";
            default:
                throw new ArgumentOutOfRangeException(nameof(because), because, "This reason has no trail phrase; add one here.");
        }
    }

    private static string LogPhrase(MealDocumentVerdict verdict, int count)
    {
        switch (verdict)
        {
            case MealDocumentVerdict.Delivered:
                return $"meal documents: {count} documents delivered";
            case MealDocumentVerdict.Replaced:
                return $"meal documents: {count} documents replaced";
            case MealDocumentVerdict.Unchanged:
                return $"meal documents: {count} documents unchanged";
            case MealDocumentVerdict.NotYet:
                return $"meal documents: {count} documents not yet";
            case MealDocumentVerdict.TooLate:
                return $"meal documents: {count} documents too late";
            case MealDocumentVerdict.NotHere:
                return $"meal documents: {count} documents not here";
            case MealDocumentVerdict.NotBuilt:
                return $"meal documents: {count} documents not built";
            case MealDocumentVerdict.NotDelivered:
                return $"meal documents: {count} documents failed";
            case MealDocumentVerdict.Kept:
                return $"meal documents: {count} documents kept";
            default:
                throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "This verdict has no trail phrase; add one here.");
        }
    }
}
