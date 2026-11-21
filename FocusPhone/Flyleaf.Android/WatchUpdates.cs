using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.Gms.Common.Apis;
using Android.Gms.Wearable;
using Codex.Infrastructure.Updates;
using Codex.Ui;
using Codex.Ui.Http;
using Storyworthtelling.Updates;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Flyleaf;

/// <summary>
/// Flyleaf's half of the watch's update channel (P14 W-3.1.3, A70): inside the six-hourly run, after the phone's own
/// installs, it finds the watch that carries the Point installer, asks what it has, and sends it the watch's release's
/// APKs that are newer, one channel each, framed by the same codec the installer reads (<see cref="UpdateApkFrame"/>).
/// It reads the watch's own release only (<see cref="ReleaseChannels.Watch"/>), which no phone installs from.
///
/// Every decision is <see cref="WatchSendPlan"/>'s; this half only finds the watch, carries bytes and keeps the record.
/// A failure here never reaches the phone's own installs: it runs after them, from its own try. It runs under the run's
/// one deadline (<see cref="RunDeadline"/>, release A fix round): nothing starts past it, and every Data Layer call is
/// waited for at most its own wait, cut to what is left, so a call that never answers cannot hold the watch's gate.
/// </summary>
internal static class WatchUpdates
{
    private const string Tag = "FlyleafWatch";  // not copy: a log tag

    // A downloaded watch APK waits in the cache only while it is sent; the asset's id names it.
    private const string CacheFilePrefix = "watch-";  // not copy: a file name

    // One read or write of the APK down the channel.
    private const int ChunkBytes = 64 * 1024;

    /// <summary>The watch half of a run, logged and never thrown: the phone's run has already been recorded.</summary>
    public static async Task RunQuietlyAsync(Context context, bool force, RunDeadline deadline)
    {
        try
        {
            await RunAsync(context, force, deadline);
        }
        catch (Exception e)
        {
            // Anything unforeseen in the watch half stops the watch half only; its shape is logged, never its message.
            HeadLog.Quiet(Tag, "The watch half of the update run stopped; it runs again next period", e);
        }
    }

    private static async Task RunAsync(Context context, bool force, RunDeadline deadline)
    {
        var store = new WatchUpdateStore(context);
        if (deadline.Passed(DateTimeOffset.UtcNow))
        {
            // The phone's half used the run's time: the watch is looked for next run, and its record is left as it was.
            HeadLog.Info(Tag, WatchSendLines.OutOfTime("looking for the watch"));
            return;
        }

        var (node, reach) = await FindAsync(context, deadline);
        if (node is null)
        {
            Finish(store, WatchAnswer.Not(reach), [], [], []);
            return;
        }

        var answer = await AskAsync(context, node, deadline);
        if (answer.Reach != WatchReach.Answered)
        {
            Finish(store, answer, [], [], []);
            return;
        }

        var token = new UpdateSettingsStore(context).GitHubToken;
        if (string.IsNullOrEmpty(token))
        {
            // The phone's own line already says there is no token; the watch's record is left as it was.
            HeadLog.Info(Tag, "WATCH run: no update token, so nothing was looked for");
            return;
        }

        var http = HeadHttp.Shared;
        var client = new GitHubReleaseClient(http, UpdateService.Repo, token);
        ReleaseInfo release;
        try
        {
            release = await client.GetReleaseAsync(ReleaseChannels.Watch).WaitAsync(deadline.Left(DateTimeOffset.UtcNow));
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException)
        {
            HeadLog.Warn(Tag, $"WATCH run: the watch’s release didn’t read ({e.GetType().Name}); it runs again next period");
            Finish(store, answer, [], [], []);
            return;
        }

        var offered = await OfferedAsync(client, release, deadline);
        var decisions = WatchSendPlan.Decide([.. offered.Select(pair => pair.Apk)], answer, store.Sent, DateTimeOffset.UtcNow, force);
        foreach (var decision in decisions)
        {
            HeadLog.Info(Tag, WatchSendLines.Decision(decision));
        }

        var sent = new List<WatchApp>();
        var notSent = new List<WatchApp>();
        foreach (var decision in decisions.Where(decision => decision.Send))
        {
            var candidate = decision.Apk.Candidate;
            var app = new WatchApp(candidate.Package, candidate.VersionCode);
            if (!deadline.Allows(DateTimeOffset.UtcNow, WatchSendPlan.SendNeeds))
            {
                // Too little of the run is left to download, send and hear back: the next run starts it.
                HeadLog.Info(Tag, WatchSendLines.LeftForNextRun(candidate.Package, candidate.VersionCode));
                continue;
            }

            var asset = offered.Single(pair => pair.Apk.AssetId == decision.Apk.AssetId).Asset;
            if (await SendOneAsync(context, http, token, asset, candidate, node, deadline))
            {
                store.RecordSent(new SentApk(candidate.Package, candidate.VersionCode, DateTimeOffset.UtcNow));
                sent.Add(app);
            }
            else
            {
                notSent.Add(app);
            }
        }

        Finish(store, answer, decisions, sent, notSent);
    }

