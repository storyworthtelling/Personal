namespace Constitution.Domain.Waiting;

/// <summary>
/// What a Waiting row's one button does, named and not wired: the domain says which act the owner
/// asked for and the Android head decides how to carry it out. A closed list; the label lives in
/// <see cref="WaitingButton"/>.
/// </summary>
public enum WaitingActionId
{
    /// <summary>Pair the reMarkable again (Settings, Connections).</summary>
    PairAgain,

    /// <summary>Sign in again to the bed (Settings, Connections).</summary>
    SignInAgain,

    /// <summary>Allow Health Connect again.</summary>
    ReconnectHealth,

    /// <summary>Open the scanned card that is waiting to be checked.</summary>
    CheckCard,

    /// <summary>Scan a recipe card.</summary>
    ScanCard,

    /// <summary>Send the grocery list to Keep.</summary>
    SendToKeep,
}

/// <summary>A row's single button: its label and the act it stands for.</summary>
public sealed record WaitingButton(string Label, WaitingActionId Action);
