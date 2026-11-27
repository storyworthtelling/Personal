using Android.Content;
using Codex.Infrastructure.Dash;
using Codex.Ui;

namespace Post;

/// <summary>
/// The phone's Rolodex for the wrist (P14 B6, W-5.1.2; PC6), read as the Rolodex area reads it
/// (<see cref="ContactPicker.People"/>): each person's device contact id, display name and numbers, in the Rolodex's
/// order. What crosses, and what is left out, is <see cref="RolodexRelay"/>'s alone: names and digest ids, never a number
/// or an address. Without the contacts permission there is no book to show, so the wrist is shown none. Off the main
/// thread: the read is a provider query.
/// </summary>
internal static class DashContacts
{
    private const string Tag = "Dash";  // not copy: a log tag

    public static RolodexRelay Rolodex(Context context)
    {
        try
        {
            return new RolodexRelay(ContactPicker.People(context).Select(person => new RolodexPerson(person.Id, person.Name, person.Phones)).ToList());
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CONTACTS is not granted: the phone's own Rolodex shows nobody either.
            HeadLog.Warn(Tag, "DASH the phone’s contacts couldn’t be read; the wrist is shown none");
            return new RolodexRelay([]);
        }
    }
}
