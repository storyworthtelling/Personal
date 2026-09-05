using Android.Content;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>
/// Where the Darkroom lives on the phone: an index in this head's own preferences, and the
/// pictures themselves in app-private storage.
///
/// <para><b>Private on purpose.</b> Nothing in the Darkroom has been backed up — it goes
/// when the owner sends it, and not before. Writing captures into MediaStore at the shutter
/// would put them straight into Google Photos and there would be nothing left to choose
/// between, which is the whole point of the room.</para>
///
/// <para>That means Plate holds the only copy until the owner acts. It is the cost they
/// accepted knowingly, and it is why the index is written on every change rather than at
/// some convenient later moment.</para>
/// </summary>
public sealed class DarkroomStore
{
    private const string PreferencesName = "darkroom";
    private const string RoomKey = "room";
    private const string Folder = "darkroom";

    private readonly Context context;
    private readonly ISharedPreferences preferences;

    public DarkroomStore(Context context)
    {
        this.context = context;
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public Darkroom Load() => DarkroomCodec.Read(preferences.GetString(RoomKey, null));

    public void Save(Darkroom room) =>
        preferences.Edit()!.PutString(RoomKey, DarkroomCodec.Write(room))!.Apply();

    /// <summary>The folder the pictures sit in, made if it is not there yet.</summary>
    public Java.IO.File Directory()
    {
        var directory = new Java.IO.File(context.FilesDir, Folder);
        if (!directory.Exists())
        {
            directory.Mkdirs();
        }
        return directory;
    }

    /// <summary>A new, unused file for a capture. The name is the item's id.</summary>
    public Java.IO.File NewFile(string extension) =>
        new(Directory(), $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.{extension}");

    public Java.IO.File FileFor(DarkroomItem item) => new(Directory(), item.Id);

    /// <summary>
    /// Removes the bytes. Called only once an undo can no longer put the item back — a
    /// delete the owner can still take back must not have thrown the picture away already.
    /// </summary>
    public void Forget(DarkroomItem item)
    {
        var file = FileFor(item);
        if (file.Exists())
        {
            file.Delete();
        }
    }
}
