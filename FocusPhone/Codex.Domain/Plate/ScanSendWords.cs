namespace Codex.Domain.Plate;

/// <summary>Why a scan did not reach the reMarkable, as the head classified it.</summary>
public enum ScanSendProblem
{
    /// <summary>The phone had no network: nothing is wrong with the scan or the tablet.</summary>
    NoNetwork,

    /// <summary>Over the tablet's size limit, refused before a byte was sent; trying again changes nothing.</summary>
    TooLarge,

    /// <summary>The tablet's cloud gave a reason of its own (running out of memory sending it, for one).</summary>
    TabletRefused,

    /// <summary>Anything else: worth another try.</summary>
    Failed,
}

/// <summary>
/// What Plate says when a scan does not reach the reMarkable (audit 11 §21). It used to say "the reMarkable refused it"
/// with the raw message whatever happened — blaming the tablet for a phone with no network, and offering nothing to do.
/// The scan is filed under Documents before it is sent, so every sentence opens by saying so and then says what next.
/// </summary>
public static class ScanSendWords
{
    public static string NotSent(ScanSendProblem problem, string? reason) => problem switch
    {
        ScanSendProblem.NoNetwork =>
            "Saved to Documents. There's no network, so it hasn't gone to the reMarkable — send it again once you're back online.",
        ScanSendProblem.TooLarge => $"Saved to Documents. It can't go to the reMarkable: {reason}.",
        ScanSendProblem.TabletRefused => $"Saved to Documents. The reMarkable didn't take it: {reason}.",
        _ => "Saved to Documents. It didn't reach the reMarkable — send it again to try once more.",
    };
}
