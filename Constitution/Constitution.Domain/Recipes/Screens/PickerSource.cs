using Constitution.Domain.Diagnostics;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>The ways a recipe can come into the box (book p.14, C-6.5.4).</summary>
public enum PickerSource
{
    /// <summary>The owner's phone: Plate's recipe read, as many pages as it takes.</summary>
    Plate,

    /// <summary>The partner's phone: one photo per page, read on the phone.</summary>
    Camera,

    /// <summary>Waiting on written permission (PC8); a row only when it is allowed.</summary>
    Paste,

    /// <summary>Typing the card in. Needs nothing from any other app, so it is always there.</summary>
    TypeIn,
}

/// <summary>One row of the picker. A disabled row stays on the page and says why in its subtitle.</summary>
public sealed record PickerRow(PickerSource Source, string Title, string Subtitle, bool Enabled);

/// <summary>What choosing a row came to: the source to start, or the sentence for why nothing started (or neither, for a row that is not there).</summary>
public sealed record PickerChoice(PickerSource? Started, string? Sentence);

/// <summary>The picker's words, written once.</summary>
public static class RecipePickerText
{
    public const string PlateSubtitle = "Opens Plate · as many pages as it takes";

    public const string CameraSubtitle = "One photo per page";

    public const string CannotReadSubtitle = "This phone can’t read a card yet, so type it in instead.";

    public const string PasteTitle = "Paste an NYT Cooking link";

    public const string PasteSubtitle = "From the Cooking app’s Share button";

    public const string TypeInTitle = "Type it in";

    public const string TypeInSubtitle = "A title, the ingredients and the steps, in your own words";
}

/// <summary>
/// The inline picker that grows up out of the pinned bar (book p.14, section 7: in place, no dialog). Which rows a phone is offered
/// is decided here and nowhere else: the owner's phone scans through Plate, the partner's through its own camera when the phone can
/// read a photograph and otherwise says so, the paste row only with written permission (PC8, D82), and typing always. A phone whose
/// role is not yet known is offered Plate, which answers or says it is absent. Pure; the head only paints the rows.
/// </summary>
public sealed class RecipePickerModel
{
    private readonly List<PickerRow> rows;

    private RecipePickerModel(List<PickerRow> rows)
    {
        this.rows = rows;
    }

    public IReadOnlyList<PickerRow> Rows => rows;

    public bool IsOpen { get; private set; }

    /// <param name="role">Whose phone this is.</param>
    /// <param name="cameraCanRead">Whether this phone can turn a photographed page into words on the phone.</param>
    /// <param name="pasteAllowed">Whether the NYT spike found written permission to read a pasted link. Not so today.</param>
    public static RecipePickerModel For(InstallRole role, bool cameraCanRead, bool pasteAllowed)
    {
        var rows = new List<PickerRow>();
        if (role == InstallRole.Partner)
        {
            rows.Add(new PickerRow(
                PickerSource.Camera,
                RecipeBoxText.ScanAction,
                cameraCanRead ? RecipePickerText.CameraSubtitle : RecipePickerText.CannotReadSubtitle,
                cameraCanRead));
        }
        else
        {
            rows.Add(new PickerRow(PickerSource.Plate, RecipeBoxText.ScanAction, RecipePickerText.PlateSubtitle, true));
        }

        if (pasteAllowed)
        {
            rows.Add(new PickerRow(PickerSource.Paste, RecipePickerText.PasteTitle, RecipePickerText.PasteSubtitle, true));
        }

        rows.Add(new PickerRow(PickerSource.TypeIn, RecipePickerText.TypeInTitle, RecipePickerText.TypeInSubtitle, true));
        return new RecipePickerModel(rows);
    }

    public void Toggle() => IsOpen = !IsOpen;

    public void Close() => IsOpen = false;

    /// <summary>
    /// A row was tapped. An enabled row closes the picker and names its source; a disabled one leaves it open and gives its
    /// subtitle as the sentence; a source with no row here starts nothing and says nothing.
    /// </summary>
    public PickerChoice Choose(PickerSource source)
    {
        var row = rows.FirstOrDefault(candidate => candidate.Source == source);
        if (row is null)
        {
            return new PickerChoice(null, null);
        }

        if (!row.Enabled)
        {
            return new PickerChoice(null, row.Subtitle);
        }

        IsOpen = false;
        return new PickerChoice(source, null);
    }

    /// <summary>The bar's Scan a recipe card while the box is short of ten: the scan row of this install, chosen without opening the picker.</summary>
    public PickerChoice ScanChoice() => Choose(rows[0].Source);
}
