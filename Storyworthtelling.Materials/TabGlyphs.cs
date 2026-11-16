namespace Storyworthtelling.Materials;

/// <summary>
/// The one set of tab glyphs (DESIGN.md §10, the sheet approved in W8.8): colored-pencil line drawings, 2-unit
/// strokes on a 24-unit grid, round caps and joins, no fills. Each is SVG path data, drawn by both kits through
/// <see cref="PathData"/> with the pencil's tooth, and by IconStudio into the few vector drawables Android needs as
/// resources (a notification's small icon). A concept drawn in more than one app is one glyph: every Settings tab is
/// <see cref="Settings"/>, and Pulp and Foreword share <see cref="Calendar"/>. A new glyph is a drawing the owner
/// approves before it lands here.
/// </summary>
public static class TabGlyphs
{
    /// <summary>The grid every glyph is drawn on.</summary>
    public const float Viewport = 24f;

    /// <summary>The pencil's width on that grid: 2 units, so 2dp at the 24dp size.</summary>
    public const float StrokeWidth = 2f;

    /// <summary>Sliders: every app's Settings.</summary>
    public const string Settings =
        "M3 6H13M17 6H21M3 12H7M11 12H21M3 18H15M19 18H21M13 6a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M7 12a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M15 18a2 2 0 1 0 4 0a2 2 0 1 0 -4 0";

    /// <summary>Pulp's and Foreword's Calendar.</summary>
    public const string Calendar =
        "M4 6H20V21H4ZM4 10H20M8 3V7M16 3V7M8 14H8.01M12 14H12.01M16 14H16.01M8 17.5H8.01M12 17.5H12.01";

    /// <summary>Constitution's Body.</summary>
    public const string Figure =
        "M10 5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M5 9.5H19M12 9.5V15M12 15L8 21M12 15L16 21";

    /// <summary>Constitution's Meals.</summary>
    public const string Bowl =
        "M3 11H21A9 8.5 0 0 1 3 11ZM10 3.5L9 7M14.5 3.5L13.5 7";

    /// <summary>Constitution's Today.</summary>
    public const string Page =
        "M6 3H14L19 8V21H6ZM14 3V8H19M9 13H15M9 17H13";

    /// <summary>Constitution's Workouts.</summary>
    public const string Dumbbell =
        "M6.5 12H17.5M6.5 7V17M17.5 7V17M3 9.5V14.5M21 9.5V14.5";

    /// <summary>Plate's Camera.</summary>
    public const string Camera =
        "M3 8H7L9 5H15L17 8H21V19H3ZM8.5 13a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0 -7 0";

    /// <summary>Plate's Darkroom.</summary>
    public const string HangingPrint =
        "M3.5 4H20.5M12 4V7M7 7H17V20H7Z";

    /// <summary>Plate's Scan.</summary>
    public const string ScanFrame =
        "M4 8V4H8M16 4H20V8M20 16V20H16M8 20H4V16M8 9H16M8 12H16M8 15H13";

    /// <summary>Post's Filed.</summary>
    public const string Folder =
        "M3 5H9L11 7.5H21V19H3ZM3 10.5H21";

    /// <summary>Post's Mailbox.</summary>
    public const string Mailbox =
        "M8.5 7H16.5A4.5 4.5 0 0 1 21 11.5V17H4V11.5A4.5 4.5 0 0 1 8.5 7ZM8.5 7A4.5 4.5 0 0 1 13 11.5V17M10.5 17V22M16.5 7V3H19.5";

    /// <summary>Post's Rolodex.</summary>
    public const string Rolodex =
        "M4 7H20V19H4ZM8.5 4V9M15.5 4V9M8 13H16M8 16H13";

    /// <summary>Score's Music.</summary>
    public const string Note =
        "M10 17.5V4L18 6.5M4 17.5a3 3 0 1 0 6 0a3 3 0 1 0 -6 0";

    /// <summary>Score's Podcasts.</summary>
    public const string Microphone =
        "M9 6A3 3 0 0 1 15 6V11A3 3 0 0 1 9 11ZM5.5 11A6.5 6.5 0 0 0 18.5 11M12 17.5V21M8.5 21H15.5";

    /// <summary>Score's Playing.</summary>
    public const string PlayCircle =
        "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0M10 8.5V15.5L16 12Z";

    /// <summary>Score's Discover.</summary>
    public const string Compass =
        "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0M15.5 8.5L13.5 13.5L8.5 15.5L10.5 10.5Z";

    /// <summary>Score's Workout.</summary>
    public const string Metronome =
        "M7 21L10 3H14L17 21ZM12 16L16.5 7.5M8 17H16";

