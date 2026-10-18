using Android.App;

// Debug builds add INTERNET on their own; a signed Release build does not, and without it the
// availability, sports, calendar and vault calls cannot connect and say nothing about why.
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
