using Foreword.Domain.Prayer;

namespace Foreword.Domain.Interfaces;

/// <summary>
/// Reads the hand-written journal page back into the two things it holds: what was written in the
/// free space, and what was written under "Add to the list".
/// </summary>
public interface IJournalTranscriber
{
    Task<JournalRead> Read(byte[] annotatedPdf, DateOnly date);
}
