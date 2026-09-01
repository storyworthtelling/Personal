using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface IChecklistPdfComposer
{
    byte[] Compose(
        IReadOnlyList<TodoItem> tasks,
        DateOnly date,
        IReadOnlyList<ParsedNote> unreadableCarryovers);
}
