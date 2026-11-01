using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.1, O10: an action that starts background work declares what it should do, so it is never judged
/// dead and its failure or silence is logged. Each head's key actions are listed in <see cref="LogLoopHeads"/>
/// by id — a new one is a deliberate edit — and each is actually declared, through its constant, in the head.
/// Send logs now is declared by the shared Diagnostics screen, so for it the head hands the constant over.
/// </summary>
public partial class KeyActionsDeclaredTests
{
    [Theory]
    [MemberData(nameof(LogLoopHeads.Apps), MemberType = typeof(LogLoopHeads))]
    public void EachHeadDeclaresExactlyItsKeyActions(string app)
    {
        var head = LogLoopHeads.For(app);
        var constants = Constants(head);

        Assert.Equal(head.KeyActions.Order(StringComparer.Ordinal), constants.Values.Order(StringComparer.Ordinal));
        Assert.All(constants.Values, id => Assert.StartsWith(app + ".", id));
    }

    [Theory]
    [MemberData(nameof(LogLoopHeads.Apps), MemberType = typeof(LogLoopHeads))]
    public void EveryKeyActionIsDeclaredInTheHeadThroughItsConstant(string app)
    {
        var head = LogLoopHeads.For(app);
        var text = string.Concat(LogLoopSource.Sources(head.Folder).Select(s => s.Text));

        var missing = Constants(head).Keys
            .Where(name => !text.Contains($"TapWatch.Action({head.KeyActionsClass}.{name}", StringComparison.Ordinal)
                && !(name == "SendLogsNow" && text.Contains($"{head.KeyActionsClass}.SendLogsNow", StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0, $"{app}: declared but never started, so never watched: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(LogLoopHeads.Folders), MemberType = typeof(LogLoopHeads))]
    public void NoKeyActionIsNamedByALiteral(string folder)
    {
        var literals = LogLoopSource.Sources(folder).Where(s => LiteralAction().IsMatch(s.Text)).Select(s => s.Path).ToList();

        Assert.Empty(literals);
    }

    private static Dictionary<string, string> Constants(LogLoopHeads.Head head) =>
        Constant().Matches(LogLoopSource.File(head.KeyActionsFile))
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["id"].Value, StringComparer.Ordinal);

    [GeneratedRegex(@"public const string (?<name>\w+) = ""(?<id>[^""]+)"";")]
    private static partial Regex Constant();

    [GeneratedRegex(@"TapWatch\.Action\(\s*\$?""")]
    private static partial Regex LiteralAction();
}
