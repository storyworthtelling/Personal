using Constitution.Domain.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;
using Storyworthtelling.Paper.Binder;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// The frame on Constitution's pages (owner's decision: every tablet document wears Foreword's). Constitution lays its
/// pages out in the tablet's own points, where its pen targets are measured, so the frame is drawn on the shared Letter
/// board and scaled onto the page: the same size as on Foreword's sheets, and not one target moved.
/// </summary>
public static class ConstitutionSheet
{
    public static readonly TabletSheet Frame = SheetFrame.LetterBoard;

    /// <summary>Signed in the documents' muted cerulean, the one colour a Constitution page carries; tabs cut from the cloth.</summary>
    public static readonly SheetSignature Signature =
        new("Constitution", AppIdentityTokens.ConstitutionDocument, AppIdentityTokens.ConstitutionGround, AppIdentityTokens.ConstitutionDeep);

    /// <summary>The names the footer line is printed under (<c>PageDraft.Footer</c>); the frame's folio prints them.</summary>
    public const string FooterName = "footer";

    public const string PageNumberName = "page-number";

    /// <summary>One tab per run of pages under the same section, each linked to the run's first page.</summary>
    public static IReadOnlyList<(BinderTab Tab, int FirstPage)> Tabs(IReadOnlyList<InkPageLayout> pages)
    {
        var tabs = new List<(BinderTab, int)>();
        for (var index = 0; index < pages.Count; index++)
        {
            if (index == 0 || pages[index].Section != pages[index - 1].Section)
            {
                var tab = new BinderTab(pages[index].Section, $"constitution-{tabs.Count + 1}", Signature.Cloth, Signature.Deep);
                tabs.Add((tab, index));
            }
        }

        var holds = BinderStrip.Fits(Frame.DesignHeight, SheetFrame.FirstTabTop);
        if (tabs.Count > holds)
        {
            throw new InvalidOperationException(
                $"The document has {tabs.Count} sections and the binder strip holds {holds} tabs; give its pages fewer sections.");
        }

        return tabs;
    }

    /// <summary>
    /// The frame for one page, drawn on the Letter board under a scale and handed back: the ground, the header signed
    /// "Constitution", the strip with this page's section open (anchored on the section's first page), and the folio
    /// from the page's own footer line.
    /// </summary>
    public static void Draw(SKCanvas canvas, InkPageLayout page, IReadOnlyList<(BinderTab Tab, int FirstPage)> tabs, int pageIndex)
    {
        SheetFrame.Clear(canvas);
        canvas.Save();
        canvas.Scale(Frame.Scale);

        SheetFrame.Header(canvas, Frame, page.Title, Signature);

        var active = tabs.Count - 1 - tabs.Reverse().TakeWhile(t => t.FirstPage > pageIndex).Count();
        SheetFrame.Binder(canvas, Frame, tabs.Select(t => t.Tab).ToList(), active, anchor: tabs[active].FirstPage == pageIndex);

        var footer = page.Printed.FirstOrDefault(item => item.Name == FooterName)?.Text;
        var number = page.Printed.FirstOrDefault(item => item.Name == PageNumberName)?.Text;
        if (footer is not null || number is not null)
        {
            SheetFrame.Folio(canvas, Frame, footer ?? string.Empty, number);
        }

        canvas.Restore();
    }
}
