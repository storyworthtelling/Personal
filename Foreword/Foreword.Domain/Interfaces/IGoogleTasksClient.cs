using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface IGoogleTasksClient
{
    // The user's own outstanding tasks across their task lists.
    Task<IReadOnlyList<TodoItem>> GetMyTasks();

    Task AddTask(string title, DateOnly? due);

    /// <summary>
    /// Writes a once task's title and due date back to Google (D37). Addressed by BOTH ids, because
    /// the API addresses a task as <c>lists/{tasklist}/tasks/{task}</c>.
    ///
    /// <para>Needs the Tasks <b>write</b> scope rather than the read-only one. Without it this
    /// throws and the caller keeps the edit locally and says so — a divergent copy kept quietly is
    /// exactly the drift the mirror exists to prevent.</para>
    /// </summary>
    Task UpdateTask(string listId, string taskId, string title, DateOnly? due);

    Task DeleteTask(string listId, string taskId);
}
