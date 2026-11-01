using Codex.Ui;
using Android.App;
using Android.Content;
using Android.Media;
using Android.Media.Session;
using Codex.Domain.Apps;
using MediaController = Android.Media.Session.MediaController;

// Pulp declares RECEIVE_SESSIONS at signature level; Flyleaf must hold it for the relay to land.
[assembly: UsesPermission(PlaybackSessionHandoff.Permission)]

namespace Flyleaf;

/// <summary>
/// Reads other apps' media sessions through <see cref="TileNotificationListener"/>'s listener grant
/// (finding 2.3: an enabled notification listener may call
/// <c>MediaSessionManager.GetActiveSessions</c>) and relays the ones Pulp named to Pulp. What leaves
/// the phone is decided by the pure <see cref="PlaybackSessionRelay"/> against the stored
/// <see cref="HiddenApps"/> set; this class only reads the platform and sends. An explicit broadcast
/// naming Pulp's receiver, under the signature permission. Not a queue and not a fallback: Pulp absent
/// (her unrestricted phone has no Flyleaf at all) drops the send.
/// </summary>
internal sealed class PlaybackSessionWatcher
{
    private readonly Context context;
    private readonly ComponentName listener;
    private readonly PlaybackSessionRelay relay = new();
    private readonly Dictionary<string, SessionCallback> watching = new(StringComparer.Ordinal);
    private MediaSessionManager? manager;
    private SessionsChanged? changed;
    private HiddenApps cachedNamed = HiddenApps.None;
    private int cachedVersion = -1;

    public PlaybackSessionWatcher(Context context, ComponentName listener)
    {
        this.context = context;
        this.listener = listener;
    }

    public void Start()
    {
        try
        {
            manager = (MediaSessionManager?)context.GetSystemService(Context.MediaSessionService);
            if (manager is null)
            {
                return;
            }

            changed = new SessionsChanged(this);
            manager.AddOnActiveSessionsChangedListener(changed, listener);
            Refresh(manager.GetActiveSessions(listener));
        }
        catch (Java.Lang.SecurityException)
        {
            // Listener access not granted yet (pre-provisioning); nothing to read.
        }
    }

    public void Stop()
    {
        try
        {
            if (manager is not null && changed is not null)
            {
                manager.RemoveOnActiveSessionsChangedListener(changed);
            }
        }
        catch (Java.Lang.SecurityException)
        {
        }

        foreach (var callback in watching.Values)
        {
            callback.Detach();
        }

        watching.Clear();
        relay.ForgetAll();
        changed = null;
    }

    /// <summary>The stored set, parsed once and reloaded only when the receiver has written a new one.</summary>
    private HiddenApps Named()
    {
        var current = HiddenAppsStore.Version;
        if (current != cachedVersion)
        {
            cachedNamed = new HiddenAppsStore(context).Load();
            cachedVersion = current;
        }

        return cachedNamed;
    }

    private void Refresh(IList<MediaController>? controllers)
    {
        var named = Named();
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var controller in controllers ?? [])
        {
            var package = controller.PackageName;
            if (package is null || !named.Contains(package))
            {
                continue;
            }

            live.Add(package);
            watching.TryGetValue(package, out var existing);
            var sameToken = existing is not null && Equals(existing.Token, controller.SessionToken);
            var decision = PlaybackSessionWatch.Decide(existing is not null, true, sameToken);
            if (decision != SessionWatch.Keep)
            {
                // The app recreated its session: the old callback and controller are stale.
                existing?.Detach();
                var callback = new SessionCallback(this, controller, package);
                controller.RegisterCallback(callback);
                watching[package] = callback;
            }

            Offer(controller, package);
        }

