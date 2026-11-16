using System.Security.Cryptography;
using System.Text;
using Folio.Domain.Analysis;
using Folio.Domain.Readiness;

namespace Folio.Domain.Markup;

/// <summary>
/// A finding's short, stable name (R15): the revision plan says which findings each item draws on by
/// these keys, and the report links the item to the place the finding is written.
///
/// <para>The key is the finding's own content, hashed — never a position. The plan is written in one pass
/// and the report drawn in another, perhaps re-rendered later, and both have to name one finding the same
/// way; a counter would move as soon as a finding above it did. Eight hex digits are short enough for a
/// model to copy without slips and leave a collision among a book's few hundred findings out of reach.</para>
/// </summary>
public static class FindingKey
{
    public static string Of(params string?[] parts)
    {
        var joined = string.Join('\u001f', parts.Select(part => (part ?? string.Empty).Trim()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return "f" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}

/// <summary>One finding as the plan's synthesis is shown it: its key, what it is, where, and what it says.</summary>
public sealed record FindingEntry(string Key, string Source, string Label, string Chapter, string Text, ReadinessTier Severity);

/// <summary>
/// Every finding the report writes on the manuscript, keyed — the list the revision plan's synthesis
/// draws on (R15). It is <see cref="MarkSource"/>'s own walk, so the keys are exactly the ones the marks and
/// chapter notes carry, whatever the manuscript ends up placing.
/// </summary>
public static class FindingCatalogue
{
    public static IReadOnlyList<FindingEntry> Of(BookAnalysisResult analysis) =>
        MarkSource.Build(string.Empty, analysis).Findings;
}
