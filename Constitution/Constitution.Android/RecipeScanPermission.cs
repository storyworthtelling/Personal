using Android.App;
using Constitution.Domain.Recipes;

// Plate declares READ_RECIPE at protectionLevel="signature" (Plate.Android/ManifestPermissions.cs); a caller must hold it too,
// and every head shares one keystore, so this costs nothing and keeps any other app from starting a recipe read. Constitution only
// uses the permission and never declares it. It is a custom permission, not a runtime one: nothing asks the owner for it.
// The name comes from the contract constant, which is Plate's package plus ".READ_RECIPE".
[assembly: UsesPermission(Name = RecipeReadContract.Permission)]
