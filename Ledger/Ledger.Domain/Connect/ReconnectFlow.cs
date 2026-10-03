using Index.Domain.External;

namespace Index.Domain.Connect;

/// <summary>
/// D150: the one call a reconnect makes — Plaid Link's UPDATE MODE against an existing Item's access
/// token, never a fresh Link. Pulled out to its own pure-ish (single-call, no UI) wrapper so Guard G16
/// (<c>UpdateModeReconnectTests</c>) can prove <see cref="PlaidLinkPage"/>'s reconnect path never falls
/// back to <see cref="IPlaidClient.CreateLinkTokenAsync"/> without loading the Android/MAUI assembly
/// that page lives in — a plain <c>net10.0</c> Domain type and a spy <see cref="IPlaidClient"/> are
/// enough to watch which overload gets called.
/// </summary>
public static class ReconnectFlow
{
    public static Task<PlaidLinkToken> BeginAsync(
        IPlaidClient plaid, string existingAccessToken, CancellationToken cancellationToken) =>
        plaid.CreateUpdateLinkTokenAsync(existingAccessToken, cancellationToken);
}
