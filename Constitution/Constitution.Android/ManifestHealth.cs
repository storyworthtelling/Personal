using Android.App;

// Health Connect, read only (C-7.1.1; TF §1.3, R5). The seven signals the brief charts, plus the one
// background permission. There is deliberately NO WRITE_* permission here and no
// READ_HEALTH_DATA_HISTORY (the brief wants nothing from before install day).
//
// Written as string literals, not Android.Manifest.Permission constants, because HealthManifestTests
// reads this file by content and a constant reference cannot be read. The Domain record of the same set
// is Constitution.Domain.Health.HealthPermissions; the test fails if the two disagree. Every name was
// read out of Mono.Android's own metadata (Microsoft.Android.Ref.36), not typed from memory.
//
// Body composition is body fat alone (lean body mass was declared and never read, review S11); "active minutes" is read from exercise sessions
// (their duration), the closest source the platform has. See HealthPermissions for what was assumed,
// since the device spike (C-11.4.2) was skipped (D97).
[assembly: UsesPermission("android.permission.health.READ_WEIGHT")]
[assembly: UsesPermission("android.permission.health.READ_BODY_FAT")]
[assembly: UsesPermission("android.permission.health.READ_RESTING_HEART_RATE")]
[assembly: UsesPermission("android.permission.health.READ_HEART_RATE_VARIABILITY")]
[assembly: UsesPermission("android.permission.health.READ_STEPS")]
[assembly: UsesPermission("android.permission.health.READ_EXERCISE")]
[assembly: UsesPermission("android.permission.health.READ_SLEEP")]
[assembly: UsesPermission("android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND")]
