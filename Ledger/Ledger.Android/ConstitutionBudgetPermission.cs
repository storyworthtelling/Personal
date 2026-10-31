using global::Android.App;
using Index.Domain.Handoff;

// Constitution declares RECEIVE_BUDGET at protectionLevel="signature"; a sender must hold it too, and every
// head shares one keystore, so this costs nothing and keeps any other app from being sent to by mistake.
// Index only uses the permission; it never declares it.
[assembly: UsesPermission(Name = ConstitutionBudgetContract.Permission)]
