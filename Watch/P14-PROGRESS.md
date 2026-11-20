# P14 · Watch — progress

Red runs are recorded before the code that turns them green (plan §0.2). Every figure here is from a
real run of `dotnet test Watch/Watch.Tests/Watch.Tests.csproj` on 20 Nov 2026.

The red run was taken with the tests written and the Domain types present only as stubs, so that
the suite compiled and each test failed on its own assertion: methods threw
`NotImplementedException`, and constants and lists were empty or zero. One run covers all three PBIs:
**34 tests, 30 failed, 4 passed.** The 4 that passed are the leak guard's four tests. They pass on the
stubs because the stubs hold no colour, type size or Paper & Ink name. To show the guard does fail, a
probe file holding a hex string, `SetTextSize(14f)`, `TextSize = 12` and "Cascadia" was put in
`Watch.Domain` for one run. All three guard tests failed, each reporting `Watch/Watch.Domain/Probe.cs:2`.
The probe was then deleted.

## W-1.1.1 · WatchTokens and the leak guard

- **Red:** 12 of `WatchTokensTests` failed (`EachAppHasItsEnds` ×4, `EveryAppHasARange`,
  `ThreeTypeSizesDescending`, `FourHaptics`, `HapticShapesAreTheApprovedOnes`, `EaseOffStrongest`,
  `MotionIsTheApprovedDurations`, `TypefaceIsPlexMono`). The first errors were
  `System.NotImplementedException : The method or operation is not implemented.` from `RangeOf`,
  `Assert.Equal() Failure: Strings differ` from `TypefaceIsPlexMono`, and
  `Assert.Equal() Failure: Values differ … Expected: 4` from `FourHaptics`. The four
  `WatchLeakGuardTests` passed (see above).
- **Green:** 16 of 16 (12 token tests and 4 guard tests).

## W-1.1.2 · DayColour

- **Red:** all 8 `DayColourTests` failed (`MidnightIsTheNightEnd`, `NoonIsTheDayEnd`, `NeverJumps`,
  `ShorterWayRound`, `HueStaysOnTheWheel`, `DarkEndsStayDark`, `BeatRunsGreenToRed`,
  `MorningAndEveningMatch`), each with `System.NotImplementedException`.
- **Green:** 8 of 8.

## W-1.1.3 · HourShape

- **Red:** all 10 `HourShapeTests` failed (`OneIsADot`, `TwoIsALine`, `FourIsADiamondOnItsPoint`,
  `TwelveHasTwelveSides`, `VerticesLieOnTheRadius`, `MinuteSweepsTheWheel`, and
  `AfternoonHasASecondRing` ×5 for 13:41, 11:59, 12:00, 00:00 and 23:59), each with
  `System.NotImplementedException`.
- **Green:** 10 of 10.

## Chunk A1 totals

- `Watch.Tests`: **34 passed, 0 failed.**
- `RepoGuards.Tests`: 386 passed, 0 failed, with the new projects in place.
  `Storyworthtelling.Updates.Tests` (CI hygiene): 111 passed.
- No CI workflow runs `Watch.Tests` yet. That is deliberate: this chunk adds none.

---

# Chunk A2 · the watch kit and haptics (W-1.2.1, W-1.3.1)

Every figure below is from a real run of `dotnet test Watch/Watch.Tests/Watch.Tests.csproj` on 20 Nov
2026. As in A1, the red runs were taken with the tests written and the Domain types present only as
stubs (methods throwing `NotImplementedException`, constants zero), so the suite compiled and each
test failed on its own assertion.

## First red run (W-1.2.1 pure halves, W-1.3.1, the sRGB conversion)

**98 tests, 63 failed, 35 passed.** The 35 were A1's 34 and `WaveGeometryTests.QuietestStillWaves`,
which reads a constant the stub already held. The 63 failures, every one
`System.NotImplementedException : The method or operation is not implemented.`:

