using Constitution.Domain.Calendar;
using Constitution.Domain.Diagnostics;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Where the calendar side of the weekly run stands, decided from facts alone: is there a Google sign-in, is there a household setup,
/// whose phone this is, and which settings fell back to a stated stand-in. Every sentence is fixed (nothing the household wrote is
/// quoted: names, addresses and calendar ids stay out), so <see cref="Line"/> may go to the trail and the Diagnostics export.
/// </summary>
/// <param name="Owner">The adult whose pages this phone delivers (and whose Body this phone holds), or null when none is marked.</param>
/// <param name="Assumed">The settings the run takes a stand-in for, in <see cref="AssumedSetting"/> order.</param>
/// <param name="NotReadyBecause">Why the run cannot read the calendars, or null when it can try.</param>
public sealed record CalendarSideStanding(string? Owner, IReadOnlyList<AssumedSetting> Assumed, string? NotReadyBecause)
{
    public const string SignInReason = "Google is not signed in on this phone.";

    public bool IsReady => NotReadyBecause is null;

    /// <summary>One value-free line for the trail.</summary>
    public string Line
    {
        get
        {
            var ready = IsReady ? "calendar side ready" : "calendar side not ready: " + NotReadyBecause;
            var owner = Owner is null ? "no owner marked" : "owner marked";
            var assumed = Assumed.Count == 0 ? "nothing assumed" : "assumed: " + string.Join(", ", Assumed.Select(DiagnosticsPhrases.Of));
            return $"{ready}; {owner}; {assumed}";
        }
    }

    public static CalendarSideStanding Decide(bool signedIn, HouseholdSetupRead setup, string? workScheduleJson, TimeZoneInfo phoneZone)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(phoneZone);

        if (setup is not HouseholdSetupRead.Configured configured)
        {
            return new CalendarSideStanding(null, [], ((HouseholdSetupRead.NotSet)setup).Reason);
        }

        var owner = configured.Setup.DeliverFor.FirstOrDefault();
        var assumed = SetupRules.From(configured.Setup, workScheduleJson, phoneZone).Assumed;
        return new CalendarSideStanding(owner, assumed, signedIn ? null : SignInReason);
    }
}

/// <summary>The household's setup file in the app's private storage, read without ever throwing.</summary>
public static class PlanningSetupFile
{
    public const string UnreadableReason = "The saved household setup could not be opened.";

    public static HouseholdSetupRead Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Directory.Exists(path))
        {
            // Something else sits where the file should be: it is there, and it cannot be read, which is not "nothing saved".
            return new HouseholdSetupRead.NotSet(UnreadableReason);
        }

        try
        {
            return File.Exists(path)
                ? PlanningSetupCodec.Read(File.ReadAllText(path))
                : new HouseholdSetupRead.NotSet(PlanningSetupCodec.NothingSavedReason);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The sentence is the reason; the exception's text can quote a path, and the type is the head's to log.
            return new HouseholdSetupRead.NotSet(UnreadableReason);
        }
    }

    /// <summary>
    /// Writes the setup atomically (a temporary file beside it, then a move). A file already there that cannot be used as a setup is
    /// <b>never overwritten</b>: it is set aside first (<c>UnreadableCopies</c>), so the owner's earlier file survives whatever it held.
    /// </summary>
    public static PlanningSetupWrite Write(string path, PlanningSetup setup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(setup);
        if (Directory.Exists(path))
        {
            throw new IOException("The household setup was not saved: a directory sits where the file belongs, and it was left alone.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var result = PlanningSetupWrite.Written;
        if (File.Exists(path) && Read(path) is HouseholdSetupRead.NotSet)
        {
            UnreadableCopies.SetAside(path);
            result = PlanningSetupWrite.WrittenAfterSettingAsideAnUnreadableFile;
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, PlanningSetupCodec.Write(setup));
        File.Move(temporary, path, overwrite: true);
        return result;
    }
}

/// <summary>What <see cref="PlanningSetupFile.Write"/> did.</summary>
public enum PlanningSetupWrite
{
    Written,
    WrittenAfterSettingAsideAnUnreadableFile,
}
