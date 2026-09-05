using Google.Common.Util.Concurrent;
using Java.Lang;
using Java.Util.Concurrent;

namespace Score;

/// <summary>
/// A <see cref="IListenableFuture"/> that is already done — for
/// <see cref="TidalMediaSessionPlayer"/>'s <c>Handle*</c> overrides, which
/// <see cref="AndroidX.Media3.Common.SimpleBasePlayer"/> requires return one even
/// though the actual work (a call through <see cref="TidalPlaybackBridge"/>) always
/// completes synchronously before returning.
///
/// Media3's own Java samples reach for Guava's <c>Futures.immediateVoidFuture()</c>
/// for exactly this, but the bound <c>Xamarin.Google.Guava.ListenableFuture</c>
/// package (confirmed by inspecting the actual DLL) binds only the
/// <c>ListenableFuture</c> interface itself, not the <c>Futures</c> factory class —
/// so there is nothing to call. This is the standard fallback for that gap: a
/// minimal, already-complete <see cref="IFuture"/> implementation, which is the
/// entire interface surface actually needed here.
/// </summary>
internal sealed class ImmediateFuture : Java.Lang.Object, IListenableFuture
{
    public static readonly ImmediateFuture Instance = new();

    // Listeners are called synchronously and immediately, matching Guava's own
    // documented behaviour for an already-done future's listener registration.
    public void AddListener(IRunnable listener, IExecutor executor) => executor.Execute(listener);

    public bool Cancel(bool mayInterruptIfRunning) => false;

    public bool IsCancelled => false;

    public bool IsDone => true;

    public Java.Lang.Object? Get() => null;

    public Java.Lang.Object? Get(long timeout, Java.Util.Concurrent.TimeUnit? unit) => null;
}
