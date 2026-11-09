using Android.App;
using Android.Content.PM;
using Pulp.Domain.Hidden;
using Pulp.Domain.LockedOpen;
using Pulp.Domain.Sessions;
using Pulp.Domain.Usage;

// Every cross-app contract Pulp takes part in, from either end (RepoGuards ContractPermissionsTests).
// Each is declared at signature level, so only an app signed with the suite's one keystore can hold it,
// and requested, because declaring a permission does not grant it to the declaring app: a broadcast sent
// under P reaches only a receiver whose app holds P, and a component guarded by P accepts only a caller
// holding it. Both apps of a contract declare it, so it exists whichever is installed first.

// Hidden apps: Pulp sends, Flyleaf's HiddenAppsReceiver receives.
[assembly: Permission(Name = HiddenAppsHandOff.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = HiddenAppsHandOff.Permission)]

// The locked hand-off: Pulp sends (ordered), Flyleaf's LockedOpenReceiver answers.
[assembly: Permission(Name = LockedOpenHandOff.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = LockedOpenHandOff.Permission)]

// Playback sessions: Flyleaf sends, SessionReceiver receives.
[assembly: Permission(Name = SessionHandOff.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = SessionHandOff.Permission)]

// The usage count: Pulp sends, Index's UsageReceiver receives.
[assembly: Permission(Name = IndexUsageContract.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = IndexUsageContract.Permission)]
