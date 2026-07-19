using Android.Content;
using Android.Provider;

namespace FocusPhone.Communication;

/// <summary>
/// Resolves timeline contact keys (normalized phone numbers, email addresses,
/// or bare display names from notifications) to contact display names.
/// </summary>
public static class ContactNameResolver
{
    public static string DisplayName(Context context, string contactKey)
    {
        if (contactKey.Contains('@'))
        {
            return LookupEmail(context, contactKey) ?? contactKey;
        }
        if (contactKey.All(char.IsAsciiDigit) && contactKey.Length >= 7)
        {
            return LookupPhone(context, contactKey) ?? contactKey;
        }
        return contactKey;
    }

    /// <summary>
    /// Reverse lookup for notification senders: notifications carry display
    /// names, and keying by the contact's number instead merges those items
    /// into the same thread as calls and provider texts. Case-insensitive
    /// because callers may pass an already-normalized (lowercased) key.
    /// </summary>
    public static string? PhoneForDisplayName(Context context, string displayName)
    {
        try
        {
            using var cursor = context.ContentResolver!.Query(
                ContactsContract.CommonDataKinds.Phone.ContentUri!,
                [ContactsContract.CommonDataKinds.Phone.Number],
                $"{ContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName} = ? COLLATE NOCASE",
                [displayName], null);
            var number = cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
            return string.IsNullOrWhiteSpace(number)
                ? null
                : Domain.Timeline.ContactKeyNormalizer.NormalizePhone(number);
        }
        catch (Java.Lang.SecurityException)
        {
            return null; // READ_CONTACTS not granted yet; keep the name key.
        }
    }

    private static string? LookupPhone(Context context, string number)
    {
        var uri = Android.Net.Uri.WithAppendedPath(
            ContactsContract.PhoneLookup.ContentFilterUri!, Android.Net.Uri.Encode(number));
        return QuerySingle(context, uri, ContactsContract.PhoneLookup.InterfaceConsts.DisplayName);
    }

    private static string? LookupEmail(Context context, string email)
    {
        var uri = Android.Net.Uri.WithAppendedPath(
            ContactsContract.CommonDataKinds.Email.ContentLookupUri!, Android.Net.Uri.Encode(email));
        return QuerySingle(context, uri, ContactsContract.Contacts.InterfaceConsts.DisplayName);
    }

    private static string? QuerySingle(Context context, Android.Net.Uri uri, string column)
    {
        try
        {
            using var cursor = context.ContentResolver!.Query(uri, [column], null, null, null);
            return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CONTACTS not granted yet; fall back to the raw key. Otherwise
            // this uncaught throw crashes the inbox, opening a thread, and the
            // new-email compose flow — all of which name a contact from its key.
            return null;
        }
    }
}
