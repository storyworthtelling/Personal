using System.Text.RegularExpressions;

namespace Pulp.Domain.LockedOpen;

/// <summary>
/// What Pulp asks Flyleaf when a title should open <i>locked</i> (Release G, D288): the service's
/// package and the title's https deep link. The wire names are Flyleaf's own, copied byte-for-byte
/// from <c>FocusPhone/Codex.Domain/Apps/LockedOpen.cs</c> (<c>LockedOpenHandoff</c>);
/// <c>LockedOpenContractTests</c> (guard G19) reads that file by content, so a drift on either side
/// fails there instead of dropping the broadcast silently.
///
/// The send is an explicit broadcast to Flyleaf's named receiver under its signature permission, with
/// the two values as exact-text extras. Flyleaf decides whether to honour it (D296); Pulp only asks,
/// and only for a title the planner has already allowed.
/// </summary>
public static class LockedOpenHandOff
{
    public const string Package = "com.storyworthtelling.focusphone.launcher";

    public const string Receiver = "com.storyworthtelling.focusphone.launcher.LockedOpenReceiver";

    public const string Action = "com.storyworthtelling.focusphone.launcher.action.LOCKED_OPEN";

    public const string Permission = "com.storyworthtelling.focusphone.launcher.RECEIVE_LOCKED_OPEN";

    public const string PackageExtra = "package";

    public const string LinkExtra = "link";

    /// <summary>
    /// Flyleaf's answer, as the ordered broadcast's result code (D301): <c>Activity.RESULT_OK</c>, and only
    /// when a locked session started. Copied from the launcher's <c>LockedOpenHandoff</c>.
    /// </summary>
    public const int ResultStarted = -1;

    /// <summary>Anything else: refused, or could not start. The initial code, so silence reads as this.</summary>
    public const int ResultRefused = 0;

    /// <summary>How long Pulp waits for Flyleaf's answer before falling back to the plain hand-off.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Flyleaf refuses a longer link; Pulp does not send one.</summary>
    public const int MaxLinkLength = 2048;

    private static readonly Regex PackageShape = new(
        @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

    /// <summary>The payload as intent extras: the package and the https link, as exact text.</summary>
    public static IReadOnlyDictionary<string, string> Extras(string package, Uri link)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(link);
        if (!PackageShape.IsMatch(package))
        {
            throw new ArgumentException($"'{package}' is not a package id, so Flyleaf would refuse the request.", nameof(package));
        }

        if (!link.IsAbsoluteUri || link.Scheme != Uri.UriSchemeHttps || link.AbsoluteUri.Length > MaxLinkLength)
        {
            throw new ArgumentException("A locked open needs an absolute https link within the length Flyleaf accepts.", nameof(link));
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PackageExtra] = package,
            [LinkExtra] = link.AbsoluteUri,
        };
    }
}

/// <summary>
/// What Pulp does with Flyleaf's answer (D301): fall back to the plain hand-off on anything but a started
/// session or on no answer in time, and never twice, whichever of the answer and the timeout comes second.
/// One instance per ask; safe to call from the receiver and the timer at once.
/// </summary>
public sealed class LockedOpenAnswer
{
    private int settled;

    /// <param name="resultCode">The ordered broadcast's result code, or null when the timeout fired first.</param>
    /// <returns>True exactly once, for the first call that is not a started answer.</returns>
    public bool ShouldFallBack(int? resultCode)
    {
        if (Interlocked.Exchange(ref settled, 1) != 0)
        {
            return false;
        }

        return resultCode != LockedOpenHandOff.ResultStarted;
    }
}
