using Android.Content;

namespace FocusPhone.Launcher;

/// <summary>
/// The six home-screen tiles: which icon each shows, what it launches, and
/// which packages' notifications light it up.
/// </summary>
public record Tile(string Name, string IconResourceName, string[] NotificationPackages)
{
    public const string CommunicationPackage = "com.storyworthtelling.focusphone.communication";
    public const string CameraPackage = "com.storyworthtelling.focusphone.camera";
    public const string AudioPackage = "com.storyworthtelling.focusphone.audio";
    public const string DrivePackage = "com.google.android.apps.docs";
    public const string GeminiPackage = "com.google.android.apps.bard";
    public const string MapsPackage = "com.google.android.apps.maps";

    public static readonly Tile Communication = new("Communication", "tile_communication",
        [CommunicationPackage, "com.google.android.apps.messaging", "com.google.android.dialer", "com.google.android.gm"]);
    public static readonly Tile Files = new("Files", "tile_files", [DrivePackage]);
    public static readonly Tile Camera = new("Camera", "tile_camera", [CameraPackage]);
    public static readonly Tile Ai = new("AI", "tile_ai", [GeminiPackage]);
    public static readonly Tile Audio = new("Audio", "tile_audio", [AudioPackage]);
    public static readonly Tile Maps = new("Maps", "tile_maps", [MapsPackage]);

    public static readonly IReadOnlyList<Tile> All =
        [Communication, Files, Camera, Ai, Audio, Maps];

    /// <summary>
    /// The package this tile launches; our own apps may not be installed yet
    /// during development, in which case the tile is inert.
    /// </summary>
    public string LaunchPackage => NotificationPackages[0];

    public Intent? BuildLaunchIntent(Context context) =>
        context.PackageManager!.GetLaunchIntentForPackage(LaunchPackage);
}
