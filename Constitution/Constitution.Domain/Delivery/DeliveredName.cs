namespace Constitution.Domain.Delivery;

/// <summary>
/// What a document is called on the tablet, in one function. Delivery uploads under it and the morning
/// read-back (C-2.2.3) downloads by exactly it, so the two cannot drift. The day is in the name because
/// the read-back finds yesterday's pages by title and a tablet may hold several days at once.
/// </summary>
public static class DeliveredName
{
    private const string Prefix = "Constitution";
    private const string Separator = " — ";
    private static readonly char[] Illegal = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>The title for this document; <paramref name="subject"/> (an assessment, a variant) is optional.</summary>
    public static string For(DeliveryKind kind, DateOnly date, string? subject = null)
    {
        var name = Prefix + Separator + kind + Separator + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var safe = Safe(subject);
        return safe.Length == 0 ? name : name + Separator + safe;
    }

    private static string Safe(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        var cleaned = subject.Trim();
        foreach (var illegal in Illegal)
        {
            cleaned = cleaned.Replace(illegal, '-');
        }

        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
