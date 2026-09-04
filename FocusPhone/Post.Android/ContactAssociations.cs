using Android.Content;
using Android.Provider;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Storage;

namespace Post;

/// <summary>
/// Reads the device's contact cards and returns, for each contact that has more
/// than one way to reach them, the set of normalized keys (phone numbers +
/// email addresses) that belong to that one person. This is the automatic
/// half of identity resolution — the owner's manual links/unlinks layer on top.
/// </summary>
public static class ContactAssociations
{
    public static IReadOnlyList<IReadOnlyCollection<string>> Load(Context context)
    {
        var byContact = new Dictionary<long, HashSet<string>>();
        try
        {
            // "contact_id" (ContactsContract.Data.CONTACT_ID) is the stable column
            // name shared by phone and email data rows.
            ReadInto(context, byContact,
                ContactsContract.CommonDataKinds.Phone.ContentUri!,
                "contact_id",
                ContactsContract.CommonDataKinds.Phone.Number!,
                ContactKeyNormalizer.NormalizePhone);
            ReadInto(context, byContact,
                ContactsContract.CommonDataKinds.Email.ContentUri!,
                "contact_id",
                ContactsContract.CommonDataKinds.Email.Address!,
                ContactKeyNormalizer.NormalizeEmail);
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CONTACTS not granted yet; fall back to no auto-associations.
            return [];
        }

        return byContact.Values
            .Where(keys => keys.Count > 1) // A single-key contact links nothing.
            .Cast<IReadOnlyCollection<string>>()
            .ToList();
    }

    /// <summary>Builds the full identity resolver: contact associations + manual links/unlinks.</summary>
    public static IIdentityResolver BuildResolver(Context context, IdentityLinkStore links) =>
        new IdentityMap(Load(context), links.LoadLinks(), links.LoadStandalone());

    private static void ReadInto(
        Context context, Dictionary<long, HashSet<string>> byContact,
        Android.Net.Uri uri, string contactIdColumn, string valueColumn, Func<string, string> normalize)
    {
        using var cursor = context.ContentResolver!.Query(
            uri, [contactIdColumn, valueColumn], null, null, null);
        if (cursor is null)
        {
            return;
        }
        while (cursor.MoveToNext())
        {
            var contactId = cursor.GetLong(0);
            var value = cursor.GetString(1);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (!byContact.TryGetValue(contactId, out var set))
            {
                set = [];
                byContact[contactId] = set;
            }
            set.Add(normalize(value));
        }
    }
}
