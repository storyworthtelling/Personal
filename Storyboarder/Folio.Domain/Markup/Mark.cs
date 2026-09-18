using Folio.Domain.Readiness;

namespace Folio.Domain.Markup;

/// <summary>
/// The copyeditor's marks, and only these four. They are the ones a copyeditor already uses, which
/// is why the drawn page carries no legend: a caret means insert, a dele means cut, a query means
/// the story does not hold here, a check means keep.
/// </summary>
public enum MarkKind
{
    /// <summary>Insert — drawn as a caret in the gutter at the point of insertion.</summary>
    Caret,

    /// <summary>Cut — drawn as the dele loop, with the run struck through.</summary>
    Dele,

    /// <summary>A question about the story, drawn as an underline under the run.</summary>
    Query,

    /// <summary>Keep, the fourth tier's mark — an underline and a tick.</summary>
    Check,
}

/// <summary>
/// One note on one place in the manuscript.
///
/// <para><b>Nothing here can hold replacement prose, and that is structural rather than editorial</b>
/// (F40, guard G19). Gate 1's anatomy listed "line rewrites" among the marks, and the brief's first
/// principle — <i>nudge never write</i> — forbids ghost-writing the author's sentences. The way to
/// keep a principle is not to remember it: a record with a <c>SuggestedText</c> on it would be filled
/// in by the next prompt that could, and the product would quietly become a different product. So the
/// record has no member that could carry a replacement, and the guard fails if one appears.</para>
///
/// <para><see cref="Reason"/> says what the line is doing wrong and stops. <see cref="Label"/> is the
/// two or three words printed beside the glyph. <see cref="Provenance"/> is the section or reader
/// that raised it, because the drawn page ends every note with where it came from — a mark a
/// whole-book section also found is a louder mark.</para>
///
/// <para>The span is half-open, <c>[StartOffset, EndOffset)</c>, matching <c>Scene</c>'s. Both index
/// the same stored manuscript string, which is the only reason a mark can be trusted to land on the
/// line it means (finding R-6).</para>
/// </summary>
public sealed record Mark(
    MarkKind Kind,
    ReadinessTier Severity,
    int StartOffset,
    int EndOffset,
    string Label,
    string Reason,
    string Provenance)
{
    /// <summary>A caret marks a point, not a run, so its span is empty.</summary>
    public bool IsPoint => EndOffset <= StartOffset;

    public int Length => Math.Max(EndOffset - StartOffset, 0);
}
