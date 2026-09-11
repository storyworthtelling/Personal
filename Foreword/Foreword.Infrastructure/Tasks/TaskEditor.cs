using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;

namespace Foreword.Infrastructure.Tasks;

/// <summary>Whether an edit landed, and what to say when it did not.</summary>
public readonly record struct TaskSaveResult(bool Saved, string Message)
{
    public static TaskSaveResult Ok { get; } = new(true, string.Empty);
}

/// <summary>
/// Where an edit goes (D37).
///
/// <para>A once task is Google's. Its title and due date are written <b>back</b> rather than kept
/// here, so the two can never disagree — a Foreword copy that drifted from Google's would be two
/// answers to "what is this task called", with no way to tell which one the morning would print.
/// Everything else is Foreword's, because Google Tasks cannot express a window, a season or a
/// condition, and none of that travels the other way.</para>
/// </summary>
public class TaskEditor(ITaskStore store, IGoogleTasksClient google)
{
    public async Task<TaskSaveResult> Save(ForewordTask task)
    {
        if (task.Kind != TaskKind.Once)
        {
            store.Save(task);
            return TaskSaveResult.Ok;
        }

        try
        {
            if (task.GoogleTaskId is { Length: > 0 } taskId
                && task.GoogleListId is { Length: > 0 } listId)
            {
                await google.UpdateTask(listId, taskId, task.Title, task.Due);
            }
            else
            {
                // Typed here and read as a one-off, so Google has never seen it. Created there
                // rather than kept locally, which is what puts it in the owner's own task list
                // instead of only on the sheet.
                await google.AddTask(task.Title, task.Due);
            }

            // Deliberately NOT also kept here. Two copies is the thing the write-back exists to
            // avoid, and keeping one "just in case" is how they start disagreeing.
            store.Delete(task.Id);
            return TaskSaveResult.Ok;
        }
        catch (Exception ex)
        {
            // Google refused — no scope granted, a revoked token. The edit is kept here so it is not
            // lost, and the screen is TOLD: a divergent copy kept quietly is exactly the drift the
            // mirror exists to prevent, and doing it silently is worse than failing.
            store.Save(task);
            return new TaskSaveResult(false,
                $"Saved on this phone only — Google would not take the change ({ex.Message}). "
                + "It needs the Tasks write permission.");
        }
    }

    public async Task<TaskSaveResult> Delete(ForewordTask task)
    {
        if (task.Kind != TaskKind.Once)
        {
            store.Delete(task.Id);
            return TaskSaveResult.Ok;
        }

        try
        {
            if (task.GoogleTaskId is { Length: > 0 } taskId
                && task.GoogleListId is { Length: > 0 } listId)
            {
                // Google's copy is the only one. Removing the mirror and leaving the original would
                // make it reappear on the next sync, which reads as an app that ignores you.
                await google.DeleteTask(listId, taskId);
            }

            store.Delete(task.Id);
            return TaskSaveResult.Ok;
        }
        catch (Exception ex)
        {
            return new TaskSaveResult(false,
                $"Google would not remove it ({ex.Message}). It needs the Tasks write permission.");
        }
    }
}
