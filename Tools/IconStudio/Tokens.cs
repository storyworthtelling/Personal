using SkiaSharp;

namespace IconStudio;

/// <summary>
/// The Paper &amp; Ink palette as used by the icon family. Mirrors DESIGN.md at
/// the repository root; IconStudio.Tests keeps these values honest.
/// </summary>
public static class Tokens
{
    public const string PaperHex = "#FAF8F2";
    public const string InkHex = "#33322B";
    public const string AccentHex = "#C2542F";
    public const string HairlineHex = "#E3DCC9";

    // Flyleaf's identity (warm study amber) — its icon is a cream bookplate on this warm ground.
    public const string FlyleafWarmHex = "#C9803A";
    public const string FlyleafMarkHex = "#B4552F";
    public const string FlyleafDeepHex = "#8A4A22";

    public static readonly SKColor Paper = SKColor.Parse(PaperHex);
    public static readonly SKColor Ink = SKColor.Parse(InkHex);
    public static readonly SKColor Accent = SKColor.Parse(AccentHex);
    public static readonly SKColor Hairline = SKColor.Parse(HairlineHex);
    // Score's identity (teal-slate) — its icon is a keyboard on this ground.
    public const string ScoreGroundHex = "#93ABA5";
    public const string ScoreMarkHex = "#436A61";
    public const string ScoreDeepHex = "#263B36";

    public static readonly SKColor ScoreGround = SKColor.Parse(ScoreGroundHex);

    // Post's identity (deep indigo, sampled from the cover's BINDING rather than its warm
    // mailbox painting — see DESIGN.md §3). Its icon is that cover: the indigo cloth as the
    // ground, with the painting set into a window on the foreground.
    public const string PostGroundHex = "#A1A1C7";
    public const string PostMarkHex = "#5C5894";
    public const string PostDeepHex = "#333252";

    // Foreword's identity: sunrise, OKLCH hue 47°, locked at gate 3. The icon is its cover —
    // the engraved doorstep set into a window on this ground. It sits between Flyleaf's warm
    // 38.6° and the accent terracotta, which is deliberate: the two apps are neighbours in the
    // morning and their covers were chosen to read as the same library.
    public const string ForewordFillHex = "#E8B99A";
    public const string ForewordGroundHex = "#DFA277";
    public const string ForewordSunriseHex = "#CA662D";
    public const string ForewordDeepHex = "#6E3512";


    // Folio: olive, OKLCH hue 107°, locked at gate 3. Committed at the CENTRE of the 105–109 window
    // rather than at the art's own 105.4°, which clears the caution ochre by only 20.4°: a re-crop or
    // a regenerated source would drift a measured value under §3's 20° binding and break the build
    // with nothing visibly different. By gate 3 the wheel had four gaps left, two too narrow to use —
    // this is the only window Folio fits.
    public const string FolioFillHex = "#C1C197";
    public const string FolioGroundHex = "#919059";
    public const string FolioMarkHex = "#6B681E";
    public const string FolioTextHex = "#575410";
    public const string FolioDeepHex = "#3B3902";

    // The binding, and deliberately NOT the identity: §12 does not require a cloth to match the
    // identity hue, and this red sits about 3° from the problem rose — fine for a binding, and
    // disqualifying for an identity token. IdentityLadderTests reads the ladder, never this.
    public const string FolioClothHex = "#B32738";

    public static readonly SKColor ForewordFill = SKColor.Parse(ForewordFillHex);
    public static readonly SKColor ForewordGround = SKColor.Parse(ForewordGroundHex);
    public static readonly SKColor ForewordSunrise = SKColor.Parse(ForewordSunriseHex);
    public static readonly SKColor ForewordDeep = SKColor.Parse(ForewordDeepHex);

