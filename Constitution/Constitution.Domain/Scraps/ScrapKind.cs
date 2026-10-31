namespace Constitution.Domain.Scraps;

/// <summary>
/// What a scrap is made of (D42): the paper says what the thing is. Workouts are magazine
/// clippings, recipes and meals are index cards, the voiced line is a torn notepad leaf, dinner
/// out is a receipt (D46), and Body's graphs are chart scraps torn from a doctor's chart (D44).
/// </summary>
public enum ScrapKind
{
    MagazineClipping,
    IndexCard,
    NotepadLeaf,
    Receipt,
    ChartScrap,
}