    /// <summary>Pulp's Discover.</summary>
    public const string CardStack =
        "M4 8H14V21H4ZM8 8V4H20V17H14";

    /// <summary>Pulp's Playlists.</summary>
    public const string List =
        "M4 7H4.01M4 12H4.01M4 17H4.01M8.5 7H20M8.5 12H20M8.5 17H20";

    /// <summary>Pulp's Sports.</summary>
    public const string Baseball =
        "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0M6 5.2C8.6 8.4 8.6 15.6 6 18.8M18 5.2C15.4 8.4 15.4 15.6 18 18.8M5.6 8.2L7.6 7.6M6.6 11.2H8.6M5.6 15.8L7.6 16.4M18.4 8.2L16.4 7.6M17.4 11.2H15.4M18.4 15.8L16.4 16.4";

    /// <summary>Pulp's Watching.</summary>
    public const string Television =
        "M3 4.5H21V16.5H3ZM12 16.5V20.5M8 20.5H16M10.5 8V13L14.5 10.5Z";

    /// <summary>Index's Accounts.</summary>
    public const string Bank =
        "M3 9L12 4L21 9ZM6 11.5V17M10 11.5V17M14 11.5V17M18 11.5V17M3 20H21";

    /// <summary>Index's Snowball.</summary>
    public const string Staircase =
        "M4 3V20H21M7 6H10V9.5H13.5V13H17V16.5H20";

    /// <summary>Index's Budget.</summary>
    public const string Ledger =
        "M4 4H20V20H4ZM4 9.5H20M4 15H20M10 4V20";

    /// <summary>Index's Savings.</summary>
    public const string MoneyBag =
        "M9 3H15L13.5 7H10.5ZM10.5 7C5.5 9.5 4 13 4.5 16.5C5 20 8 21 12 21S19 20 19.5 16.5C20 13 18.5 9.5 13.5 7M12 11V17M14 12.5C13 11.5 10 11.5 10 13S14 14 14 15.5 11 16.5 10 15.5";

    /// <summary>Folio's Work.</summary>
    public const string OpenBook =
        "M12 6.5C10 5 7 4.5 3 4.5V18C7 18 10 18.5 12 20C14 18.5 17 18 21 18V4.5C17 4.5 14 5 12 6.5ZM12 6.5V20";

    /// <summary>Folio's Shelf.</summary>
    public const string BookShelf =
        "M3 21H21M5 4H8.5V21H5ZM10.5 6H14V21H10.5ZM15.5 7.5L18.5 6.5L21.5 19.5L18.5 20.5Z";

    /// <summary>Foreword's Today.</summary>
    public const string Sunrise =
        "M3 16.5H21M7 20.5H17M7 16.5A5 5 0 0 1 17 16.5M12 4V7M5 8.5L7 10.5M19 8.5L17 10.5";

    /// <summary>Foreword's Tasks.</summary>
    public const string Checklist =
        "M3 6.5L5 8.5L8.5 4.5M11.5 6.5H21M11.5 12H21M11.5 17.5H21M3.5 12H7M3.5 17.5H7";

    /// <summary>Every glyph by name.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [nameof(Settings)] = Settings,
        [nameof(Calendar)] = Calendar,
        [nameof(Figure)] = Figure,
        [nameof(Bowl)] = Bowl,
        [nameof(Page)] = Page,
        [nameof(Dumbbell)] = Dumbbell,
        [nameof(Camera)] = Camera,
        [nameof(HangingPrint)] = HangingPrint,
        [nameof(ScanFrame)] = ScanFrame,
        [nameof(Folder)] = Folder,
        [nameof(Mailbox)] = Mailbox,
        [nameof(Rolodex)] = Rolodex,
        [nameof(Note)] = Note,
        [nameof(Microphone)] = Microphone,
        [nameof(PlayCircle)] = PlayCircle,
        [nameof(Compass)] = Compass,
        [nameof(Metronome)] = Metronome,
        [nameof(CardStack)] = CardStack,
        [nameof(List)] = List,
        [nameof(Baseball)] = Baseball,
        [nameof(Television)] = Television,
        [nameof(Bank)] = Bank,
        [nameof(Staircase)] = Staircase,
        [nameof(Ledger)] = Ledger,
        [nameof(MoneyBag)] = MoneyBag,
        [nameof(OpenBook)] = OpenBook,
        [nameof(BookShelf)] = BookShelf,
        [nameof(Sunrise)] = Sunrise,
        [nameof(Checklist)] = Checklist,
    };
}
