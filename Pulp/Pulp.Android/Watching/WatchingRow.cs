using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Library;

namespace Pulp.Watching;

/// <summary>
/// One Continue watching row, read and worded off the UI thread so the page only lays it out: the
/// Domain's row, the title's name and cached copy, the lines <see cref="Pulp.Domain.Watching.ContinueRowText"/>
/// wrote, the best home and its service's name (for Play and the card's header), and the poster as a
/// local file for <see cref="Codex.Ui.CoverCache"/> — null when there is none, which the artwork
/// window answers with the bookplate.
/// </summary>
public sealed record WatchingRow(
    ContinueRow Row,
    string Name,
    CatalogueTitle? Title,
    string? NextUp,
    string? Where,
    Home? Home,
    string? ServiceName,
    string? PosterPath);
