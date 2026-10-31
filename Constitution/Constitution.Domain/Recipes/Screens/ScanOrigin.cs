namespace Constitution.Domain.Recipes.Screens;

/// <summary>
/// Where a draft's words came from. A typed card goes through the same check-and-keep step as a scan, but it is labelled as
/// typed, never says a picture was involved, and is kept with the typed source label.
/// </summary>
public enum ScanOrigin
{
    Scanned,
    Typed,
}
