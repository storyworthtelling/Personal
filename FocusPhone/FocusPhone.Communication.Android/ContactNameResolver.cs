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
        using var cursor = context.ContentResolver!.Query(uri, [column], null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
    }
}
