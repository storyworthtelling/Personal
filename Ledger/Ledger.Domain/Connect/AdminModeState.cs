namespace Index.Domain.Connect;

/// <summary>
/// Where the locked phone stands on the one thing Index's bank sign-in needs from it: a Chrome that is
/// allowed to run. Plaid's sign-in has to happen in real Chrome (banks block embedded web views), and on
/// the FocusPhone Chrome is suspended by the device-owner policy except while Admin mode is open.
/// </summary>
public enum AdminModeState
{
    /// <summary>Chrome is installed but suspended: Admin mode has to be opened first (mockup 5, frame 1).</summary>
    Needed,

    /// <summary>Chrome is installed and runnable: Admin mode is open (mockup 5, frame 2).</summary>
    Open,

    /// <summary>
    /// Chrome is not on the phone at all (or Android is hiding it from this app). Not "Admin mode
    /// needed": opening Admin mode would not make a browser appear, so it has its own sentence.
    /// </summary>
    ChromeMissing,
}
