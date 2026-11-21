using System.Globalization;
using System.Text.Json;
using Storyworthtelling.Updates;

namespace Watch.Domain.Updates;

/// <summary>What Flyleaf heard from the watch at the start of a run.</summary>
public enum WatchReach
{
    /// <summary>No watch with the installer is paired, or the phone has no Wear OS at all: the line says nothing.</summary>
    NoWatch,

    /// <summary>Paired, but not reachable now.</summary>
    Unreachable,

    /// <summary>Reachable, asked, and no report came back in time.</summary>
    NoAnswer,

    /// <summary>A report came back that would not read.</summary>
    Unreadable,

    /// <summary>A report came back in a newer version of <c>update/installed</c> than this Flyleaf reads.</summary>
    Newer,

    /// <summary>The watch said what it has.</summary>
    Answered,
}

/// <summary>The watch's answer to the run's ask: how far the phone got, and the report when there is one.</summary>
public sealed record WatchAnswer
{
    private WatchAnswer(WatchReach reach, InstalledReport? report)
    {
        Reach = reach;
        Report = report;
    }

    public WatchReach Reach { get; }

    public InstalledReport? Report { get; }

    public static WatchAnswer Answered(InstalledReport report) =>
        new(WatchReach.Answered, report ?? throw new ArgumentNullException(nameof(report)));

    /// <summary>Any reach but <see cref="WatchReach.Answered"/>: there is no report to go on.</summary>
    public static WatchAnswer Not(WatchReach reach) =>
        reach == WatchReach.Answered
            ? throw new ArgumentException("An answered watch carries its report; use Answered.", nameof(reach))
            : new(reach, null);
}

/// <summary>One APK the phone sent the watch, and when its last byte left.</summary>
public sealed record SentApk(string Package, long VersionCode, DateTimeOffset At);

/// <summary>What the phone does with one watch APK on the release.</summary>
public enum WatchSendVerdict
{
    Send,

    /// <summary>The watch has it, or a newer one, or said it installed this very build.</summary>
    AlreadyCurrent,

    /// <summary>Sent less than <see cref="WatchSendPlan.InFlightWait"/> ago and not reported yet: still landing.</summary>
    InFlight,

    /// <summary>The system's prompt for this build is on the wrist, and was put there less than
    /// <see cref="WatchSendPlan.WaitingOffer"/> ago.</summary>
    WaitingOnTheWrist,

    /// <summary>The wrist turned this build down; a newer build is offered, this one is not (unless the owner asks).</summary>
    Declined,

    /// <summary>The watch refused this build as not its own (a phone APK, or outside the suite): sending it again
    /// changes nothing.</summary>
    RefusedByTheWatch,
}

/// <summary>The decision for one watch APK.</summary>
public sealed record WatchSendDecision(ReleaseApk Apk, WatchSendVerdict Verdict)
{
    public bool Send => Verdict == WatchSendVerdict.Send;
}

/// <summary>
/// Flyleaf's half of the watch's update channel (P14 W-3.1.3): of the release's watch APKs, which to send the Point
/// installer this run. The watch's own report is the truth about what it has; the phone's sent-record only stops it
/// sending the same APK twice while the first is still landing or waiting on the wrist.
/// </summary>
public static class WatchSendPlan
{
    /// <summary>How long the phone waits for the watch's report after asking.</summary>
    public static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(30);

    /// <summary>How long after a send with no word back the phone takes it as lost and sends it again.</summary>
    public static readonly TimeSpan InFlightWait = TimeSpan.FromHours(1);

    /// <summary>How long a build left waiting on the wrist is left alone before it is offered again.</summary>
    public static readonly TimeSpan WaitingOffer = TimeSpan.FromDays(3);

    /// <summary>How long one Data Layer call is waited for (finding the watch, asking it, opening a channel or its
    /// stream): a call that has not answered by then is taken as out of reach, so the run's gate is always let go.</summary>
    public static readonly TimeSpan DataLayerWait = TimeSpan.FromSeconds(30);

    /// <summary>How long closing a channel is waited for.</summary>
    public static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(10);

    /// <summary>How long a send waits for the installer to report on it before its channel is closed.</summary>
    public static readonly TimeSpan ReportWait = TimeSpan.FromMinutes(1);

    /// <summary>What a send needs left of the run to start at all (the download, the transfer and the report's wait);
    /// with less, it waits for the next run.</summary>
    public static readonly TimeSpan SendNeeds = TimeSpan.FromMinutes(3);

    /// <param name="offered">Every APK on the release as the phone read it (sidecar and name agreed); only the watch ones
    /// are decided.</param>
    /// <param name="force">The owner's "Install from GitHub now": a declined build, or one waiting on the wrist, is
    /// offered again at once.</param>
    public static IReadOnlyList<WatchSendDecision> Decide(
        IReadOnlyList<ReleaseApk> offered,
        WatchAnswer answer,
        IReadOnlyList<SentApk> sent,
        DateTimeOffset now,
        bool force = false)
    {
        if (answer.Reach != WatchReach.Answered || answer.Report is not { } report)
        {
            return [];
        }

        // Only the suite's watch APKs are the watch's: a phone APK carries the same package (PC1), an unknown form is
        // installed nowhere (A4), and anything outside the namespace is not the channel's to carry.
        var watchApks = offered
            .Where(apk => apk.Candidate.Form == ApkForm.Watch && SuiteApps.IsOwnApp(apk.Candidate.Package))
            .ToList();
        var installed = report.Apps
            .GroupBy(app => app.Package, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(app => app.VersionCode), StringComparer.Ordinal);

        // A4's rule with the watch's own form: newer than the watch has, or not there at all.
        var byVersion = SiblingUpdatePlan.Decide(UpdateRole.Custodian, WatchIdentity.InstallerPackage, ApkForm.Watch, watchApks, installed);

        var decisions = byVersion
            .Select(decision => new WatchSendDecision(
                decision.Apk,
                decision.Install ? Due(decision.Apk.Candidate, report, sent, now, force) : WatchSendVerdict.AlreadyCurrent))
            .ToList();

        // The installer replacing itself ends its process, so whatever is sent after it lands on nobody: it goes last.
        return
        [
            .. decisions.Where(decision => !IsInstaller(decision)),
            .. decisions.Where(IsInstaller),
        ];
    }

