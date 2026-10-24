using Android.Content;
using Constitution.Domain.Calendar;
using Constitution.Domain.Settings;
using Constitution.Infrastructure.Workouts;
using Constitution.WorkoutDelivery;
using Constitution.WorkoutWeek;

namespace Constitution.Settings;

/// <summary>
/// The planning-setup file the weekly run reads, kept in the weekly run's own folder (<c>NoBackupFilesDir/workout-week/</c>). The
/// Household group reads it to show what is set and writes it when a choice changes. Every ending is a plain sentence for the group
/// to show inline; nothing here throws for an expected failure and nothing is written over a file that cannot be read.
/// </summary>
public sealed class HouseholdSetupStore(Context context)
{
    private readonly string path = Path.Combine(
        WorkoutWeekPaths.Folder(
            (context.ApplicationContext ?? context).NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the household setup in.")),
        WorkoutWeekCalendarSeams.SetupFile);

    public HouseholdSetupRead Read() => PlanningSetupFile.Read(path);

    /// <summary>Applies the choices to the adults the saved setup names, and writes it. Returns what to say.</summary>
    public string Save(HouseholdChoices choices)
    {
        if (Read() is not HouseholdSetupRead.Configured configured)
        {
            return "There is no adult to plan for yet. Add one below, or wait for the household file.";
        }

        return Write(PlanningSetupFromRoster.Apply(configured.Setup, choices));
    }

    /// <summary>
    /// With no household file, the only way for the setup to have an adult is to name one here. Kept in the setup file alone; the roster
    /// replaces it when the household file is wired.
    /// </summary>
    public string AddAdult(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "Type the adult's name first.";
        }

        var existing = Read() is HouseholdSetupRead.Configured configured ? configured.Setup : new PlanningSetup([]);
        if (existing.FindAdult(trimmed) is not null)
        {
            return $"{trimmed} is already here.";
        }

        var adults = existing.Adults.Append(new HouseholdAdult(trimmed, null, null, null, null, DeliversHere: existing.Adults.Count == 0)).ToList();
        return Write(PlanningSetupFromRoster.Apply(existing with { Adults = adults }, HouseholdChoices.From(existing with { Adults = adults })));
    }

    private string Write(PlanningSetupBuild build)
    {
        if (build.Setup is null)
        {
            return HouseholdGroupModel.Describe(build.Gaps);
        }

        try
        {
            var result = PlanningSetupFile.Write(path, build.Setup);
            var saved = result == PlanningSetupWrite.WrittenAfterSettingAsideAnUnreadableFile
                ? "Saved. The earlier file could not be read, so it was kept beside this one."
                : "Saved on this phone.";
            var missing = HouseholdGroupModel.Describe(build.Gaps);
            return missing.Length == 0 ? saved : saved + " " + missing;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"Not saved: the household setup could not be written ({failure.GetType().Name}).";
        }
    }
}
