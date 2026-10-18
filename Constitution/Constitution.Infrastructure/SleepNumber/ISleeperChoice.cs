namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// "Which side of the bed is mine", a stored choice per install (the household login is shared, so the
/// person is not). Null until chosen. The Android head implements it over the phone's preferences.
/// </summary>
public interface ISleeperChoice
{
    BedSide? Side { get; }
}