    // The run's record, with whichever report is newest: one that came in while sending says more than the first answer.
    private static void Finish(WatchUpdateStore store, WatchAnswer answer, IReadOnlyList<WatchSendDecision> decisions, IReadOnlyList<WatchApp> sent, IReadOnlyList<WatchApp> notSent)
    {
        HeadLog.Info(Tag, WatchSendLines.Run(answer.Reach));
        var run = WatchRunRecord.Of(DateTimeOffset.UtcNow, answer, decisions, sent, notSent);
        store.LastRun = WatchReports.Latest is { } latest && answer.Reach == WatchReach.Answered ? run.WithReport(latest) : run;
    }

    // The watch with the installer: none paired (or no Wear OS on this phone) says nothing; paired but out of reach is said.
    private static async Task<(INode? Node, WatchReach Reach)> FindAsync(Context context, RunDeadline deadline)
    {
        var capabilities = WearableClass.GetCapabilityClient(context);
        ICapabilityInfo? paired;
        try
        {
            paired = await BoundedAsync(capabilities.GetCapabilityAsync(WatchIdentity.InstallerCapability, CapabilityClient.FilterAll), deadline, WatchSendPlan.DataLayerWait);
        }
        catch (ApiException e)
        {
            // Play services has no Wearable API here (no watch was ever set up): the same as no watch, and quiet.
            HeadLog.Info(Tag, $"WATCH run: no Wearable API ({nameof(ApiException)} {e.StatusCode})");
            return (null, WatchReach.NoWatch);
        }
        catch (TimeoutException)
        {
            // No answer about the pairings at all: said in the log, and as quiet as no watch on Settings.
            HeadLog.Warn(Tag, WatchSendLines.TimedOut("GetCapability"));
            return (null, WatchReach.NoWatch);
        }
        if (paired?.Nodes is not { Count: > 0 })
        {
            return (null, WatchReach.NoWatch);
        }

        try
        {
            var reachable = await BoundedAsync(capabilities.GetCapabilityAsync(WatchIdentity.InstallerCapability, CapabilityClient.FilterReachable), deadline, WatchSendPlan.DataLayerWait);
            var node = reachable?.Nodes?.OrderByDescending(candidate => candidate.IsNearby).FirstOrDefault();
            return node is null ? (null, WatchReach.Unreachable) : (node, WatchReach.Answered);
        }
        catch (Exception e) when (e is ApiException or TimeoutException)
        {
            // Paired, and no word on whether it is near: out of reach this run.
            HeadLog.Warn(Tag, WatchSendLines.TimedOut("GetCapability"));
            return (null, WatchReach.Unreachable);
        }
    }

    private static async Task<WatchAnswer> AskAsync(Context context, INode node, RunDeadline deadline)
    {
        var ticket = WatchReports.Mark();
        try
        {
            await BoundedAsync(WearableClass.GetMessageClient(context).SendMessageAsync(node.Id!, WatchIdentity.UpdateInstalledPath, InstalledCodec.Encode(new InstalledAsk())), deadline, WatchSendPlan.DataLayerWait);
            HeadLog.Info(Tag, UpdateLines.Sent(UpdateLines.InstalledContract));
        }
        catch (Exception e) when (e is Java.Lang.Exception or TimeoutException)
        {
            // The watch went out of reach between finding it and asking, or the ask never went: nothing is sent this run.
            HeadLog.Warn(Tag, UpdateLines.NotSent(UpdateLines.InstalledContract, e.GetType().Name));
            return WatchAnswer.Not(WatchReach.NoAnswer);
        }

        var read = await WatchReports.WaitAsync(ticket, deadline.Cut(DateTimeOffset.UtcNow, WatchSendPlan.AnswerWait));
        return read switch
        {
            null => WatchAnswer.Not(WatchReach.NoAnswer),
            { Refusal: ContractRefusal.Newer } => WatchAnswer.Not(WatchReach.Newer),
            { Message: InstalledReport report } => WatchAnswer.Answered(report),
            _ => WatchAnswer.Not(WatchReach.Unreadable),
        };
    }