        foreach (var gone in watching.Keys.Where(package => !live.Contains(package)).ToList())
        {
            watching[gone].Closed();
            watching.Remove(gone);
        }
    }

    private void Offer(MediaController controller, string package, bool closed = false)
    {
        var reading = Read(controller, package, closed);
        if (reading is null)
        {
            return;
        }

        // A locked hand-off (D288) watches every reading of its own app, before the relay's change filter.
        LockedSessionController.OnReading(context, reading, Send);

        var named = Named();
        var toSend = closed ? relay.Close(reading, named) : relay.Offer(reading, named);
        if (toSend is not null)
        {
            Send(toSend);
        }
    }

    private static PlaybackSessionReading? Read(MediaController controller, string package, bool closed)
    {
        var state = controller.PlaybackState;
        var metadata = controller.Metadata;
        var position = state?.Position ?? 0L;
        if (state is not null && state.State == PlaybackStateCode.Playing)
        {
            // Position is as of the last update; carry it forward to now at the playback speed.
            var elapsed = Android.OS.SystemClock.ElapsedRealtime() - state.LastPositionUpdateTime;
            position += (long)(elapsed * state.PlaybackSpeed);
        }

        var duration = metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0L;
        var title = metadata?.GetString(MediaMetadata.MetadataKeyTitle);
        var mapped = closed ? PlaybackSessionState.Stopped : state?.State switch
        {
            PlaybackStateCode.Playing or PlaybackStateCode.Buffering => PlaybackSessionState.Playing,
            PlaybackStateCode.Paused => PlaybackSessionState.Paused,
            _ => PlaybackSessionState.Stopped,
        };

        // Without a published state there is nothing to say.
        if (state is null && !closed)
        {
            return null;
        }

        return new PlaybackSessionReading(package, title, Math.Max(0L, position), Math.Max(0L, duration), mapped);
    }

    private void Send(PlaybackSessionReading reading)
    {
        try
        {
            var intent = new Intent(PlaybackSessionHandoff.Action);
            intent.SetComponent(new ComponentName(PlaybackSessionHandoff.Package, PlaybackSessionHandoff.Receiver));
            intent.PutExtra(PlaybackSessionHandoff.SessionExtra, PlaybackSessionCodec.Encode(reading));
            context.SendBroadcast(intent, PlaybackSessionHandoff.Permission);
        }
        catch (Exception e)
        {
            // Dropped on purpose: Pulp not being on this phone is an ordinary case.
            Android.Util.Log.Info("Flyleaf", $"Playback-session relay to Pulp did not land: {e.Message}");
        }
    }

    private sealed class SessionsChanged : Java.Lang.Object, MediaSessionManager.IOnActiveSessionsChangedListener
    {
        private readonly PlaybackSessionWatcher owner;

        public SessionsChanged(PlaybackSessionWatcher owner) => this.owner = owner;

        public void OnActiveSessionsChanged(IList<MediaController>? controllers) => owner.Refresh(controllers);
    }

    private sealed class SessionCallback : MediaController.Callback
    {
        private readonly PlaybackSessionWatcher owner;
        private readonly MediaController controller;
        private readonly string package;

        public SessionCallback(PlaybackSessionWatcher owner, MediaController controller, string package)
        {
            this.owner = owner;
            this.controller = controller;
            this.package = package;
        }

        public object? Token => controller.SessionToken;

        public override void OnPlaybackStateChanged(PlaybackState? state) => owner.Offer(controller, package);

        public override void OnMetadataChanged(MediaMetadata? metadata) => owner.Offer(controller, package);

        public override void OnSessionDestroyed()
        {
            owner.Offer(controller, package, closed: true);
            if (owner.watching.TryGetValue(package, out var current) && ReferenceEquals(current, this))
            {
                owner.watching.Remove(package);
            }
        }

        /// <summary>The session left the active list without being destroyed: say it stopped.</summary>
        public void Closed()
        {
            Detach();
            owner.Offer(controller, package, closed: true);
        }

        public void Detach()
        {
            try
            {
                controller.UnregisterCallback(this);
            }
            catch (Exception)
            {
            }
        }
    }
}
