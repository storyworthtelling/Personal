using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Apps;
using Codex.Domain.Home;
using Codex.Ui;

namespace Flyleaf;

/// <summary>
/// The realistic Flyleaf bookcase (a scoped DESIGN.md §1/§5 exception): a warm walnut back wall
/// with implied firelight, wooden ledges, and books that stand cover-out on the home shelves and
/// spine-out down the tray, with antique-bronze bookends on the category shelves. It hosts the
/// full continuous scene — clock shelf, home, siblings, categories, loose — inside a ScrollView,
/// so swiping up scrolls the same shelf down into the tray.
/// <para>
/// Geometry is the unit-tested <see cref="BookshelfLayout"/>; this view is the painter. The static
/// scene is baked once per size into a bitmap and blitted on scroll; only the live clock hands and
/// the notification bookmark tabs are redrawn on invalidate. The look is tuned on-device (this is
/// the first pass); the math underneath is verified in Codex.Tests.
/// </para>
/// </summary>
public class BookshelfView : View
{
    private readonly IShelfArt art;
    private readonly float density;
    private readonly ShelfMetrics metrics;

    private IReadOnlyList<ShelfSpec> shelves = [];
    private ShelfSceneLayout layout = new([], 0);
    private Bitmap? baked;
    private TimeSpan time;
    private float viewportHeight;

