using Foreword.Domain.Tasks;

namespace Foreword.Domain.Interfaces;

/// <summary>
/// Foreword's own tasks — the rules and smart tasks Google Tasks cannot express — and the away
/// pause.
///
/// <para>Once tasks are deliberately <b>not</b> here: they live in Google and are mirrored, so that
/// the two can never disagree about a title or a due date.</para>
/// </summary>
public interface ITaskStore
{
    IReadOnlyList<ForewordTask> All();

    void Save(ForewordTask task);

    void Delete(string id);

    AwayPause Away { get; set; }
}
