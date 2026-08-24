# Flyleaf — the cover book is a plate, not a full-bleed image

**Status:** specified, not implemented. Handed to the P5 thread.
**Spec:** `DESIGN.md` §12, "The ex-libris plate — the anatomy, in order" (committed).
**Blocked on:** `[B9]` in the P5 on-device test plan (see *Cloth* below).

---

## What's wrong today

`Flyleaf.Android/BookshelfView.cs` draws a cover-out book two ways, and neither is
a plate.

**`DrawCoverBook` (line 265)** — the bespoke-art path stretches the cover bitmap
edge to edge into the book rect and adds a `PaperDeep` keyline:

```csharp
canvas.DrawBitmap(cover, null, rect, clip);
```

There is no binding cloth, no rule, no flourishes, and no window — the painting
*is* the whole book. §12 says the art is a window **set into** the binding.

**`DrawClothCover` (line 285)** — the fallback fills the rect with cloth, floats
the launcher icon at 42% width and 22% down, and lays a `Paper`-at-235-alpha band
across the bottom 26% for the label. That is closer in spirit (it has a ground and
a title band) but the icon floats unframed and the band is a slab, not a plate.

This is the gap the owner meant by *"the implementation doesn't quite work yet."*
The docs are the source of truth.

---

## What it should draw

Five elements, outermost first, per §12:

| # | Element | Notes |
|---|---|---|
| 1 | **Binding cloth** | The ground. See *Cloth* below — this is the open question. |
| 2 | **Fine rule** | Hairline frame inset from the edge, **all four sides**. |
| 3 | **Corner flourishes** | Quarter-curve at each corner, each a **reflection** of the top-left — not a rotation. A rotated asymmetric curl gives a pinwheel with no mirror line. |
| 4 | **Illustration window** | The painted art, framed by a keyline. **Rectangular** for Flyleaf's shelf books. Inset must clear the flourishes or the top pair gets painted over. |
| 5 | **Title band** | Beneath the window, on the cloth. |

The shelf cover **keeps its title** — a book with no title cannot be identified in
a row of six. (The square launcher icon drops it, because Android already prints
the app name underneath. Two variants, one plate.)

**No maker's mark** on either the shelf cover or the launcher icon. It appears only
once the cover art is expanded into the app-open load screen.

---

## Baked vs. live — the split

Decided, no owner input needed:

- **The launcher icon is baked.** IconStudio renders the whole plate to a PNG,
  because Android needs a finished asset. Unchanged from today.
- **The shelf cover is composed live** by `BookshelfView`. The app supplies only
  the **window art**; cloth, rule, flourishes and title band are drawn at runtime.

Live composition is what makes the cloth an *app* property rather than a property
baked into someone's painting, and it keeps a 1dp rule at 1dp instead of resampling
a hairline down to a 96dp-wide book.

**Seam consequence:** `IShelfArt.Cover` (`IShelfArt.cs`) is currently documented as
*"the bespoke painted cover for a featured book"* and consumed as a whole book. Its
contract becomes **the window art only** — the doc comment and every producer need
updating together, or existing paintings will be drawn inset with their own frames
still in them.

---

## Cloth — this is the part that is actually blocked

`HomeActivity`'s art provider resolves cloth by hash:

```csharp
public int Cloth(string packageKey) =>
    Android.Graphics.Color.ParseColor(BookClothPalette.ForPackage(packageKey)).ToArgb();
```

`BookClothPalette.ForPackage` picks from a fixed list (role deeps + the retired
terracotta identity trio + `WalnutLight`) by a stable hash over the package id.
That was correct when no app had an identity hue of its own. It no longer is: apps
now get an identity hue during their phase, and a hashed binding will disagree with
the app's own cover art.

Likely shape: **identity hue if the app has one, hashed fallback if it doesn't.**

But that is not automatically right either, and `[B9]` on the on-device test plan is
what settles it — *can you tell the six home books apart at a glance by their covers
alone?* Binding cloth does not have to match the app's art, and a binding that
harmonises with its own cover makes the book the hardest of six to find. §12 now
records this explicitly, along with the first decision made under it: **Score's
binding is cream linen**, the only light book on the shelf.

Contrast is a finite resource on a shelf of six. Exactly one app can hold it.

Note that `Cloth` also feeds **`DrawSpineBook` (line 309)**, so whatever replaces the
hash changes the spine-out tray as well as the cover-out home — worth checking both
in the same on-device pass.

---

## Where the code goes

Flyleaf's established split is *pure geometry in Domain, thin painter in the view*
(`BookshelfLayout`, `ShelfComposer`, `ShelfScene`, `SpineSizing`, `BookClothPalette`).
The plate follows it: a pure `PlateLayout` in `Codex.Domain/Apps` computes the rule
inset, flourish boxes, window rect and title band from a book rect, and
`BookshelfView` paints what it returns.

That makes the parts worth testing testable without a device:

- the window rect clears all four flourish boxes at every book size on the shelf
  (96 × 141 cover-out is the case that matters, from `ShelfMetrics.Default`);
- the four flourishes are reflections — mirroring the top-left across the vertical
  axis gives the top-right, and across both axes gives the bottom-right;
- the title band never overlaps the window;
- the plate degrades sanely when the window art is missing (the fallback path).

Tests land in `Codex.Tests` alongside the existing shelf-geometry tests:

```
dotnet test FocusPhone/Codex.Tests/Codex.Tests.csproj
```

---

## Order of work

1. `PlateLayout` in Domain, TDD, against the four properties above.
2. Repoint `DrawCoverBook` / `DrawClothCover` at it; both paths draw the same plate,
   differing only in what fills the window (painting vs. launcher icon).
3. Update the `IShelfArt.Cover` contract and its doc comment to *window art*.
4. **Hold cloth until `[B9]` reports.** Steps 1–3 do not depend on it — they take
   whatever `Cloth` returns.
