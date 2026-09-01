using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface IGoogleTasksClient
{
    // The user's own outstanding tasks across their task lists.
    Task<IReadOnlyList<TodoItem>> GetMyTasks();

    Task AddTask(string title, DateOnly? due);
}
