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

    /// <summary>One person with every way to reach them, for the person-first New flow.</summary>
    public record Person(string Name, IReadOnlyList<string> Phones, IReadOnlyList<string> Emails);

    /// <summary>
    /// Device contacts grouped into people (phones + emails under one contact id),
    /// so New can pick a person and then offer the channels that person has.
    /// </summary>
    public static IReadOnlyList<Person> People(Context context)
    {
        var byId = new Dictionary<long, (string Name, List<string> Phones, List<string> Emails)>();
        Gather(context, ContactsContract.CommonDataKinds.Phone.ContentUri!,
            ContactsContract.CommonDataKinds.Phone.Number, isEmail: false, byId);
        Gather(context, ContactsContract.CommonDataKinds.Email.ContentUri!,
            ContactsContract.CommonDataKinds.Email.Address, isEmail: true, byId);

        return byId.Values
            .Where(person => person.Phones.Count > 0 || person.Emails.Count > 0)
            .Select(person => new Person(person.Name, person.Phones, person.Emails))
            .OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Gather(
        Context context, Android.Net.Uri uri, string? valueColumn, bool isEmail,
        Dictionary<long, (string Name, List<string> Phones, List<string> Emails)> byId)
    {
        using var cursor = context.ContentResolver!.Query(
            uri, ["contact_id", "display_name", valueColumn], null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            var id = cursor.GetLong(0);
            var name = cursor.IsNull(1) ? null : cursor.GetString(1);
            var value = cursor.IsNull(2) ? null : cursor.GetString(2);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (!byId.TryGetValue(id, out var person))
            {
                person = (string.IsNullOrWhiteSpace(name) ? value : name!, [], []);
                byId[id] = person;
            }
            var list = isEmail ? person.Emails : person.Phones;
            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }
    }

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
