using Android.Content;
using Codex.Infrastructure.Dash;
using Codex.Ui;
using Watch.Domain;

namespace Post;

/// <summary>
/// The book of the wrist's acts for this process, kept on the phone (P14 B2) so it outlives the process: a duplicated
/// act or send heard after a restart is still done once, an undo still takes back what its act took, and a delete still
/// inside its window is still owed (<see cref="DashHolds.Resume"/>). One book a process, read from the preferences the
/// first time it is wanted and written whole after every change, synchronously, since the listener runs off the main
/// thread and a book written later could be lost with the process.
/// </summary>
internal static class DashBook
{
    private const string Tag = "Dash";  // not copy: a log tag

    private static readonly Lock Gate = new();
    private static DashActs? acts;

    public static DashActs Acts(Context context)
    {
        lock (Gate)
        {
            if (acts is null)
            {
                var read = DashActsCodec.Decode(Preferences(context).GetString(WatchIdentity.DashActsKey, null));
                if (read.Unread)
                {
                    // A record that would not read starts a new book rather than stopping the relay: its acts' windows
                    // are long past, and only an undo of one of them is lost.
                    HeadLog.Warn(Tag, "DASH the acts’ record didn’t read; a new one starts");
                }
                acts = DashActs.From(read.Records);
            }
            return acts;
        }
    }

    /// <summary>Writes the book whole, as it stands now.</summary>
    public static void Save(Context context)
    {
        lock (Gate)
        {
            if (acts is null)
            {
                return;
            }
            var written = Preferences(context).Edit()!.PutString(WatchIdentity.DashActsKey, DashActsCodec.Encode(acts.All))!.Commit();
            if (!written)
            {
                HeadLog.Warn(Tag, "DASH the acts’ record wasn’t written; it is written again after the next act");
            }
        }
    }

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(WatchIdentity.DashPreferences, FileCreationMode.Private)!;
}
