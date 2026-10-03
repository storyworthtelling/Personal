using System.Net;
using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// The approved Waiting page, read out of <c>Ledger/design/mockup-17.html</c> at TEST time (R-G26).
/// Every string the guards compare against is parsed here from the drawing, not retyped in a test, so
/// the test fails if either the notices or the mockup drifts — which is the point: a test holding its
/// own copy of the wording can agree with a wrong notice forever.
/// </summary>
internal static class Mockup17
{
    /// <summary>One notice as drawn: its role bar, headline, date, detail and (optional) action.</summary>
    internal sealed record Drawn(string BarHex, string Headline, string Date, string Detail, string? Action);

    private static readonly Regex Row = new(
        @"<i class=""nt-m"" style=""background:(?<bar>#[0-9A-Fa-f]{6})""></i><div class=""wp-b"">"
        + @"<div class=""wp-top""><span class=""wp-h"">(?<h>.*?)</span><span class=""wp-w"">(?<w>.*?)</span></div>"
        + @"<div class=""wp-d"">(?<d>.*?)</div>(?:<div class=""kbtn wp-btn"">(?<a>.*?)</div>)?</div></div>",
        RegexOptions.Singleline);

    internal static string Html() =>
        File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "design", "mockup-17.html"));

    /// <summary>The three notices frame 1 draws, top to bottom.</summary>
    internal static IReadOnlyList<Drawn> Notices()
    {
        var rows = Row.Matches(Html())
            .Select(m => new Drawn(
                m.Groups["bar"].Value,
                Decode(m.Groups["h"].Value),
                Decode(m.Groups["w"].Value),
                Decode(m.Groups["d"].Value),
                m.Groups["a"].Success ? Decode(m.Groups["a"].Value) : null))
            .ToList();
        Assert.True(rows.Count == 3, $"Expected the three notices frame 1 draws, parsed {rows.Count}.");
        return rows;
    }

    /// <summary>The status line frame 1 draws under the rule.</summary>
    internal static string StatusLine() =>
        Decode(Regex.Match(Html(), @"<div class=""dv-status"">(?<s>For Accounts.*?)</div>").Groups["s"].Value);

    /// <summary>
    /// The failed-refresh sentence, which the notes quote rather than draw as a card. Whitespace is
    /// normalised because the HTML wraps the quotation across two source lines.
    /// </summary>
    internal static string FailedRefreshSentence()
    {
        var quoted = Regex.Match(Html(), @"&ldquo;(?<q>Couldn't reach Plaid\..*?)&rdquo;", RegexOptions.Singleline);
        Assert.True(quoted.Success, "The notes no longer quote the failed-refresh sentence.");
        return Regex.Replace(Decode(quoted.Groups["q"].Value), @"\s+", " ");
    }

    /// <summary>The role a bar colour names: the kit's token whose hex it is, minus "Mark".</summary>
    internal static string RoleOf(string barHex)
    {
        var dictionary = File.ReadAllText(Path.Combine(
            DesignTestHelpers.LedgerRoot(), "..", "Storyworthtelling.Maui", "Resources", "Styles", "Paper.xaml"));
        var match = Regex.Matches(dictionary, @"<Color x:Key=""(?<key>\w+)Mark"">(?<hex>#[0-9A-Fa-f]{6})</Color>")
            .FirstOrDefault(m => string.Equals(m.Groups["hex"].Value, barHex, StringComparison.OrdinalIgnoreCase));
        Assert.True(match is not null, $"No role mark in the kit is {barHex}, the colour mockup 17 draws.");
        return match!.Groups["key"].Value;
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html).Trim();
}