    public static readonly SKColor FolioFill = SKColor.Parse(FolioFillHex);
    public static readonly SKColor FolioGround = SKColor.Parse(FolioGroundHex);
    public static readonly SKColor FolioMark = SKColor.Parse(FolioMarkHex);
    public static readonly SKColor FolioText = SKColor.Parse(FolioTextHex);
    public static readonly SKColor FolioDeep = SKColor.Parse(FolioDeepHex);
    public static readonly SKColor FolioCloth = SKColor.Parse(FolioClothHex);

    public static readonly SKColor PostGround = SKColor.Parse(PostGroundHex);
    public static readonly SKColor PostMark = SKColor.Parse(PostMarkHex);
    public static readonly SKColor PostDeep = SKColor.Parse(PostDeepHex);

    // Plate's identity (magenta, the one hue in the suite the app's own art picked
    // unprompted — DESIGN.md §3). Unlike Post, Plate's BINDING is the LIGHT ground tier
    // with the framing inverted to ink: its cover art is a near-black lens carrying almost
    // no chroma, so §12's "a binding that harmonises with its art makes the book hardest to
    // pick out of the row" points the other way here. That is Score's construction.
    public const string PlateGroundHex = "#B69BB6";
    public const string PlateMarkHex = "#7A4F7B";
    public const string PlateDeepHex = "#442D44";

    public static readonly SKColor PlateGround = SKColor.Parse(PlateGroundHex);
    public static readonly SKColor PlateMark = SKColor.Parse(PlateMarkHex);
    public static readonly SKColor PlateDeep = SKColor.Parse(PlateDeepHex);

    public static readonly SKColor FlyleafWarm = SKColor.Parse(FlyleafWarmHex);
    public static readonly SKColor FlyleafMark = SKColor.Parse(FlyleafMarkHex);
    public static readonly SKColor FlyleafDeep = SKColor.Parse(FlyleafDeepHex);

    // Index: the bookplate green, OKLCH hue 129°, locked at gate 3 (D115, D117). Sampled from
    // the cover painting itself — the median of its green pixels, at the art's own chroma — the
    // same method Folio's olive and Foreword's sunrise used before it.
    //
    // This is a RECORDED DEPARTURE from DESIGN.md §3's ≥20°-from-every-role floor: 129° is 0°
    // from the Positive role (`#517025` mark, OKLCH ~129°) — Index's identity hue and Positive
    // ARE the same hue. It is recorded here rather than resampled or rotated because D116 makes
    // the collision inert rather than a status/identity ambiguity: Index's own pages draw NO
    // Positive role anywhere — a paid-off loan is Resolved (settled, crossed out per D12) rather
    // than shown Positive, and an under-limit budget line takes no colour at all — so nothing on
    // an Index page could be mistaken for the identity wearing a status. It is a single, named
    // exception carved into the suite's hue-separation guard for Index against Positive only;
    // every other role, and every other app's identity, still has to clear 20° from Index.
    public const string IndexFillHex = "#B6C3A9";
    public const string IndexGroundHex = "#839372";
    public const string IndexMarkHex = "#596C45";
    public const string IndexTextHex = "#475735";
    public const string IndexDeepHex = "#2F3C21";

    // The binding, and deliberately NOT required to match the identity hue (see Folio's and
    // Post's cloth comments above): §12 does not require a cloth to match the identity, and
    // Index's cloth is simply the identity hue deepened rather than a separately chosen colour.
    public const string IndexClothHex = "#4E5F3A";

    public static readonly SKColor IndexFill = SKColor.Parse(IndexFillHex);
    public static readonly SKColor IndexGround = SKColor.Parse(IndexGroundHex);
    public static readonly SKColor IndexMark = SKColor.Parse(IndexMarkHex);
    public static readonly SKColor IndexText = SKColor.Parse(IndexTextHex);
    public static readonly SKColor IndexDeep = SKColor.Parse(IndexDeepHex);
    public static readonly SKColor IndexCloth = SKColor.Parse(IndexClothHex);
}
