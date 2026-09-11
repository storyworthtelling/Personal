using Foreword.Domain.Models;
using Foreword.Domain.Tasks;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// Google's tasks, as Foreword sees them (D37).
///
/// <para>A once task is a <b>mirror</b>, not a copy: its title and due date belong to Google and are
/// written back rather than kept here, so the two can never disagree. What Foreword adds is the part
/// Google Tasks cannot express — a rule, a season, a window — and those never go the other way.</para>
/// </summary>
public class GoogleTaskMirrorTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    [Fact]
    public void AGoogleTaskBecomesAOnceTask()
    {
        var mirrored = GoogleTaskMirror.From(
            [new TodoItem { Title = "Call the dentist", Due = Today }]).Single();

        Assert.Equal(TaskKind.Once, mirrored.Kind);
        Assert.Equal("Call the dentist", mirrored.Title);
        Assert.Equal(Today, mirrored.Due);
    }

    /// <summary>
    /// A closed task is carried rather than dropped. It shows struck through on the list screen so
    /// the owner can see <i>why</i> it will not print — one that simply vanished would read as one
    /// Foreword lost.
    /// </summary>
    [Fact]
    public void ATaskClosedInGoogleIsCarriedAndMarked()
    {
        var mirrored = GoogleTaskMirror.From(
            [new TodoItem { Title = "Dry cleaning", Completed = true }]).Single();

        Assert.True(mirrored.CompletedInGoogle);
    }

    /// <summary>
    /// The id is Google's, because that is what a write-back is addressed to. Without it an edit in
    /// Foreword would have nowhere to go and the two copies would drift apart — which is the whole
    /// thing the mirror exists to prevent.
    /// </summary>
    [Fact]
    public void TheGoogleIdIsKeptSoAnEditCanBeWrittenBack()
    {
        var mirrored = GoogleTaskMirror.From(
            [new TodoItem { Id = "g-123", Title = "Call the dentist" }]).Single();

        Assert.Equal("g-123", mirrored.GoogleTaskId);
        Assert.Equal("g-123", mirrored.Id);
    }

    /// <summary>
    /// Foreword's own rules and Google's tasks are one list on the page. Merging them here rather
    /// than at the point of drawing is what makes "one list, not three" a fact about the data.
    /// </summary>
    [Fact]
    public void TheMirrorAndForewordsOwnRulesAreOneList()
    {
        var rules = new[]
        {
            new ForewordTask { Id = "vitamins", Title = "Vitamins", Kind = TaskKind.Rule },
        };

        var merged = GoogleTaskMirror.Merge(rules, [new TodoItem { Id = "g-1", Title = "Call Mom" }]);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, t => t.Kind == TaskKind.Rule);
        Assert.Contains(merged, t => t.Kind == TaskKind.Once);
    }

    /// <summary>
    /// A Google task the owner has already turned into a Foreword rule is not carried twice. The
    /// rule wins: it is the richer of the two, and the mirror is the copy.
    /// </summary>
    [Fact]
    public void ARuleMadeFromAGoogleTaskReplacesTheMirrorRatherThanJoiningIt()
    {
        var rules = new[]
        {
            new ForewordTask
            {
                Id = "lawn", Title = "Mow the lawn", Kind = TaskKind.Smart, GoogleTaskId = "g-9",
            },
        };

        var merged = GoogleTaskMirror.Merge(rules, [new TodoItem { Id = "g-9", Title = "Mow the lawn" }]);

        Assert.Equal(TaskKind.Smart, merged.Single().Kind);
    }
}