    // The release's watch APKs as their sidecars describe them; the sidecar's form and the asset's name must agree (A4).
    private static async Task<List<(ReleaseAsset Asset, ReleaseApk Apk)>> OfferedAsync(GitHubReleaseClient client, ReleaseInfo release, RunDeadline deadline)
    {
        var offered = new List<(ReleaseAsset, ReleaseApk)>();
        foreach (var asset in release.ApkAssets.Where(asset => ApkForms.FromAssetName(asset.Name) == ApkForm.Watch))
        {
            if (deadline.Passed(DateTimeOffset.UtcNow))
            {
                HeadLog.Info(Tag, WatchSendLines.OutOfTime($"reading {asset.Name}’s sidecar"));
                break;
            }
            try
            {
                if (await client.ReadSidecarAsync(release, asset).WaitAsync(deadline.Left(DateTimeOffset.UtcNow)) is { } candidate)
                {
                    offered.Add((asset, new ReleaseApk(asset.Id, candidate with { Form = ApkForms.Agree(candidate.Form, ApkForm.Watch) })));
                }
                else
                {
                    // Every watch APK is published with a sidecar (A4); without one its version is unknown, so it waits.
                    HeadLog.Warn(Tag, $"WATCH {asset.Name}: no sidecar, so it isn’t sent");
                }
            }
            catch (Exception e) when (e is HttpRequestException or TimeoutException)
            {
                HeadLog.Info(Tag, $"WATCH {asset.Name}: the sidecar didn’t read ({e.GetType().Name}); it waits for the next run");
            }
        }
        return offered;
    }

    /// <returns>Whether the whole APK went down the channel.</returns>
    private static async Task<bool> SendOneAsync(Context context, HttpClient http, string token, ReleaseAsset asset, InstallCandidate candidate, INode node, RunDeadline deadline)
    {
        var path = Path.Combine(context.CacheDir!.AbsolutePath!, $"{CacheFilePrefix}{asset.Id}.apk");
        var channels = WearableClass.GetChannelClient(context);
        ChannelClient.IChannel? channel = null;
        // Everything this send does stops at the run's deadline: the download, the reads of the file, and the wait on the
        // channel's writes, which Play services may hold on a stalled connection.
        using var cancel = new CancellationTokenSource(deadline.Left(DateTimeOffset.UtcNow));
        try
        {
            await UpdateService.DownloadAsync(http, asset, token, path, cancel.Token);

            // The APK's own reading must be the sidecar's, or the watch is sent nothing it would have to refuse.
            if (ApkArchive.Read(context, path) is not { } read || read.Form != ApkForm.Watch
                || !string.Equals(read.Package, candidate.Package, StringComparison.Ordinal) || read.VersionCode != candidate.VersionCode)
            {
                HeadLog.Warn(Tag, WatchSendLines.NotSent(candidate.Package, candidate.VersionCode, "the APK isn’t what its sidecar says"));
                return false;
            }

            var ticket = WatchReports.Mark();
            channel = await BoundedAsync(channels.OpenChannelAsync(node.Id!, UpdatePaths.ApkPathFor(WatchSendPlan.TransferName(asset.Id, Guid.NewGuid()))), deadline, WatchSendPlan.DataLayerWait);
            var streaming = StreamAsync(channels, channel, path, candidate, deadline, cancel.Token);
            try
            {
                await streaming.WaitAsync(cancel.Token);
            }
            catch (OperationCanceledException)
            {
                // The channel is closed below, which ends a write still held; whatever the abandoned stream throws then
                // is read, not left unobserved.
                Observe(streaming);
                throw;
            }
            HeadLog.Info(Tag, UpdateLines.Sent(UpdateLines.ApkContract));

            // Closing the output ended the watch's input. The installer reports once the install has an outcome, and the
            // channel stays open until then (or the wait runs out) so nothing still in flight is cut off.
            await WatchReports.WaitForAsync(ticket, candidate.Package, candidate.VersionCode, deadline.Cut(DateTimeOffset.UtcNow, WatchSendPlan.ReportWait));
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TimeoutException or OperationCanceledException or Java.Lang.Exception)
        {
            // A send that broke off or ran out of time: its channel is closed below, so the watch's half-received file is
            // dropped.
            HeadLog.Warn(Tag, WatchSendLines.NotSent(candidate.Package, candidate.VersionCode, e.GetType().Name));
            return false;
        }
        finally
        {
            if (channel is not null)
            {
                await CloseQuietlyAsync(channels, channel, deadline);
            }
            DeleteCached(path);
        }
    }

