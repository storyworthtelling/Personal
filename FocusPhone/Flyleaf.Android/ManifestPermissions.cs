using Android;

// As the launcher/DPC, this app must see every installed package: to launch
// the tile targets and app-tray entries (GetLaunchIntentForPackage /
// LauncherApps), and to categorize, quarantine, and suspend arbitrary installs
// (AppCategorizer / PackageWatcher / PolicyManager). Without this, Android 11+
// package-visibility filtering hides all other apps and every tile tap is a
// silent no-op. The suite is sideloaded / device-owner-installed (never on
// Play), so QUERY_ALL_PACKAGES is unrestricted here.
[assembly: UsesPermission(Manifest.Permission.QueryAllPackages)]

// RestrictedBrowserActivity hosts the device's only WebView; without INTERNET
// every page load fails at the network layer with net::ERR_CACHE_MISS.
[assembly: UsesPermission(Manifest.Permission.Internet)]

// The non-admin Settings panel (LauncherSettingsActivity) toggles the radios and
// force-closes a misbehaving app: reading/changing Wi-Fi state, enabling/disabling
// Bluetooth, and killing an app's background processes.
[assembly: UsesPermission(Manifest.Permission.AccessWifiState)]
[assembly: UsesPermission(Manifest.Permission.ChangeWifiState)]
[assembly: UsesPermission(Manifest.Permission.Bluetooth)]
[assembly: UsesPermission(Manifest.Permission.BluetoothAdmin)]
[assembly: UsesPermission(Manifest.Permission.BluetoothConnect)]
[assembly: UsesPermission(Manifest.Permission.KillBackgroundProcesses)]

// Long-press a book -> Uninstall routes to Android's own uninstall confirmation via
// ACTION_UNINSTALL_PACKAGE. That intent is silently dropped unless the caller holds
// REQUEST_DELETE_PACKAGES — which is why the uninstall did "nothing" on device (no
// ActivityNotFoundException was thrown, so the ACTION_DELETE fallback never ran either).
[assembly: UsesPermission(Manifest.Permission.RequestDeletePackages)]

// A page in RestrictedBrowserActivity asking for the camera (a video call, a QR or card photo
// in a browser). A WebView can only be granted what the app itself holds, so the app declares it;
// the owner is asked once at the phone's own prompt when the first page asks.
[assembly: UsesPermission(Manifest.Permission.Camera)]
