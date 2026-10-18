using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Constitution.Tests.Health;

/// <summary>
/// Reads Constitution's Health Connect declarations out of text, so the guards can run on the head's
/// source attributes, its hand-written manifest and the merged Release manifest alike. Pure: it takes
/// the text, so a test can hand it a deliberately bad sample and watch the detector fire.
/// </summary>
internal static class HealthManifestReader
{
    public static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    /// <summary>The string literal in <c>[assembly: UsesPermission("...")]</c> or <c>UsesPermission(Name = "...")</c>.</summary>
    private static readonly Regex LiteralPermission = new(
        "UsesPermission\\(\\s*(?:Name\\s*=\\s*)?\"([^\"]+)\"",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex AnyPermission = new(
        "\\[assembly:\\s*UsesPermission\\(([^)]*)\\)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>Permission names written as string literals in source attributes.</summary>
    public static IReadOnlyList<string> SourcePermissions(string source) =>
        LiteralPermission.Matches(source).Select(match => match.Groups[1].Value).ToList();

    /// <summary>
    /// The arguments of <c>UsesPermission</c> attributes that are NOT a string literal (a constant
    /// reference). The guard cannot read a value out of those, so only INTERNET, which is not a
    /// health permission, may be written that way.
    /// </summary>
    public static IReadOnlyList<string> SourceNonLiteralArguments(string source) =>
        AnyPermission.Matches(source)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(argument => !argument.StartsWith('"') && !argument.StartsWith("Name", StringComparison.Ordinal))
            .ToList();

    /// <summary>Every <c>uses-permission</c> name in a manifest.</summary>
    public static IReadOnlyList<string> ManifestPermissions(XDocument manifest) =>
        manifest.Descendants("uses-permission")
            .Select(element => (string?)element.Attribute(Android + "name"))
            .OfType<string>()
            .ToList();

    /// <summary>The <c>activity</c> element with this registered name, or null.</summary>
    public static XElement? Activity(XDocument manifest, string name) =>
        manifest.Descendants("activity").SingleOrDefault(element => (string?)element.Attribute(Android + "name") == name);

    /// <summary>The <c>activity-alias</c> element with this registered name, or null.</summary>
    public static XElement? Alias(XDocument manifest, string name) =>
        manifest.Descendants("activity-alias").SingleOrDefault(element => (string?)element.Attribute(Android + "name") == name);

    public static IReadOnlyList<string> Actions(XElement element) =>
        element.Elements("intent-filter").Elements("action")
            .Select(action => (string?)action.Attribute(Android + "name"))
            .OfType<string>()
            .ToList();

    public static bool IsExported(XElement element) => (string?)element.Attribute(Android + "exported") == "true";

    /// <summary>True when one intent filter of the element holds both this action and this category.</summary>
    public static bool HasFilter(XElement element, string action, string category) =>
        element.Elements("intent-filter").Any(filter =>
            filter.Elements("action").Any(a => (string?)a.Attribute(Android + "name") == action)
            && filter.Elements("category").Any(c => (string?)c.Attribute(Android + "name") == category));
}
