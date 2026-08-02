using FocusPhone.Domain.Music;

namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// The model-backed half of Listening Genius: folds fresh <see cref="TasteEvidence"/> into the durable
/// <see cref="TasteProfile"/>. An interface so the pipeline can fake it in tests and a keyless app can
/// supply a no-op. Failures propagate; <see cref="TasteProfileRefresher"/> turns them into a graceful
/// no-change.
/// </summary>
public interface ITasteProfileClient
{
    Task<TasteProfile> RefineAsync(TasteProfile existing, TasteEvidence evidence, CancellationToken cancellationToken);
}
