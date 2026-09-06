namespace Storyworthtelling.Updates;

/// <summary>
/// The quiet line at the foot of every Settings screen: app name, version, package id.
///
/// <para>It is not decoration. There is no adb after the cutover and the update channel installs
/// silently, so this line is the only way to tell an update actually landed — especially the
/// updates that change nothing visible, which is most of them. Flyleaf has had it since the
/// channel went live; nothing else did, so on six of seven apps "did that install?" was
/// unanswerable on the device.</para>
///
/// <para>Pure, so the wording is testable without an Activity, and identical everywhere. The
/// version is the <i>versionName</i> rather than the versionCode: the code is the channel's
/// business, the name is the owner's.</para>
/// </summary>
public static class BuildLine
{
    /// <summary>What to show when the package manager will not say.</summary>
    public const string UnknownVersion = "?";

    public static string Format(string appName, string? versionName, string? package)
    {
        var version = string.IsNullOrWhiteSpace(versionName) ? UnknownVersion : versionName.Trim();
        var name = string.IsNullOrWhiteSpace(appName) ? UnknownVersion : appName.Trim();

        // The package id is what the update channel matches on, so a line without it cannot
        // answer the question it exists for. An interpunct joins metadata (DESIGN.md §14).
        return string.IsNullOrWhiteSpace(package)
            ? $"{name} {version}"
            : $"{name} {version} · {package.Trim()}";
    }
}
