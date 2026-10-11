using Android.App;
using Android.Content;
using Codex.Ui.Voice;
using Pulp.App;
using Pulp.Domain.FirstRun;
using Pulp.Vault;

namespace Pulp.FirstRun;

/// <summary>
/// The two things first run keeps of its own — which steps were skipped, and whether it was finished
/// — in this install's private preferences. Everything else in <see cref="FirstRunState"/> is read
/// fresh from where it actually lives, which is what makes first run resumable without a cursor that
/// could disagree with what is in place.
/// </summary>
internal static class FirstRunProgress
{
    private const string PrefsName = "pulpFirstRun";
    private const string SkippedKey = "skipped";
    private const string FinishedKey = "finished";

    public static IReadOnlySet<FirstRunStep> Skipped =>
        (Prefs().GetStringSet(SkippedKey, null) ?? [])
            .Select(word => Enum.TryParse<FirstRunStep>(word, out var step) ? step : (FirstRunStep?)null)
            .OfType<FirstRunStep>()
            .ToHashSet();

    public static bool Finished => Prefs().GetBoolean(FinishedKey, false);

    public static void Skip(FirstRunStep step)
    {
        var words = Skipped.Append(step).Select(each => each.ToString()).ToList();
        var editor = Prefs().Edit()!;
        editor.PutStringSet(SkippedKey, words);
        editor.Apply();
    }

    public static void Finish()
    {
        var editor = Prefs().Edit()!;
        editor.PutBoolean(FinishedKey, true);
        editor.Apply();
    }

    /// <summary>The state now. Reads the household and the install's files, so call it off the UI thread.</summary>
    public static async Task<FirstRunState> ReadAsync(PulpServices services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var view = await services.HouseholdAsync(cancellationToken);
        var phone = services.ThisPhoneOf(view);

        return new FirstRunState(
            VoiceGoogleAccount.IsSignedIn,
            VaultPassphraseStore.IsSet,
            PulpVault.KeysPresent(services.Keys.Keys),
            view.Roster is not null,
            phone.Status,
            Skipped,
            Finished,
            MayAddPeople: services.Rights.CanAddOrRemoveViewers);
    }

    private static ISharedPreferences Prefs() =>
        Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
}

/// <summary>
/// The shell's one question at launch: open first run or not (<see cref="FirstRunGate"/>). MainActivity
/// calls <see cref="ShouldOpenAsync"/> and, when it says so, starts <see cref="FirstRunActivity.For"/>.
/// </summary>
public static class FirstRunLaunch
{
    public static async Task<bool> ShouldOpenAsync(Context context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = PulpServices.For(context);
        var state = await Task.Run(() => FirstRunProgress.ReadAsync(services, cancellationToken), cancellationToken);
        return FirstRunGate.ShouldOpen(state);
    }
}