    // The frame: the header (length and SHA-256 read from the file), then exactly the APK, on /update/apk/<transfer>.
    // Off the calling thread throughout: the file is read and hashed asynchronously, and each write waits on its own.
    private static async Task StreamAsync(ChannelClient channels, ChannelClient.IChannel channel, string path, InstallCandidate candidate, RunDeadline deadline, CancellationToken cancellation)
    {
        await using var apk = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkBytes, useAsync: true);
        var header = await UpdateApkFrame.DescribeAsync(candidate.Package, candidate.VersionCode, ApkForm.Watch, apk, cancellation);
        using var output = await BoundedAsync(channels.GetOutputStreamAsync(channel), deadline, WatchSendPlan.DataLayerWait);
        var frame = UpdateApkFrame.Header(header);
        await output.WriteAsync(frame, 0, frame.Length);
        var buffer = new byte[ChunkBytes];
        int count;
        while ((count = await apk.ReadAsync(buffer, cancellation)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            await output.WriteAsync(buffer, 0, count);
        }
        output.Flush();
        output.Close();
    }

    private static async Task CloseQuietlyAsync(ChannelClient channels, ChannelClient.IChannel channel, RunDeadline deadline)
    {
        try
        {
            // Asked even past the deadline; only the wait for its answer is cut.
            await BoundedAsync(channels.CloseAsync(channel), deadline, WatchSendPlan.CloseWait);
        }
        catch (Java.Lang.Exception e)
        {
            // Already closed by the watch or by a dropped connection: nothing is left to close.
            HeadLog.Info(Tag, $"CONTRACT {UpdateLines.ApkContract}: the channel was already closed ({e.GetType().Name})");
        }
        catch (TimeoutException)
        {
            // Play services closes it in its own time; the run does not wait past its own.
            HeadLog.Info(Tag, WatchSendLines.TimedOut("CloseChannel"));
        }
    }

    // A Data Layer call waited for at most its own wait, cut to what is left of the run: one that never answers cannot
    // hold the watch's gate. Play services goes on with the call; only the wait ends, and what the call throws after
    // that is read rather than left unobserved.
    private static async Task<T> BoundedAsync<T>(Task<T> call, RunDeadline deadline, TimeSpan wait)
    {
        try
        {
            return await call.WaitAsync(deadline.Cut(DateTimeOffset.UtcNow, wait));
        }
        catch (TimeoutException)
        {
            Observe(call);
            throw;
        }
    }

    private static async Task BoundedAsync(Task call, RunDeadline deadline, TimeSpan wait)
    {
        try
        {
            await call.WaitAsync(deadline.Cut(DateTimeOffset.UtcNow, wait));
        }
        catch (TimeoutException)
        {
            Observe(call);
            throw;
        }
    }

    // A task the run stopped waiting for: its fault, if it comes, is logged by type, never thrown into nothing.
    private static void Observe(Task abandoned) =>
        abandoned.ContinueWith(
            static task => HeadLog.Info(Tag, $"WATCH run: a call it stopped waiting for later failed ({task.Exception?.InnerException?.GetType().Name})"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static void DeleteCached(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException e)
        {
            HeadLog.Warn(Tag, $"The cached watch APK wasn’t deleted ({e.GetType().Name}); the next send replaces it");
        }
    }
}

/// <summary>
/// Flyleaf's ear for the installer's reports on <c>update/installed</c> (plan §2.5): the answer to the run's ask, and
/// the report the installer sends every connected phone after each settled install, so Settings' line learns of a tap on
/// the wrist without waiting six hours. Play services binds it; the filter names the one frozen path.
/// </summary>
[Service(Name = WatchIdentity.PhoneReportListenerName, Exported = true)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.UpdateInstalledPath)]
public sealed class WatchReportListener : WearableListenerService
{
    private const string Tag = "FlyleafWatch";  // not copy: a log tag

    public override void OnMessageReceived(IMessageEvent messageEvent)
    {
        if (!string.Equals(messageEvent.Path, WatchIdentity.UpdateInstalledPath, StringComparison.Ordinal))
        {
            return;
        }

        var read = InstalledCodec.Decode(messageEvent.GetData());
        HeadLog.Info(Tag, WatchSendLines.Heard(read));
        if (read.Message is InstalledAsk)
        {
            // Only a phone asks; a watch never does. Nothing to answer.
            return;
        }
        if (read.Message is InstalledReport report)
        {
            new WatchUpdateStore(this).UpdateReport(report);
        }
        WatchReports.Arrived(read);
    }
}

