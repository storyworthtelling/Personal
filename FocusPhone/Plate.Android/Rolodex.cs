using Android.Content;
using Android.Provider;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>One person out of the phone's address book.</summary>
public sealed record Contact(string Id, string Name);

/// <summary>
/// The Rolodex, read rather than rebuilt.
///
/// <para>Post's Rolodex <b>is</b> the phone's contacts — that is why Post counts the ones
/// with no photo and offers Google's own sync first. So Plate reads the same place directly
/// rather than inventing a second list of people or a new cross-app contract for something
/// the platform already holds.</para>
///
/// <para>Two jobs. It supplies the names the owner picks from when they say who a face is,
/// and it supplies the <b>starting</b> faces: a contact who already has a photo is a
/// labelled face that exists before Plate has been taught anything at all.</para>
/// </summary>
public sealed class Rolodex(Context context)
{
    public bool CanRead =>
        context.CheckSelfPermission(Android.Manifest.Permission.ReadContacts)
        == Android.Content.PM.Permission.Granted;

    /// <summary>
    /// Everyone with a name, in the order the address book gives them. Empty when the
    /// permission is not granted, which is a Rolodex the owner has not opened rather than a
    /// fault worth reporting.
    /// </summary>
    public IReadOnlyList<Contact> People()
    {
        if (!CanRead)
        {
            return [];
        }

        var people = new List<Contact>();
        try
        {
            using var cursor = context.ContentResolver!.Query(
                ContactsContract.Contacts.ContentUri!,
                [ContactsContract.Contacts.InterfaceConsts.Id,
                 ContactsContract.Contacts.InterfaceConsts.DisplayName],
                null, null,
                ContactsContract.Contacts.InterfaceConsts.DisplayName + " ASC");

            while (cursor?.MoveToNext() == true)
            {
                var id = cursor.GetString(0);
                var name = cursor.GetString(1);
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrWhiteSpace(name))
                {
                    people.Add(new Contact(id, name));
                }
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("Plate", $"Could not read contacts: {e.Message}");
        }
        return people;
    }

    /// <summary>
    /// A contact's own photo, full size where there is one. This is the seed: a face the
    /// owner already labelled, in an app they already keep up to date, without being asked
    /// to label anything twice.
    /// </summary>
    public Android.Graphics.Bitmap? PhotoFor(string contactId)
    {
        if (!CanRead || !long.TryParse(contactId, out var id))
        {
            return null;
        }

        try
        {
            var uri = ContentUris.WithAppendedId(ContactsContract.Contacts.ContentUri!, id);
            using var stream = ContactsContract.Contacts.OpenContactPhotoInputStream(
                context.ContentResolver!, uri, preferHighres: true);
            return stream is null ? null : Android.Graphics.BitmapFactory.DecodeStream(stream);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("Plate", $"Could not read a contact photo: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Teaches the book every contact who has a photo and is not in it yet. Runs once and
    /// then only for people who are new, because it decodes a bitmap per contact.
    /// </summary>
    public FaceBook Seed(FaceBook book, FaceRecogniser recogniser)
    {
        if (!CanRead || !recogniser.CanRecognise)
        {
            return book;
        }

        foreach (var contact in People())
        {
            if (book.People.Any(person => person.ContactId == contact.Id))
            {
                continue;
            }

            using var photo = PhotoFor(contact.Id);
            if (photo is null)
            {
                continue;
            }

            var faces = recogniser.FindAsync(photo).GetAwaiter().GetResult();
            var face = faces.FirstOrDefault(found => found.CanBeMatched);
            if (face is not null)
            {
                book = book.Learn(contact.Id, contact.Name, face.Descriptor);
            }

            foreach (var found in faces)
            {
                found.Crop.Recycle();
            }
        }
        return book;
    }
}
