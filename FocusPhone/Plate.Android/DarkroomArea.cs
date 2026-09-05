using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Plate;
using Codex.Domain.Time;
using Codex.Ui;

namespace Plate;

/// <summary>
/// The Darkroom: everything that has not left, in one room.
///
/// <para>Pictures to choose between and the scan whose handoff has not been confirmed wait
/// together, because both are "not yet gone" and splitting them would mean remembering which
/// room to check.</para>
///
/// <para>The tab takes the <b>in-progress</b> teal rather than an identity hue: the Darkroom
/// is a workspace, not a warning, and the ochre that suited "Unsent" would read as a problem
/// on a room the owner is meant to enjoy being in. §9 lets a middle area draw on a semantic
/// role, which Post already does.</para>
///
/// <para><b>Nothing leaves on its own.</b> No expiry, no auto-send, no nag — all three were
/// offered and none was chosen. "My darkroom, my job."</para>
/// </summary>
public sealed class DarkroomArea(PlateActivity host) : PlateArea(host)
{
    private readonly PlateActivity shell = host;

    public override BinderArea Tab =>
        new("Darkroom", Resource.Drawable.area_darkroom,
            PaperTheme.ProgressMark, PaperTheme.ProgressFill);

    private bool choosing;

    public override View BuildView()
    {
        var room = shell.Room;
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X3);

