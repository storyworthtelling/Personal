using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;
using GoogleTask = global::Google.Apis.Tasks.v1.Data.Task;

namespace DailyLoader.Infrastructure.Google;

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
            request.ShowCompleted = false;
            var tasks = (await request.ExecuteAsync()).Items ?? [];
            items.AddRange(tasks.Select(ToTodoItem));
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

    private static TodoItem ToTodoItem(GoogleTask task) => new()
    {
        Title = task.Title ?? "(untitled)",
        Completed = string.Equals(task.Status, "completed", StringComparison.OrdinalIgnoreCase),
        Due = DateTimeOffset.TryParse(task.Due, out var d) ? DateOnly.FromDateTime(d.Date) : null
    };
}
