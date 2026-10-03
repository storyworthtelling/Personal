using Microsoft.Extensions.Logging;
using Index.Domain.External;

namespace Index.Infrastructure.Services;

/// <summary>
/// The one place a bank is released at Plaid (<c>/item/remove</c>). A hand-entered bank has a blank access
/// token — there is no Item to release, and sending Plaid an empty token is a guaranteed rejection — so a
/// blank token is skipped here rather than at each of the call sites.
/// </summary>
public static class PlaidItemRelease
{
    /// <summary>Returns true when Plaid was asked and accepted; false when there was nothing to release or
    /// Plaid refused. A refusal is logged with its detail rather than thrown: the local disconnect has
    /// already happened and must not be undone by a remote failure.</summary>
    public static async Task<bool> ReleaseAsync(
        IPlaidClient plaid, string accessToken, string bankName, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        try
        {
            await plaid.RemoveItemAsync(accessToken, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception, "Releasing {Bank} at Plaid (/item/remove) failed; the bank is disconnected locally "
                + "and its Item may still be live at Plaid.", bankName);
            return false;
        }
    }
}
