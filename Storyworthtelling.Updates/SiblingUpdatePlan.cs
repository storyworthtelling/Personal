namespace Storyworthtelling.Updates;

/// <summary>One APK on the release: its asset id and what it was read as.</summary>
public sealed record ReleaseApk(long AssetId, InstallCandidate Candidate);

/// <summary>What the channel does with one APK: install it, or the settled outcome that says why not.</summary>
public sealed record ApkDecision(ReleaseApk Apk, InstallOutcome? Settled)
{
    public bool Install => Settled is null;
}

/// <summary>
/// The update channel's decision per release asset: for each APK, install it, or say why not.
///
/// Each asset is judged as itself, never by its package. Since P14 a release can hold two APKs of one package (Post's
/// and Dash, Score's and Wave, Constitution's and Beat, Flyleaf's and the Point installer), so "this package is wanted"
/// would carry the other form along with it, and the phone would download and install Dash because Post was due. The
/// form is judged first, before any version is compared (W-3.1.1).
/// </summary>
public static class SiblingUpdatePlan
{
    public static IReadOnlyList<ApkDecision> Decide(
        UpdateRole role,
        string selfPackage,
        ApkForm deviceForm,
        IReadOnlyList<ReleaseApk> apks,
        IReadOnlyDictionary<string, long> installedVersionCodes)
    {
        var decisions = new List<ApkDecision>(apks.Count);
        foreach (var apk in apks)
        {
            if (deviceForm == ApkForm.Unknown || apk.Candidate.Form != deviceForm)
            {
                decisions.Add(new ApkDecision(apk, InstallOutcome.NotForThisDevice));
                continue;
            }

            var chosen = UpdateScope.Decide(role, selfPackage, deviceForm, [apk.Candidate], installedVersionCodes);
            decisions.Add(new ApkDecision(apk, chosen.Count > 0 ? null : InstallOutcome.AlreadyCurrent));
        }
        return decisions;
    }
}
