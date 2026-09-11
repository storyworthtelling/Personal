using Foreword.Infrastructure.Google;
using Xunit;
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;

namespace Foreword.Tests.Google;

/// <summary>
/// What Foreword keeps of a Google task, and why each piece is load-bearing.
///
/// <para>Two things were being thrown away, and both are needed by decisions taken at Gate 3.</para>
///
/// <para><b>The ids.</b> D37 writes a once task's title and due date back to Google, and the API
/// addresses a task as <c>lists/{tasklist}/tasks/{task}</c> — <i>both</i> ids, read from the
/// published reference. Without them an edit made in Foreword has nowhere to go, so the two copies
/// drift apart, which is the exact thing the mirror exists to prevent.</para>
///
/// <para><b>Completion.</b> T3 shows a task closed in Google's own app struck through, so the owner
/// can see why it will not print — one that simply vanished would read as one Foreword lost.</para>
/// </summary>
public class GoogleTasksMappingTests
{
    [Fact]
    public void BothIdsAreKeptSoAnEditCanBeWrittenBack()
    {
        var mapped = GoogleTasksClient.ToTodoItem(
            new GoogleTask { Id = "task-123", Title = "Call the dentist" }, listId: "list-abc");

        Assert.Equal("task-123", mapped.Id);
        Assert.Equal("list-abc", mapped.ListId);
    }

    [Fact]
    public void ACompletedTaskIsMappedAsCompletedRatherThanDropped()
    {
        var mapped = GoogleTasksClient.ToTodoItem(
            new GoogleTask { Id = "t", Title = "Dry cleaning", Status = "completed" }, "l");

        Assert.True(mapped.Completed);
    }

    [Fact]
    public void ADueDateIsReadAsADate()
    {
        var mapped = GoogleTasksClient.ToTodoItem(
            new GoogleTask { Id = "t", Title = "Call", Due = "2026-09-18T00:00:00.000Z" }, "l");

        Assert.Equal(new DateOnly(2026, 9, 18), mapped.Due);
    }

    [Fact]
    public void ATaskWithNoTitleStillHasOne()
    {
        Assert.False(string.IsNullOrWhiteSpace(
            GoogleTasksClient.ToTodoItem(new GoogleTask { Id = "t" }, "l").Title));
    }
}
