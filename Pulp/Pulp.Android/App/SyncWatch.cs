using Android.App;
using Pulp.Domain.Household;

namespace Pulp.App;

/// <summary>
/// Lets a screen that shows the household read it again when the sync pass changed it: subscribe in
/// Resume, unsubscribe in Pause, so a screen that is not showing is never held by the services (which
/// live for the process). The pass runs off the UI thread, so the refresh is posted to it, and only when
/// <see cref="SyncRefresh.Needed"/> says a pass brought records home or the status moved — not on every
/// quiet pass, which would redraw a screen under a finger for nothing.
/// </summary>
public sealed class SyncWatch
{
    private readonly Activity host;
    private readonly PulpServices services;
    private readonly Action refresh;
    private HouseholdSyncStatus? last;

    public SyncWatch(Activity host, PulpServices services, Action refresh)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
    }

    public void Resume()
    {
        Volatile.Write(ref last, services.SyncStatus);
        services.SyncStatusChanged -= OnChanged;
        services.SyncStatusChanged += OnChanged;
    }

    public void Pause() => services.SyncStatusChanged -= OnChanged;

    private void OnChanged(object? sender, EventArgs e)
    {
        var next = services.SyncStatus;
        var previous = Interlocked.Exchange(ref last, next);
        if (!SyncRefresh.Needed(previous, next))
        {
            return;
        }

        host.RunOnUiThread(() =>
        {
            if (!host.IsFinishing && !host.IsDestroyed)
            {
                refresh();
            }
        });
    }
}
