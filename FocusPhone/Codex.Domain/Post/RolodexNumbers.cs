namespace Codex.Domain.Post;

/// <summary>
/// One of a contact's numbers as the phone's contacts app lists it: the number, whether the contacts app marks it primary
/// (its super-primary or primary flag), and its type (Android's <c>Phone.TYPE</c>).
/// </summary>
public readonly record struct ContactNumber(string Number, bool Primary, int Type);

/// <summary>
/// Which of a contact's numbers Post's Rolodex reaches first (P14 B6): the one the contacts app marks primary, else a
/// mobile, else the first as the contacts app lists them; a number listed twice (two accounts behind one contact) is
/// listed once, primary if either is. The Rolodex area calls and texts the first, and a new text from the wrist goes to
/// it, so the phone and the wrist can never reach two different numbers. Post's Rolodex keeps no labels of its own, so
/// the wrist is shown no choice between numbers.
/// </summary>
public static class RolodexNumbers
{
    /// <summary>Android's <c>Phone.TYPE_MOBILE</c>.</summary>
    public const int MobileType = 2;

    /// <summary>Android's <c>Phone.TYPE_WORK_MOBILE</c>.</summary>
    public const int WorkMobileType = 17;

    public static bool IsMobile(int type) => type is MobileType or WorkMobileType;

    public static IReadOnlyList<string> Order(IEnumerable<ContactNumber> numbers)
    {
        var listed = new List<(string Number, bool Primary, bool Mobile)>();
        foreach (var number in numbers)
        {
            var known = listed.FindIndex(entry => entry.Number == number.Number);
            if (known < 0)
            {
                listed.Add((number.Number, number.Primary, IsMobile(number.Type)));
            }
            else
            {
                var entry = listed[known];
                listed[known] = (entry.Number, entry.Primary || number.Primary, entry.Mobile || IsMobile(number.Type));
            }
        }

        // OrderBy is stable, so within each rank the contacts app's own order stands.
        return listed.OrderBy(entry => entry.Primary ? 0 : entry.Mobile ? 1 : 2).Select(entry => entry.Number).ToList();
    }
}
