namespace Codex.Domain.Timeline;

/// <summary>
/// Whether one address can be taken out of a correspondence the address book (or a link) joined it into
/// (orphan audit §25). Only a person's correspondence that holds more than one address, and only an address
/// it holds; a group's members are not one person's addresses.
/// </summary>
public static class IdentityRepair
{
    public static bool CanSeparate(IReadOnlyCollection<string> memberKeys, string key, bool isGroup) =>
        !isGroup
        && memberKeys.Distinct(StringComparer.Ordinal).Count() > 1
        && memberKeys.Contains(key, StringComparer.Ordinal);
}
