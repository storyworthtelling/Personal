namespace DailyLoader.Domain.Models;

// A single Google Tasks item rendered as a checkbox on the daily checklist.
public class TodoItem
{
    public required string Title { get; init; }
    public DateOnly? Due { get; init; }
    public bool Completed { get; init; }
}
