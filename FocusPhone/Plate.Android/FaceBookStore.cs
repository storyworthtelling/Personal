using Android.Content;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>
/// Where the face book lives: this head's own preferences, on this phone.
///
/// <para>Deliberately <b>not</b> in the shared vault. The vault holds what the household
/// owns and syncs it between devices; a biometric measurement of the owner's friends is not
/// something to move around for convenience. Losing it costs a few confirmations they can
/// make again.</para>
/// </summary>
public sealed class FaceBookStore(Context context)
{
    private const string PreferencesName = "faces";
    private const string BookKey = "book";

    private ISharedPreferences Prefs() =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public FaceBook Load() => FaceBookCodec.Read(Prefs().GetString(BookKey, null));

    public void Save(FaceBook book) =>
        Prefs().Edit()!.PutString(BookKey, FaceBookCodec.Write(book))!.Apply();
}
