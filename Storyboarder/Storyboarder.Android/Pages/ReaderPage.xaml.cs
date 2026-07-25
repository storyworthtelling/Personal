using System.Collections.ObjectModel;
using Storyboarder.Infrastructure.Services;
// Disambiguate the read-model record from this page class (both are named ReaderPage).
using ReaderModelPage = Storyboarder.Infrastructure.Services.ReaderPage;

namespace Storyboarder.Android.Pages;

// Reads a finished book in-app: its composed pages in order, with a chapter-jump control and tap-to-zoom
// on any page. Constructed with the already-loaded ReaderBook, so it needs no services of its own.
public partial class ReaderPage : ContentPage
{
    private readonly ObservableCollection<PageRow> pages = [];
    // First page index (into `pages`) of each chapter, parallel to the picker's items, for the jump.
    private readonly List<int> chapterStartIndex = [];

    public ReaderPage(ReaderBook book)
    {
        InitializeComponent();
        TitleLabel.Text = book.Title;
        PagesList.ItemsSource = pages;

        foreach (var page in book.Pages)
            pages.Add(new PageRow(page.ImagePath, Caption(page)));

        BuildChapterJump(book);
    }

    // One picker entry per distinct chapter, in reading order, remembering the page it starts on. Chapter
    // labels can be blank (a book converted before chapter detection); those fall back to a page marker.
    private void BuildChapterJump(ReaderBook book)
    {
        var seen = new HashSet<int>();
        for (var i = 0; i < book.Pages.Count; i++)
        {
            var page = book.Pages[i];
            if (!seen.Add(page.ChapterNumber))
                continue;
            var label = string.IsNullOrWhiteSpace(page.ChapterLabel)
                ? (page.ChapterNumber > 0 ? $"Chapter {page.ChapterNumber}" : $"Volume {page.VolumeNumber}, page {page.PageNumber}")
                : page.ChapterLabel;
            ChapterPicker.Items.Add(label);
            chapterStartIndex.Add(i);
        }

        // Only worth showing when there's more than one place to jump to.
        ChapterBar.IsVisible = chapterStartIndex.Count > 1;
    }

    private void OnChapterSelected(object? sender, EventArgs e)
    {
        var index = ChapterPicker.SelectedIndex;
        if (index < 0 || index >= chapterStartIndex.Count)
            return;
        var pageIndex = chapterStartIndex[index];
        if (pageIndex >= 0 && pageIndex < pages.Count)
            PagesList.ScrollTo(pages[pageIndex], position: ScrollToPosition.Start, animate: true);
    }

    private async void OnPageTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Image { BindingContext: PageRow row })
            await Navigation.PushAsync(new PageZoomPage(row.ImagePath));
    }

    private static string Caption(ReaderModelPage page) =>
        page.VolumeTitle is { Length: > 0 } title
            ? $"{title} · page {page.PageNumber}"
            : $"Volume {page.VolumeNumber} · page {page.PageNumber}";

    // Display projection of a composed page for the list.
    public sealed record PageRow(string ImagePath, string Caption);
}
