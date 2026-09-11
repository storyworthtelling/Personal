using Moq;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;
using Foreword.Infrastructure.Tasks;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// Where an edit goes (D37).
///
/// <para>A once task is Google's. Its title and due date are written <b>back</b> rather than kept
/// here, so the two can never disagree — a Foreword copy that drifted from Google's would be two
/// answers to "what is this task called", and the owner would have no way to tell which one their
/// phone was about to print.</para>
///
/// <para>Everything else is Foreword's, because Google Tasks cannot express a window, a season or a
/// condition. Those never travel the other way.</para>
/// </summary>
public class TaskEditorTests
{
    private readonly Mock<IGoogleTasksClient> google = new();
    private readonly FakeStore store = new();

    private TaskEditor Editor() => new(store, google.Object);

    private static ForewordTask GoogleTask(string title = "Call the dentist") => new()
    {
        Id = "g-1", GoogleTaskId = "g-1", GoogleListId = "list-1",
        Title = title, Kind = TaskKind.Once, Due = new DateOnly(2026, 9, 18),
    };

    private static ForewordTask Rule() => new()
    {
        Id = "vitamins", Title = "Vitamins", Kind = TaskKind.Rule,
    };

    [Fact]
    public async Task AOnceTaskIsWrittenBackToGoogle()
    {
        var result = await Editor().Save(GoogleTask("Call the orthodontist"));

        google.Verify(g => g.UpdateTask("list-1", "g-1", "Call the orthodontist",
            new DateOnly(2026, 9, 18)), Times.Once);
        Assert.True(result.Saved);
    }

    /// <summary>
    /// And it is not ALSO kept here. Two copies is the thing the write-back exists to avoid, and
    /// keeping a local one "just in case" is how they start disagreeing.
    /// </summary>
    [Fact]
    public async Task AOnceTaskIsNotAlsoKeptHere()
    {
        await Editor().Save(GoogleTask());

        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ARuleIsKeptHereAndNotSentToGoogle()
    {
        var result = await Editor().Save(Rule());

        Assert.Single(store.All());
        Assert.True(result.Saved);
        google.Verify(g => g.UpdateTask(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly?>()),
            Times.Never);
    }

    /// <summary>
    /// A once task Foreword made itself — typed into the add screen and read as a one-off — has no
    /// Google id yet, so there is nothing to write back to. It is created there instead, which is
    /// what puts it in the owner's own task list rather than only on the sheet.
    /// </summary>
    [Fact]
    public async Task AOnceTaskWithNoGoogleIdIsCreatedInGoogle()
    {
        var fresh = new ForewordTask
        {
            Id = "new", Title = "Book the vet", Kind = TaskKind.Once,
            Due = new DateOnly(2026, 9, 20),
        };

        var result = await Editor().Save(fresh);

        google.Verify(g => g.AddTask("Book the vet", new DateOnly(2026, 9, 20)), Times.Once);
        Assert.True(result.Saved);
    }

    /// <summary>
    /// The degrade that matters. If Google refuses the write — no scope granted, a revoked token —
    /// the edit is kept here and the screen is TOLD, because silently keeping a divergent copy is
    /// exactly the drift the mirror exists to prevent, and doing it quietly is worse than failing.
    /// </summary>
    [Fact]
    public async Task ARefusedWriteIsKeptHereAndSaidOutLoud()
    {
        google.Setup(g => g.UpdateTask(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly?>()))
            .ThrowsAsync(new HttpRequestException("403 insufficient scope"));

        var result = await Editor().Save(GoogleTask("Call the orthodontist"));

        Assert.False(result.Saved);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Single(store.All());
        Assert.Equal("Call the orthodontist", store.All()[0].Title);
    }

    [Fact]
    public async Task DeletingARuleRemovesIt()
    {
        store.Save(Rule());

        await Editor().Delete(Rule());

        Assert.Empty(store.All());
    }

    /// <summary>
    /// Deleting a once task deletes Google's, because Google's is the only one. Removing the mirror
    /// and leaving the original would make it reappear on the next sync, which reads as an app that
    /// ignores you.
    /// </summary>
    [Fact]
    public async Task DeletingAOnceTaskDeletesGooglesCopy()
    {
        await Editor().Delete(GoogleTask());

        google.Verify(g => g.DeleteTask("list-1", "g-1"), Times.Once);
    }

    private sealed class FakeStore : ITaskStore
    {
        private readonly Dictionary<string, ForewordTask> tasks = [];

        public IReadOnlyList<ForewordTask> All() => [.. tasks.Values];

        public void Save(ForewordTask task) => tasks[task.Id] = task;

        public void Delete(string id) => tasks.Remove(id);

        public AwayPause Away { get; set; } = AwayPause.None;
    }
}
