using Pulp.Domain.Children;

namespace Pulp.Domain.HandOffs;

/// <summary>
/// The plain-language sentences a hand-off that did not happen leaves in the dog-ear (D122): what
/// happened and what to do next. Never voiced (errors are not among the voice's seats) and never a
/// toast; a hand-off that worked has no sentence at all, because the service's app opening is the
/// answer.
/// </summary>
public static class HandOffNotice
{
    public static string ForRefusal(StartRefusal refusal, string serviceName)
    {
        var service = RequireName(serviceName);
        return refusal switch
        {
            StartRefusal.Hidden => "This title is not on the list for everyone watching, so it was not opened.",
            StartRefusal.NotAHome => $"This title is not on {service}, so it was not opened. Pick one of the services it is on.",
            StartRefusal.ServiceBlocked => $"{service} is blocked for a child watching, so it was not opened.",
            StartRefusal.ServiceNotAttested =>
                $"{service} is for adults until its adult profiles are PIN-locked. Lock them in {service}, then tick it in Settings.",
            StartRefusal.UnknownService => $"Pulp does not know how to open {service}, so nothing was opened.",
            _ => throw new ArgumentException(
                $"'{refusal}' is not a refusal; a hand-off that may start has no notice.", nameof(refusal)),
        };
    }

    public static string NotInstalled(string serviceName)
    {
        var service = RequireName(serviceName);
        return $"{service} is not installed on this phone. Install it, then open the title again.";
    }

    public static string LandedElsewhere(string serviceName)
    {
        var service = RequireName(serviceName);
        return $"{service} opened, but not on this title. Look for it in {service}, or mark it watched here.";
    }

    private static string RequireName(string serviceName) =>
        string.IsNullOrWhiteSpace(serviceName)
            ? throw new ArgumentException("A notice names the service it is about.", nameof(serviceName))
            : serviceName.Trim();
}
