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
