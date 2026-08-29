using Android.Runtime;
using Java.Interop;

namespace Score;

/// <summary>
/// Calls exactly the handful of static, no-argument JVM methods Xamarin's binding
/// generator dropped from Codex.Tidal.Binding — the multi-file-class and top-level-
/// function patterns it routinely fails to expose, the same class of gap that
/// silently dropped <c>Player</c>'s own constructor until <c>Square.OkHttp3</c> was
/// added to satisfy one of its parameters (see that commit). Reaching past the
/// binding via raw JNI is the fallback once a member turns out not to be there at
/// all: <see cref="TidalCredentialsProvider"/> needs the coroutine-suspend sentinel,
/// and there is no C# binding for it to call.
///
/// Built on <see cref="Java.Interop.JniEnvironment"/> — the modern, properly-public
/// low-level API the generated bindings themselves use internally (e.g.
/// <c>_members.InstanceMethods.InvokeNonvirtualObjectMethod</c>), not the older
/// <see cref="Android.Runtime.JNIEnv"/>, whose reference assembly narrows most members
/// to internal-only despite the runtime assembly showing them as public — a curated
/// API surface, not a bug, but one that makes JNIEnv unusable from a normal project.
///
/// Every class/method/signature triple used here is verified against the actual
/// compiled <c>.class</c> file (<c>javap</c> on the real <c>.aar</c> this project's
/// NuGet packages resolve to) rather than assumed from documentation or memory — see
/// the comment at each call site for exactly how. That only proves the JVM really has
/// a member by that name and shape; it proves nothing about whether calling it this
/// way actually behaves correctly, which is why every step below logs loudly rather
/// than failing silently — a wrong class or method name needs to show up in Logcat on
/// the very first run, not manifest as a mysterious hang three calls later.
/// </summary>
internal static class TidalKotlinBridge
{
    private const string LogTag = "TidalKotlinBridge";

    /// <summary>
    /// The sentinel a Kotlin suspend function's JVM ABI requires returning when it
    /// will complete asynchronously (via <c>Continuation.resumeWith</c> later) rather
    /// than synchronously. Confirmed by decompiling the actual
    /// <c>kotlin-stdlib</c> .aar this project resolves
    /// (<c>Xamarin.Kotlin.StdLib</c> 2.4.0.1) with <c>javap</c>:
    ///
    /// <code>
    /// public final class kotlin.coroutines.intrinsics.IntrinsicsKt__IntrinsicsKt
    ///     extends kotlin.coroutines.intrinsics.IntrinsicsKt__IntrinsicsJvmKt {
    ///   public static final java.lang.Object getCOROUTINE_SUSPENDED();
    /// }
    /// </code>
    ///
    /// The owning class is deliberately <c>IntrinsicsKt__IntrinsicsKt</c>, not the
    /// <c>IntrinsicsKt</c> facade Kotlin source code calls this through — Kotlin's
    /// multi-file-class split compiles <c>IntrinsicsKt</c> down to a class with no
    /// static methods of its own (confirmed the same way); source-level "inherited
    /// static method" resolution is a compiler convenience with no JVM bytecode
    /// equivalent, so a raw JNI call has to target the class that actually declares
    /// the method.
    /// </summary>
    public static Java.Lang.Object? CoroutineSuspended() => Cached(ref coroutineSuspended, () =>
        InvokeStaticObjectMethod<Java.Lang.Object>(
            "kotlin/coroutines/intrinsics/IntrinsicsKt__IntrinsicsKt",
            "getCOROUTINE_SUSPENDED",
            "()Ljava/lang/Object;"));

    /// <summary>
    /// A <c>Flow</c> that never emits and completes immediately — for
    /// <c>CredentialsProvider.Bus</c>, which Score has no push-based credential
    /// updates to feed. Unlike <see cref="CoroutineSuspended"/>, this one <b>is</b> on
    /// the facade class Kotlin source calls it through; confirmed the same way
    /// (<c>javap</c> on the real <c>kotlinx-coroutines-core</c> .aar,
    /// <c>Xamarin.KotlinX.Coroutines.Core.Jvm</c> 1.10.2.1):
    ///
    /// <code>
    /// public final class kotlinx.coroutines.flow.FlowKt {
    ///   public static final &lt;T&gt; kotlinx.coroutines.flow.Flow&lt;T&gt; emptyFlow();
    /// }
    /// </code>
    /// </summary>
    public static Xamarin.KotlinX.Coroutines.Flow.IFlow? EmptyFlow() => Cached(ref emptyFlow, () =>
        InvokeStaticObjectMethod<Xamarin.KotlinX.Coroutines.Flow.IFlow>(
            "kotlinx/coroutines/flow/FlowKt",
            "emptyFlow",
            "()Lkotlinx/coroutines/flow/Flow;"));

    // Both are process-lifetime singletons on the Kotlin side (COROUTINE_SUSPENDED is
    // a fixed marker object; an empty Flow carries no state), so the JNI round trip
    // only has to happen once each.
    private static Java.Lang.Object? coroutineSuspended;
    private static Xamarin.KotlinX.Coroutines.Flow.IFlow? emptyFlow;
    private static readonly object gate = new();

    private static T? Cached<T>(ref T? cache, Func<T?> resolve) where T : class
    {
        lock (gate)
        {
            return cache ??= resolve();
        }
    }

    private static T? InvokeStaticObjectMethod<T>(string className, string methodName, string signature)
        where T : class, IJavaObject
    {
        try
        {
            Android.Util.Log.Debug(LogTag, $"FindClass(\"{className}\")");
            if (!JniEnvironment.Types.TryFindClass(className, out var type))
            {
                Android.Util.Log.Error(LogTag, $"FindClass returned null for \"{className}\" — class not on the " +
                    "classpath at all, or the name is wrong. Nothing further to try.");
                return null;
            }

            try
            {
                Android.Util.Log.Debug(LogTag, $"GetStaticMethodID(\"{className}\", \"{methodName}\", \"{signature}\")");
                var method = JniEnvironment.StaticMethods.GetStaticMethodID(type, methodName, signature);

                Android.Util.Log.Debug(LogTag, "CallStaticObjectMethod(...)");
                var resultRef = JniEnvironment.StaticMethods.CallStaticObjectMethod(type, method);
                if (!resultRef.IsValid)
                {
                    Android.Util.Log.Warn(LogTag, $"\"{className}.{methodName}\" returned null (not necessarily " +
                        "wrong — check whether null is a valid result for this specific method).");
                    return null;
                }

                // TransferLocalRef, not disposed separately: ownership of the local
                // reference CallStaticObjectMethod handed back moves to the managed
                // wrapper here, the same pattern the generated bindings themselves
                // use (e.g. Java.Lang.Object.GetObject<T>(__rm.Handle,
                // JniHandleOwnership.TransferLocalRef) in Com.Tidal.Sdk.Auth.TidalAuth.cs)
                // — disposing resultRef ourselves as well would release it twice.
                var result = Java.Lang.Object.GetObject<T>(resultRef.Handle, JniHandleOwnership.TransferLocalRef);
                Android.Util.Log.Debug(LogTag,
                    $"\"{className}.{methodName}\" -> {result} (expected type {typeof(T).Name})");
                return result;
            }
            finally
            {
                JniObjectReference.Dispose(ref type);
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Error(LogTag, $"\"{className}.{methodName}{signature}\" threw: {e}");
            return null;
        }
    }
}