/// <summary>
/// The reports heard in this process, so a run waiting on its ask (or on one install) hears the listener's report. A
/// ticket is the count of reports heard so far; a wait returns the first report heard after it.
/// </summary>
internal static class WatchReports
{
    private static readonly Lock Gate = new();
    private static long arrivals;
    private static ContractRead<InstalledMessage>? last;
    private static TaskCompletionSource next = NewSignal();

    public static long Mark()
    {
        lock (Gate)
        {
            return arrivals;
        }
    }

    /// <summary>The newest report heard in this process, or null when the newest message heard would not read.</summary>
    public static InstalledReport? Latest
    {
        get
        {
            lock (Gate)
            {
                return last?.Message as InstalledReport;
            }
        }
    }

    public static void Arrived(ContractRead<InstalledMessage> read)
    {
        TaskCompletionSource heard;
        lock (Gate)
        {
            arrivals++;
            last = read;
            heard = next;
            next = NewSignal();
        }
        heard.TrySetResult();
    }

    /// <summary>The first message heard after <paramref name="ticket"/>, or null when none came in time.</summary>
    public static async Task<ContractRead<InstalledMessage>?> WaitAsync(long ticket, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            Task signal;
            lock (Gate)
            {
                if (arrivals > ticket)
                {
                    return last;
                }
                signal = next.Task;
            }

            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero || await Task.WhenAny(signal, Task.Delay(remaining)) != signal)
            {
                return null;
            }
        }
    }

    /// <summary>Waits until a report heard after <paramref name="ticket"/> says something about this build, or the time
    /// runs out.</summary>
    public static async Task WaitForAsync(long ticket, string package, long versionCode, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var seen = ticket;
        while (clock.Elapsed < timeout)
        {
            if (await WaitAsync(seen, timeout - clock.Elapsed) is not { } read)
            {
                return;
            }
            seen = Mark();
            if (read.Message is InstalledReport report && Names(report, package, versionCode))
            {
                return;
            }
        }
    }

    private static bool Names(InstalledReport report, string package, long versionCode) =>
        report.Results.Any(result => string.Equals(result.Package, package, StringComparison.Ordinal) && result.VersionCode == versionCode)
        || report.Apps.Any(app => string.Equals(app.Package, package, StringComparison.Ordinal) && app.VersionCode >= versionCode);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// The watch half's own record in Flyleaf's preferences: what it last sent each app (so the phone's own processed set,
/// which marks every watch asset NotForThisDevice, never hides a watch APK from it), and the last run for Settings.
/// </summary>
internal sealed class WatchUpdateStore
{
    private static readonly Lock Gate = new();

    private readonly ISharedPreferences preferences;

    public WatchUpdateStore(Context context)
    {
        preferences = context.GetSharedPreferences(WatchIdentity.PhonePreferences, FileCreationMode.Private)!;
    }

    public IReadOnlyList<SentApk> Sent
    {
        get
        {
            lock (Gate)
            {
                return WatchSentRecord.Decode(preferences.GetString(WatchIdentity.SentKey, null));
            }
        }
    }

    public void RecordSent(SentApk sent)
    {
        lock (Gate)
        {
            var record = WatchSentRecord.Record(WatchSentRecord.Decode(preferences.GetString(WatchIdentity.SentKey, null)), sent);
            preferences.Edit()!.PutString(WatchIdentity.SentKey, WatchSentRecord.Encode(record))!.Apply();
        }
    }

    public WatchRunRecord? LastRun
    {
        get
        {
            lock (Gate)
            {
                return WatchRunCodec.Decode(preferences.GetString(WatchIdentity.LastRunKey, null));
            }
        }
        set
        {
            lock (Gate)
            {
                var editor = preferences.Edit()!;
                (value is null ? editor.Remove(WatchIdentity.LastRunKey) : editor.PutString(WatchIdentity.LastRunKey, WatchRunCodec.Encode(value)))!.Apply();
            }
        }
    }

    /// <summary>A report that came after the run: the run keeps its time and reach and takes the watch's newer word.</summary>
    public void UpdateReport(InstalledReport report)
    {
        lock (Gate)
        {
            if (WatchRunCodec.Decode(preferences.GetString(WatchIdentity.LastRunKey, null)) is { } run)
            {
                preferences.Edit()!.PutString(WatchIdentity.LastRunKey, WatchRunCodec.Encode(run.WithReport(report)))!.Apply();
            }
        }
    }
}
