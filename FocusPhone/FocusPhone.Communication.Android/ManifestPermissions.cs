using Android;

// READ_CALL_LOG is a hard-restricted permission: it must be whitelisted by the
// installer (`adb install -g` at cutover; SetWhitelistedRestrictedPermissions
// in every silent-update session) before any grant works.
[assembly: UsesPermission(Manifest.Permission.ReadCallLog)]
[assembly: UsesPermission(Manifest.Permission.CallPhone)]
[assembly: UsesPermission(Manifest.Permission.ReadContacts)]
[assembly: UsesPermission(Manifest.Permission.PostNotifications)]
[assembly: UsesPermission(Manifest.Permission.Internet)]
