using System.Text;
using Android.Content;
using Constitution.Body;
using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Credentials;
using Constitution.Domain.Health;
using Constitution.Domain.Notices;
using Constitution.Health;
using Constitution.Services;
using Constitution.Settings;

namespace Constitution.BodyTab;

/// <summary>
/// Signals taken away from Health Connect and not yet allowed again, remembered for the life of the
/// process. The open-time reconcile (<c>BodyServices.RefreshAtOpen</c>) demotes a saved "allowed" to
/// "asked" as soon as the platform stops granting it, so without this memory a revoked chart would turn
/// into one that had merely never been allowed, and the Problem notice would have nothing to stand on.
/// </summary>
internal static class BodyRevocations
{
    private static readonly object Gate = new();
    private static readonly HashSet<BodySignal> Seen = [];

    /// <summary>Adds what has just been taken away, forgets what is granted again, and returns what is still revoked.</summary>
    public static IReadOnlySet<BodySignal> Update(IEnumerable<BodySignal> revokedNow, Func<BodySignal, bool> isGranted)
    {
        lock (Gate)
        {
            Seen.UnionWith(revokedNow);
            Seen.RemoveWhere(signal => isGranted(signal));
            return new HashSet<BodySignal>(Seen);
        }
    }
}

/// <summary>
/// What Health Connect and the bed's settings say right now: the saved choices, what the platform lets be
/// read, which signals were taken away, and whether a side of the bed is chosen. Cheap and synchronous on
/// purpose (a preferences read and a permission check each), so it can be taken in <c>Resume</c>
/// <em>before</em> the open-time reconcile rewrites the choices.
/// </summary>
internal sealed record BodyAccess(
    HealthChoices Choices,
    IReadOnlyDictionary<BodySignal, bool> Grants,
    IReadOnlySet<BodySignal> Revoked,
    bool BedChosen)
{
    /// <summary>Reads the access and states it to the notice board: a taken-away permission raises its Problem notice, an allowed-again one takes it away.</summary>
    public static BodyAccess Read(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var choices = HealthChoices.FromJson(new ConstitutionConnections(appContext).HealthConnectChoicesJson);
        var source = new HealthConnectSource(appContext, new BodyLog(appContext));
        var grants = BodySignals.HealthConnect.ToDictionary(signal => signal, source.IsGranted);

        var revokedNow = PermissionAsk.RevokedSignals(choices, signal => grants[signal]);
        var revoked = BodyRevocations.Update(revokedNow, signal => grants[signal]);
        ConnectionNoticeBoard.With(board => PermissionAsk.Apply(board, revoked, DateTimeOffset.UtcNow));

        return new BodyAccess(choices, grants, revoked, new SleeperChoice(appContext).Side is not null);
    }

    /// <summary>The access as a line of words and numbers, for telling whether two reads differ. Holds no reading.</summary>
    public string Signature()
    {
        var text = new StringBuilder(Choices.ToJson()).Append(BedChosen ? "|bed" : "|nobed");
        foreach (var signal in BodySignals.HealthConnect)
        {
            text.Append('|').Append(Grants[signal] ? 'g' : 'n').Append(Revoked.Contains(signal) ? 'r' : '-');
        }

        return text.ToString();
    }
}

/// <summary>The stored readings and the access, taken together, as of one day: everything the Body tab is drawn from.</summary>
internal sealed record BodySnapshot(IReadOnlyList<BodyReading> Readings, BodyAccess Access, DateOnly Today)
{
    public BodyTabInputs Inputs(BodyWindow window) => new(
        Readings,
        window,
        Today,
        Access.Choices,
        signal => Access.Grants.GetValueOrDefault(signal),
        Access.BedChosen,
        Access.Revoked);

    /// <summary>A short signature of the snapshot: equal when nothing the page shows has changed. Holds no reading's value.</summary>
    public string Signature() =>
        $"{Readings.Count}|{(Readings.Count == 0 ? "none" : Readings.Max(reading => reading.Day).ToString("yyyy-MM-dd"))}|{Today:yyyy-MM-dd}|{Access.Signature()}";

    /// <summary>
    /// States the bed to the notice board. A quiet bed raises the Problem notice and never reaches the
    /// voice; a refused login is left to Settings, which owns that notice.
    /// </summary>
    public void StateTheBed(Context context)
    {
        var state = BedOffline.Judge(Readings, Access.BedChosen, Today);
        var report = ConstitutionHealthReport.Read();
        var saves = new ConstitutionCredentialSaves(context);
        var rejected = ConstitutionHealthReport.HealthOf(report, CredentialIds.SleepNumber, saves.SavedAt(CredentialIds.SleepNumber))
            == ConnectionHealth.Rejected;
        ConnectionNoticeBoard.With(board => BedOffline.Apply(board, state, rejected, DateTimeOffset.UtcNow));
    }
}
