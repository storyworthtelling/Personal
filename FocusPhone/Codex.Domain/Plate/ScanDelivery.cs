using Codex.Domain.Policy;

namespace Codex.Domain.Plate;

/// <summary>How a finished scan leaves the phone.</summary>
public enum DeliveryRoute
{
    /// <summary>Handed to an app that already knows how to receive a PDF.</summary>
    ShareIntent,

    /// <summary>Uploaded to the root of the paired reMarkable through the shared library.</summary>
    RemarkableRoot,
}

/// <summary>
/// The delivery is deliberately hybrid, and this is where that decision is written down.
///
/// <para>Drive and Post are apps on the phone that already accept a PDF, so Plate shares to
/// them rather than acquiring a Drive API client and a <c>drive</c> write scope of its own.
/// The reMarkable has no app to share to, so it goes through
/// <c>Storyworthtelling.Paper.IPaperDelivery</c> — and to the account <b>root</b>, using the
/// plain upload DailyLoader has exercised all along rather than the folder overload, whose
/// wire format the repo still records as reconstructed and not live-verified.</para>
/// </summary>
public static class ScanDelivery
{
    public static DeliveryRoute RouteFor(ScanDestination destination) => destination switch
    {
        ScanDestination.Drive or ScanDestination.Post => DeliveryRoute.ShareIntent,
        ScanDestination.Remarkable => DeliveryRoute.RemarkableRoot,
        _ => throw new ArgumentOutOfRangeException(nameof(destination), destination,
            "A scan destination with no delivery route would silently deliver nowhere."),
    };

    /// <summary>
    /// The app the share is aimed at, or null where there is no app to share to. A bare
    /// chooser on a locked phone offers whatever survived the lockdown rather than the door
    /// the owner actually picked, so the package is named.
    /// </summary>
    public static string? TargetPackageFor(ScanDestination destination) => destination switch
    {
        ScanDestination.Drive => PolicyContext.DrivePackage,
        ScanDestination.Post => PolicyContext.CommunicationPackage,
        _ => null,
    };
}
