using Android.App;

// Debug builds add INTERNET on their own; a signed Release build does not, and without it vault
// restore, reMarkable pairing and the voice calls cannot connect and say nothing about why.
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

// The brief's short list of notices posts as ordinary notifications (C-9.1.3). From Android 13 a
// notification may not be posted without this, and nothing says so when it is refused. It is a
// runtime permission, asked once from the shell; the lockdown on the owner's phone suppresses the
// alert, so there it is the Flyleaf bookmark that shows.
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
