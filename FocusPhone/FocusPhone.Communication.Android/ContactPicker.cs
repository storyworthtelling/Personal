using Android.Content;
using Android.Provider;

namespace FocusPhone.Communication;

/// <summary>
/// Lists device contacts for the new-conversation recipient picker: phone
/// entries for calls/texts, email entries for mail.
/// </summary>
public static class ContactPicker
{
    public record Entry(string Name, string Address);

    public static IReadOnlyList<Entry> PhoneContacts(Context context) =>
        Query(context,
            ContactsContract.CommonDataKinds.Phone.ContentUri!,
            ContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName,
            ContactsContract.CommonDataKinds.Phone.Number);

    public static IReadOnlyList<Entry> EmailContacts(Context context) =>
        Query(context,
            ContactsContract.CommonDataKinds.Email.ContentUri!,
            ContactsContract.CommonDataKinds.Email.InterfaceConsts.DisplayNamePrimary,
            ContactsContract.CommonDataKinds.Email.Address);

    private static IReadOnlyList<Entry> Query(
        Context context, Android.Net.Uri uri, string nameColumn, string addressColumn)
    {
        var entries = new List<Entry>();
        using var cursor = context.ContentResolver!.Query(
            uri, [nameColumn, addressColumn], null, null, $"{nameColumn} ASC");
        while (cursor is not null && cursor.MoveToNext())
        {
            var name = cursor.IsNull(0) ? null : cursor.GetString(0);
            var address = cursor.IsNull(1) ? null : cursor.GetString(1);
            if (!string.IsNullOrWhiteSpace(address))
            {
                entries.Add(new Entry(string.IsNullOrWhiteSpace(name) ? address : name, address));
            }
        }
        return entries.DistinctBy(entry => (entry.Name, entry.Address)).ToList();
    }
}
