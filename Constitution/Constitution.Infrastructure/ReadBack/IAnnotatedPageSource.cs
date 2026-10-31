namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The tablet's cloud, as the read-back uses it: find a document by the title delivery uploaded it under,
/// fetch it with whatever pen strokes the cloud rendered onto it, and send it to the trash. Failures are
/// the transport's (<see cref="HttpRequestException"/>, a timeout); an absent document is a null, not an error.
/// </summary>
public interface IAnnotatedPageSource
{
    /// <summary>The document with this exact title, flattened to PDF; null when none is on the tablet.</summary>
    Task<TabletDocument?> Download(string title);

    /// <summary>The id of the document with this title without fetching its pages; null when none is on the tablet.</summary>
    Task<string?> Locate(string title);

    /// <summary>Moves the document to the tablet's trash.</summary>
    Task Trash(string documentId);
}
