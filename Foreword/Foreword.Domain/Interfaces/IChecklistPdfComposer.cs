using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface IChecklistPdfComposer
{
    byte[] Compose(
        IReadOnlyList<TodoItem> tasks,
        DateOnly date,
        IReadOnlyList<ParsedNote> unreadableCarryovers);
}
