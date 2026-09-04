using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Audio;
using Codex.Domain.Design;
using Codex.Ui;

namespace Score;

/// <summary>
/// The download budget as one bar: music, podcasts, and what is left.
///
/// Split for reading, not for accounting — there is one pool, and the split only
/// answers the question the owner actually has, which is "where did it go". Two
/// separate meters would imply two separate allowances and invite the app to reserve
/// one for each, which is precisely the decision the owner wanted to keep making
/// themselves.
///
/// Overfilled is drawn honestly: past the budget the bar is full and the figure above
/// it says by how much. A meter that stops at 100% would hide the state the owner
/// agreed to be in.
/// </summary>
public sealed class BudgetMeter : View
{
    public const int HeightDp = 14;

    private readonly Paint music = new() { AntiAlias = true };
    private readonly Paint podcasts = new() { AntiAlias = true };
    private readonly Paint free = new() { AntiAlias = true };
    private readonly float radius;

    private DownloadBudget budget = DownloadBudget.Empty;

    public BudgetMeter(Context context) : base(context)
    {
        music.SetStyle(Paint.Style.Fill);
        music.Color = PaperTheme.ScoreMark;

        // The lighter tier of the same hue rather than a second colour: both halves
        // are the same allowance, and two hues would read as two things.
        podcasts.SetStyle(Paint.Style.Fill);
        podcasts.Color = PaperTheme.ScoreFill;

        free.SetStyle(Paint.Style.Fill);
        free.Color = PaperTheme.PaperShade;

        radius = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp) * 3f;
    }

    public void Show(DownloadBudget value)
    {
        budget = value;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        var box = new RectF(0, 0, Width, Height);
        canvas.DrawRoundRect(box, radius, radius, free);

        // Shares of the capacity, not of what is used — a half-empty budget has to
        // look half empty.
        var scale = DownloadBudget.Capacity.TotalSeconds;
        var musicWidth = (float)Math.Min(1d, budget.UsedBy(DownloadKind.Music).TotalSeconds / scale) * Width;
        var podcastWidth = (float)Math.Min(1d, budget.UsedBy(DownloadKind.Podcast).TotalSeconds / scale) * Width;
        var filled = Math.Min(Width, musicWidth + podcastWidth);

        canvas.Save();
        canvas.ClipRect(box);
        canvas.DrawRoundRect(new RectF(0, 0, filled, Height), radius, radius, podcasts);
        canvas.DrawRoundRect(new RectF(0, 0, Math.Min(musicWidth, Width), Height), radius, radius, music);
        canvas.Restore();
    }
}
