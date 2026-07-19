using Android.Content;
using Android.Content.PM;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Ui;

namespace FocusPhone.Launcher;

/// <summary>
/// Renders the app tray as textless icon cells (DESIGN.md §7): each app's own
/// launcher icon on the grained paper background, no label. The app name rides
/// along as the content description so the long-press/undo flow and screen
/// readers still know which app a cell is.
/// </summary>
public class AppIconAdapter : BaseAdapter
{
    private readonly Context context;
    private readonly IReadOnlyList<ResolveInfo> apps;
    private readonly PackageManager packageManager;

    public AppIconAdapter(Context context, IReadOnlyList<ResolveInfo> apps)
    {
        this.context = context;
        this.apps = apps;
        packageManager = context.PackageManager!;
    }

    public override int Count => apps.Count;

    public override Java.Lang.Object? GetItem(int position) => null;

    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var view = convertView as ImageView ?? BuildCell();
        var app = apps[position];
        view.SetImageDrawable(app.LoadIcon(packageManager));
        view.ContentDescription = app.LoadLabel(packageManager).ToString();
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
