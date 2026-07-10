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

    // The two hard-restricted permissions the Communication app needs to read
    // the system text and call-log stores. These are the canonical Android
    // permission strings (matching Android.Manifest.Permission.ReadSms /
    // ReadCallLog), kept here so the platform-agnostic Domain can name them in
    // the policy plan without an Android reference.
    public const string ReadSmsPermission = "android.permission.READ_SMS";
    public const string ReadCallLogPermission = "android.permission.READ_CALL_LOG";

    public static readonly IReadOnlySet<string> FocusPhonePackages = new HashSet<string>
    {
        LauncherPackage,
        CommunicationPackage,
        CameraPackage,
        AudioPackage,
    };
}
