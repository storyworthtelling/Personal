namespace FocusPhone.Domain.Policy;

/// <summary>
/// Everything the policy plan depends on, gathered by the Android layer:
/// which packages we ship, which look like browsers or launchers, and the
/// persistent blocklist.
/// </summary>
public record PolicyContext(
    IReadOnlySet<string> OwnPackages,
    IReadOnlySet<string> BrowserPackages,
    IReadOnlySet<string> OtherHomePackages,
    IReadOnlySet<string> BlockedPackages,
    bool DebuggingLockedDown)
{
    public const string LauncherPackage = "com.storyworthtelling.focusphone.launcher";
    public const string CommunicationPackage = "com.storyworthtelling.focusphone.communication";
    public const string CameraPackage = "com.storyworthtelling.focusphone.camera";
    public const string AudioPackage = "com.storyworthtelling.focusphone.audio";
    public const string PlayStorePackage = "com.android.vending";

    public static readonly IReadOnlySet<string> FocusPhonePackages = new HashSet<string>
    {
        LauncherPackage,
        CommunicationPackage,
        CameraPackage,
        AudioPackage,
    };
}
