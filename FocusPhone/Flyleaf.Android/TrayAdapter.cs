using Android.Content;
using Android.Content.PM;
using Android.Views;
using Android.Widget;
using Codex.Domain.Apps;
using Codex.Domain.Design;
using Codex.Ui;

namespace Flyleaf;

/// <summary>One tray cell: either an installed app or a category folder.</summary>
public abstract class TrayCell;

/// <summary>An app, drawn with its own launcher icon (textless, per DESIGN.md §7).</summary>
public sealed class AppCell(ResolveInfo app) : TrayCell
{
    public ResolveInfo App { get; } = app;
}

/// <summary>A category shelf, drawn as its ink glyph on the paper background.</summary>
public sealed class FolderCell(Category category) : TrayCell
{
    public Category Category { get; } = category;
}

/// <summary>
/// Renders the tray grid. App cells keep the app's own icon (the name rides along
/// as the content description for the long-press flow and screen readers); folder
/// cells draw the folder's hand-drawn ink glyph tinted to Ink, textless, with the
/// folder name as the content description.
/// </summary>
public class TrayAdapter : BaseAdapter
{
    private readonly Context context;
    private readonly PackageManager packageManager;
    private readonly IReadOnlyList<TrayCell> cells;

    public TrayAdapter(Context context, IReadOnlyList<TrayCell> cells)
    {
        this.context = context;
        this.cells = cells;
        packageManager = context.PackageManager!;
    }

    public override int Count => cells.Count;

    public override Java.Lang.Object? GetItem(int position) => null;

    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var view = convertView as ImageView ?? BuildCell();
        switch (cells[position])
        {
            case AppCell app:
                // Recycled cells may carry a folder's Ink tint — clear it for the full-color icon.
                view.ClearColorFilter();
                view.SetImageDrawable(app.App.LoadIcon(packageManager));
                view.ContentDescription = app.App.LoadLabel(packageManager).ToString();
                break;
            case FolderCell folder:
                var resourceId = context.Resources!.GetIdentifier(folder.Category.Glyph, "drawable", context.PackageName);
                view.SetImageResource(resourceId);
                view.SetColorFilter(PaperTheme.Ink);
                view.ContentDescription = folder.Category.Name;
                break;
        }
        return view;
    }

    private ImageView BuildCell()
    {
        var view = new ImageView(context);
        view.SetScaleType(ImageView.ScaleType.FitCenter);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X3);
        view.SetPadding(pad, pad, pad, pad);
        view.LayoutParameters = new AbsListView.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp + DesignTokens.Space.X6));
        return view;
    }
}
