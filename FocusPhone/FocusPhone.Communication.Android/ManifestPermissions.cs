using Android;

// READ_CALL_LOG is a hard-restricted permission: it must be whitelisted by the
// installer (`adb install -g` at cutover; SetWhitelistedRestrictedPermissions
// in every silent-update session) before any grant works.
[assembly: UsesPermission(Manifest.Permission.ReadCallLog)]
[assembly: UsesPermission(Manifest.Permission.CallPhone)]
[assembly: UsesPermission(Manifest.Permission.ReadContacts)]
[assembly: UsesPermission(Manifest.Permission.PostNotifications)]
[assembly: UsesPermission(Manifest.Permission.Internet)]

// Needed to resolve/deep-link the dialer, Google Messages, and email apps under
// Android 11+ package-visibility filtering. Sideloaded, so QUERY_ALL_PACKAGES is fine.
[assembly: UsesPermission(Manifest.Permission.QueryAllPackages)]
