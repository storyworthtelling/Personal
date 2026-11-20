namespace Storyworthtelling.People;

/// <summary>The household file's name in Drive. <b>Frozen</b>: it is the file's address, and every app finds it by this name.</summary>
public static class PeopleFile
{
    public const string FileName = "storyworthtelling-household.json";
}

/// <summary>The record kinds of the household file. <b>Frozen</b>: a renamed kind is a reader that finds nothing.</summary>
public static class PeopleKinds
{
    /// <summary>One person (<see cref="HouseholdPerson"/>).</summary>
    public const string Person = "person";

    /// <summary>The household's one rhythm record (<see cref="HouseholdRhythm"/>).</summary>
    public const string Rhythm = "household-rhythm";
}

/// <summary>
/// The apps that link their own ids to a person. <b>Frozen</b>: each is the suffix of a <c>link.</c> field, and an app's records about a
/// person find that person through it.
/// </summary>
public static class PeopleApps
{
    public const string Constitution = "constitution";

    public const string Pulp = "pulp";

    public const string Foreword = "foreword";
}
