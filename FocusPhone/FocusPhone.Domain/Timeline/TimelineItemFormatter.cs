namespace FocusPhone.Domain.Timeline;

/// <summary>
/// One-line description of a timeline item for list rows: call snippets are
/// already self-describing ("Missed call"), other kinds get a kind label so
/// the mixed feed stays readable.
/// </summary>
public static class TimelineItemFormatter
{
    public static string Describe(TimelineItem item)
    {
        var snippet = (item.Snippet ?? "").Trim();
        return item.Kind switch
        {
            TimelineItemKind.Call => snippet,
            TimelineItemKind.Voicemail when snippet.Length == 0 || snippet == "Voicemail" => "Voicemail",
            TimelineItemKind.Voicemail => $"Voicemail: {snippet}",
            TimelineItemKind.Email => snippet.Length == 0 ? "Email" : $"Email: {snippet}",
            _ => snippet.Length == 0 ? "Text" : $"Text: {snippet}",
        };
    }
}
