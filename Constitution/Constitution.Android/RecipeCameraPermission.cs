using Android.App;

// D105: the partner's phone photographs a recipe card page inside Constitution and reads it on the phone with ML Kit. That is the
// app's one camera use and CAMERA is the one permission it adds; it is a runtime permission, asked for at the moment the partner
// taps the camera row (MealsArea), after a sentence that says why. The photograph is a file in the app's cache, deleted straight
// after it is read, and nothing leaves the phone.
//
// The two features are declared not required, because holding CAMERA otherwise makes the store treat a camera as a requirement and
// refuse the install on a phone without one (or without autofocus). Typing a card in needs neither.
//
// Kept in its own file, like RecipeScanPermission.cs, so the manifest guards can read it by content. The permission is written as a
// platform constant (not a string) and the guards name exactly this line.
[assembly: UsesPermission(Name = Android.Manifest.Permission.Camera)]
[assembly: UsesFeature("android.hardware.camera", Required = false)]
[assembly: UsesFeature("android.hardware.camera.autofocus", Required = false)]
