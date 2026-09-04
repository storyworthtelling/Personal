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

    // The walnut wood texture, tiled as the single continuous back wall of the whole case. Loaded once,
    // recycled on detach. (The old freestanding painted home backdrop is retired — one native case now.)
    private Bitmap? woodTexture;

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

    private Bitmap? WoodTexture => woodTexture ??= TryDecode(Resource.Drawable.shelf_wood_texture);

    private Bitmap? TryDecode(int resId)
    {
        try { return BitmapFactory.DecodeResource(Resources, resId); }
        catch (Exception) { return null; }
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
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
            // Every shelf draws its own native board now (home + tray), all in one coordinate system, so
            // books rest on the shelves by construction.
            DrawLedge(canvas, shelf.Ledge);
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
        // ONE continuous walnut back wall for the whole case (home + tray) — the wood texture tiled down
        // the entire scroll — so the home shelves flow straight into the tray with no seam. The freestanding
        // painted backdrop (a self-contained cabinet with its own base) is retired: it couldn't continue
        // downward, which is what made scrolling into the tray feel unnatural.
        if (WoodTexture is { } tex)
        {
            using var paint = new Paint { AntiAlias = true };
            using var shader = new BitmapShader(tex, Shader.TileMode.Clamp!, Shader.TileMode.Repeat!);
            var scale = Width / (float)tex.Width;
            using var m = new Matrix();
            m.SetScale(scale, scale);
            shader.SetLocalMatrix(m);
            paint.SetShader(shader);
            canvas.DrawRect(0, 0, Width, Height, paint);
        }
        else
        {
            DrawGradientWall(canvas, 0, Height);
        }
        DrawCaseFrame(canvas);
    }

    // The case's own structure drawn over the back wall: two side posts running the full scroll height and
    // a lit crown board at the very top. Gives the continuous case real depth and frames every shelf, home
    // and tray alike, between the posts.
    private void DrawCaseFrame(Canvas canvas)
    {
        var side = metrics.SidePadding; // the side posts occupy the content inset; shelves span between them
        using (var post = new Paint { AntiAlias = true })
        {
            post.SetShader(Linear(0, 0, side, 0, PaperTheme.WalnutDeep, PaperTheme.Walnut));
            canvas.DrawRect(0, 0, side, Height, post);
            post.SetShader(Linear(Width - side, 0, Width, 0, PaperTheme.Walnut, PaperTheme.WalnutDeep));
            canvas.DrawRect(Width - side, 0, Width, Height, post);
        }
        using (var bevel = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.WalnutLight, 130), StrokeWidth = 1.5f * density })
        {
            bevel.SetStyle(Paint.Style.Stroke);
            canvas.DrawLine(side, 0, side, Height, bevel);
            canvas.DrawLine(Width - side, 0, Width - side, Height, bevel);
        }
        var crownH = 12 * density;
        using (var crown = new Paint { AntiAlias = true })
        {
            crown.SetShader(Linear(0, 0, 0, crownH, PaperTheme.WalnutLight, PaperTheme.Walnut));
            canvas.DrawRect(0, 0, Width, crownH, crown);
        }
        using (var sh = new Paint { AntiAlias = true })
        {
            sh.SetShader(Linear(0, crownH, 0, crownH + 8 * density,
                WithAlpha(PaperTheme.WalnutDeep, 110), WithAlpha(PaperTheme.WalnutDeep, 0)));
            canvas.DrawRect(0, crownH, Width, crownH + 8 * density, sh);
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
        var r = ToRectF(rect);
        // A soft, blurred cast shadow pooled under the item and offset slightly right (light from the
        // upper-left), so it reads as a solid object standing ON the wood rather than a card pasted on.
        using var shadow = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.WalnutDeep, 150) };
        shadow.SetMaskFilter(new BlurMaskFilter(5 * density, BlurMaskFilter.Blur.Normal));
        canvas.DrawOval(r.Left + 1 * density, r.Bottom - 4 * density, r.Right + 7 * density, r.Bottom + 7 * density, shadow);
    }

    private void DrawCoverBook(Canvas canvas, PlacedItem item)
    {
        DrawContactShadow(canvas, item.Rect);
        var rect = ToRectF(item.Rect);

        // The book's top page-block (fore-edge pages seen slightly from above), just above the cover, so a
        // flat cover reads as a bound book with real thickness.
        var pageDepth = 5 * density;
        using (var pages = new Paint { AntiAlias = true })
        {
            pages.SetShader(Linear(0, rect.Top - pageDepth, 0, rect.Top,
                PaperTheme.PaperBright, WithAlpha(PaperTheme.PaperDeep, 235)));
            canvas.DrawRect(rect.Left + 2 * density, rect.Top - pageDepth, rect.Right - 1 * density, rect.Top, pages);
        }

        // Every home book is the ex-libris bookplate (the approved Post mockup): the app's identity cloth
        // fills the plate, its illustration fills the window, the title sits on the cloth.
        DrawBookplateCover(canvas, item, rect);

        // A gentle form gradient (lit upper-left, shaded lower-right) for roundness, a darker seam down the
        // left edge (the spine hinge), then a thin keyline — the cues that make a bound book read as 3-D.
        using (var form = new Paint { AntiAlias = true })
        {
            form.SetShader(Linear(rect.Left, rect.Top, rect.Right, rect.Bottom,
                WithAlpha(PaperTheme.PaperBright, 26), WithAlpha(PaperTheme.WalnutDeep, 42)));
            canvas.DrawRect(rect, form);
        }
        using (var spine = new Paint { AntiAlias = true })
        {
            spine.SetShader(Linear(rect.Left, 0, rect.Left + 6 * density, 0,
                WithAlpha(PaperTheme.WalnutDeep, 90), WithAlpha(PaperTheme.WalnutDeep, 0)));
            canvas.DrawRect(rect.Left, rect.Top, rect.Left + 6 * density, rect.Bottom, spine);
        }
        using (var edge = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.PaperDeep, 200), StrokeWidth = 1.5f * density })
        {
            edge.SetStyle(Paint.Style.Stroke);
            canvas.DrawRect(rect, edge);
        }
    }

    private static readonly Paint ImageFill = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);

    // The ex-libris bookplate cover, matching the approved Post mockup (artifact 759ca17e): the app's
    // IDENTITY CLOTH fills the whole plate; a fine light rule + mitred-bracket corners frame it; the
    // ILLUSTRATION fills a near-square window (its bespoke painting, or the launcher icon as a fallback);
    // and the title sits on the cloth below. Proportions are the mockup's (window at ~13.75% / 14.5%,
    // 72.5% wide, square). Every home book uses this, so the six read as one set.
    private void DrawBookplateCover(Canvas canvas, PlacedItem item, RectF rect)
    {
        var cloth = new Color(art.Cloth(item.Key));

        // 1. Binding cloth fills the whole plate (a hair of vertical grade for depth).
        using (var fill = new Paint { AntiAlias = true })
        {
            fill.SetShader(Linear(0, rect.Top, 0, rect.Bottom, cloth, Darken(cloth, 0.86f)));
            canvas.DrawRect(rect, fill);
        }

        // 2. Fine light rule inset ~4.7%, then mitred-bracket corners.
        using (var rule = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.Paper, 180), StrokeWidth = 1.3f * density })
        {
            rule.SetStyle(Paint.Style.Stroke);
            canvas.DrawRect(Inset(rect, rect.Width() * 0.047f), rule);
        }
        DrawMitredBrackets(canvas, Inset(rect, rect.Width() * 0.072f));

        // 3. Illustration window (near-square), the painting or the icon filling it, keyline-framed.
        var winLeft = rect.Left + rect.Width() * 0.1375f;
        var winTop = rect.Top + rect.Width() * 0.145f;
        var winSide = rect.Width() * 0.725f;
        var window = new RectF(winLeft, winTop, winLeft + winSide, winTop + winSide);
        canvas.Save();
        canvas.ClipRect(window);
        var illustration = art.Cover(item.Key);
        if (illustration is not null)
        {
            canvas.DrawBitmap(illustration, SourceCropFor(illustration, window), window, ImageFill);
        }
        else
        {
            // No bespoke painting yet (the heads await their Gemini illustration): fill the window with the
            // identity cloth so it reads as one object, not a white box, and lay the launcher icon large
            // on top as the interim art.
            using (var bg = new Paint { AntiAlias = true })
            {
                bg.SetShader(Linear(window.Left, window.Top, window.Right, window.Bottom, cloth, Darken(cloth, 0.88f)));
                canvas.DrawRect(window, bg);
            }
            var icon = art.Icon(item.Key);
            if (icon is not null)
            {
                var s = winSide * 0.92f;
                var ix = window.CenterX() - s / 2;
                var iy = window.CenterY() - s / 2;
                icon.SetBounds((int)ix, (int)iy, (int)(ix + s), (int)(iy + s));
                icon.Draw(canvas);
            }
        }
        canvas.Restore();
        using (var keyline = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.Paper, 235), StrokeWidth = 1.2f * density })
        {
            keyline.SetStyle(Paint.Style.Stroke);
            canvas.DrawRect(window, keyline);
        }

        // 4. Title on the cloth below the window — light ink with a soft dark halo so it reads on any cloth.
        DrawTitleOnCloth(canvas, art.Label(item.Key),
            rect.Left + 6 * density, window.Bottom, rect.Right - 6 * density, rect.Bottom - 6 * density);
    }

    // Two nested right-angle brackets at each corner, drawn once and mirrored into all four (the mockup's
    // "mitred-bracket" motif), in the light cloth-rule ink.
    private void DrawMitredBrackets(Canvas canvas, RectF r)
    {
        var leg = r.Width() * 0.13f;
        var step = 4 * density;
        void Bracket(float cx, float cy, float sx, float sy)
        {
            using var outer = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.Paper, 235), StrokeWidth = 2.2f * density };
            outer.SetStyle(Paint.Style.Stroke); outer.StrokeCap = Paint.Cap.Square;
            canvas.DrawLine(cx, cy + sy * leg, cx, cy, outer);
            canvas.DrawLine(cx, cy, cx + sx * leg, cy, outer);
            using var inner = new Paint { AntiAlias = true, Color = WithAlpha(PaperTheme.Paper, 140), StrokeWidth = 1.2f * density };
            inner.SetStyle(Paint.Style.Stroke); inner.StrokeCap = Paint.Cap.Square;
            canvas.DrawLine(cx + sx * step, cy + sy * (leg * 0.6f + step), cx + sx * step, cy + sy * step, inner);
            canvas.DrawLine(cx + sx * step, cy + sy * step, cx + sx * (leg * 0.6f + step), cy + sy * step, inner);
        }
        Bracket(r.Left, r.Top, 1, 1);
        Bracket(r.Right, r.Top, -1, 1);
        Bracket(r.Left, r.Bottom, 1, -1);
        Bracket(r.Right, r.Bottom, -1, -1);
    }

    private void DrawTitleOnCloth(Canvas canvas, string label, float left, float top, float right, float bottom)
    {
        using var text = new Paint(PaintFlags.AntiAlias) { Color = WithAlpha(PaperTheme.Paper, 245), TextAlign = Paint.Align.Center };
        text.SetTypeface(PaperTheme.SerifSemibold(Context!));
        text.TextSize = Math.Min((bottom - top) * 0.40f, 13 * density);
        text.SetShadowLayer(2f * density, 0, 0.5f * density, WithAlpha(PaperTheme.Ink, 160));
        var trimmed = Ellipsize(label, text, left, right);
        canvas.DrawText(trimmed, (left + right) / 2, (top + bottom) / 2 + text.TextSize / 3f, text);
    }

    // Center-crop the source sub-rect whose aspect matches the window, so the art fills it without distortion.
    private static Rect SourceCropFor(Bitmap art, RectF window)
    {
        float bw = art.Width, bh = art.Height;
        var aspect = window.Width() / window.Height();
        var cropW = bw;
        var cropH = bw / aspect;
        if (cropH > bh) { cropH = bh; cropW = bh * aspect; }
        var left = (bw - cropW) / 2f;
        var top = (bh - cropH) / 2f;
        return new Rect((int)left, (int)top, (int)(left + cropW), (int)(top + cropH));
    }

    private static RectF Inset(RectF r, float d) => new(r.Left + d, r.Top + d, r.Right - d, r.Bottom - d);

    private static Color Darken(Color c, float factor) =>
        new((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor), c.A);

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
        // Pass the Color struct directly — never Color.ToArgb(). The int overload-resolves to
        // SetShadowLayer(float,float,float,long), where the value is read as a wide-gamut ColorLong
        // and Android rejects the non-RGB colorspace ("colorSpace must be an RGB color space").
        text.SetShadowLayer(2.4f * density, 0, 0, PaperTheme.WalnutDeep);

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
        // A cast shadow under the mantel body so the clock stands ON the shelf.
        DrawContactShadow(canvas, rect);
        var faceR = r.Width() * 0.36f;
        var cx = r.CenterX();
        var cy = r.Top + faceR + 10 * density;

        // Watercolor bloom behind the face.
        using var bloom = new Paint { AntiAlias = true };
        bloom.SetShader(Radial(cx, cy, faceR * 1.5f,
            WithAlpha(PaperTheme.FlyleafWarm, 90), WithAlpha(PaperTheme.FlyleafWarm, 0)));
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
        using var hour = new Paint { AntiAlias = true, Color = PaperTheme.FlyleafMark, StrokeWidth = 3 * density, StrokeCap = Paint.Cap.Round };
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
