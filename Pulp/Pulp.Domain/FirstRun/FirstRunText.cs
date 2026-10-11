namespace Pulp.Domain.FirstRun;

/// <summary>First run's words (D142: no tour — each step says what it sets up and nothing else). Never voiced.</summary>
public static class FirstRunText
{
    public const string Skip = "Skip for now";

    public const string VaultTitle = "The vault";

    public const string VaultLead =
        "Sign in and enter the vault passphrase your other apps use. Pulp's keys come back by themselves.";

    public const string OpenVault = "Open the vault";

    public const string AddKeysInVault = "Add the keys in Vault";

    public const string HouseholdTitle = "The household";

    public const string HouseholdLead =
        "Add everyone who watches. A child sees only what a parent allows.";

    public const string NameHint = "Name";

    public const string Adult = "Adult";

    public const string Child = "Child";

    public const string Add = "Add";

    public const string TakeOff = "Take off";

    public const string ChildMark = "child";

    public const string SaveHousehold = "Save the household";

    public const string Saving = "Saving the household…";

    public const string Looking = "Looking for the household…";

    public const string PhoneTitle = "This phone";

    public const string PhoneLead =
        "Whose phone is this? Their Continue watching, playlists and Friday stack are the ones shown here.";

    public static string Step(int number, int of) => $"Step {number} of {of}";

    public static string ThisPhoneIs(string name) => $"This phone is {name}'s";

    /// <summary>Said when the household arrived from Drive while the owner typed, and some of those typed were already in it.</summary>
    public static string AlreadyThere(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Count switch
        {
            0 => throw new ArgumentException("Nobody to name.", nameof(names)),
            1 => $"{names[0]} is already in the household.",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} are already in the household.",
        };
    }
}