- `WaveGeometryTests`: 21 of 22 (`SpansTheFrame`, `TwoPeriods`, `HeightIsTheAmplitude`,
  `FirstCrestRisesUp`, `FlatWhenPaused`, `BrightFollowsProgress`, `NothingBrightAtTheStart`,
  `AllBrightAtTheEnd`, `BrightStaysWhenPaused`, `VolumeIsHeight` ×4, `LouderIsTaller`,
  `CrownNudgesByStep`, `CrownStopsAtTheEnds`, `EdgesGoBackAndSkip` ×3, `EdgesAreAtLeastATouchTarget`,
  `MiddleAlwaysRemains`). First error: `at Watch.Domain.Kit.WaveGeometry.Points(...)`.
- `UndoWindowTests`: 7 of 7. First error: `at Watch.Domain.Kit.UndoWindow.For(TimeSpan recommended)`.
- `WatchTextTests`: 16 of 16. First error: `at Watch.Domain.Kit.WatchText.Check(String text)`.
- `HapticPlanTests`: 14 of 14 (`FourPatternsOneMeaningEach`, `MeaningsMatchTheTable`,
  `EveryPatternMeansWhatItIsUsedFor`, `TextsWaitForRest`, `HeldTextsBuzzOnce`,
  `NothingHeldNothingReleased`, `SessionEndReleasesToo`, `StartingASetReleasesNothing`,
  `TextsBuzzOutsideASet` ×2, `MailIsSilent`, `CallsComeThroughASet`, `RestEndOnlyInRest`,
  `EaseOffAlwaysBuzzes`). First error: `at Watch.Domain.Touch.HapticPlan.For(...)`.
- `SrgbTests`: 5 of 5 (the OKLCH-to-screen conversion every view paints through).

**First green attempt:** 97 of 98. `WatchTextTests.WrapsOnceAtAWord` failed with
`Assert.Equal() Failure: Values differ … Expected: Fits`. The fault was the test: "phone nearby." is 13
characters, 130 units in the test's monospace measure, and the test gave a width of 120. The width was
corrected to 130 (the code was not touched). **Green: 98 of 98.**

## Neutrals (WD §4.3, needed by the views)

