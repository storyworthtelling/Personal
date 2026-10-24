using global::Android.Content;
using global::Android.Content.PM;
using Index.Domain.Handoff;
using Index.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Index.Android;

/// <summary>
/// The thin Android half of the budget contract: after a refresh recomputes the month, send Groceries'
/// month, budget and spent to Constitution as an explicit, permission-guarded broadcast, but only when the
/// figures changed since the last one delivered (<see cref="BudgetSendPlan"/>). Constitution not installed is
/// the ordinary case: nothing is queued or reported, and the last-sent record does not move.
/// </summary>
public static class ConstitutionBudgetSender
{
    private const string LastSentKey = "ConstitutionBudgetLastSent";

    public static async Task SendIfChangedAsync(BudgetService budget, string yearMonth, CancellationToken cancellationToken)
    {
        var logger = IPlatformApplication.Current?.Services.GetService<ILogger<BudgetService>>();
        try
        {
            var current = await budget.GetGroceriesPayloadAsync(yearMonth, cancellationToken);
            var previous = ConstitutionBudgetPayload.TryParseStored(Preferences.Get(LastSentKey, string.Empty));
            if (current is null || BudgetSendPlan.Decide(previous, current) == BudgetSendDecision.Nothing)
            {
                return;
            }

            var outcome = Send(Platform.AppContext, current, logger);
            if (BudgetSendPlan.AdvancesRecord(outcome))
            {
                Preferences.Set(LastSentKey, current.ToStoredText());
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The budget screen must never fail because a courtesy broadcast did.
            logger?.LogWarning(ex, "Reading Groceries figures for Constitution failed; the screen is unaffected.");
        }
    }

    private static BudgetSendOutcome Send(Context context, ConstitutionBudgetPayload payload, ILogger? logger)
    {
        if (!IsInstalled(context))
        {
            return BudgetSendPlan.Outcome(receiverInstalled: false, sendThrew: false);
        }

        var threw = false;
        try
        {
            var intent = new Intent(ConstitutionBudgetContract.Action);
            intent.SetComponent(new ComponentName(ConstitutionBudgetContract.Package, ConstitutionBudgetContract.Receiver));
            intent.PutExtra(ConstitutionBudgetContract.MonthExtra, payload.Month);
            intent.PutExtra(ConstitutionBudgetContract.BudgetExtra, payload.BudgetText);
            intent.PutExtra(ConstitutionBudgetContract.SpentExtra, payload.SpentText);
            context.SendBroadcast(intent, ConstitutionBudgetContract.Permission);
        }
        catch (Exception ex)
        {
            threw = true;
            logger?.LogInformation(ex, "Budget broadcast to Constitution failed; the next change sends again.");
        }

        return BudgetSendPlan.Outcome(receiverInstalled: true, sendThrew: threw);
    }

    private static bool IsInstalled(Context context)
    {
        try
        {
            context.PackageManager!.GetPackageInfo(ConstitutionBudgetContract.Package, (PackageInfoFlags)0);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }
}
