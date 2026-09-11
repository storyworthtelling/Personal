using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;
using GoogleTask = global::Google.Apis.Tasks.v1.Data.Task;

namespace Foreword.Infrastructure.Google;

// Reads and creates the user's Google Tasks across their task lists.
public class GoogleTasksClient : IGoogleTasksClient
{
    private readonly IGoogleClientFactory factory;

    public GoogleTasksClient(IGoogleClientFactory factory)
    {
        this.factory = factory;
    }

    public async Task<IReadOnlyList<TodoItem>> GetMyTasks()
    {
        var service = factory.CreateTasksService();
        var lists = (await service.Tasklists.List().ExecuteAsync()).Items ?? [];

        var items = new List<TodoItem>();
        foreach (var list in lists)
        {
            var request = service.Tasks.List(list.Id);

            // BOTH flags. The reference says it in as many words: "showHidden must also be True to
            // show tasks completed in first party clients, such as the web UI and Google's mobile
            // apps" — which is exactly the case T3 is about. Set one and not the other and nothing
            // fails: the request succeeds, the list comes back, and the closed task is simply
            // absent, so Foreword draws the day as though it had never existed.
            request.ShowCompleted = true;
            request.ShowHidden = true;

            var tasks = (await request.ExecuteAsync()).Items ?? [];
            items.AddRange(tasks.Select(task => ToTodoItem(task, list.Id)));
        }

        return items;
    }

    public async Task AddTask(string title, DateOnly? due)
    {
        var service = factory.CreateTasksService();
        var task = new GoogleTask
        {
            Title = title,
            // Google Tasks stores due as an RFC3339 string with date-only semantics (time ignored).
            Due = due is { } d ? d.ToString("yyyy-MM-dd'T'00:00:00.000'Z'") : null
        };

        // Adds to the default task list.
        await service.Tasks.Insert(task, "@default").ExecuteAsync();
    }

    /// <summary>
    /// Internal rather than private so it can be tested for real. Both ids are kept because the API
    /// addresses a task as lists/{tasklist}/tasks/{task}, and an edit written back to half an
    /// address goes nowhere.
    /// </summary>
    internal static TodoItem ToTodoItem(GoogleTask task, string listId) => new()
    {
        Id = task.Id ?? string.Empty,
        ListId = listId,
        Title = task.Title ?? "(untitled)",
        Completed = string.Equals(task.Status, "completed", StringComparison.OrdinalIgnoreCase),
        Due = DateTimeOffset.TryParse(task.Due, out var d) ? DateOnly.FromDateTime(d.Date) : null
    };
}
