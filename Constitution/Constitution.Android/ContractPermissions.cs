using Android.App;
using Android.Content.PM;
using Constitution.Domain;
using Constitution.Domain.Recipes;

// Every cross-app contract Constitution takes part in, from either end (RepoGuards ContractPermissionsTests).
// Each is declared at signature level, so only an app signed with the suite's one keystore can hold it,
// and requested, because declaring a permission does not grant it to the declaring app: a broadcast sent
// under P reaches only a receiver whose app holds P, and a component guarded by P accepts only a caller
// holding it. Both apps of a contract declare it, so it exists whichever is installed first.

// The month's grocery budget: Index sends, BudgetReceiver receives.
[assembly: Permission(Name = ConstitutionIdentity.BudgetPermission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = ConstitutionIdentity.BudgetPermission)]

// A recipe read: Constitution starts Plate's RecipeReadActivity, which only a holder may start.
[assembly: Permission(Name = RecipeReadContract.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = RecipeReadContract.Permission)]
