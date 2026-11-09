namespace Storyworthtelling.Voice.Secrets;

/// <summary>
/// How an app's value meets the vault's on a publish, for every app alike (duplication audit §2). Folio and
/// Foreword used to write their values unconditionally, so a phone that had never paired the tablet wrote a
/// blank reMarkable token over the one a sibling app had published.
/// </summary>
public static class VaultMerge
{
    /// <summary>
    /// What the vault should hold for one slot. A value this install does not hold leaves the vault's alone; a
    /// slot the vault leaves empty takes what this install holds; and a slot the vault already fills is replaced
    /// only by a value the owner has just changed here.
    /// </summary>
    public static string Pick(string local, string inVault, bool changed) =>
        string.IsNullOrEmpty(local) ? inVault
        : changed || string.IsNullOrEmpty(inVault) ? local
        : inVault;

    /// <summary>The same rule for a number, where 0 is "not set" (an unset coordinate).</summary>
    public static double Pick(double local, double inVault, bool changed) =>
        local == 0 ? inVault
        : changed || inVault == 0 ? local
        : inVault;

    /// <summary>
    /// The vault's map of well-known file names to Drive ids, with what this session captured folded in: a
    /// captured id wins for its name, and an empty one (a file this install forgot after trashing it) erases nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MergeDriveFileIds(
        IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> captured)
    {
        var merged = new Dictionary<string, string>(existing);
        foreach (var (name, id) in captured)
        {
            if (!string.IsNullOrEmpty(id))
            {
                merged[name] = id;
            }
        }

        return merged;
    }
}
