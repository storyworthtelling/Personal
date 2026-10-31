using System.Reflection;
using System.Text.RegularExpressions;
using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.1, O10: an action that starts background work declares what it should do, so it is never
/// judged dead and its failure or silence is logged. Post's key actions are listed here by id — a new one
/// is a deliberate edit — and each is actually declared, through its constant, somewhere in the head.
/// </summary>
public partial class KeyActionsDeclaredTests
{
    private static readonly string[] Expected =
    [
        "post.send-sms",
        "post.send-mms",
        "post.file",
        "post.delete",
        "post.request-sms-role",
        "post.gmail-sign-in",
        "post.open-attachment",
        "post.send-logs-now",
    ];

    private static IReadOnlyList<FieldInfo> Constants() =>
        typeof(PostKeyActions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToList();

    [Fact]
    public void PostDeclaresExactlyTheseKeyActions()
    {
        var ids = Constants().Select(f => (string)f.GetRawConstantValue()!).OrderBy(i => i, StringComparer.Ordinal);

        Assert.Equal(Expected.OrderBy(i => i, StringComparer.Ordinal), ids);
        Assert.Equal(Expected.Length, PostKeyActions.All.Count);
    }

    [Fact]
    public void EveryKeyActionIsDeclaredInTheHeadThroughItsConstant()
    {
        var head = string.Concat(LogLoopSource.Sources("FocusPhone/Post.Android").Select(s => s.Text));

        var missing = Constants()
            .Where(f => !head.Contains($"TapWatch.Action(PostKeyActions.{f.Name}", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        Assert.True(missing.Count == 0, "Declared but never started, so never watched: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoKeyActionIsNamedByALiteral()
    {
        var literals = LogLoopSource.Sources("FocusPhone/Post.Android")
            .Where(s => LiteralAction().IsMatch(s.Text))
            .Select(s => s.Path)
            .ToList();

        Assert.Empty(literals);
    }

    [GeneratedRegex(@"TapWatch\.Action\(\s*""")]
    private static partial Regex LiteralAction();
}
