using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Domain.Readiness;
using Folio.Domain.Report;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Imaging;

/// <summary>
/// The manuscript with the report's findings written on it — the phase's largest surface and the one
/// the author actually revises from (mockup p. 61).
///
/// <para>Four proofreading marks, because a marked page is a thing that already exists and the
/// author has seen one: a <b>caret</b> where something is missing, a <b>dele</b> through what should
/// go, a <b>query</b> under what is not clear, and a <b>check</b> under what is working. Each is
/// drawn, not typed — they are marks on a page, so they are strokes.</para>
///
/// <para><b>The mark says what is wrong and stops.</b> Nothing here writes the author a replacement
/// line; the note carries a reason (F40, guarded by <c>NudgeNeverWriteTests</c>). A dele means "this
/// is doing nothing", not "put this instead".</para>
///
/// <para><b>The page breaks early rather than separating a note from its line</b> — the owner's
/// Release G gate decision, arithmetic in <see cref="MarginColumn"/>. This class asks where the page
/// ends and honours the answer; it does not decide.</para>
/// </summary>
internal static class MarkedManuscript
{
    private const float MarkStroke = 1.1f;

    /// <summary>
    /// Fewest lines worth carrying on beneath a scene's block. Less than this and the sheet is
    /// finished: a margin column cannot pack its notes into two lines of room.
    /// </summary>
    private const int MinimumRoomInLines = 4;

    /// <param name="scenes">
    /// Each scene's picture and notes, inserted as a block under the scene's own last line. The
    /// manuscript is cut at those lines and carries on beneath each block, on the same sheet when
    /// there is room for it.
    /// </param>
    public static void Render(
        PageWriter writer, string manuscript, IReadOnlyList<Mark> marks, IReadOnlyList<SceneEvidence>? scenes = null)
    {
        if (string.IsNullOrWhiteSpace(manuscript))
            return;

        using var prose = ReportType.Manuscript();
        using var note = ReportType.Small();

        var lines = SkiaText.WrapSpans(prose, manuscript, (float)MarkupGeometry.TextWidth);
        if (lines.Count == 0)
            return;

        var spans = lines
            .Select((line, index) => new LineSpan(index, line.Start, line.End))
            .ToList();

        var all = marks ?? [];

        // Two different questions, and conflating them was a real defect: a mark is *drawn* on every
        // line it touches, while its note is *anchored* to the first of them.
        var drawnOn = all
            .SelectMany(mark => MarkPlacement.Lines(mark, spans).Select(line => (Line: line, Mark: mark)))
            .ToLookup(placed => placed.Line, placed => placed.Mark);
        var anchored = Anchor(all, spans);

        // Each block follows the line its scene ends on; one with no known end follows the last line
        // rather than being lost. The sort is stable, so scenes ending on one line keep reading order.
        var pending = new Queue<(int Line, SceneEvidence Scene)>(
            (scenes ?? [])
                .Select(scene => (
                    Line: SceneBlockPlan.LineAfter(manuscript, spans, scene.EndOffset) ?? lines.Count - 1,
                    Scene: scene))
                .OrderBy(placed => placed.Line));

        var first = 0;
        var continuePage = false;
        while (first < lines.Count)
        {
            var limit = pending.Count > 0 ? Math.Max(pending.Peek().Line + 1, first + 1) : lines.Count;
            first = writer.ManuscriptPage(manuscript, lines, first, limit, continuePage, drawnOn, anchored, prose, note);

            var drew = false;
            while (pending.Count > 0 && pending.Peek().Line < first)
            {
                DrawSceneBlock(writer, pending.Dequeue().Scene);
                drew = true;
            }

            // The next scene carries on under the block it just got, unless the block ended the sheet.
            continuePage = drew && (writer.OnFreshPage || writer.Remaining >= MinimumRoomInLines * MarkupGeometry.LineHeight);
        }

        while (pending.Count > 0)
            DrawSceneBlock(writer, pending.Dequeue().Scene);
    }

