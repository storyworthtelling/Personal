using Android.Content;
using Codex.Ui.Voice;
using Constitution.Body;
using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Calendar;
using Constitution.Infrastructure.Workouts;
using Constitution.ReadBack;
using Constitution.Services;

namespace Constitution.WorkoutWeek;

/// <summary>
/// The calendar and learning side of the weekly run's seams, built from this install's own stores: Google Calendar over the account the
/// app already signs in with, the records the run keeps (written keys, issued numbers, the ledgers the pages taught), the owner's Body
/// store for the morning's recovery, and the household's setup file. Every file is under the no-backup folder, so no copy goes to a
/// cloud backup. Missing sign-in or an unset calendar is a typed, reasoned result from the adapters (never an exception, never an
/// empty list), and <see cref="CalendarSideSeams.Standing"/> says so in words that hold no name, address or token.
///
/// <para>The forecast and the hotel-gym lookup have no client yet; they honestly say they are unavailable, which the run reads as
/// "changes nothing". This file takes and holds no token: the sign-in is asked for only when a calendar call is made.</para>
///
/// <para>Device-gated: nothing here has run against Google Calendar or on a phone.</para>
/// </summary>
public static class WorkoutWeekCalendarSeams
{
    public const string FolderName = "workout-week";
    public const string SetupFile = "planning-setup.json";
    public const string WrittenKeysFile = "written-keys.json";
    public const string IssuedNumbersFile = "issued-numbers.json";
    public const string LearningFolder = "learning";

    private const string ApplicationName = "Constitution";

    /// <param name="context">The app's context.</param>
    /// <param name="ownFamily">
    /// Which run is reading the calendars, so which events it drops as its own earlier work. The workout run (and the Workouts tab, which re-derives its
    /// week) reads as <see cref="KeyFamily.Workout"/> and so sees the meal run's cook blocks, slow-cooker starts and Groceries trip as busy time; the
    /// meal run passes <see cref="KeyFamily.HouseholdMeal"/> and sees the workouts. Everything else about the seams is the one set of records both share.
    /// </param>
    public static CalendarSideSeams Create(Context context, KeyFamily ownFamily = KeyFamily.Workout)
    {
        ArgumentNullException.ThrowIfNull(context);
        var appContext = context.ApplicationContext ?? context;
        var directory = FolderOf(appContext);
        var setupPath = Path.Combine(directory, SetupFile);

        // The setup is read afresh by every adapter call, so a setup saved during a run is seen by the next call, not the next run.
        Func<HouseholdSetupRead> setup = () => PlanningSetupFile.Read(setupPath);

        var workScheduleJson = NullIfBlank(new ConstitutionConnections(appContext).WorkScheduleJson);
        var standing = CalendarSideStanding.Decide(VoiceGoogleAccount.IsSignedIn, setup(), workScheduleJson, TimeZoneInfo.Local);

        var account = ConstitutionGoogle.Create();
        var service = GoogleCalendarServices.Create(
            () => VoiceGoogleAccount.IsSignedIn,
            cancellation => TokenOf(account, cancellation),
            ApplicationName);

        var learning = new FileWorkoutLearning(ReadBackServices.LedgerOf(appContext), Path.Combine(directory, LearningFolder));
        IRecoveryReadSource recovery = standing.Owner is { } owner
            ? new BodyRecoveryReadSource(owner, BodyServices.CreateStore(appContext))
            : new NoRecoveryRead();

        return new CalendarSideSeams(
            new GoogleHouseholdCalendars(service, setup, ownFamily),
            new GoogleCalendarWriter(service, setup, ZoneOf(setup(), workScheduleJson)),
            new GoogleWorkoutEventEditor(service, setup),
            new FileWrittenKeys(Path.Combine(directory, WrittenKeysFile)),
            new FileKeyAssignments(Path.Combine(directory, IssuedNumbersFile)),
            learning,
            recovery,
            new UnavailableConditions(),
            new UnavailableHotelGyms(),
            standing);
    }

    private static string FolderOf(Context appContext)
    {
        var root = appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the weekly run's records in.");
        return Path.Combine(root, FolderName);
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static TimeZoneInfo ZoneOf(HouseholdSetupRead read, string? workScheduleJson) =>
        read is HouseholdSetupRead.Configured configured
            ? SetupRules.From(configured.Setup, workScheduleJson, TimeZoneInfo.Local).Rules.Zone
            : TimeZoneInfo.Local;

    private static async Task<string> TokenOf(VoiceGoogleAccount account, CancellationToken cancellation)
    {
        try
        {
            return await account.GetAccessToken().WaitAsync(cancellation);
        }
        catch (Java.Lang.Exception failure)
        {
            // The platform's own exception types are not .NET's; say only which one, never what it carried.
            throw new InvalidOperationException("The account manager gave no token (" + failure.GetType().Name + ").", failure);
        }
    }

    /// <summary>For an install that marks nobody as the owner: no one can be read, and an absent person is left as planned.</summary>
    private sealed class NoRecoveryRead : IRecoveryReadSource
    {
        public IReadOnlyDictionary<string, RecoveryRead> Read(IReadOnlyList<string> people, DateOnly morning) =>
            new Dictionary<string, RecoveryRead>();
    }
}

/// <summary>
/// What the calendar and learning side of <see cref="Constitution.Infrastructure.Workouts.WorkoutWeekSeams"/> needs, built for this
/// install: the members line up with that record by name so the head's later assembly is a straight copy.
/// </summary>
/// <param name="Standing">Whether the calendars can be tried, who the owner is and what was assumed.</param>
public sealed record CalendarSideSeams(
    IHouseholdCalendarsSource Calendars,
    ICalendarWriter CalendarWriter,
    IWorkoutEventEditor EventEditor,
    IWrittenKeyStore WrittenKeys,
    IKeyAssignmentStore KeyAssignments,
    IWorkoutLearning Learning,
    IRecoveryReadSource Recovery,
    IConditionsSource Conditions,
    IHotelGymLookup HotelGyms,
    CalendarSideStanding Standing);
