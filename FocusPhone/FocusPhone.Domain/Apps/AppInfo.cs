namespace FocusPhone.Domain.Apps;

/// <summary>
/// Facts about an installed package as observed by the Android layer
/// (intent queries and ApplicationInfo), independent of any Play metadata.
/// </summary>
public record AppInfo(string PackageName, bool HandlesHttpViewIntents, bool IsCategoryGame);

public enum ContentRating
{
    Unknown,
    Everyone,
    Teen,
    Mature,
}

public enum AppVerdict
{
    Allowed,
    Blocked,
    Quarantined,
}