    /// <summary>
    /// One scene's block: its citation, the picture a reader forms from the prose — or, for a scene
    /// that changed, the previous draft's beside it — then what the scene is for and whether it earns
    /// its place. It spans the whole measure, because it sits between lines of prose rather than
    /// beside them, and it is kept whole on one sheet where the picture allows.
    /// </summary>
    public static void DrawSceneBlock(PageWriter writer, SceneEvidence scene)
    {
        using var body = ReportType.Body();
        using var italic = ReportType.BodyItalic();

        writer.Gap(PageGeometry.BlockGap);
        writer.KeepTogether(PageGeometry.SceneImageHeight + 80f);
        writer.Hairline();
        writer.Paragraph(string.IsNullOrWhiteSpace(scene.Citation) ? "Scene" : scene.Citation, italic, ReportPalette.Rule);

        if (scene.ShowsPair)
            writer.ScenePair(scene.PreviousImage!, scene.Image!);
        else if (scene.Image is { Length: > 0 })
            writer.Image(scene.Image);
        else
            writer.Paragraph("Image unavailable for this scene.", italic, ReportPalette.InkMuted);

        if (!string.IsNullOrWhiteSpace(scene.Purpose))
            writer.Paragraph(scene.Purpose, body, ReportPalette.Ink);
        if (!string.IsNullOrWhiteSpace(scene.Verdict))
            writer.Paragraph(scene.Verdict, italic, ReportPalette.InkMuted);

        writer.Hairline();
        writer.Gap(PageGeometry.BlockGap);
    }

    /// <summary>
    /// Every mark tied to the line it is drawn on. A mark whose span the wrap put nowhere — an offset
    /// past the end of the text, which a model can produce — is dropped rather than drawn at the
    /// nearest line, because a mark on the wrong sentence is worse than a missing one.
    /// </summary>
    private static ILookup<int, Mark> Anchor(IReadOnlyList<Mark> marks, IReadOnlyList<LineSpan> spans)
    {
        return marks
            .Select(mark => (Mark: mark, Line: MarkPlacement.AnchorLine(mark, spans)))
            .Where(placed => placed.Line is not null)
            .ToLookup(placed => placed.Line!.Value, placed => placed.Mark);
    }

    /// <summary>Draws one mark over the run of its line that it covers.</summary>
    /// <summary>
    /// Draws one line's worth of a mark. <paramref name="tail"/> says whether this run <i>ends</i>
    /// the mark: the dele's loop and the check's tick are terminal flourishes and belong once, at the
    /// end of the whole thing. Drawn per line they read as several marks rather than one — which is
    /// what a struck sentence spanning two lines printed as before this was measured on the page.
    /// </summary>
    public static void DrawMark(
        SKCanvas canvas, Mark mark, float left, float baseline, float width, SKColor ink, bool tail)
    {
        using var pen = new SKPaint
        {
            Color = ink, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = MarkStroke,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };

        switch (mark.Kind)
        {
            case MarkKind.Caret:
                // Between the words, pointing up into the gap: the classic "something belongs here".
                var apex = baseline + 1f;
                using (var path = new SKPath())
                {
                    path.MoveTo(left - 3.5f, apex + 4.5f);
                    path.LineTo(left, apex);
                    path.LineTo(left + 3.5f, apex + 4.5f);
                    canvas.DrawPath(path, pen);
                }
                break;

            case MarkKind.Dele:
                // Struck through, with the loop that says delete rather than emphasise.
                var middle = baseline - 3.5f;
                canvas.DrawLine(left, middle, left + width, middle, pen);
                if (!tail)
                    break;
                using (var loop = new SKPath())
                {
                    loop.MoveTo(left + width, middle);
                    loop.CubicTo(
                        left + width + 5f, middle - 5f,
                        left + width + 6f, middle + 4f,
                        left + width + 1.5f, middle + 3f);
                    canvas.DrawPath(loop, pen);
                }
                break;

            case MarkKind.Query:
                // Underlined only: the prose is not wrong, it is unclear.
                canvas.DrawLine(left, baseline + 2f, left + width, baseline + 2f, pen);
                break;

            case MarkKind.Check:
                // Underlined and ticked, so the one mark that is praise cannot be mistaken for a query.
                canvas.DrawLine(left, baseline + 2f, left + width, baseline + 2f, pen);
                if (!tail)
                    break;
                using (var tick = new SKPath())
                {
                    tick.MoveTo(left + width + 3f, baseline - 1f);
                    tick.LineTo(left + width + 5.5f, baseline + 2f);
                    tick.LineTo(left + width + 10f, baseline - 5f);
                    canvas.DrawPath(tick, pen);
                }
                break;
        }
    }

    /// <summary>The severity's printed hue — the same four the chart and the plan use.</summary>
    public static SKColor Hue(ReadinessTier severity) => severity switch
    {
        ReadinessTier.Must => ReportPalette.MustFix,
        ReadinessTier.Should => ReportPalette.Should,
        ReadinessTier.Consider => ReportPalette.Consider,
        _ => ReportPalette.Great,
    };
}
