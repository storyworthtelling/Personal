namespace Codex.Domain.Timeline;

/// <summary>
/// Who said a message, as a group conversation's rows must show it: in a group the arrow alone
/// does not say which member spoke.
/// </summary>
public static class GroupSender
{
    /// <summary>"You" for the owner's own message, else the sender's name; null when it cannot be said.</summary>
    public static string? Label(TimelineItem item, Func<string, string> displayName)
    {
        if (!item.Incoming)
        {
            return "You";
        }

        // A group item that carries no member of its own (keyed on the group) names nobody.
        if (string.IsNullOrWhiteSpace(item.ContactKey)
            || (item.GroupId is not null && item.ContactKey == item.GroupId))
        {
            return null;
        }

        var name = displayName(item.ContactKey);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
