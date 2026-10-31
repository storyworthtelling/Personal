using Android.Content;
using Constitution.Domain.Settings;

namespace Constitution.Services;

/// <summary>
/// Puts a failed connection act on the trail the way every other outcome goes there: a short phrase
/// naming the act and the exception's type, and never its message (<see cref="ConnectionFailure.LogPhrase"/>).
/// </summary>
public static class ConnectionTrail
{
    /// <summary>Records one failure.</summary>
    public static void Record(Context context, ConnectionAct act, Exception failure)
    {
        var appContext = context.ApplicationContext ?? context;
        new ConstitutionVaultLog(appContext).Outcome(ConnectionFailure.LogPhrase(act, failure));
    }

    /// <summary>
    /// For an exception filter: records the failure whatever it is, then says whether the caller expected
    /// it. A type nobody expected is therefore on the trail by name even though it goes on to propagate
    /// (with its stack) rather than being caught.
    /// </summary>
    public static bool Expecting(Context context, ConnectionAct act, Exception failure, bool expected)
    {
        Record(context, act, failure);
        return expected;
    }
}