`WatchTokens` gained `Neutral.Ink`/`Soft`/`Faint` (the mockup's INK, SOFT and FAINT, converted from HSL
to OKLCH), `GlyphLift` (Dash's glyphs at +0.08 lightness, as the mockup draws them), `Light`
(unplayed wave 0.22, edge marks 0.5, a quiet glyph 0.35) and `TouchTargetDp` (48). Tests first in
`NeutralTokensTests`, stubbed to zero: **6 tests, 5 failed** (`InkReadsOnBlack`, `SoftReadsOnBlack`,
`InkOverSoftOverFaint`, `OnlyDashIsLifted`, `UnplayedIsFainter`; `NeutralsHaveNoColour` passed on a
zero chroma). **Green: 6 of 6.** Ink and Soft keep 4.5:1 on black (WD §12).

## Glyph and wave placement (the mockup's numbers)

`GlyphGeometryTests` (6), with `GlyphGeometry` and the wave's span, stroke, glow and edge marks
stubbed to zero: **6 failed**, together with the two "the guard sees the kit" tests below, which failed
because `Watch/Watch.Ui.Wear` did not exist yet (`WatchLeakGuardTests.TheGuardSeesTheKit`,
`WatchA11yTests.TheGuardSeesTheKit`): **116 tests, 8 failed.** Green once the values from
`screens-r1.html` (`glyph`, `waveLine`, `edgeMarks`, `ring`, "Filed, with Undo") went in and the kit was
written: **116 of 116.**

## The views (W-1.2.1, Watch.Ui.Wear)

`WaveView`, `DashGlyphView`, `RingView`, `UndoRingView`, `WatchTextView`, plain Android views over a
canvas (`TextView` for the words), each a thin painter over Domain, colours only through
`WatchPaints.Of` → `Srgb.Argb` from `WatchTokens`/`DayColour`. Each names its class to TalkBack in
`OnInitializeAccessibilityNodeInfo`; the wave offers Back and Skip as custom actions (ids in
`Resources/values/ids.xml`) and its middle through `PerformClick`; the ring is a progress bar with a
percent range; the undo ring is a button; a Glance line is a heading.

**`WatchA11yTests` was shown to fail:** a probe `ProbeView : Android.Views.View` with an
`OnTouchEvent` and no description was put in `Watch.Ui.Wear` for one run. Three guard tests failed,
each naming `Watch/Watch.Ui.Wear/Probe.cs (ProbeView)`: `EveryViewDescribesItself`,
`EveryViewSaysWhatItIs`, `ATappableViewCanBeTappedByTalkBack`. The probe was then deleted.

**The leak guard now covers the kit** (it already read every `.cs` under `Watch/`): it also refuses
`Codex.Ui`, `Storyworthtelling.Maui` and `Storyworthtelling.Materials` in source, and a new
`NoWatchProjectReferencesPaperAndInk` refuses a watch `.csproj` naming a Paper & Ink kit or the
repository's `/Fonts`.

**Compiled here:** `dotnet build Watch/Watch.Ui.Wear/Watch.Ui.Wear.csproj
-p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`. The first build failed with
four `CS0104` (`Action` ambiguous between `System.Action` and `Android.Views.Accessibility.Action` in
`UndoRingView`); an alias fixed it. **Build succeeded, 0 warnings, 0 errors.**

## Decisions made in this chunk

- **The undo window is the phones' rule, by reference:** `Watch.Domain` references
  `Storyworthtelling.Folds` (BCL only) and `UndoWindow.For` is `FoldTiming.WindowFor`. The view asks
  `AccessibilityManager.GetRecommendedTimeoutMillis` itself and does not call `FoldTiming.Adopt`, so the
  watch never changes a process-wide value. The act is final when the window closes, even with the
  screen asleep (a delayed post) or the view gone.
- **The crown moves the wave's height**, from `Quietest` 0.04 to `Loudest` 0.36 of the face's radius,
  0.02 a detent. The quietest wave is still a wave, so silence never reads as paused.
- **The wave's edges** go back and skip across a fifth of the width each, at least 48 dp and at most
  two fifths, so a play/pause middle always remains. A mark lights in Wave's colour while pressed.
- **`WatchText`** refuses nothing at all, two sentences (a `.`, `?`, `!` or `…` followed by a space),
  and any `!` (WD §6). A decimal ("3.15") is one sentence. It wraps greedily at words into two lines at
  most and has no size to shrink: a line that does not fit is `TooLong`. `WatchTextView.Sentence`
  throws for a refused sentence (fixed copy, so a mistake is found at once); `Label` (names, titles)
  skips the sentence rules. A line that is too long at run time is logged by its length only, never its
  words, and ends in an ellipsis.
- **`HapticPlan`:** a call buzzes in every state (A26: only calls come through a set); a text is held
  during a set, and the held texts buzz once, as one Text pattern, when the set gives way to a rest or
  the session ends; a rest's end buzzes only in a rest; ease off always buzzes; mail never does. Every
  pattern the plan uses means the one event `MeaningOf` gives it.
- **`DashGlyphView` says "Dash"** to TalkBack, as an app icon's label does; the name is still never
  drawn (A48).

## Not done, not verified

- **IBM Plex Mono is not in the repository.** Nothing was downloaded. `WatchTextView` asks for the
  family by its token name (`Typeface.Create(WatchTokens.Typeface, …)`), which Android answers
  with its default sans-serif face until the font asset lands. **The font asset is pending.**
- The overnight rule (A12: only favourites and repeat callers buzz at night) is not in `HapticPlan`:
  it needs the Rolodex's stars and the call history, which arrive with Dash (E4).
- The views have no motion yet (the flat-to-wave ease on pause, the next track filling from the left);
  WD §8's reduced-motion rule applies when they do.
- The always-on (outline) form of the kit's views is not drawn; only the face's always-on is specified.
- **Built, device check pending — all of it:** the wave's look against the mockup on a real round
  screen, the glow under hardware acceleration, rotary input reaching `WaveView`, TalkBack reading
  each view and offering Back and Skip, the recommended timeout on Wear OS, and the four patterns felt
  on the wrist (W-1.3.1 maps events to patterns; nothing plays them yet).

## Chunk A2 totals

- `Watch.Tests`: **116 passed, 0 failed.**
- `RepoGuards.Tests`: 386 passed, 0 failed. `Storyworthtelling.Updates.Tests`: 111 passed.
- `Watch.Ui.Wear` compiles here. No CI workflow builds it or runs `Watch.Tests` yet (still deliberate).
