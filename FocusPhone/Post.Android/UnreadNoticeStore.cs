using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// Keeps the raw bytes of an MMS notice Post could not read, in the app's private files
/// (never the log: the bytes hold the sender's number and the fetch URL), so the sweep can
/// read it again once a build that understands it is installed. Retention is
/// <see cref="UnreadNotices"/>'s call.
/// </summary>
internal static class UnreadNoticeStore
{
    private const string Folder = "mms-unread";

    public static void Save(Context context, byte[] pdu)
    {
        try
        {
            var directory = Directory(context);
            File.WriteAllBytes(Path.Combine(directory, UnreadNotices.NameFor(DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N")[..8])), pdu);
            Prune(directory);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("Mms", "Could not keep an unreadable MMS notice for a later retry", e);
        }
    }

    /// <summary>Re-reads every kept notice; one that now reads goes to a download and is removed.</summary>
    public static void Retry(Context context)
    {
        try
        {
            var directory = Directory(context);
            var kept = Prune(directory);
            foreach (var name in kept)
            {
                var path = Path.Combine(directory, name);
                var notification = MmsNotificationParser.Parse(File.ReadAllBytes(path));
                if (notification is null)
                {
                    continue;
                }
                PostDiagnostics.LogText("Mms", "Retrying an MMS notice that could not be read earlier");
                MmsDownloader.RequestDownload(context, notification);
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("Mms", "Could not retry unreadable MMS notices", e);
        }
    }

    private static List<string> Prune(string directory)
    {
        var files = System.IO.Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(name => (Name: name, Saved: UnreadNotices.SavedSeconds(name)))
            .ToList();
        var plan = UnreadNotices.Plan(files.Where(f => f.Saved is not null).Select(f => (f.Name, f.Saved!.Value)), DateTimeOffset.UtcNow);
        foreach (var name in plan.Discard.Concat(files.Where(f => f.Saved is null).Select(f => f.Name)))
        {
            File.Delete(Path.Combine(directory, name));
        }
        return [.. plan.Keep];
    }

    private static string Directory(Context context)
    {
        var path = Path.Combine(context.FilesDir!.AbsolutePath, Folder);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
}
