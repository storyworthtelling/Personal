namespace Folio.Domain.Markup;

/// <summary>
/// The manuscript text as the report is allowed to draw it — not as an extractor faithfully
/// preserved it.
///
/// <para><b>The extractors are right and this is not their bug.</b> A .docx author who indents a
/// paragraph with a literal Tab keystroke gets a real <c>'\t'</c> in <c>DocxTextExtractor</c>'s
/// output, and that is correct: the extractor's whole job is to reproduce the document, and
/// <c>RtfTextExtractor</c> renders a Word/RTF tab the same way. Nothing upstream of the page is
/// wrong. What breaks is that a report page has no glyph to draw for a tab — the embedded prose
/// font maps it to <c>.notdef</c>, so the character printed as a small black box, and reading the
/// PDF back showed a bare NUL at the start of every paragraph the author had indented. Found on the
/// first report generated from a real manuscript on a real device; nothing synthetic ever tabs a
/// paragraph.</para>
///
/// <para><b>A tab becomes one space, never dropped.</b> <see cref="Domain.Markup.MarkSource"/> and
/// the page's own line-wrapping both work in offsets into this exact string, and a mark quoted from
/// three paragraphs down has to land on the right line regardless of how many tabs came before it.
/// Removing the character would shift every offset after it; substituting one printable character
/// for one unprintable one shifts nothing. The report draws no first-line indents anywhere else, so
/// a lost tab costs nothing typographic — the paragraph still reads as its own block, just flush
/// left like every other one.</para>
/// </summary>
public static class ManuscriptText
{
    public static string ForPrint(string manuscript) =>
        string.IsNullOrEmpty(manuscript) ? manuscript : manuscript.Replace('\t', ' ');
}
