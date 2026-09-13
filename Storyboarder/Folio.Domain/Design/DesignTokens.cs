namespace Folio.Domain.Design;

/// <summary>
/// What is left of this head's own design literals, and why each piece is still here.
///
/// <para>The <b>screens</b> no longer read any of it. P10 Release C deleted this head's XAML
/// dictionary and its in-app type ladder, and the app now takes both from the shared
/// <c>Storyworthtelling.Maui</c> kit — one palette, enforced by reflection against
/// <c>PaperColorTokens</c> rather than by a second hand-typed copy that could be wrong in the same
/// way.</para>
///
/// <para>The <b>printed</b> side has gone the same way. Release E moved the report's colour onto
/// <c>Storyworthtelling.Paper</c>'s Document profile and its geometry onto <c>PageGeometry</c>, so
/// the palette that used to sit here — including an <c>Accent</c> that was still the retired
/// terracotta — and the <c>Pdf</c> block of 150-DPI pixel sizes are both gone rather than merely
/// unread. An unread constant is an invitation, and those read as points while being pixels.</para>
///
/// <para><see cref="Motion"/> and <see cref="Grain"/> are recorded DESIGN.md values that nothing in
/// this app reads yet. Left rather than deleted: they are facts, not duplicates of the kit.</para>
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

    /// <summary>Paper grain: deterministic seeded ink noise (DESIGN.md §6).</summary>
    public static class Grain
    {
        public const int TileSize = 256;
        public const int Alpha = 32;         // of 255 — ~12.5% ink
        public const int Density = 6;        // ~1 in Density pixels carries grain
        public const uint Seed = 1913;
    }
}
