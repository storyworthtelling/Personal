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

    /// <summary>
    /// The raw packed <c>long</c> a <c>kotlin.time.Duration</c> inline value class
    /// erases to at the JVM level for the given number of milliseconds — needed
    /// because <see cref="BufferConfiguration"/>/<see cref="AssetTimeoutConfig"/>/
    /// <see cref="StreamingApiTimeoutConfig"/> are Kotlin data classes whose only
    /// public constructors take <c>Duration</c> parameters already erased to this raw
    /// form (confirmed by <c>javap</c>: e.g.
    /// <c>BufferConfiguration(long, long, long, long, long, long,
    /// DefaultConstructorMarker)</c>), and Kotlin's packing of that raw value
    /// (whether it stores nanoseconds or milliseconds, and in which bit) is
    /// deliberately undocumented, internal representation — not something safe to
    /// hand-encode. Rather than guess that encoding, this calls Kotlin's own runtime
    /// converter and uses whatever it returns verbatim, the same way
    /// <c>20.seconds</c> in TIDAL's own Kotlin source compiles down to a call to this
    /// exact function:
    ///
    /// <code>
    /// public final class kotlin.time.DurationKt {
    ///   public static final long toDuration(long, kotlin.time.DurationUnit);
    /// }
    /// public final class kotlin.time.DurationUnit extends java.lang.Enum&lt;...&gt; {
    ///   public static final kotlin.time.DurationUnit MILLISECONDS;
    /// }
    /// </code>
    ///
    /// (both confirmed by <c>javap</c> on the real <c>kotlin-stdlib</c> .aar this
    /// project resolves).
    /// </summary>
    public static long? DurationOfMillis(long millis)
    {
        var unit = Cached(ref millisecondsUnit, () => GetStaticObjectField<Java.Lang.Object>(
            "kotlin/time/DurationUnit", "MILLISECONDS", "Lkotlin/time/DurationUnit;"));
        if (unit is null)
        {
            Android.Util.Log.Error(LogTag, "DurationUnit.MILLISECONDS lookup failed; cannot convert " +
                $"{millis}ms to a raw Duration — nothing sane to return.");
            return null;
        }

        try
        {
            Android.Util.Log.Debug(LogTag, $"FindClass(\"kotlin/time/DurationKt\")");
            if (!JniEnvironment.Types.TryFindClass("kotlin/time/DurationKt", out var type))
            {
                Android.Util.Log.Error(LogTag, "FindClass returned null for \"kotlin/time/DurationKt\".");
                return null;
            }

            try
            {
                const string signature = "(JLkotlin/time/DurationUnit;)J";
                Android.Util.Log.Debug(LogTag, $"GetStaticMethodID(\"kotlin/time/DurationKt\", \"toDuration\", " +
                    $"\"{signature}\")");
                var method = JniEnvironment.StaticMethods.GetStaticMethodID(type, "toDuration", signature);

                Android.Util.Log.Debug(LogTag, $"CallStaticLongMethod(toDuration({millis}, MILLISECONDS))");
                unsafe
                {
                    var args = stackalloc JniArgumentValue[2];
                    args[0] = new JniArgumentValue(millis);
                    args[1] = new JniArgumentValue(((Java.Lang.Object)unit).Handle);
                    var raw = JniEnvironment.StaticMethods.CallStaticLongMethod(type, method, args);
                    Android.Util.Log.Debug(LogTag, $"toDuration({millis}ms) -> raw {raw}");
                    return raw;
                }
            }
            finally
            {
                JniObjectReference.Dispose(ref type);
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Error(LogTag, $"kotlin/time/DurationKt.toDuration threw: {e}");
            return null;
        }
    }

    /// <summary>
    /// Constructs an instance of a Kotlin data class whose only public constructor —
    /// confirmed by <c>javap</c> at each call site — takes <paramref name="millisArgs"/>
    /// raw <c>Duration</c> longs (see <see cref="DurationOfMillis"/>) followed by a
    /// trailing, always-null <c>DefaultConstructorMarker</c>. That marker-typed
    /// overload is not Kotlin's synthetic default-argument dispatcher (no bitmask
    /// parameter) — decompiling its bytecode shows it does nothing but forward every
    /// argument straight to the class's real private constructor, discarding the
    /// marker outright, so passing null for it is not a guess about unused defaults;
    /// it is calling the exact constructor TIDAL's own defaults resolve to, with every
    /// value supplied explicitly.
    /// </summary>
    public static T? NewDurationDataClass<T>(string className, string ctorSignature, params long[] millisArgs)
        where T : class, IJavaObject
    {
        var raw = new long[millisArgs.Length];
        for (var i = 0; i < millisArgs.Length; i++)
        {
            if (DurationOfMillis(millisArgs[i]) is not { } value)
            {
                Android.Util.Log.Error(LogTag,
                    $"Duration conversion failed for argument {i} ({millisArgs[i]}ms); aborting {className} construction.");
                return null;
            }

            raw[i] = value;
        }

        try
        {
            Android.Util.Log.Debug(LogTag, $"FindClass(\"{className}\")");
            if (!JniEnvironment.Types.TryFindClass(className, out var type))
            {
                Android.Util.Log.Error(LogTag, $"FindClass returned null for \"{className}\".");
                return null;
            }

            try
            {
                Android.Util.Log.Debug(LogTag, $"GetMethodID(\"{className}\", \"<init>\", \"{ctorSignature}\")");
                var ctor = JniEnvironment.InstanceMethods.GetMethodID(type, "<init>", ctorSignature);

                Android.Util.Log.Debug(LogTag,
                    $"NewObject({className}({string.Join(", ", raw)}, null /* marker */))");
                unsafe
                {
                    var args = stackalloc JniArgumentValue[raw.Length + 1];
                    for (var i = 0; i < raw.Length; i++)
                    {
                        args[i] = new JniArgumentValue(raw[i]);
                    }

                    args[raw.Length] = new JniArgumentValue(IntPtr.Zero);

                    var resultRef = JniEnvironment.Object.NewObject(type, ctor, args);
                    if (!resultRef.IsValid)
                    {
                        Android.Util.Log.Error(LogTag, $"NewObject returned an invalid reference for {className}.");
                        return null;
                    }

                    var result = Java.Lang.Object.GetObject<T>(resultRef.Handle, JniHandleOwnership.TransferLocalRef);
                    Android.Util.Log.Debug(LogTag, $"NewObject({className}) -> {result}");
                    return result;
                }
            }
            finally
            {
                JniObjectReference.Dispose(ref type);
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Error(LogTag, $"Constructing {className} threw: {e}");
            return null;
        }
    }

    private static T? GetStaticObjectField<T>(string className, string fieldName, string signature)
        where T : class, IJavaObject
    {
        try
        {
            Android.Util.Log.Debug(LogTag, $"FindClass(\"{className}\")");
            if (!JniEnvironment.Types.TryFindClass(className, out var type))
            {
                Android.Util.Log.Error(LogTag, $"FindClass returned null for \"{className}\".");
                return null;
            }

            try
            {
                Android.Util.Log.Debug(LogTag, $"GetStaticFieldID(\"{className}\", \"{fieldName}\", \"{signature}\")");
                var field = JniEnvironment.StaticFields.GetStaticFieldID(type, fieldName, signature);

                Android.Util.Log.Debug(LogTag, $"GetStaticObjectField(\"{className}.{fieldName}\")");
                var resultRef = JniEnvironment.StaticFields.GetStaticObjectField(type, field);
                if (!resultRef.IsValid)
                {
                    Android.Util.Log.Error(LogTag, $"\"{className}.{fieldName}\" resolved to an invalid reference.");
                    return null;
                }

                var result = Java.Lang.Object.GetObject<T>(resultRef.Handle, JniHandleOwnership.TransferLocalRef);
                Android.Util.Log.Debug(LogTag, $"\"{className}.{fieldName}\" -> {result}");
                return result;
            }
            finally
            {
                JniObjectReference.Dispose(ref type);
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Error(LogTag, $"\"{className}.{fieldName}\" threw: {e}");
            return null;
        }
    }

    // Process-lifetime singletons on the Kotlin side (COROUTINE_SUSPENDED is a fixed
    // marker object, an empty Flow carries no state, DurationUnit.MILLISECONDS is an
    // enum constant), so each JNI round trip only has to happen once.
    private static Java.Lang.Object? coroutineSuspended;
    private static Xamarin.KotlinX.Coroutines.Flow.IFlow? emptyFlow;
    private static Java.Lang.Object? millisecondsUnit;
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