    // Painted wood assets (the "bake a painted bookcase" direction): the empty four-compartment home
    // backdrop, and a walnut texture tiled as the tray's back wall. Loaded once, recycled on detach.
    private Bitmap? homeBackdrop;
    private Bitmap? woodTexture;
    private static readonly Paint ImagePaint = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);

    private readonly GestureDetector gesture;

    public Action<PlacedItem>? ItemTapped;
    public Action<PlacedItem>? ItemLongPressed;

    public BookshelfView(Context context, IShelfArt art) : base(context)
    {
        this.art = art;
        density = context.Resources!.DisplayMetrics!.Density;
        metrics = PxMetrics(density);
        gesture = new GestureDetector(context, new TapListener(this));
    }

    /// <summary>Sets the shelves to render; re-lays out and re-bakes on the next measure/draw.</summary>
    public void SetScene(IReadOnlyList<ShelfSpec> newShelves)
    {
        shelves = newShelves;
        DropBaked();
        RequestLayout();
        Invalidate();
    }

    /// <summary>Updates the live clock time (redraws hands only).</summary>
    public void SetTime(TimeSpan value)
    {
        time = value;
        Invalidate();
    }

    /// <summary>Re-queries notification state and redraws the bookmark tabs (no re-bake).</summary>
    public void RefreshBookmarks() => Invalidate();

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        // We live inside a ScrollView, which measures us with an unbounded height — so the viewport
        // (the first screen the home fills) is the immersive display height, not the measure spec.
        var vh = Resources!.DisplayMetrics!.HeightPixels;
        if (width > 0 && (layout.Shelves.Count == 0 || Math.Abs(BakedWidth() - width) > 0.5f
            || Math.Abs(viewportHeight - vh) > 0.5f))
        {
            viewportHeight = vh;
            layout = BookshelfLayout.Build(width, metrics, shelves, viewportHeight);
            DropBaked();
        }
        var height = Math.Max((int)layout.TotalHeight, MeasureSpec.GetSize(heightMeasureSpec));
        SetMeasuredDimension(width, height);
    }

    // The bottom of the painted home region (the first screen). Zero when there are no home shelves
    // (e.g. a unit-shaped scene), which falls the whole case back to the tray treatment.
    private float HomeRegionBottom() =>
        layout.Shelves.Any(s => s.Home) ? viewportHeight : 0f;

    private Bitmap? HomeBackdrop => homeBackdrop ??= TryDecode(Resource.Drawable.shelf_bookcase_home);
    private Bitmap? WoodTexture => woodTexture ??= TryDecode(Resource.Drawable.shelf_wood_texture);

    private Bitmap? TryDecode(int resId)
    {
        try { return BitmapFactory.DecodeResource(Resources, resId); }
        catch (Exception) { return null; }
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        homeBackdrop?.Recycle(); homeBackdrop = null;
        woodTexture?.Recycle(); woodTexture = null;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is not null)
        {
            gesture.OnTouchEvent(e);
        }
        return true;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (Width <= 0 || layout.Shelves.Count == 0)
        {
            return;
        }
        EnsureBaked();
        if (baked is not null)
        {
            canvas.DrawBitmap(baked, 0, 0, null);
        }
        else
        {
            DrawScene(canvas); // OOM fallback: paint straight to the screen canvas
        }
        DrawLiveLayer(canvas);
    }

    // ---- Baking ----

    private float bakedWidth = -1;
    private float BakedWidth() => bakedWidth;

    private void DropBaked()
    {
        baked?.Recycle();
        baked = null;
        bakedWidth = -1;
    }

    private void EnsureBaked()
    {
        if (baked is not null || layout.TotalHeight <= 0)
        {
            return;
        }
        try
        {
            var bitmap = Bitmap.CreateBitmap(Width, (int)layout.TotalHeight, Bitmap.Config.Argb8888!);
            DrawScene(new Canvas(bitmap!));
            baked = bitmap;
            bakedWidth = Width;
        }
        catch (Java.Lang.Throwable)
        {
            baked = null; // out of memory for a tall scene — fall back to per-frame drawing
        }
    }

    // ---- Static scene ----

    private void DrawScene(Canvas canvas)
    {
        DrawBackWall(canvas);
        foreach (var shelf in layout.Shelves)
        {
            // Home ledges are painted into the backdrop; only the native tray draws its own boards.
            if (!shelf.Home)
            {
                DrawLedge(canvas, shelf.Ledge);
            }
            if (shelf.Bookend is { } bookend)
            {
                DrawBookend(canvas, bookend, shelf.BookendGlyph);
            }
            foreach (var item in shelf.Items)
            {
                switch (item.Face)
                {
                    case ShelfFace.CoverOut: DrawCoverBook(canvas, item); break;
                    case ShelfFace.SpineOut: DrawSpineBook(canvas, item); break;
                    case ShelfFace.Clock: break; // drawn live
                }
            }
        }
    }

    private void DrawBackWall(Canvas canvas)
    {
        var homeBottom = HomeRegionBottom();

        // Home region: the painted four-compartment bookcase backdrop, scaled to fill the first screen.
        if (homeBottom > 0)
        {
            if (HomeBackdrop is { } backdrop)
            {
                canvas.DrawBitmap(backdrop, null, new RectF(0, 0, Width, homeBottom), ImagePaint);
            }
            else
            {
                DrawGradientWall(canvas, 0, homeBottom);
            }
        }

        // Tray region: the walnut wood texture tiled as the back wall (scaled so it fills the width and
        // tiles down the scroll), replacing the old flat gradient.
        if (Height > homeBottom)
        {
            if (WoodTexture is { } tex)
            {
                using var paint = new Paint { AntiAlias = true };
                using var shader = new BitmapShader(tex, Shader.TileMode.Clamp!, Shader.TileMode.Repeat!);
                var scale = Width / (float)tex.Width;
                using var m = new Matrix();
                m.SetScale(scale, scale);
                m.PostTranslate(0, homeBottom);
                shader.SetLocalMatrix(m);
                paint.SetShader(shader);
                canvas.DrawRect(0, homeBottom, Width, Height, paint);
            }
            else
            {
                DrawGradientWall(canvas, homeBottom, Height);
            }
        }
    }

    private void DrawGradientWall(Canvas canvas, float top, float bottom)
    {
        using var paint = new Paint { AntiAlias = true };
        paint.SetShader(Linear(0, top, 0, bottom, PaperTheme.WalnutDeep, PaperTheme.Walnut));
        canvas.DrawRect(0, top, Width, bottom, paint);
    }

    private void DrawLedge(Canvas canvas, LayoutRect ledge)
    {
        using var fill = new Paint { AntiAlias = true, Color = PaperTheme.Walnut };
        var r = 3 * density;
        canvas.DrawRoundRect(ledge.Left, ledge.Top, ledge.Right, ledge.Bottom, r, r, fill);
        using var top = new Paint { AntiAlias = true, Color = PaperTheme.WalnutLight, StrokeWidth = density };
        canvas.DrawLine(ledge.Left, ledge.Top + density, ledge.Right, ledge.Top + density, top);
        // Soft shadow the shelf casts below.
        using var shadow = new Paint { AntiAlias = true };
        shadow.SetShader(Linear(0, ledge.Bottom, 0, ledge.Bottom + 8 * density,
            WithAlpha(PaperTheme.WalnutDeep, 90), WithAlpha(PaperTheme.WalnutDeep, 0)));
        canvas.DrawRect(ledge.Left, ledge.Bottom, ledge.Right, ledge.Bottom + 8 * density, shadow);
    }

    private void DrawContactShadow(Canvas canvas, LayoutRect rect)
    {
        using var shadow = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.WalnutDeep, 120) };
        canvas.DrawOval(rect.Left - 2 * density, rect.Bottom - 3 * density, rect.Right + 2 * density, rect.Bottom + 3 * density, shadow);
    }

    private void DrawCoverBook(Canvas canvas, PlacedItem item)
    {
        DrawContactShadow(canvas, item.Rect);
        var rect = ToRectF(item.Rect);
        var cover = art.Cover(item.Key);
        if (cover is not null)
        {
            using var clip = new Paint { AntiAlias = true, FilterBitmap = true };
            canvas.DrawBitmap(cover, null, rect, clip);
        }
        else
        {
            DrawClothCover(canvas, item, rect);
        }
        // A thin ink keyline + a page-edge on the right for depth.
        using var edge = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.PaperDeep, 220), StrokeWidth = 1.5f * density };
        edge.SetStyle(Paint.Style.Stroke);
        canvas.DrawRect(rect, edge);
    }

    private void DrawClothCover(Canvas canvas, PlacedItem item, RectF rect)
    {
        using var cloth = new Paint { AntiAlias = true, Color = new Color(art.Cloth(item.Key)) };
        canvas.DrawRect(rect, cloth);
        // A small centered launcher icon as the cover art.
        var icon = art.Icon(item.Key);
        if (icon is not null)
        {
            var s = rect.Width() * 0.42f;
            var ix = rect.CenterX() - s / 2;
            var iy = rect.Top + rect.Height() * 0.22f;
            icon.SetBounds((int)ix, (int)iy, (int)(ix + s), (int)(iy + s));
            icon.Draw(canvas);
        }
        // A lower bookplate band with the label.
        var bandTop = rect.Bottom - rect.Height() * 0.26f;
        using var plate = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.Paper, 235) };
        canvas.DrawRect(rect.Left + 4 * density, bandTop, rect.Right - 4 * density, rect.Bottom - 6 * density, plate);
        DrawFittedLabel(canvas, art.Label(item.Key),
            rect.Left + 6 * density, bandTop, rect.Right - 6 * density, rect.Bottom - 6 * density);
    }

    private void DrawSpineBook(Canvas canvas, PlacedItem item)
    {
        DrawContactShadow(canvas, item.Rect);
        var rect = ToRectF(item.Rect);
        using var cloth = new Paint { AntiAlias = true, Color = new Color(art.Cloth(item.Key)) };
        canvas.DrawRect(rect, cloth);
        // A soft highlight up the left edge (rounded book back).
        using var sheen = new Paint { AntiAlias = true };
        sheen.SetShader(Linear(rect.Left, 0, rect.Right, 0,
            WithAlpha(PaperTheme.PaperBright, 40), WithAlpha(PaperTheme.WalnutDeep, 60)));
        canvas.DrawRect(rect, sheen);

        // The square real-color app icon near the base.
        var icon = art.Icon(item.Key);
        var s = Math.Min(rect.Width() * 0.82f, 26 * density);
        var ix = rect.CenterX() - s / 2;
        var iy = rect.Bottom - s - 6 * density;
        if (icon is not null)
        {
            icon.SetBounds((int)ix, (int)iy, (int)(ix + s), (int)(iy + s));
            icon.Draw(canvas);
        }

        // Vertical serif title above the icon, reading top-to-bottom.
        DrawVerticalTitle(canvas, art.Label(item.Key), rect, iy - 6 * density);
    }

    private void DrawVerticalTitle(Canvas canvas, string label, RectF spine, float bottomLimit)
    {
        using var text = new Paint(PaintFlags.AntiAlias)
        {
            Color = PaperTheme.PaperBright,
            TextSize = Math.Min(spine.Width() * 0.64f, 13 * density),
            TextAlign = Paint.Align.Left,
        };
        text.SetTypeface(PaperTheme.SerifSemibold(Context!));
        // A dark halo so the serif title stays legible over ANY cloth colour (the old cream-on-light
        // spines read as blank — "no text visible").
        text.SetShadowLayer(2.4f * density, 0, 0, PaperTheme.WalnutDeep.ToArgb());

        var top = spine.Top + 8 * density;
        var trimmed = Ellipsize(label, text, 0, bottomLimit - top);

        canvas.Save();
        // Translate to the spine's top-centre, rotate 90° CW: a left-aligned string then runs DOWN the
        // spine, its baseline offset centring it across the spine's width.
        canvas.Translate(spine.CenterX(), top);
        canvas.Rotate(90);
        canvas.DrawText(trimmed, 0, text.TextSize / 3f, text);
        canvas.Restore();
    }

    private void DrawBookend(Canvas canvas, LayoutRect rect, string? glyph)
    {
        var r = ToRectF(rect);
        // A cast-bronze bookend statue, not a thin book: an upright metal slab on a wider foot, with a
        // vertical metal gradient, a bright top/left bevel and a dark bottom/right edge for relief, and
        // a contact shadow so it sits ON the shelf. One per category shelf, carrying that shelf's glyph.
        DrawContactShadow(canvas, rect);
        var footHeight = 6 * density;
        var foot = new RectF(r.Left - 3 * density, r.Bottom - footHeight, r.Right + 3 * density, r.Bottom);
        using (var metal = new Paint { AntiAlias = true })
        {
            metal.SetShader(Linear(0, r.Top, 0, r.Bottom, PaperTheme.Bronze, PaperTheme.BronzeDeep));
            canvas.DrawRoundRect(r, 3 * density, 3 * density, metal);
            canvas.DrawRoundRect(foot, 2 * density, 2 * density, metal);
        }
        using (var hi = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.PaperBright, 150), StrokeWidth = 1.4f * density })
        {
            hi.SetStyle(Paint.Style.Stroke);
            canvas.DrawLine(r.Left + 1.2f * density, r.Top + 1.2f * density, r.Right - 1.2f * density, r.Top + 1.2f * density, hi);
            canvas.DrawLine(r.Left + 1.2f * density, r.Top + 1.2f * density, r.Left + 1.2f * density, r.Bottom, hi);
        }
        using (var lo = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.WalnutDeep, 150), StrokeWidth = 1.2f * density })
        {
            lo.SetStyle(Paint.Style.Stroke);
            canvas.DrawLine(r.Right - 1f * density, r.Top + 2 * density, r.Right - 1f * density, r.Bottom, lo);
        }
        if (string.IsNullOrEmpty(glyph))
        {
            return;
        }

        // The shelf's glyph engraved as a raised bronze medallion, centered on the slab; three kinds of
        // glyph id: a built-in "folder_*" drawable, an owner shelf's AI vector ("path:<d>"), or the
        // offline pencil monogram ("mono:<letters>").
        var s = r.Width() * 0.78f;
        var gx = r.CenterX() - s / 2;
        var gy = r.CenterY() - s / 2;
        var glyphRect = new RectF(gx, gy, gx + s, gy + s);

        if (glyph.StartsWith("path:", StringComparison.Ordinal))
        {
            DrawGlyphPath(canvas, glyph[5..], glyphRect);
        }
        else if (glyph.StartsWith("mono:", StringComparison.Ordinal))
        {
            DrawGlyphMonogram(canvas, glyph[5..], glyphRect);
        }
        else
        {
            DrawGlyphDrawable(canvas, glyph, glyphRect);
        }
    }

    private void DrawGlyphDrawable(Canvas canvas, string glyph, RectF area)
    {
        var id = Resources!.GetIdentifier(glyph, "drawable", Context!.PackageName);
        var drawable = id == 0 ? null : Context.GetDrawable(id);
        if (drawable is null)
        {
            return;
        }
        drawable.SetBounds((int)area.Left, (int)area.Top, (int)area.Right, (int)area.Bottom);
        drawable.SetTint(PaperTheme.BronzeDeep.ToArgb());
        drawable.Draw(canvas);
    }

    private void DrawGlyphPath(Canvas canvas, string pathData, RectF area)
    {
        var subpaths = SvgGlyph.Parse(pathData);
        if (subpaths is null)
        {
            return; // unparseable — leave the bookend plain
        }
        using var stroke = new Paint(PaintFlags.AntiAlias)
        {
            Color = PaperTheme.BronzeDeep,
            StrokeWidth = 2 * density,
            StrokeCap = Paint.Cap.Round,
            StrokeJoin = Paint.Join.Round,
        };
        stroke.SetStyle(Paint.Style.Stroke);
        foreach (var polyline in subpaths)
        {
            using var path = new Android.Graphics.Path();
            for (var i = 0; i < polyline.Count; i++)
            {
                var x = area.Left + polyline[i].X / SvgGlyph.ViewBox * area.Width();
                var y = area.Top + polyline[i].Y / SvgGlyph.ViewBox * area.Height();
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }
            canvas.DrawPath(path, stroke);
        }
    }

    private void DrawGlyphMonogram(Canvas canvas, string letters, RectF area)
    {
        using var text = new Paint(PaintFlags.AntiAlias)
        {
            Color = PaperTheme.BronzeDeep,
            TextAlign = Paint.Align.Center,
            TextSize = area.Height() * 0.7f,
        };
        text.SetTypeface(PaperTheme.SerifSemibold(Context!));
        canvas.DrawText(letters, area.CenterX(), area.CenterY() + text.TextSize / 3f, text);
    }

    // ---- Live layer (clock hands + bookmark tabs) ----

    private void DrawLiveLayer(Canvas canvas)
    {
        foreach (var shelf in layout.Shelves)
        {
            foreach (var item in shelf.Items)
            {
                if (item.Face == ShelfFace.Clock)
                {
                    DrawMantelClock(canvas, item.Rect);
                }
                else if (art.HasNotification(item.Key))
                {
                    DrawBookmark(canvas, item.Rect);
                }
            }
        }
    }

    private void DrawMantelClock(Canvas canvas, LayoutRect rect)
    {
        var r = ToRectF(rect);
        var faceR = r.Width() * 0.36f;
        var cx = r.CenterX();
        var cy = r.Top + faceR + 10 * density;

        // Watercolor bloom behind the face.
        using var bloom = new Paint { AntiAlias = true };
        bloom.SetShader(Radial(cx, cy, faceR * 1.5f,
            WithAlpha(PaperTheme.IdentityWarm, 90), WithAlpha(PaperTheme.IdentityWarm, 0)));
        canvas.DrawCircle(cx, cy, faceR * 1.5f, bloom);

        // Mantel body + little legs.
        using var body = new Paint { AntiAlias = true, Color = PaperTheme.Walnut };
        var bodyTop = r.Top + 4 * density;
        var bodyBottom = r.Bottom - 8 * density;
        canvas.DrawRoundRect(r.Left + r.Width() * 0.16f, bodyTop, r.Right - r.Width() * 0.16f, bodyBottom, 6 * density, 6 * density, body);
        using var legs = new Paint { AntiAlias = true, Color = PaperTheme.WalnutDeep };
        var legW = 6 * density;
        canvas.DrawRect(r.Left + r.Width() * 0.22f, bodyBottom, r.Left + r.Width() * 0.22f + legW, r.Bottom, legs);
        canvas.DrawRect(r.Right - r.Width() * 0.22f - legW, bodyBottom, r.Right - r.Width() * 0.22f, r.Bottom, legs);

        // Face.
        using var faceFill = new Paint { AntiAlias = true, Color = PaperTheme.Paper };
        canvas.DrawCircle(cx, cy, faceR, faceFill);
        using var ring = new Paint { AntiAlias = true, Color = PaperTheme.Ink, StrokeWidth = 2 * density };
        ring.SetStyle(Paint.Style.Stroke);
        canvas.DrawCircle(cx, cy, faceR, ring);
        // Dot markers.
        using var dot = new Paint { AntiAlias = true, Color = PaperTheme.Ink };
        for (var i = 0; i < 12; i++)
        {
            var a = i * 30 * Math.PI / 180.0;
            canvas.DrawCircle(cx + (float)Math.Sin(a) * faceR * 0.82f, cy - (float)Math.Cos(a) * faceR * 0.82f, 1.6f * density, dot);
        }
        // Hands (live).
        var (hourDeg, minuteDeg) = ClockAngles.FromTime(time);
        using var hour = new Paint { AntiAlias = true, Color = PaperTheme.IdentityMark, StrokeWidth = 3 * density, StrokeCap = Paint.Cap.Round };
        using var minute = new Paint { AntiAlias = true, Color = PaperTheme.Ink, StrokeWidth = 2 * density, StrokeCap = Paint.Cap.Round };
        DrawHand(canvas, cx, cy, hourDeg, faceR * 0.52f, hour);
        DrawHand(canvas, cx, cy, minuteDeg, faceR * 0.78f, minute);
        canvas.DrawCircle(cx, cy, 2.4f * density, dot);
    }

    private static void DrawHand(Canvas canvas, float cx, float cy, float degrees, float length, Paint paint)
    {
        var rad = (degrees - 90f) * Math.PI / 180.0;
        canvas.DrawLine(cx, cy, cx + (float)Math.Cos(rad) * length, cy + (float)Math.Sin(rad) * length, paint);
    }

    private void DrawBookmark(Canvas canvas, LayoutRect rect)
    {
        var r = ToRectF(rect);
        var w = 10 * density;
        var x = r.Right - w - 6 * density;
        var top = r.Top - 12 * density;
        using var ribbon = new Paint { AntiAlias = true, Color = PaperTheme.Accent };
        using var path = new Android.Graphics.Path();
        path.MoveTo(x, top);
        path.LineTo(x + w, top);
        path.LineTo(x + w, r.Top + 8 * density);
        path.LineTo(x + w / 2, r.Top + 3 * density); // notched tail
        path.LineTo(x, r.Top + 8 * density);
        path.Close();
        canvas.DrawPath(path, ribbon);
    }

    // ---- Text helpers ----

    private void DrawFittedLabel(Canvas canvas, string label, float left, float top, float right, float bottom)
    {
        using var text = new Paint(PaintFlags.AntiAlias) { Color = PaperTheme.Ink, TextAlign = Paint.Align.Center };
        text.SetTypeface(PaperTheme.SerifRegular(Context!));
        text.TextSize = Math.Min((bottom - top) * 0.5f, 12 * density);
        var trimmed = Ellipsize(label, text, left, right);
        canvas.DrawText(trimmed, (left + right) / 2, (top + bottom) / 2 + text.TextSize / 3f, text);
    }

    private static string Ellipsize(string label, Paint paint, float start, float end)
    {
        var max = end - start;
        if (paint.MeasureText(label) <= max)
        {
            return label;
        }
        var trimmed = label;
        while (trimmed.Length > 1 && paint.MeasureText(trimmed + "…") > max)
        {
            trimmed = trimmed[..^1];
        }
        return trimmed + "…";
    }

    // ---- Hit testing ----

    private PlacedItem? HitTest(float x, float y)
    {
        foreach (var shelf in layout.Shelves)
        {
            foreach (var item in shelf.Items)
            {
                var r = item.Rect;
                // Generous vertical padding so the thin spines/covers are easy to tap.
                if (x >= r.Left - 4 && x <= r.Right + 4 && y >= r.Top - 12 && y <= r.Bottom + 8)
                {
                    return item;
                }
            }
        }
        return null;
    }

    // ---- Metrics / small helpers ----

    private static ShelfMetrics PxMetrics(float d)
    {
        var m = ShelfMetrics.Default;
        return m with
        {
            SidePadding = m.SidePadding * d,
            ShelfGap = m.ShelfGap * d,
            ItemGap = m.ItemGap * d,
            LedgeThickness = m.LedgeThickness * d,
            CoverWidth = m.CoverWidth * d,
            SpineHeight = m.SpineHeight * d,
            ClockSize = m.ClockSize * d,
            BookendWidth = m.BookendWidth * d,
            SpineWidths = m.SpineWidths.ToDictionary(kv => kv.Key, kv => kv.Value * d),
        };
    }

    private static RectF ToRectF(LayoutRect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private static Color WithAlpha(Color color, int alpha) => new(color.R, color.G, color.B, (byte)alpha);

    // Pass Color structs directly so overload resolution binds the (Color, Color, TileMode) ctor.
    // Color has no implicit int conversion, only the reverse (Color -> int), so an int/int call
    // site here resolves to the (long, long, TileMode) wide-gamut ctor instead: a plain ARGB int
    // widened to long isn't a valid Color.pack() long, and the native side throws trying to read
    // a bogus embedded ColorSpace id out of it (IllegalArgumentException: Invalid ID: <n>).
    private static LinearGradient Linear(float x0, float y0, float x1, float y1, Color c0, Color c1) =>
        new(x0, y0, x1, y1, c0, c1, Shader.TileMode.Clamp!);

    private static RadialGradient Radial(float cx, float cy, float radius, Color c0, Color c1) =>
        new(cx, cy, radius, c0, c1, Shader.TileMode.Clamp!);

    private sealed class TapListener(BookshelfView view) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnSingleTapUp(MotionEvent e)
        {
            if (view.HitTest(e.GetX(), e.GetY()) is { } item)
            {
                view.ItemTapped?.Invoke(item);
                return true;
            }
            return false;
        }

        public override void OnLongPress(MotionEvent e)
        {
            if (view.HitTest(e.GetX(), e.GetY()) is { } item)
            {
                view.ItemLongPressed?.Invoke(item);
            }
        }
    }
}
