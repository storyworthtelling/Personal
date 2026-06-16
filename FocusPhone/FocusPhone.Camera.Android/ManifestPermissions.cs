using Android;

// CAMERA is a sensor permission and cannot be silently granted by the device
// owner; it is granted once via the runtime dialog at cutover.
[assembly: UsesPermission(Manifest.Permission.Camera)]
[assembly: UsesFeature("android.hardware.camera.any", Required = true)]