        if (!room.HasAnything)
        {
            // The voice gets this seat, exactly as Post's gets the empty Mailbox: Pinyon
            // Script in ink-blue, the one place in the suite that hand appears. Nothing else
            // is on the page, because there is nothing to act on.
            var empty = new LinearLayout(Host) { Orientation = Orientation.Vertical };
            empty.SetGravity(GravityFlags.Center);
            empty.AddView(PaperUi.Voice(Host, shell.EmptyLine()));
            column.AddView(empty, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
            return column;
        }

        var caption = room.Items.FirstOrDefault(item =>
            item.Kind == DarkroomKind.Photo && item.Caption.Length > 0);
        if (caption is not null)
        {
            column.AddView(PaperUi.Voice(Host, caption.Caption));
        }

        var photos = room.Items.Where(item => item.Kind == DarkroomKind.Photo).ToList();
        if (photos.Count > 0)
        {
            column.AddView(Grid(photos), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        foreach (var scan in room.Items.Where(item => item.Kind == DarkroomKind.Scan))
        {
            column.AddView(ScanRow(scan));
        }

        var note = PaperUi.Secondary(Host, choosing
            ? $"{room.ChosenCount} chosen"
            : "Nothing here has been backed up. It goes when you send it.");
        note.SetPadding(0, gap, 0, gap);
        column.AddView(note);
        return column;
    }

    // Three across, square cells, hairline outlines. A chosen cell is washed in the
    // in-progress hue rather than badged, so the count on the bar is the only number.
    private View Grid(IReadOnlyList<DarkroomItem> photos)
    {
        var grid = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X2);

        for (var start = 0; start < photos.Count; start += 3)
        {
            var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
            for (var offset = 0; offset < 3; offset++)
            {
                var index = start + offset;
                var cell = index < photos.Count ? Cell(photos[index]) : new Space(Host);
                var layout = new LinearLayout.LayoutParams(0,
                    ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
                layout.SetMargins(offset == 0 ? 0 : gap, gap, 0, 0);
                row.AddView(cell, layout);
            }
            grid.AddView(row);
        }
        return grid;
    }

    private View Cell(DarkroomItem item)
    {
        var frame = new SquareFrame(Host)
        {
            Background = PaperUi.Outline(Host, PaperTheme.PaperShade, PaperTheme.Ink, 0),
        };

        var image = new ImageView(Host);
        image.SetScaleType(ImageView.ScaleType.CenterCrop);
        image.SetImageURI(Android.Net.Uri.FromFile(shell.DarkroomFiles.FileFor(item)));
        frame.AddView(image, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        if (shell.Room.IsChosen(item.Id))
        {
            var wash = new View(Host);
            wash.SetBackgroundColor(PaperTheme.Wash(PaperTheme.ProgressMark));
            frame.AddView(wash, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        }

        frame.Clickable = true;
        frame.Click += (_, _) =>
        {
            if (choosing)
            {
                shell.Room = shell.Room.Choose(item.Id);
                Invalidate();
            }
            else
            {
                Open(item);
            }
        };
        // A long-press starts the cull with that picture already picked, which is how it
        // actually begins: you see the bad one first.
        frame.LongClickable = true;
        frame.LongClick += (_, _) =>
        {
            if (choosing)
            {
                // Already culling — a long-press is just another pick.
                shell.Room = shell.Room.Choose(item.Id);
                Invalidate();
                return;
            }
            ShowPhotoActions(item);
        };
        return frame;
    }

    private void ShowPhotoActions(DarkroomItem item)
    {
        ActionCard.Show(Host, Overlay, "This one",
            TimeFormats.Clock12(item.CapturedAt),
            new CardAction("Open it", () => Open(item), Lead: true),
            new CardAction("Start choosing", () =>
            {
                // How a cull actually begins: you see the bad one first.
                choosing = true;
                shell.Room = shell.Room.Choose(item.Id);
                Invalidate();
            }),
            new CardAction("Write the caption", () => WriteCaption(item),
                Detail: "The model will not name anyone in a picture."));
    }

    // The owner's own words. Where a name belongs, this is how it gets there — and it is the
    // answer whenever the machine's line is simply wrong.
    private void WriteCaption(DarkroomItem item)
    {
        var field = PaperUi.Field(Host, "in your own words");
        field.Text = item.Caption;

        var card = PaperUi.HairlineCard(Host);
        card.AddView(PaperUi.Label(Host, "Caption"));
        card.AddView(field);
        card.AddView(PaperUi.PrimaryButton(Host, "Keep it", () =>
        {
            ActionCard.Dismiss(Overlay);
            shell.CaptionByHand(item, field.Text ?? string.Empty);
        }));

        ActionCard.Dismiss(Overlay);
        var holder = new FrameLayout(Host);
        holder.AddView(card);
        Overlay.AddView(holder);
    }

    private View ScanRow(DarkroomItem scan)
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        row.SetMinimumHeight(PaperTheme.Dp(Host, DesignTokens.Shape.ListRowMinHeightDp));
        row.AddView(PaperUi.Body(Host, $"{scan.Title} · {Pages(scan.Pages)}"));

        // A share intent cannot tell you it arrived, so a Drive or Post scan says exactly
        // that rather than claiming a success nobody confirmed. Only the reMarkable route
        // goes through our own library and can clear itself.
        var detail = scan.Destination == ScanDestination.Remarkable
            ? $"To the reMarkable · {Attempts(scan.Attempts)}"
            : $"Handed to {Where(scan.Destination)} — not confirmed";
        row.AddView(PaperUi.Secondary(Host, detail));

        row.Clickable = true;
        row.Click += (_, _) => ShowScanActions(scan);
        return row;
    }

    private static string Pages(int pages) => pages == 1 ? "1 page" : $"{pages} pages";

    private static string Attempts(int attempts) => attempts switch
    {
        0 => "not sent yet",
        1 => "1 attempt",
        _ => $"{attempts} attempts",
    };

    private static string Where(ScanDestination destination) =>
        destination == ScanDestination.Post ? "Post" : "Drive";

    private void ShowScanActions(DarkroomItem scan)
    {
        var actions = new List<CardAction>();
        if (scan.Destination == ScanDestination.Remarkable)
        {
            actions.Add(new CardAction("Try again", () => shell.RetryScan(scan)));
        }
        else
        {
            // The owner ticks it off, because only they can see whether it landed.
            actions.Add(new CardAction("It arrived", () => Clear(scan)));
            actions.Add(new CardAction("Hand it over again", () => shell.RetryScan(scan)));
        }
        actions.Add(new CardAction("Throw it away", () => Clear(scan), Destructive: true));

        ActionCard.Show(Host, Overlay, scan.Title,
            $"scanned {TimeFormats.Clock12(scan.CapturedAt)}", [.. actions]);
    }

    private void Clear(DarkroomItem scan)
    {
        shell.Room = shell.Room.Choose(scan.Id);
        var (remaining, taken) = shell.Room.TakeChosen();
        shell.Room = remaining;
        shell.Forget(taken);
        Invalidate();
    }

    private void Open(DarkroomItem item) => shell.OpenEditor(item);

    public override View? BuildBottomBar()
    {
        if (!shell.Room.HasAnything)
        {
            return null;
        }

        if (!choosing)
        {
            return PaperUi.BottomBar(Host,
                PaperUi.PrimaryButton(Host, "Send all", Send),
                PaperUi.SecondaryButton(Host, "Choose", () => { choosing = true; Invalidate(); }));
        }

        var chosen = shell.Room.ChosenCount;
        return PaperUi.BottomBar(Host,
            PaperUi.PrimaryButton(Host, chosen == 0 ? "Send all" : $"Send {chosen}", Send),
            PaperUi.DestructiveButton(Host,
                chosen == 0 ? "Delete" : $"Delete {chosen}", Delete),
            PaperUi.QuietButton(Host, "Done", () =>
            {
                choosing = false;
                shell.Room = ClearChoices(shell.Room);
                Invalidate();
            }));
    }

    /// <summary>Back leaves the cull rather than the app, which is a level to come up from.</summary>
    public override bool GoBack()
    {
        if (!choosing)
        {
            return false;
        }
        choosing = false;
        shell.Room = ClearChoices(shell.Room);
        return true;
    }

    private static Darkroom ClearChoices(Darkroom room)
    {
        var (cleared, taken) = room.TakeChosen();
        return room.ChosenCount == 0 ? room : cleared.Restore(taken);
    }

    private void Send()
    {
        var (remaining, taken) = shell.Room.TakeChosen();
        if (taken.Count == 0)
        {
            return;
        }

        shell.Room = remaining;
        choosing = false;
        Invalidate();
        shell.Send(taken);
    }

    private void Delete()
    {
        var (remaining, taken) = shell.Room.TakeChosen();
        if (taken.Count == 0)
        {
            return;
        }

        // §7: the act happens and an undo fold carries it, never a confirm dialog. This is a
        // decision made twenty times a day, and a dialog between the owner and each one is
        // the reason nobody culls. The bytes are not removed until the fold closes.
        shell.Room = remaining;
        choosing = false;
        Invalidate();

        DogEar.Show(Host, Overlay, taken.Count == 1 ? "1 deleted." : $"{taken.Count} deleted.",
            () =>
            {
                shell.Room = shell.Room.Restore(taken);
                Invalidate();
            });
        shell.ForgetWhenTheFoldCloses(taken);
    }

    /// <summary>A cell that is as tall as it is wide, so the grid stays a grid.</summary>
    private sealed class SquareFrame(Context context) : FrameLayout(context)
    {
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            var width = MeasureSpec.GetSize(widthMeasureSpec);
            base.OnMeasure(widthMeasureSpec,
                MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly));
        }
    }
}
