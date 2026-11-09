using Android.App;
using Android.Content.PM;
using Codex.Domain.Plate;
using Codex.Domain.Post;

// Every cross-app contract Post takes part in, from either end (RepoGuards ContractPermissionsTests).
// Each is declared at signature level, so only an app signed with the suite's one keystore can hold it,
// and requested, because declaring a permission does not grant it to the declaring app: a broadcast sent
// under P reaches only a receiver whose app holds P, and a component guarded by P accepts only a caller
// holding it. Both apps of a contract declare it, so it exists whichever is installed first.

// Post's balance hand-off: Post sends, Index's BalanceReceiver receives.
[assembly: Permission(Name = IndexHandoff.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = IndexHandoff.Permission)]

// The card read: the Rolodex starts Plate's PaperReadActivity, which only a holder may start.
[assembly: Permission(Name = ReadRequest.PaperPermission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = ReadRequest.PaperPermission)]
