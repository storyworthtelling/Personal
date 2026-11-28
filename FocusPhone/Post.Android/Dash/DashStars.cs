using Android.Content;
using Android.Provider;

namespace Post;

/// <summary>
/// Whether a sender is a favourite (P14 B8, A12: "favourites, starred in the Rolodex"): starred in the phone's own address
/// book, which is the Rolodex. Read by the sender's key (a number, as the timeline keys a person) through the contacts
/// provider's own number lookup. Off the main thread: it is a provider query. Without the contacts permission, or for a
/// key that is no number, nobody is starred.
/// </summary>
internal static class DashStars
{
    private const int ShortestNumber = 7;

    public static bool IsStarred(Context context, string key)
    {
        if (key.Length < ShortestNumber || !key.All(char.IsAsciiDigit))
        {
            return false;
        }

        var uri = Android.Net.Uri.WithAppendedPath(ContactsContract.PhoneLookup.ContentFilterUri!, Android.Net.Uri.Encode(key))!;
        try
        {
            using var cursor = context.ContentResolver!.Query(uri, [ContactsContract.PhoneLookup.InterfaceConsts.Starred], null, null, null);
            while (cursor is not null && cursor.MoveToNext())
            {
                if (cursor.GetInt(0) != 0)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Java.Lang.SecurityException)
        {
            // Without READ_CONTACTS nobody is starred, so overnight no text buzzes the wrist; the launch dialog asks for it.
            return false;
        }
    }
}
