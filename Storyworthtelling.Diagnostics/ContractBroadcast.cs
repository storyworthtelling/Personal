namespace Storyworthtelling.Diagnostics;

/// <summary>How a courier's send of a cross-app broadcast went.</summary>
public enum ContractSendOutcome
{
    Sent,

    /// <summary>The receiving app is not installed: the ordinary case on a phone without it.</summary>
    ReceiverAbsent,

    /// <summary>This app does not hold the contract's permission, so the platform would drop the broadcast silently.</summary>
    NotPermitted,

    /// <summary>The platform threw.</summary>
    Failed,
}

/// <summary>
/// The rules every cross-app broadcast follows (audit W2, 09 L1 and L2). <c>SendBroadcast</c> reports nothing, which
/// is how every contract in the suite stayed broken with nobody seeing it: so a courier checks the receiver is
/// installed and that it holds the permission before sending, and logs the outcome; a receiver logs what arrived;
/// and every broadcast carries <see cref="VersionExtra"/>, so a later change of meaning can be told apart from an
/// older sender. The lines name the contract and the outcome only, never anything from the payload.
/// </summary>
public static class ContractBroadcast
{
    /// <summary>The integer extra every contract broadcast carries.</summary>
    public const string VersionExtra = "v";

    /// <summary>The version every contract speaks today. A contract whose meaning changes raises its own.</summary>
    public const int Version = 1;

    public static ContractSendOutcome Before(bool receiverInstalled, bool holdsPermission) =>
        !receiverInstalled ? ContractSendOutcome.ReceiverAbsent
        : !holdsPermission ? ContractSendOutcome.NotPermitted
        : ContractSendOutcome.Sent;

    /// <param name="failure">For <see cref="ContractSendOutcome.Failed"/>, the exception's type name; never its message.</param>
    public static string SendLine(string contract, ContractSendOutcome outcome, string? failure = null) => outcome switch
    {
        ContractSendOutcome.Sent => $"CONTRACT {contract}: sent v{Version}",
        ContractSendOutcome.ReceiverAbsent => $"CONTRACT {contract}: not sent, the receiving app is not installed",
        ContractSendOutcome.NotPermitted => $"CONTRACT {contract}: not sent, this app does not hold the permission",
        _ => $"CONTRACT {contract}: not sent, {failure ?? "the platform refused"}",
    };

    /// <summary>The version a broadcast carries. A broadcast with none came from a sender older than the extra: v1.</summary>
    public static int ReadVersion(int extra) => extra <= 0 ? 1 : extra;

    /// <summary>Whether a receiver reads this version. A newer one may mean something else by the same keys.</summary>
    public static bool Accepts(int version) => version <= Version;

    public static string ReceiptLine(string contract, int version) => Accepts(version)
        ? $"CONTRACT {contract}: received v{version}"
        : $"CONTRACT {contract}: refused v{version}, newer than this app reads; update it";
}
