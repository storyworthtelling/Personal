using Android;

// As the launcher/DPC, this app must see every installed package: to launch
// the tile targets and app-tray entries (GetLaunchIntentForPackage /
// LauncherApps), and to categorize, quarantine, and suspend arbitrary installs
// (AppCategorizer / PackageWatcher / PolicyManager). Without this, Android 11+
// package-visibility filtering hides all other apps and every tile tap is a
// silent no-op. The suite is sideloaded / device-owner-installed (never on
// Play), so QUERY_ALL_PACKAGES is unrestricted here.
[assembly: UsesPermission(Manifest.Permission.QueryAllPackages)]
