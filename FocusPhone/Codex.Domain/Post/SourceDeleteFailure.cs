namespace Codex.Domain.Post;

/// <summary>What a delete through the Google Messages bridge came to. The client says this and logs nothing.</summary>
public enum BridgeDeleteResult
{
    Deleted,
    NotConnected,
    Refused,
}

/// <summary>
/// Why a delete owed at the source did not land, named for the filing retry's throttled line
/// (<see cref="PostLogLines.OwedDeleteFailing"/>) — a kind, never the item.
/// </summary>
public enum SourceDeleteFailure
{
    NotConnected,
    BridgeRefused,
    NotPermitted,
    StillAtSource,
    Unaddressable,
    Threw,
}

public static class SourceDeleteFailures
{
    /// <summary>Null when the bridge delete landed; otherwise why it did not.</summary>
    public static SourceDeleteFailure? FromBridge(BridgeDeleteResult result) => result switch
    {
        BridgeDeleteResult.Deleted => null,
        BridgeDeleteResult.NotConnected => SourceDeleteFailure.NotConnected,
        _ => SourceDeleteFailure.BridgeRefused,
    };

    public static string Words(SourceDeleteFailure reason) => reason switch
    {
        SourceDeleteFailure.NotConnected => "not connected",
        SourceDeleteFailure.BridgeRefused => "the bridge refused",
        SourceDeleteFailure.NotPermitted => "not permitted",
        SourceDeleteFailure.StillAtSource => "still at the source",
        SourceDeleteFailure.Unaddressable => "no address to delete",
        _ => "an error",
    };
}