    /// <summary>The transfer's name on <c>/update/apk/&lt;transfer&gt;</c>: the asset id and a fresh nonce, so a send
    /// never lands in a file an earlier one left.</summary>
    public static string TransferName(long assetId, Guid nonce) =>
        $"{assetId.ToString(CultureInfo.InvariantCulture)}-{nonce:N}";

    private static bool IsInstaller(WatchSendDecision decision) =>
        string.Equals(decision.Apk.Candidate.Package, WatchIdentity.InstallerPackage, StringComparison.Ordinal);

    // A build the watch doesn't have yet: what its own report says about this very build decides first, then the record
    // of what was sent.
    private static WatchSendVerdict Due(InstallCandidate candidate, InstalledReport report, IReadOnlyList<SentApk> sent, DateTimeOffset now, bool force)
    {
        var result = report.Results.FirstOrDefault(entry =>
            string.Equals(entry.Package, candidate.Package, StringComparison.Ordinal) && entry.VersionCode == candidate.VersionCode);
        var last = sent.FirstOrDefault(entry =>
            string.Equals(entry.Package, candidate.Package, StringComparison.Ordinal) && entry.VersionCode == candidate.VersionCode);

        switch (result?.Outcome)
        {
            // The watch said it installed this build. If it has gone since, it was taken off by hand (or is out of the
            // installer's sight), and the phone doesn't push it back on.
            case WatchInstallOutcome.Installed or WatchInstallOutcome.InstalledAfterAsking or WatchInstallOutcome.AlreadyCurrent:
                return WatchSendVerdict.AlreadyCurrent;

            // The watch refused it as not its own: the same bytes would be refused again.
            case WatchInstallOutcome.NotForThisWatch or WatchInstallOutcome.NotOurs:
                return WatchSendVerdict.RefusedByTheWatch;

            case WatchInstallOutcome.Declined:
                return force ? WatchSendVerdict.Send : WatchSendVerdict.Declined;

            // The card is on the wrist. Swiped away, it leaves the session waiting until the platform drops it after three
            // days, when there is nothing left to tap; then it is offered again. With no record of the send, it is too.
            case WatchInstallOutcome.Waiting:
                return force || last is null || now - last.At >= WaitingOffer
                    ? WatchSendVerdict.Send
                    : WatchSendVerdict.WaitingOnTheWrist;

            // A damaged transfer or a failed install: the watch asks for it again (WatchInstallOutcomes.SendAgain).
            case WatchInstallOutcome.Failed or WatchInstallOutcome.Damaged:
                return WatchSendVerdict.Send;
        }

        // No word on this build yet: sent within the hour, it is still landing. A dropped channel or a full disk leaves no
        // result at all, so after the hour it is taken as lost.
        return last is not null && now - last.At < InFlightWait ? WatchSendVerdict.InFlight : WatchSendVerdict.Send;
    }
}

/// <summary>The phone's record of what it last sent the watch, one entry per app, kept in Flyleaf's preferences.</summary>
public static class WatchSentRecord
{
    // Stored words, never shown or sent.
    private const string PackageField = "package";
    private const string VersionCodeField = "versionCode";
    private const string AtField = "at";

    /// <summary>The record with this send in place of the app's last one: only the latest send of an app can hold a
    /// build back.</summary>
    public static IReadOnlyList<SentApk> Record(IReadOnlyList<SentApk> record, SentApk sent) =>
        [.. record.Where(entry => !string.Equals(entry.Package, sent.Package, StringComparison.Ordinal)), sent];

    public static string Encode(IReadOnlyList<SentApk> record)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartArray();
            foreach (var entry in record)
            {
                writer.WriteStartObject();
                writer.WriteString(PackageField, entry.Package);
                writer.WriteNumber(VersionCodeField, entry.VersionCode);
                writer.WriteString(AtField, entry.At);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(json.ToArray());
    }

    /// <summary>The stored record, or none: a record that will not read only means a send may happen once more.</summary>
    public static IReadOnlyList<SentApk> Decode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(stored);
            return ReadApps(document.RootElement) ?? [];
        }
        catch (JsonException)
        {
            // Damaged in Flyleaf's preferences: start again; the watch's report still says what it has.
            return [];
        }
    }

    private static List<SentApk>? ReadApps(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var read = new List<SentApk>();
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty(PackageField, out var package) || package.ValueKind != JsonValueKind.String
                || package.GetString() is not { Length: > 0 } name
                || !entry.TryGetProperty(VersionCodeField, out var code) || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt64(out var versionCode)
                || !entry.TryGetProperty(AtField, out var at) || at.ValueKind != JsonValueKind.String
                || !at.TryGetDateTimeOffset(out var when))
            {
                return null;
            }
            read.Add(new SentApk(name, versionCode, when));
        }
        return read;
    }
}
