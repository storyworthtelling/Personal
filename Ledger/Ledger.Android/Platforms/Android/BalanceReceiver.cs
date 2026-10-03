using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Index.Domain.Identity;
using Index.Infrastructure.Services;

namespace Index.Android.Platforms.Android;

/// <summary>
/// Post's frozen <c>IndexHandoff</c> contract (<c>FocusPhone/Codex.Domain/Post/MoneyMail.cs</c>,
/// read-only to this app): package, receiver class name, action and permission below are copied
/// byte-for-byte from that file's own constants, and Guard G2 (<c>IndexHandoffContractTests</c>) reads
/// <c>MoneyMail.cs</c> directly to prove it — do not retype these from memory if that file ever
/// changes; read its constants again instead.
///
/// <para><b>D87/D91's "matched by merchant name" is now live.</b> The contract carried no
/// institution/servicer/sender name for a while — Post's own doc comment on <c>IndexBalance</c>
/// explained why: "nothing about who the owner banks with belongs in an intent that any signed app
/// could receive." The owner has since overridden that reasoning, on purpose, for exactly this one
/// field (<c>IndexHandoff</c>'s own remarks record it as the owner's decision, not a drift): the
/// signature-level permission already limits receipt to the owner's own signed apps, and Index cannot
/// route a balance to the right account without some identifying signal. <c>IndexHandoff.Extras</c> now
/// carries an optional <c>sender</c> key alongside <c>balance</c>/<c>asOf</c>, present only when Post
/// (or Plate) had a name to send. <see cref="Index.Domain.Model.BalanceReceiverMatch"/> and
/// <see cref="AccountService.ApplyBalanceReceiptAsync"/> below implement D87/D91's precedence in full
/// (Guard G6) and this receiver now calls into it with the sender name when one arrives. When
/// <c>sender</c> is absent — an old Plate statement send, or any future sender that cannot supply one
/// — applying an unnamed balance to a GUESSED account would be exactly the "fabricated balance...
/// believed" failure <c>MoneyMail</c>'s own remarks warn against, so this receiver still does not
/// guess in that case: it logs an unroutable receipt instead, visible in Export diagnostics rather than
/// silently vanishing. Surfaced in the Release C report as the top open question for Part 5; this is
/// its resolution.</para>
/// </summary>
[BroadcastReceiver(
    Name = IndexIdentity.ApplicationId + ".BalanceReceiver",
    Exported = true,
    Enabled = true,
    Permission = IndexIdentity.ApplicationId + ".RECEIVE_BALANCE")]
[IntentFilter([BalanceReceiver.HandoffAction])]
public class BalanceReceiver : BroadcastReceiver
{
    // Built from the frozen IndexIdentity.ApplicationId constant, per this codebase's own convention
    // (IndexInvariantsTests.TheApplicationIdIsNotCopiedAroundTheCode forbids retyping the raw package
    // string) — the VALUES these evaluate to are exactly FocusPhone/Codex.Domain/Post/MoneyMail.cs's
    // IndexHandoff.Receiver/Action/Permission, which Guard G2 (IndexHandoffContractTests) checks by
    // reading that file directly. Named HandoffAction rather than the bare "Action" only to avoid
    // reading, at the call site below, like the unrelated System.Action delegate type.
    public const string HandoffAction = IndexIdentity.ApplicationId + ".action.BALANCE";
    public const string BalanceExtra = "balance";
    public const string AsOfExtra = "asOf";
    public const string SenderExtra = "sender";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != HandoffAction)
        {
            return;
        }

        var balanceText = intent.GetStringExtra(BalanceExtra);
        // The balance crosses as exact TEXT (IndexHandoff's own remark: 0.1 + 0.2 is not 0.3 in binary
        // floating point), so it is read as decimal, never double.
        if (string.IsNullOrEmpty(balanceText) ||
            !decimal.TryParse(
                balanceText, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var balance))
        {
            LogUnroutable(context, "balance extra missing or unparsable");
            return;
        }

        var asOfText = intent.GetStringExtra(AsOfExtra);
        DateOnly? asOf = null;
        if (!string.IsNullOrEmpty(asOfText) &&
            DateOnly.TryParseExact(
                asOfText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            asOf = parsed;
        }
        // An absent/unparsable asOf means Index stamps arrival time itself (IndexHandoff's own note);
        // AccountService.ApplyBalanceReceiptAsync already treats a null asOf that way.

        var sender = intent.GetStringExtra(SenderExtra);
        if (string.IsNullOrWhiteSpace(sender))
        {
            // No identifying signal on this receipt (an old Plate statement send, or any future
            // sender that cannot supply one) — see this class's own remarks: applying an unnamed
            // balance to a GUESSED account would be exactly the "fabricated balance...believed"
            // failure MoneyMail's own remarks warn against, so this still does not guess.
            LogUnroutable(context, $"received balance {balance:0.00} (asOf={asOf?.ToString("yyyy-MM-dd") ?? "none"}) with no sender name to match it against");
            return;
        }

        RouteAsync(context, sender, balance, asOf);
    }

    // Fire-and-forget from a BroadcastReceiver's synchronous OnReceive: PendingResult would let this
    // await properly, but the existing LogUnroutable-only version already returned immediately, and
    // AccountService's own EF work is quick. Any failure here is caught and logged, never thrown —
    // per this class's standing rule, nothing may crash the process that owns other receivers.
    private static async void RouteAsync(Context context, string sender, decimal balance, DateOnly? asOf)
    {
        try
        {
            var service = IPlatformApplication.Current?.Services.GetService<AccountService>();
            if (service is null)
            {
                LogUnroutable(context, "AccountService unavailable (DI container not ready)");
                return;
            }

            var applied = await service.ApplyBalanceReceiptAsync(sender, balance, asOf, default);
            if (!applied)
            {
                LogUnroutable(context, $"received balance {balance:0.00} from '{sender}' but no manual or stale-Plaid account matched it");
            }
        }
        catch (Exception e)
        {
            var logger = IPlatformApplication.Current?.Services.GetService<ILogger<BalanceReceiver>>();
            logger?.LogWarning(e, "BalanceReceiver: failed to route balance from '{Sender}'", sender);
        }
    }

    private static void LogUnroutable(Context context, string reason)
    {
        try
        {
            var logger = IPlatformApplication.Current?.Services.GetService<ILogger<BalanceReceiver>>();
            logger?.LogWarning("BalanceReceiver: {Reason}", reason);
        }
        catch
        {
            // Never throw out of a broadcast receiver: a missing/torn-down DI container on a cold
            // receive is a smaller loss than crashing the process that owns SmsDeliverReceiver-style
            // receivers elsewhere in the suite.
        }
    }
}
