using Android.App;

// Debug builds add INTERNET on their own; a signed Release build does not, and without it vault
// restore, reMarkable pairing and the voice calls cannot connect and say nothing about why.
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
