using Android;

// READ_CALL_LOG and READ_SMS are hard-restricted permissions: they must be
// whitelisted by the installer (`adb install -g` at cutover;
// SetWhitelistedRestrictedPermissions in every silent-update session) before
// any grant works.
[assembly: UsesPermission(Manifest.Permission.ReadCallLog)]
[assembly: UsesPermission(Manifest.Permission.ReadSms)]
[assembly: UsesPermission(Manifest.Permission.CallPhone)]
[assembly: UsesPermission(Manifest.Permission.ReadContacts)]
[assembly: UsesPermission(Manifest.Permission.PostNotifications)]
[assembly: UsesPermission(Manifest.Permission.Internet)]

// Needed to resolve/deep-link the dialer, Google Messages, and email apps under
// Android 11+ package-visibility filtering. Sideloaded, so QUERY_ALL_PACKAGES is fine.
[assembly: UsesPermission(Manifest.Permission.QueryAllPackages)]
