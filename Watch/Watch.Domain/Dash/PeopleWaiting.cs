using Watch.Domain.Kit;

namespace Watch.Domain.Dash;

/// <summary>
/// One row of People waiting (WD §11; mock: People waiting): the name, the one line under it, when it came in the
/// watch's own words, whether it is unread (the name in the ink rather than the grey; no mark, by the owner's ruling of
/// 27 Nov 2026), and Post's key for the correspondence.
/// </summary>
public sealed record PersonRow(string Key, string Name, string Line, string When, bool Unread, bool Group);

/// <summary>
/// People waiting (W-5.1.1, A14): Post's waiting correspondences, newest first, each a name and one line and nothing
/// else; no counts. Post already sends them newest first and cut to the wrist's limits (B1); the wrist orders them
/// again, so a row never moves because of the order they crossed in.
/// </summary>
public static class PeopleWaiting
{
    public static IReadOnlyList<PersonRow> Rows(DashPeople people, DateTimeOffset now, TimeZoneInfo zone) =>
        people.People
            .OrderByDescending(person => person.At)
            .Select(person => new PersonRow(person.Key, person.Name, person.Line, WhenWords.Relative(person.At, now, zone), person.Unread, person.Group))
            .ToList();
}
