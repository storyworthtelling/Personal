namespace Index.Domain.Design;

/// <summary>
/// What is left of this head's own design literals, and why the one thing here still is.
///
/// <para>The <b>screens</b> no longer read any of it. Release B deleted this head's XAML
/// dictionary and its in-app palette and type ladder, and the app now takes both from the shared
/// <c>Storyworthtelling.Maui</c> kit — one palette, enforced by reflection against
/// <c>PaperColorTokens</c> rather than by a second hand-typed copy that could be wrong in the same
/// way. Unlike Folio, Index has no printed-page renderer yet (that lands with the worksheet in
/// Release F, through <c>Storyworthtelling.Paper</c>'s own Document profile), so there is no
/// printed side of this file left to keep either — the hex palette and the <c>Type</c> ladder are
/// simply gone, not moved.</para>
///
/// <para><see cref="Motion"/> is a recorded DESIGN.md value that nothing in this app reads yet.
/// Left rather than deleted: it is a fact, not a duplicate of the kit. (Index has no documented
/// Grain concept the way FocusPhone does, so there is nothing else to carry forward here.)</para>
/// </summary>
public static class DesignTokens
{
    /// <summary>Calm functional motion: two durations, decelerate curves, no haptics.</summary>
    public static class Motion
    {
        public const int QuickMs = 150;
        public const int StandardMs = 200;
        public const int UndoWindowMs = 6_000;
    }
}
