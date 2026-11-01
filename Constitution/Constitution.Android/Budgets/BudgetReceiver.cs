using Codex.Ui;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Constitution.Domain;
using Constitution.Domain.Budgets;
using Constitution.Infrastructure.Budgets;

// The signature permission that protects BudgetReceiver, declared here and held by Index. A permission nobody declares is
// granted to nobody, so without this line a correctly signed sender would be refused; and it is signature level, so only an
// app signed with the suite's one keystore can send a budget at all. Declared, not requested: no uses-permission is added.
[assembly: Permission(Name = ConstitutionIdentity.BudgetPermission, ProtectionLevel = Protection.Signature)]

namespace Constitution.Budgets;

/// <summary>
/// Index's month of grocery budget and spend (PBI C-11.1.1), the Pulp-to-Index <c>UsageReceiver</c> pattern again: an explicit
/// broadcast naming this class, exported only because <see cref="ConstitutionIdentity.BudgetPermission"/> guards it. The names
/// are frozen in <see cref="ConstitutionIdentity"/> and Index's sender is held equal to them by content
/// (<c>Contracts/BudgetContractTests</c>).
///
/// <para>Thin on purpose: <see cref="BudgetIntake"/> parses and stores. A payload that does not parse is dropped and the trail
/// says which field, never what it held. Nothing here logs an amount.</para>
/// </summary>
[BroadcastReceiver(
    Name = ConstitutionIdentity.BudgetReceiverName,
    Exported = true,
    Enabled = true,
    Permission = ConstitutionIdentity.BudgetPermission)]
[IntentFilter([ConstitutionIdentity.BudgetAction])]
public class BudgetReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != ConstitutionIdentity.BudgetAction)
        {
            return;
        }

        var trail = new BudgetTrail(context);
        try
        {
            var folder = context.NoBackupFilesDir?.AbsolutePath
                ?? throw new InvalidOperationException("The app has no private files folder to keep the grocery budgets in.");
            var store = new FileMonthlyBudgets(Path.Combine(folder, FileMonthlyBudgets.FileName));
            new BudgetIntake(store, trail.Outcome).Receive(key => intent.GetStringExtra(key), DateTimeOffset.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A receiver must not throw: it shares a process with the work that matters more. logcat gets the type name only (D163); the
            // trail keeps the type, so the owner's diagnostics can say a budget was lost without saying what it held.
            trail.Outcome($"budget broadcast not kept: {e.GetType().Name}");
            Android.Util.Log.Error("Budget", e.GetType().Name);
            HeadDiagnostics.Current?.Warn("Budget", "The budget broadcast could not be read: " + e.GetType().Name);
        }
    }
}
