namespace Codex.Domain.Policy;

/// <summary>
/// The system's recents provider: the package Android binds for the swipe-up gesture, the
/// back-to-home animation and the recents screen. On a Pixel that is Pixel Launcher, even while
/// another app is the home screen, so it is one of the "other homes" the lockdown suspends — and a
/// suspended recents provider turns every swipe up into the "blocked by your admin" dialog. The
/// head reads the platform's <c>config_recentsComponentName</c> and passes it here.
/// </summary>
public static class SystemRecents
{
    /// <summary>The package of a flattened component ("package/class"), or null when none is named.</summary>
    public static string? PackageFrom(string? flattenedComponent)
    {
        if (string.IsNullOrWhiteSpace(flattenedComponent))
        {
            return null;
        }

        var slash = flattenedComponent.IndexOf('/');
        var package = (slash < 0 ? flattenedComponent : flattenedComponent[..slash]).Trim();
        return package.Length == 0 ? null : package;
    }
}
