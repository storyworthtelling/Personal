using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// MAUI logs "already a child of" every time a page's own content is put into the binder frame while the page still
/// holds it as its Content. The head cannot be loaded by this project, so the shape is read from the source: the
/// content is taken off the page before the frame adopts it.
/// </summary>
public class BinderWrapDetachesContentTests
{
    [Fact]
    public void WrapDetachesTheContentBeforeTheFrameAdoptsIt()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Ledger.Android", "Pages", "LedgerBinderPage.cs")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!, "Ledger.Android", "Pages", "LedgerBinderPage.cs"));

        Assert.DoesNotContain("well.Content = Content;", source, StringComparison.Ordinal);
        var detach = source.IndexOf("Content = null;", StringComparison.Ordinal);
        var adopt = source.IndexOf("well.Content = inner;", StringComparison.Ordinal);
        Assert.True(detach >= 0 && adopt > detach, "Content must be set to null before the well adopts it.");
    }
}
