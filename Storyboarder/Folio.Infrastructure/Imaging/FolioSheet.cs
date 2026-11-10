using Folio.Domain.Report;
using Storyworthtelling.Paper;
using Storyworthtelling.Paper.Binder;

namespace Folio.Infrastructure.Imaging;

/// <summary>One part of the revision report, with its binder tab's label, its folio name and its link.</summary>
public sealed record ReportSection(string Tab, string Name, string Destination);

/// <summary>
/// The revision report on the tablet's own page, in the frame every tablet document wears (owner's decision): drawn on
/// the board <see cref="PageGeometry"/> is measured in — A4's height, as wide as the glass is at that scale — and placed
/// on 445 × 594 with one scale, so the tabs sit at the glass's edge. Every page after the cover is signed with the
/// book's title and "Folio" in the owner's hand; the tabs are the report's parts, each linked to where it starts.
/// </summary>
public static class FolioSheet
{
    public static readonly TabletSheet Page = TabletSheet.Fill(PageGeometry.PageWidth, PageGeometry.PageHeight);

    /// <summary>Folio signs in its olive mark; its tabs are cut from the olive ground and lettered in the deep.</summary>
    public static readonly SheetSignature Signature =
        new("Folio", AppIdentityTokens.FolioMark, AppIdentityTokens.FolioGround, AppIdentityTokens.FolioDeep);

    public static readonly ReportSection Frame = new("Frame", "The frame", "folio-frame");
    public static readonly ReportSection Notes = new("Notes", "Whole-book notes", "folio-notes");
    public static readonly ReportSection Manuscript = new("Manuscript", "The manuscript, marked", "folio-manuscript");
    public static readonly ReportSection Readers = new("Readers", "The readers", "folio-readers");
    public static readonly ReportSection Plan = new("Plan", "What to do about it", "folio-plan");
    public static readonly ReportSection Verdict = new("Verdict", "Verdict", "folio-verdict");

    /// <summary>The report's parts in the order it binds them; a report has a tab for each it actually prints.</summary>
    public static readonly IReadOnlyList<ReportSection> All = [Frame, Notes, Manuscript, Readers, Plan, Verdict];

    public static IReadOnlyList<BinderTab> Tabs(IReadOnlyList<ReportSection> present) =>
        present.Select(section => new BinderTab(section.Tab, section.Destination, Signature.Cloth, Signature.Deep)).ToList();
}
