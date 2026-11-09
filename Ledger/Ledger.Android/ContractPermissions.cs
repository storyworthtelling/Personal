using global::Android.App;
using global::Android.Content.PM;
using Index.Domain.Handoff;
using Index.Domain.Identity;

// Every cross-app contract Index takes part in, from either end (RepoGuards ContractPermissionsTests).
// Each is declared at signature level, so only an app signed with the suite's one keystore can hold it,
// and requested, because declaring a permission does not grant it to the declaring app: a broadcast sent
// under P reaches only a receiver whose app holds P, and a receiver guarded by P accepts only a sender
// holding it. Both apps of a contract declare it, so it exists whichever is installed first.

// Post's balance hand-off: Post sends, BalanceReceiver receives.
[assembly: Permission(Name = IndexIdentity.BalancePermission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = IndexIdentity.BalancePermission)]

// Pulp's usage count: Pulp sends, UsageReceiver receives.
[assembly: Permission(Name = IndexIdentity.UsagePermission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = IndexIdentity.UsagePermission)]

// The month's grocery budget: Index sends, Constitution's BudgetReceiver receives.
[assembly: Permission(Name = ConstitutionBudgetContract.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = ConstitutionBudgetContract.Permission)]

// Plate's statement read: a hand-entered account starts Plate's PaperReadActivity, which only a holder may start.
[assembly: Permission(Name = PlateStatementRead.Permission, ProtectionLevel = Protection.Signature)]
[assembly: UsesPermission(Name = PlateStatementRead.Permission)]
