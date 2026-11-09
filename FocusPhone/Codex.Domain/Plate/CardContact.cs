namespace Codex.Domain.Plate;

/// <summary>
/// What Post saves from a business card Plate read: a name and the addresses on the card, phone first. Plate
/// leaves out a field the card did not show, so any of the three can be missing.
/// </summary>
public sealed record CardContact(string Name, IReadOnlyList<string> Addresses)
{
    /// <summary>
    /// Null when the card gave no phone and no email: a name alone is a Rolodex row Post can do nothing with.
    /// With no name the contact is called by its first address, which is what the Rolodex would show anyway.
    /// </summary>
    public static CardContact? From(string? name, string? phone, string? email)
    {
        var addresses = new[] { phone, email }
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address!.Trim())
            .ToList();
        if (addresses.Count == 0)
        {
            return null;
        }

        var called = string.IsNullOrWhiteSpace(name) ? addresses[0] : name.Trim();
        return new CardContact(called, addresses);
    }
}
