using Android.Content;
using Android.Provider;

namespace Post;

/// <summary>
/// Lists device contacts for the new-conversation recipient picker: phone
/// entries for calls/texts, email entries for mail.
/// </summary>
public static class ContactPicker
{
    public record Entry(string Name, string Address);

    /// <summary>
    /// One person with every way to reach them, for the person-first New flow. Id is the
    /// device contact id (0 for a manually-typed, not-actually-in-the-book entry, which is
    /// also why such an entry reports HasPhoto true — there is no contact to attach one to).
    /// </summary>
    public record Person(
        string Name, IReadOnlyList<string> Phones, IReadOnlyList<string> Emails,
        long Id = 0, bool HasPhoto = true);

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

        var withPhoto = PhotoIds(context);

        return byId
            .Where(entry => entry.Value.Phones.Count > 0 || entry.Value.Emails.Count > 0)
            .Select(entry => new Person(
                entry.Value.Name, entry.Value.Phones, entry.Value.Emails,
                entry.Key, withPhoto.Contains(entry.Key)))
            .OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlySet<long> PhotoIds(Context context)
    {
        var ids = new HashSet<long>();
        using var cursor = context.ContentResolver!.Query(
            ContactsContract.Contacts.ContentUri!, ["_id", "photo_id"], null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            if (!cursor.IsNull(1) && cursor.GetLong(1) != 0)
            {
                ids.Add(cursor.GetLong(0));
            }
        }
        return ids;
    }

    /// <summary>
    /// Sets a contact's photo from a JPEG the owner just took. Android's Contacts provider has
    /// no write-side counterpart to <c>OpenContactPhotoInputStream</c> for reading — a photo is
    /// set by writing (or replacing) the contact's Photo-mimetype row in the Data table
    /// directly, the same way the platform's own Contacts app does.
    /// </summary>
    public static bool SetPhoto(Context context, long contactId, byte[] jpeg)
    {
        try
        {
            var resolver = context.ContentResolver!;

            // A contact can be backed by more than one raw contact — one per account it is
            // synced from. The first is a reasonable, simple choice with nothing else to go on.
            if (FirstRawContactId(resolver, contactId) is not { } rawContactId)
            {
                return false;
            }

            var mimeType = ContactsContract.CommonDataKinds.Photo.ContentItemType;
            long? existingDataId = null;
            using (var existing = resolver.Query(
                ContactsContract.Data.ContentUri!, ["_id"],
                "raw_contact_id = ? AND mimetype = ?", [rawContactId.ToString(), mimeType], null))
            {
                if (existing?.MoveToFirst() == true)
                {
                    existingDataId = existing.GetLong(0);
                }
            }

            var values = new ContentValues();
            values.Put("raw_contact_id", rawContactId);
            values.Put("mimetype", mimeType);
            values.Put("data15", jpeg); // Photo.PHOTO is column data15

            if (existingDataId is { } dataId)
            {
                resolver.Update(ContactsContract.Data.ContentUri!, values, "_id = ?", [dataId.ToString()]);
            }
            else
            {
                resolver.Insert(ContactsContract.Data.ContentUri!, values);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Saves a sender the owner does not know as a new contact with one address. The contact
    /// is written to the phone (no account), so it is in the Rolodex here and in Post at
    /// once; it does not sync to the owner's Google account.
    /// </summary>
    /// <exception cref="InvalidOperationException">The provider would not create the contact.</exception>
    /// <exception cref="Java.Lang.SecurityException">WRITE_CONTACTS has not been granted.</exception>
    public static void CreateContact(Context context, string name, string address)
    {
        var resolver = context.ContentResolver!;
        var rawUri = resolver.Insert(ContactsContract.RawContacts.ContentUri!, new ContentValues())
            ?? throw new InvalidOperationException($"The contacts provider would not create a contact for {name}.");
        var rawContactId = ContentUris.ParseId(rawUri);

        var nameValues = new ContentValues();
        nameValues.Put("raw_contact_id", rawContactId);
        nameValues.Put("mimetype", ContactsContract.CommonDataKinds.StructuredName.ContentItemType);
        nameValues.Put(ContactsContract.CommonDataKinds.StructuredName.DisplayName, name);
        resolver.Insert(ContactsContract.Data.ContentUri!, nameValues);

        InsertAddress(resolver, rawContactId, address);
    }

    /// <summary>
    /// Adds an address to an existing contact, on its first raw contact — the same choice
    /// <see cref="SetPhoto"/> makes for the same reason.
    /// </summary>
    /// <exception cref="InvalidOperationException">The contact has no raw contact to attach it to.</exception>
    /// <exception cref="Java.Lang.SecurityException">WRITE_CONTACTS has not been granted.</exception>
    public static void AddAddress(Context context, long contactId, string address)
    {
        var resolver = context.ContentResolver!;
        var rawContactId = FirstRawContactId(resolver, contactId)
            ?? throw new InvalidOperationException(
                $"Contact {contactId} has no raw contact to add {address} to.");
        InsertAddress(resolver, rawContactId, address);
    }

    private static long? FirstRawContactId(Android.Content.ContentResolver resolver, long contactId)
    {
        using var raw = resolver.Query(
            ContactsContract.RawContacts.ContentUri!, ["_id"],
            "contact_id = ?", [contactId.ToString()], null);
        return raw is not null && raw.MoveToFirst() ? raw.GetLong(0) : null;
    }

    // A phone number is a mobile number (the type the owner's own texts come from); an
    // address with an @ is an email. Nothing else reaches here: the caller holds a sender
    // Post has already read off a call, text or mail.
    private static void InsertAddress(Android.Content.ContentResolver resolver, long rawContactId, string address)
    {
        var values = new ContentValues();
        values.Put("raw_contact_id", rawContactId);
        if (address.Contains('@'))
        {
            values.Put("mimetype", ContactsContract.CommonDataKinds.Email.ContentItemType);
            values.Put(ContactsContract.CommonDataKinds.Email.Address, address);
        }
        else
        {
            values.Put("mimetype", ContactsContract.CommonDataKinds.Phone.ContentItemType);
            values.Put(ContactsContract.CommonDataKinds.Phone.Number, address);
            // "data2" is Phone.TYPE and 2 is Phone.TYPE_MOBILE; the bindings expose neither name.
            values.Put("data2", 2);
        }
        resolver.Insert(ContactsContract.Data.ContentUri!, values);
    }

    /// <summary>
    /// How many contacts there are, and how many have no photo. This is the whole
    /// justification for the Rolodex photo pass: because the Mailbox leads every row with a
    /// face, a missing one is a gap the owner sees constantly — and whether that pass is
    /// worth building is a question about THEIR address book, not about address books.
    ///
    /// Counted against the contacts table directly rather than over <see cref="People"/>,
    /// since a contact with a photo and no reachable address still occupies the book.
    /// </summary>
    public static (int Total, int WithoutPhoto) PhotoCoverage(Context context)
    {
        var total = 0;
        var withoutPhoto = 0;

        using var cursor = context.ContentResolver!.Query(
            ContactsContract.Contacts.ContentUri!,
            ["_id", "photo_id"], null, null, null);

        while (cursor is not null && cursor.MoveToNext())
        {
            total++;
            if (cursor.IsNull(1) || cursor.GetLong(1) == 0)
            {
                withoutPhoto++;
            }
        }

        return (total, withoutPhoto);
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
