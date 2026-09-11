using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;
using Foreword.Infrastructure.Calendar;

namespace Foreword.Android;

/// <summary>
/// Foreword's own tasks and the away pause, on the phone.
///
/// <para>Once tasks are deliberately absent: they live in Google and are mirrored, so the two can
/// never disagree about a title or a due date. What is here is the part Google Tasks cannot express
/// — a window, a season, a condition — plus the pause, which is the one thing the owner asked for
/// that Google has no shape for at all.</para>
///
/// <para>Written through the same codec the calendar screens use, so the enums go to the wire as
/// words. That is not a style choice: a document written with numbers and read expecting words comes
/// back empty, silently, and the morning prints without any of the owner's rules on it.</para>
/// </summary>
public sealed class TaskStore : ITaskStore
{
    public IReadOnlyList<ForewordTask> All() =>
        CalendarMappingCodec.Read<List<ForewordTask>>(CredentialStore.TasksJson) ?? [];

    public void Save(ForewordTask task)
    {
        var tasks = All().ToList();
        var existing = tasks.FindIndex(t => t.Id == task.Id);

        if (existing >= 0)
        {
            tasks[existing] = task;
        }
        else
        {
            tasks.Add(task);
        }

        Write(tasks);
    }

    public void Delete(string id) => Write([.. All().Where(task => task.Id != id)]);

    public AwayPause Away
    {
        get => CalendarMappingCodec.Read<AwayPause>(CredentialStore.AwayJson) ?? AwayPause.None;
        set => CredentialStore.AwayJson = CalendarMappingCodec.Write(value);
    }

    private static void Write(IReadOnlyList<ForewordTask> tasks) =>
        CredentialStore.TasksJson = CalendarMappingCodec.Write(tasks);
}
