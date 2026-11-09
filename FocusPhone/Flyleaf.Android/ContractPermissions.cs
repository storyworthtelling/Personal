using Android.App;
using Android.Content.PM;
using Codex.Domain.Apps;

// Every cross-app contract Flyleaf takes part in, from either end (RepoGuards ContractPermissionsTests).
// Each is declared at signature level, so only an app signed with the suite's one keystore can hold it,
// and requested, because declaring a permission does not grant it to the declaring app: a broadcast sent
// under P reaches only a receiver whose app holds P, and a component guarded by P accepts only a caller
// holding it. Both apps of a contract declare it, so it exists whichever is installed first.

// Pulp's hidden apps: Pulp sends, HiddenAppsReceiver receives.
[assembly: Permission(Name = HiddenAppsHandoff.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = HiddenAppsHandoff.Permission)]

// Pulp's locked hand-off: Pulp sends (ordered), LockedOpenReceiver answers.
[assembly: Permission(Name = LockedOpenHandoff.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = LockedOpenHandoff.Permission)]

// Playback sessions: PlaybackSessionWatcher sends, Pulp's SessionReceiver receives.
[assembly: Permission(Name = PlaybackSessionHandoff.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = PlaybackSessionHandoff.Permission)]
