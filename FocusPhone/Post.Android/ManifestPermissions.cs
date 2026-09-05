using Android;

// READ_CALL_LOG and READ_SMS are hard-restricted permissions: they must be
// whitelisted by the installer (`adb install -g` at cutover;
// SetWhitelistedRestrictedPermissions in every silent-update session) before
// any grant works. Whitelisting only un-restricts them, so the grant itself is
// force-applied by the device owner on every policy pass (PolicyPlanBuilder /
// PolicyManager.GrantPermissions) — dialog-free, surviving silent reinstalls
// and Android's unused-app auto-reset; the runtime dialog below is only a
// pre-cutover fallback.
[assembly: UsesPermission(Manifest.Permission.ReadCallLog)]
[assembly: UsesPermission(Manifest.Permission.ReadSms)]
[assembly: UsesPermission(Manifest.Permission.CallPhone)]
[assembly: UsesPermission(Manifest.Permission.ReadContacts)]
[assembly: UsesPermission(Manifest.Permission.PostNotifications)]
[assembly: UsesPermission(Manifest.Permission.Internet)]

// The unified inbox's destructive writes: WRITE_CALL_LOG deletes a call/voicemail
// from the log (hard-restricted, so whitelisted alongside READ_CALL_LOG at install
// and in every silent update); WRITE_CONTACTS saves/links an unknown sender.
[assembly: UsesPermission(Manifest.Permission.WriteCallLog)]
[assembly: UsesPermission(Manifest.Permission.WriteContacts)]

// The SMS role. RECEIVE_SMS/RECEIVE_MMS are what let the delivery receivers fire at all,
// and SEND_SMS is the send itself. Writing to the SMS provider needs no permission of its
// own — being the role holder IS the permission, and it is also the duty: Android stops
// persisting incoming texts the moment a third-party app takes over.
[assembly: UsesPermission(Manifest.Permission.ReceiveSms)]
[assembly: UsesPermission(Manifest.Permission.ReceiveMms)]
[assembly: UsesPermission(Manifest.Permission.SendSms)]

// The calendar. READ is what makes a proposal worth making — a date offered without
// checking the day is a date the owner has to go and check themselves, which is the work
// the detection was meant to save. WRITE is only ever used after the owner confirms one.
[assembly: UsesPermission(Manifest.Permission.ReadCalendar)]
[assembly: UsesPermission(Manifest.Permission.WriteCalendar)]

// Needed to resolve/deep-link the dialer, Google Messages, and email apps under
// Android 11+ package-visibility filtering. Sideloaded, so QUERY_ALL_PACKAGES is fine.
[assembly: UsesPermission(Manifest.Permission.QueryAllPackages)]
