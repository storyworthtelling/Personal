using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// What a correspondence's row is called (P14 B1, out of the Mailbox's own row builder): a group's own name (the
/// owner's, else Google Messages'), else up to three members' names and how many more, else "Group"; a person's name.
/// The names come from the address book, which only the head can read, so it hands in the lookup.
/// </summary>
public static class ThreadTitles
{
    public static string ForRow(ContactThread thread, GroupNames groupNames, Func<string, string> nameOf)
    {
        if (!thread.IsGroup)
        {
            return nameOf(thread.ContactKey);
        }
        if (groupNames.TitleFor(thread.ContactKey) is { } named)
        {
            return named;
        }

        var joined = string.Join(", ", thread.Participants.Take(3).Select(nameOf));
        if (joined.Length == 0)
        {
            return "Group";
        }
        return thread.Participants.Count > 3 ? $"{joined} +{thread.Participants.Count - 3}" : joined;
    }
}
