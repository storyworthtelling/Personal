namespace Codex.Domain.Post;

/// <summary>
/// What to do with MMS notices Post could not read. The carrier announces a message once and
/// holds it for days, so a notice that fails to parse is kept (privately, on the phone) and
/// tried again after an update that might read it — otherwise the message is lost for good.
/// A notice older than the carrier would hold is not worth another try, and the store is bounded.
/// </summary>
public static class UnreadNotices
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    public const int DefaultMaxKept = 50;

    public sealed record Decision(IReadOnlyList<string> Keep, IReadOnlyList<string> Discard);

    /// <summary>Newest first within the age and count limits; everything else is discarded.</summary>
    public static Decision Plan(IEnumerable<(string Name, long SavedSeconds)> files, DateTimeOffset now, int maxKept = DefaultMaxKept)
    {
        var oldest = now.Subtract(MaxAge).ToUnixTimeSeconds();
        var keep = new List<string>();
        var discard = new List<string>();
        foreach (var (name, saved) in files.OrderByDescending(f => f.SavedSeconds))
        {
            (saved >= oldest && keep.Count < maxKept ? keep : discard).Add(name);
        }
        return new Decision(keep, discard);
    }

    public static string NameFor(DateTimeOffset savedAt, string unique) => $"{savedAt.ToUnixTimeSeconds()}-{unique}.pdu";

    public static long? SavedSeconds(string name)
    {
        var dash = name.IndexOf('-');
        return dash > 0 && long.TryParse(name[..dash], out var seconds) ? seconds : null;
    }
}
