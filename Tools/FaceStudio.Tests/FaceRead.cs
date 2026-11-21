using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Watch.Domain.Design;
using Watch.Domain.Face;

namespace FaceStudio.Tests;

/// <summary>Reads the generated face back, for the tests.</summary>
internal static class FaceRead
{
    public const double Radius = PointFace.Canvas / 2.0;

    private static readonly Regex ColourList = new("\"([#0-9A-Fa-f ]+)\"", RegexOptions.Compiled);

    public static XDocument Face { get; } = FaceDocument.Build();

    public static XElement Scene => Face.Root!.Element("Scene")!;

    public static XElement Layer(string name) =>
        Scene.Elements("Group").Single(group => (string?)group.Attribute("name") == name);

    /// <summary>The group a layer shows at an hour of the 24-hour day, found through its condition.</summary>
    public static XElement Hour(string layer, int hour)
    {
        var condition = Layer(layer).Element("Condition")!;
        var expression = condition.Element("Expressions")!.Elements("Expression")
            .Single(item => item.Value.Trim() == $"[HOUR_0_23] == {hour}");
        var name = (string)expression.Attribute("name")!;
        return condition.Elements("Compare").Single(compare => (string?)compare.Attribute("expression") == name)
            .Elements().Single();
    }

    public static XElement Part(XElement hour, string name) =>
        hour.Descendants().Single(element => (string?)element.Attribute("name") == name);

    public static double Number(XElement element, string attribute) =>
        double.Parse((string)element.Attribute(attribute)!, CultureInfo.InvariantCulture);

    /// <summary>The colours a <c>Transform target="color"</c> chooses between, one a minute.</summary>
    public static IReadOnlyList<string> MinuteColours(XElement painted)
    {
        var transform = painted.Elements("Transform").Single(item => (string?)item.Attribute("target") == "color");
        var value = (string)transform.Attribute("value")!;
        Assert.Contains("[MINUTE]", value, StringComparison.Ordinal);
        return ColourList.Match(value).Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>What the colour list should hold for a tone: its colour at each minute's hue, as opaque #AARRGGBB.</summary>
    public static IReadOnlyList<string> Expected(Func<double, Oklch> tone) =>
        Enumerable.Range(0, 60)
            .Select(minute => "#" + Srgb.Argb(tone(HourShape.For(new TimeOnly(0, minute)).Hue)).ToString("X8", CultureInfo.InvariantCulture))
            .ToList();

    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"No Directory.Packages.props above {AppContext.BaseDirectory}.");
    }
}
