using Microsoft.Extensions.Options;

namespace Storyworthtelling.Voice;

/// <summary>
/// An <see cref="IOptions{TOptions}"/> built afresh on every read (audit 01 §18), so a key or setting entered after
/// launch, or restored from the vault, reaches the next call without a restart. The MAUI heads and Constitution each
/// kept a private copy.
/// </summary>
public sealed class LiveOptions<T>(Func<T> factory) : IOptions<T> where T : class
{
    public T Value => factory();
}
