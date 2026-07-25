namespace Storyboarder.Infrastructure.Services;

// One composed page for the in-app reader: where it sits (which volume, which page number within that
// volume, which chapter) and the on-disk rasters. ThumbPath drives the chapter-jump strip; ImagePath is
// the full page for reading and panel zoom.
public sealed record ReaderPage(
    int VolumeNumber,
    string VolumeTitle,
    int PageNumber,
    int ChapterNumber,
    string ChapterLabel,
    string ImagePath,
    string? ThumbPath);

// A finished book laid out for the reader: its title and every composed page in reading order across
// all volumes. Empty Pages means nothing has been composed yet (the reader has nothing to show).
public sealed record ReaderBook(string Title, IReadOnlyList<ReaderPage> Pages);
