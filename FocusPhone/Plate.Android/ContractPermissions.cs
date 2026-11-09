using Android.App;
using Android.Content.PM;
using Codex.Domain.Plate;

// Every cross-app contract Plate takes part in, from either end (RepoGuards ContractPermissionsTests).
// Each is declared at signature level, so only an app signed with the suite's one keystore can hold it,
// and requested, because declaring a permission does not grant it to the declaring app: a broadcast sent
// under P reaches only a receiver whose app holds P, and a component guarded by P accepts only a caller
// holding it. Both apps of a contract declare it, so it exists whichever is installed first.

// A recipe read: Constitution starts RecipeReadActivity, which only a holder may start.
[assembly: Permission(Name = ReadRequest.RecipePermission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = ReadRequest.RecipePermission)]

// The card and statement reads: Post and Index start PaperReadActivity, which only a holder may start.
[assembly: Permission(Name = ReadRequest.PaperPermission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = ReadRequest.PaperPermission)]
