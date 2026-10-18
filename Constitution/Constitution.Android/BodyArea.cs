using System.Collections.Concurrent;
using Android.App;
using Android.Content.PM;
using Android.Views;
using Constitution.Body;
using Constitution.BodyTab;
using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Health;
using Constitution.Domain.Voice;
using Constitution.Health;
using Constitution.Infrastructure.Voice;

namespace Constitution;

/// <summary>
/// The Body tab (C-7.3.3, book page 16) and, as a state the shell hosts, a signal's page (C-7.3.4). The area
/// owns what they show: the readings and Health Connect's state as of the last read, the window both pages
/// share, and the voiced sentences. The shell owns the frame and which page is turned to. Every decision is in
/// <see cref="BodyTabModel"/> and <see cref="SignalPageModel"/>; this reads, hands them over, and paints.
///
/// <para>The readings are read off the UI thread and the page says so in a sentence while it waits. Health
/// Connect's state is read at once in <see cref="Resume"/>, before the open-time reconcile can rewrite the saved
/// choices, so a permission taken away since the last open is seen for what it is. A voiced sentence is composed
/// off the UI thread and replaces the plain one in place when it arrives.</para>
///
/// <para>Nothing here leaves the phone except the facts a seat is handed, in words: no reading, no chart and no
/// number goes to Flyleaf, the notice board or a log.</para>
/// </summary>
public sealed class BodyArea : ConstitutionAreaBase
{
    private static readonly TimeSpan[] FreshChecks = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9)];

    private readonly ActivityHealthPermissionRequester requester;
    private readonly ConcurrentDictionary<string, SeatLine> composed = new();
    private readonly object loadGate = new();

    private BodySnapshot? snapshot;
    private BodyWindow window = BodyWindows.Default;
    private BodyTabPage? tabPage;
    private SignalPageView? signalPage;
    private BodySignal? signalShown;
    private CancellationTokenSource composing = new();
    private int loadNumber;

    public BodyArea(Activity host)
        : base(host, "body", Resource.Drawable.areabody)
    {
        requester = new ActivityHealthPermissionRequester(host);
    }

    /// <summary>Raised when a chart is tapped: the shell turns to that signal's page.</summary>
    public event Action<BodySignal>? SignalRequested;

    protected override string LoadingSentence => "Reading the charts.";

    // ---- the tab ----

    protected override View BuildPage()
    {
        composing.Cancel();
        composing = new CancellationTokenSource();
        signalPage = null;
        var current = snapshot;
        if (current is null)
        {
            // Resume has not finished its first read: the base shows the loading sentence while Loading is set.
            return PageFurniture.StatusSentence(Host, LoadingSentence);
        }

        var page = new BodyTabPage(Host, window, Tab.Hue, OnWindowChosen, signal => SignalRequested?.Invoke(signal));
        tabPage = page;
        ShowTab(page, current, composing.Token);
        return page;
    }

    private void OnWindowChosen(BodyWindow chosen)
    {
        window = chosen;
        if (snapshot is { } current && tabPage is { } page)
        {
            ShowTab(page, current, composing.Token);
        }

        if (snapshot is { } now && signalPage is { } signalView && signalShown is { } signal)
        {
            ShowSignal(signalView, now, signal, composing.Token);
        }
    }

    private void ShowTab(BodyTabPage page, BodySnapshot current, CancellationToken token)
    {
        var model = BodyTabModel.Build(current.Inputs(window));
        var request = model.Monthly;
        page.Show(model, request is null ? null : LineFor(request, current.Today));
        if (request is not null)
        {
            Compose(request, current.Today, line => page.ShowSentence(line), token);
        }
    }

    // ---- a signal's page, hosted by the shell ----

    /// <summary>The header title the shell shows on a signal's page.</summary>
    public string SignalTitle(BodySignal signal) => BodySignals.Name(signal);

    /// <summary>The signal's page on the crinkled ground, or the loading sentence if the readings are not in yet.</summary>
    public View BuildSignalView(BodySignal signal)
    {
        composing.Cancel();
        composing = new CancellationTokenSource();
        tabPage = null;
        signalShown = signal;
        if (snapshot is not { } current)
        {
            return GroundPainter.Paint(PageFurniture.StatusSentence(Host, LoadingSentence));
        }

        var page = new SignalPageView(Host, window, Tab.Hue, OnWindowChosen);
        signalPage = page;
        ShowSignal(page, current, signal, composing.Token);
        return GroundPainter.Paint(page);
    }

    /// <summary>The pinned bar of a signal's page: Allow in Health Connect while the chart is waiting on it, otherwise none.</summary>
    public View? BuildSignalBar(BodySignal signal) =>
        snapshot is { } current
            ? SignalBar.Build(Host, SignalPageModel.Build(current.Inputs(window), signal), () => _ = AllowAsync(signal))
            : null;

    private void ShowSignal(SignalPageView page, BodySnapshot current, BodySignal signal, CancellationToken token)
    {
        var model = SignalPageModel.Build(current.Inputs(window), signal);
        var request = model.Seat;
        page.Show(model, request is null ? null : LineFor(request, current.Today));
        if (request is not null)
        {
            Compose(request, current.Today, line => page.ShowSentence(line), token);
        }
    }

    /// <summary>The shell forwards the platform's permission answer here.</summary>
    public void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults) =>
        requester.OnResult(requestCode, permissions, grantResults);

    // ---- reading ----

    public override void Resume()
    {
        // Health Connect's state first, synchronously: the open-time reconcile that follows rewrites the saved choices.
        var access = BodyAccess.Read(Host);
        if (snapshot is null)
        {
            Loading = true;
        }

        LoadAsync(access);
    }

    public override void Pause() => composing.Cancel();

    private void LoadAsync(BodyAccess access)
    {
        var number = Interlocked.Increment(ref loadNumber);
        _ = Task.Run(async () =>
        {
            BodyServices? services = null;
            try
            {
                services = BodyServices.Create(Host);
                var loaded = new BodySnapshot(services.Store.All(), access, DateOnly.FromDateTime(DateTime.Now));
                loaded.StateTheBed(Host);
                Adopt(loaded, number);
                await WatchForTheSync(services, number);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Logged by type only. The page keeps what it had; with nothing yet it shows the charts that are waiting,
                // and the next open reads again.
                (services?.Log ?? new BodyLog(Host)).Outcome($"body page read failed: {e.GetType().Name}");
                Adopt(snapshot ?? new BodySnapshot([], access, DateOnly.FromDateTime(DateTime.Now)), number);
            }
        });
    }

    private void Adopt(BodySnapshot loaded, int number)
    {
        lock (loadGate)
        {
            if (number != Volatile.Read(ref loadNumber))
            {
                return;
            }

            var same = snapshot is { } before && before.Signature() == loaded.Signature();
            snapshot = loaded;
            Loading = false;
            if (!same)
            {
                Host.RunOnUiThread(RaiseChanged);
            }
        }
    }

    // The open-time sync runs on its own after Resume returns. When it finishes it writes its status; read again then.
    private async Task WatchForTheSync(BodyServices services, int number)
    {
        var seen = services.Status.Last()?.At;
        foreach (var wait in FreshChecks)
        {
            await Task.Delay(wait);
            if (number != Volatile.Read(ref loadNumber))
            {
                return;
            }

            if (services.Status.Last()?.At != seen)
            {
                LoadAsync(BodyAccess.Read(Host));
                return;
            }
        }
    }

    // ---- allowing a signal ----

    /// <summary>
    /// The bar's action. Asks Health Connect for the signal; whatever the answer, saves it (which publishes it to the
    /// vault), reads at once if it was granted, and lets the page redraw itself in its new state. A request the platform
    /// no longer shows as a dialog opens Health Connect's own page instead.
    /// </summary>
    private async Task AllowAsync(BodySignal signal)
    {
        var log = new BodyLog(Host);
        try
        {
            var before = snapshot?.Access.Choices.Of(signal) ?? SignalChoice.NotAsked;
            var granted = await requester.RequestAsync(signal, CancellationToken.None);
            if (!granted && before != SignalChoice.NotAsked)
            {
                requester.OpenHealthConnectSettings(log);
            }

            await Task.Run(async () =>
            {
                var services = BodyServices.Create(Host);
                var choices = services.Choices.Load().With(signal, granted ? SignalChoice.Granted : SignalChoice.Asked);
                await services.Choices.SaveAsync(choices, CancellationToken.None);
                if (granted)
                {
                    await services.Sync.OpenTimeReadsAsync(CancellationToken.None);
                }
            });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Outcome($"health allow failed: {e.GetType().Name}");
        }

        LoadAsync(BodyAccess.Read(Host));
    }

    // ---- voice ----

    private SeatLine LineFor(SeatRequest request, DateOnly today) =>
        composed.TryGetValue(SeatLineKey.For(request, today).ToString(), out var line)
            ? line
            : new SeatLine(request.Seat, request.Fallback, IsVoiced: false);

    private void Compose(SeatRequest request, DateOnly today, Action<SeatLine> show, CancellationToken token)
    {
        var key = SeatLineKey.For(request, today).ToString();
        if (composed.ContainsKey(key))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var line = await BodySeats.ComposeAsync(Host, request, token);
                if (!line.IsVoiced || token.IsCancellationRequested)
                {
                    return;
                }

                composed[key] = line;
                Host.RunOnUiThread(() => show(line));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The page keeps its plain sentence. The trail gets the seat's id and the kind of failure, nothing else.
                new BodyLog(Host).Outcome($"voice seat {VoicedOccasions.For(request.Seat).Id}: {e.GetType().Name}");
            }
        });
    }
}
