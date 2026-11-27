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

---

# Chunk A3 · Point, the face (W-2.1.1, W-2.1.2)

Every figure below is from a real run on 20 Nov 2026. As before, the red runs were taken with the tests
written and the code present only as stubs (methods throwing `NotImplementedException`, constants zero).

## The face's domain (Watch.Domain: FaceGeometry, FaceColour, WatchTokens.Face)

- **Red:** `Watch.Tests` **129 tests, 12 failed, 117 passed** (A2's 116 and `FaceColourTests.GroundIsBlack`,
  whose token was real in the stub). Failed: `FaceColourTests` `HueIsTheMinute`, `LightnessNeverMoves`,
  `AlwaysOnIsAThird`, `DateIsQuiet`, `RingIsQuieterThanTheRim`, `GlassLightsHighAndShadesLow`;
  `FaceGeometryTests` `ShapeIsHalfTheFace`, `RingIsAtTheApprovedRadius`, `AfternoonShapeSitsInsideItsRing`,
  `DateSitsBelowEverything`, `AlwaysOnUnderFivePercent`, `GlassLightsFromHighOnTheLeft`. First errors:
  `System.NotImplementedException … at Watch.Domain.Face.FaceColour.Glass(Double hue)`,
  `Assert.InRange() Failure: Value not in range` (`RingAlpha`), `Assert.True() Failure` (`HighlightX`).
- **Green:** 129 of 129.
- **Red at the top of the hour.** The first preview showed minute 0 as OKLCH's own zero hue, a pink, where
  WD §4.1 says the hour starts on red. `TopOfTheHourIsRed` and `HueStaysOnTheWheel` were added (and
  `HueIsTheMinute` now expects the turn) with `WatchTokens.Face.RedHue` stubbed at 0: **131 tests, 1 failed**,
  `TopOfTheHourIsRed`: "Red leads blue at the top of the hour." With `RedHue` = 29.23 (sRGB red's OKLCH hue)
  and `FaceColour` turning every tone by it: **131 of 131.** Minute 20 is now green, as the mockup draws it.

## The leak guard covers FaceStudio

`WatchLeakGuardTests` now reads every `.cs` and `.csproj` under `Tools/FaceStudio` as well as `Watch/`.
- **Red:** `TheGuardSeesFaceStudio` written first, with the scope unchanged: **132 tests, 1 failed.**
  **Green:** 132 of 132.
- **Shown to fail:** a probe `Tools/FaceStudio/Probe.cs` holding `"#FF00FF00"` and "Cascadia" failed
  `NoHexOutsideTokens` and `NoPaperAndInkInTheWatch`, each reporting `Tools/FaceStudio/Probe.cs:2`. The probe
  was deleted. FaceStudio holds no colour, hex or type size: every colour attribute is formatted at run time
  from `Srgb.Argb` over `FaceColour`/`WatchTokens`, and every size comes from `FaceGeometry` or
  `WatchTokens.Face.DateSize`.

## W-2.1.1 · FaceStudio generates the face, and W-2.1.2 · no taps

`Tools/FaceStudio` (net10.0 console, BCL only, references `Watch.Domain`), tests in `Tools/FaceStudio.Tests`,
solution `Tools/FaceStudio.sln`. `dotnet run --project Tools/FaceStudio -- --repo-root . [--apk out.apk]
[--version-code n] [--version-name s]`; tests `dotnet test Tools/FaceStudio.Tests/FaceStudio.Tests.csproj`.

- **Red:** **19 tests, 18 failed, 1 passed** (`FaceManifestTests.TheFrozenIdIsALiteral`: the stub
  `PointFace` already held the literal). Failed: `FaceStudioTests` `EveryHourHasItsShape`,
  `ColourFollowsTheMinute`, `AlwaysOnIsOutlineWithoutDate`, `TheDateIsWeekdayAndDay`,
  `NothingButTimeAndDate`, `NoTapTargetsOnTheFace` (W-2.1.2), `TheFaceIsValidWff`,
  `TheSchemaRefusesABrokenFace`; `FaceManifestTests` `TheApkHasNoCode`, `TheInfoNamesThePreview`,
  `TheLabelIsPoint`; `FaceFilesTests` `TheCommittedFaceIsCurrent`, `PlexMonoIsEmbedded`,
  `EveryMaskIsShipped`; `MaskTests` `MaskCoversItsShape`, `MaskNamesSaySidesAndHalf`, `PngIsAPng`;
  `ApkTests.TheBuiltApkHasNoCode`. First errors: `System.TypeInitializationException : The type initializer
  for 'FaceStudio.Tests.FaceRead' threw an exception. ---- System.NotImplementedException` and
  `System.NotImplementedException … at FaceStudio.Apk.FindAapt2()`.
- **First green attempt: 6 failed**, all `XmlSchemaException : The Pattern constraining facet is invalid -
  Invalid pattern '^((h{2}\_(10|1)…' … Unrecognized escape sequence \_.` The schema, not the face: see
  "Validation" below. **Second: 1 failed**, `TheCommittedFaceIsCurrent` ("AndroidManifest.xml is not
  committed; run FaceStudio"), as it should until the tool had been run. **Green: 19 of 19**, with
  `TheBuiltApkHasNoCode` building a real APK here (aapt2 found).

**What the face is** (study 1b exactly, A46, WD §10). `res/raw/watchface.xml`, 450 × 450, clip CIRCLE,
`PREVIEW_TIME` 16:20 (invented), true-black scene. Two whole-face layers by the format's own ambient
pattern: `lit` (a `Variant mode="AMBIENT"` sets its alpha to 0) and `always-on` (alpha 0, the variant sets
255). Each holds one `Condition` with 24 branches on `[HOUR_0_23] == h`, one group per hour of the day,
drawn from `HourShape.For`:
- **3 to 12 sides:** the format has lines, arcs, rectangles and ellipses but no polygon fill, so the glass
  is a group with the hour's silhouette as a `renderMode="MASK"` image (`morningN`/`afternoonN`, drawn by
  FaceStudio's own supersampled rasteriser and PNG writer; 20 masks), a rectangle in the minute's colour,
  and a static radial gradient over it (white highlight high on the left, black shade at the edge); then the
  rim as N `Line`s vertex to vertex, round caps.
- **1** is a filled `Ellipse` at 0.1 R with the same gradient and a rim; **2** is one `Line` top to bottom at
  0.07 R. Morning shapes are 0.5 R, afternoon shapes 0.42 R.
- **After noon** the ring: the same polygon's lines at 0.62 R (a circle round a dot or a line), at
  0.012 R, alpha 179 (0.7).
- **The date** is the only `PartText`: `Upper(Template "%s %s" [DAY_OF_WEEK_S] [DAY])` ("SAT 31"), centred
  0.76 R below the centre, `Font family="plexmono"` (IBM Plex Mono Medium, `res/font/plexmono.ttf`),
  size 0.08 R (18 units), in the date tone.
- **The minute's colour:** every fill, stroke and the date carry a `Transform target="color"` whose value is
  `extractColorFromColors("<60 RRGGBB>", true, [MINUTE] / 59)`, the sixty colours being each tone at each
  minute's `HourShape` hue. Tones (OKLCH, `WatchTokens.Face`): glass L 0.66 C 0.16, rim 0.90/0.08, ring
  0.86/0.09, date 0.68/0.035; always-on a third of the rim's and ring's lightness at C 0.05.
- **Always-on** is outline only: the shape's lines (or the dot's circle) at 0.022 R (the line hour 0.04 R)
  and the ring; no fill, image, gradient or date. `FaceGeometry.AlwaysOnLit` puts the worst hour (twelve
  sides after noon) at about 3.3% of the face lit, under WD §2's 5%.
- **Nothing else:** the only data sources are `HOUR_0_23`, `MINUTE`, `DAY` and `DAY_OF_WEEK_S`; no
  complication slot, clock element, user configuration, animated image or gyro.
- **W-2.1.2:** no `Launch`, no complication slot, no attribute naming a tap. The face offers no gestures of
  its own; tiles and the crown are the way in (A35's fallback, TF §3.4).
- **The manifest:** package `com.storyworthtelling.watch.point` (a literal in `PointFace.PackageId`),
  `minSdkVersion`/`targetSdkVersion` 36, `uses-feature android.hardware.type.watch`, `application
  android:hasCode="false"` with label `@string/label` ("Point", A48), and the properties
  `com.google.wear.watchface.format.version` = 4 and `com.google.wear.watchface.format.publisher` =
  FaceStudio. `res/xml/watch_face_info.xml`: preview `@drawable/preview`, not editable, not in retail, one
  instance. The preview PNG is the face at 4:20 pm drawn by the rasteriser, **without the date** (the tool
  sets no type).

**Packaging:** `aapt2 compile --dir res` then `aapt2 link -I android.jar --manifest … --version-code
--version-name` into an **unsigned** APK. aapt2 is found under `ANDROID_HOME`/`ANDROID_SDK_ROOT`,
`/opt/android-sdk-min` (build-tools 36.0.0, android-36) or the .NET Android workload's pack. Built here:
`aapt2 dump badging` reads `package: name='com.storyworthtelling.watch.point' versionCode='1'`,
`minSdkVersion:'36'`, `application-label:'Point'`, both properties, and `dump xmltree` reads
`android:hasCode(0x0101000c)=false`; no `classes.dex`. `TheBuiltApkHasNoCode` returns early only when there
is no aapt2 or android-36 `android.jar`, and fails instead when `FACESTUDIO_APK_REQUIRED=1` (for W-3.2.1).

**Validation.** `TheFaceIsValidWff` checks the face against Google's published WFF version 4 XSD, committed
unmodified in `Tools/FaceStudio/Specification/wff4` (fetched from raw.githubusercontent.com/google/watchface,
Apache 2.0, NOTICE beside it). That XSD is XSD 1.1 (an `xs:all` whose elements repeat) and two patterns escape
`_` and `:`; .NET reads XSD 1.0 only, so `FaceSchema` reads each `xs:all` as a repeating `xs:choice` and drops
the two needless escapes. That keeps every element name, nesting and attribute type and loses child counts
("at most once", "required"). FaceStudio refuses to write a face that fails it; `TheSchemaRefusesABrokenFace`
shows it fails on an unknown element. **Google's own validator** was then run, which the relaxation cannot
stand in for: the released `wff-validator.jar` and the github.com repository were not reachable here, so it
was built from its source on `main` (raw.githubusercontent.com, `third_party/wff/specification/validator`:
`DWFValidationApplication`, `WatchFaceXmlValidator`, `ResourceManager`, `SupportedOptions`, `utils/*`) with the
dependencies its `build.gradle` names, from Maven Central (`xml-apis` 1.4.01, `xercesImpl` 2.12.0
`xml-schema-1.1`, `org.eclipse.wst.xml.xpath2.processor` 2.1.100, `java-cup` 10k), compiled with javac in the
session's scratch folder (not committed), its `docs.zip` holding the same v4 XSD. It refused a probe face
(`cvc-complex-type.2.4.a: Invalid content was found starting with element 'Polygon'`) and reported for
the generated face: **"valid against watch face format version #4"**. Re-run after every change to the
face's colours. **Not run:** Google's memory-footprint evaluator (`play-validations`).

**Font (A57).** `Watch/Fonts` holds IBM Plex Mono Regular, Medium and SemiBold (static TTFs, version 2.3) and
`OFL.txt`, from Google Fonts' own repository (raw.githubusercontent.com/google/fonts/main/ofl/ibmplexmono;
the IBM/plex GitHub releases were not reachable). SHA-256: Medium `a9b4c49b…d1ea6755`, Regular
`6a3412f0…7734af46`, SemiBold `d3c38e55…2d23aa46`. The face embeds Medium, unmodified, as
`res/font/plexmono.ttf`. The kit (`WatchTextView`) still asks for the family by name; packaging the font as
a Wear asset is the kit's, not done here. The A1 guard (`NoWatchProjectReferencesPaperAndInk`, which refuses
the repository's `/Fonts`) still passes: `Watch/Fonts` is the watch's own.

## Decisions made in this chunk

- **Watch Face Format version 4, not 2** (TF §2 said the face *can* use 2). Changing a colour with an
  expression (the minute's hue) arrived in version 4; Wear OS 6 runs it (developer.android.com,
  "Wear OS 6 features": "Watch Face Format version 4 is supported for devices that run Wear OS 6 or
  higher"), and both watches are on Wear OS 6. `minSdkVersion` 36 to match, as the setup guide asks.
- **The hue starts on red** (`WatchTokens.Face.RedHue` 29.23): `HourShape.Hue` (A1) is unchanged; the face
  turns it so minute 0 is red and minute 20 green, as WD §4.1 and the mockup draw.
- **Always-on lightness is a third of the lit lightness** (WD §2, §10's words) rather than the mockup's
  `hsl(h 35% 32%)`.
- **The colour list's form:** the format's reference gives `extractColorFromColors` two examples that
  disagree (a quoted list without `#`, and an unquoted list with `#`). The face uses the first. With
  interpolation on and `[MINUTE] / 59`, colour *m* is chosen whether the value is read as a position along
  the list or a share of it.
- **The generated face is committed** (`Watch/Point.Face`: manifest, `watchface.xml`, info, strings, the
  20 masks, the preview, and the font copy), like IconStudio's output, and `TheCommittedFaceIsCurrent` holds
  it byte for byte to the tool, so it is never hand-edited. The APK is not committed.
- **`test-tools.yml` runs FaceStudio's tests**: `CiHygieneTests.EveryToolsTestProjectIsRunByAWorkflow`
  failed on the new test project ("A tool's tests that no workflow runs: Tools/FaceStudio.Tests/…"), so the
  one step and its path filters were added. No face build, signing or publish (W-3.2.1).

## Not done, not verified

- **`watchface.xml` is 387 KB:** 60 colours on each of the 485 colour transforms. The format's
  version-4 `Reference` element could share one transform per tone; not done, since what it costs and saves
  on the watch is unmeasured. The memory-footprint evaluator was not run.
- **Built, device check pending — all of it:** that the watch accepts `extractColorFromColors` on a fill, a
  stroke and a font colour and picks the minute's colour from the list as expected; the mask render mode
  clipping the glass to its polygon; Plex Mono drawing the date; the ambient switch; the always-on share lit;
  the picker preview; the face installing, by hand first (Point installer, W-3.1.2) and then from the phone.

## Chunk A3 totals

- `Watch.Tests`: **132 passed, 0 failed.** `FaceStudio.Tests`: **19 passed, 0 failed** (the APK built here).
- `RepoGuards.Tests`: 386 passed, 0 failed. `Storyworthtelling.Updates.Tests`: 111 passed, 0 failed (after
  the `test-tools.yml` step; 1 failed before it, as above).

---

# Chunk A4 · the update channel tells a watch APK from a phone APK (W-3.1.1)

Every figure below is from a real run on 20–21 Nov 2026. As before, the red run was taken with the tests written
and the code present only as stubs: `ApkForms`, `ApkManifest` and `SiblingUpdatePlan` threw
`NotImplementedException` with their constants empty, `InstallCandidate`, `UpdateScope.Decide` and
`InstallOutcome` had their new members but no behaviour (every sidecar read as phone, the device's form ignored),
and `.github/scripts/stage-apks.py` was the old publish action's behaviour written as a script (read package and
versionCode, write a sidecar with no form, copy the APK under its own name, refuse nothing).

## Why: every way a watch APK could reach a phone

A watch app shares its phone app's package and signature (PC1) and is built in versionCode band 11, above every
phone band, so each of these would have installed Dash, Wave, Beat or the Point installer over Post, Score,
Constitution or Flyleaf, with nothing failing:

1. **Flyleaf, sidecar read:** `UpdateScope` compared package and version only; band 11 is "newer".
2. **Flyleaf, no readable sidecar:** the APK was downloaded and read with `GetPackageArchiveInfo` for package and
   version only.
3. **Flyleaf's package-keyed sets:** `wanted` and `toInstall` were sets of package names, so once Post's phone APK
   was due, the release's other APK of the same package was downloaded and installed too, even with a form-aware
   `UpdateScope`.
4. **Score on the partner's phone (SelfOnly):** downloads every APK and read package and version only; Wave is
   Score's package.
5. **Admin mode, "Install APK from Downloads":** `ApkInstaller` installed whatever it was given.
6. **The publish action:** .NET names a signed APK after its package, so Dash and Post both go up as
   `com.storyworthtelling.focusphone.communication-Signed.apk`, and `gh release upload --clobber` replaces by name:
   the phone APK would leave the release and the watch APK take its place and its sidecar.
7. **No aapt2 on the runner:** the action published without sidecars.
8. **A watch app that forgot `uses-feature android.hardware.type.watch`:** reads as a phone APK by every measure
   on the phone, so only CI can catch it.

## Tests written first

- `Storyworthtelling.Updates.Tests/WatchFormTests.cs` (34 cases): `APhoneNeverInstallsAWatchApk`,
  `TheWatchNeverInstallsAPhoneApk`, `EachFormInstallsItsOwn`, `NoFormIsPhone`, `TheSidecarSaysItsForm` ×2,
  `AnUnknownFormIsInstalledNowhere` ×10 (`"tablet"`, `""`, `"Watch"`, `"PHONE"`, `" phone"`, `null`, `11`, `true`,
  `{}`, `["phone"]`), `ADeviceOfNoKnownFormInstallsNothing`, `SamePackageTwoForms` (both orders, phone and watch),
  `TheFormComesBeforeTheVersion` (band 1, 10 and 11), `ThePlanKeepsTheOldRulesWithinAForm`, `ReadingsMustAgree`,
  `AWatchAssetIsNamedForIt` ×5, `TheWatchFeatureMakesAWatch`, `TheWordsAreFrozen` (values, and literals not
  `nameof` in `ApkForm.cs`), `NotForThisDeviceIsSettled`, and the manifest scan:
  `AManifestNamingTheWatchFeatureIsAWatch` (UTF-16 and UTF-8 string pools), `AManifestWithoutItIsAPhone`,
  `WhatIsNotAnApkHasNoForm`, `ReadingLeavesTheStreamForTheInstaller`, `RealApksSayTheirForm` (APKs linked by the
  SDK's aapt2: required, not-required and no watch feature).
- `Storyworthtelling.Updates.Tests/ApkStagingTests.cs` (15 cases) runs `stage-apks.py` against a stand-in aapt2:
  `APhoneApkKeepsItsName`, `AWatchApkSaysWatchAndIsNamedForIt` ×2 (`uses-feature` and
  `uses-feature-not-required`), `AWatchApkInAPhonePublishStopsIt`, `APhoneApkInAWatchPublishStopsIt`,
  `APhoneApkNamedLikeAWatchApkStopsIt`, `TwoApksOneNameStopsIt`, `AnUnknownFormStopsIt` ×3,
  `AnApkThatWontReadStopsIt` ×3, `Aapt2FailingStopsIt`, and `TheRealAapt2AgreesWithThePhone` (the same script
  over real aapt2 output, its sidecars read by `ApkSidecar.Read` and its staged APK by `ApkManifest`).
- `CiHygieneTests.EveryWatchApkSaysWatch` and `TheWatchRuleBites` (below).
- The existing `UpdateScopeTests`, `DeferredScopeTests`, `ApkSidecarTests`, `FrozenInvariantsTests` and
  `GitHubReleaseClientTests` now state the phone form at every call (the form has no default, so every reader of
  an APK says what it read).

## Red

**First attempt, not counted:** with no script at all and then with the stub script, 41 of 162 failed, but nine
staging tests passed for the wrong reason: the test helper's two `Stage` overloads let C# bind
`Stage(form, apk)` to `Stage(form, aapt2, params apks)` (the overload with more declared parameters wins when both
are expanded), so the APK's path was passed as aapt2, no APK was given, and argparse exited 2, which the refusal
tests read as a refusal. The helper was fixed (`StageWith(aapt2, form, …)`); the code was not touched.

**Red run: `Storyworthtelling.Updates.Tests` 162 tests, 49 failed, 113 passed.** The 113 are the 111 that passed
before, less `ApkSidecarTests.ThePublishActionWritesASidecarBesideEveryApk` (the action did not yet call the
script), plus three that pass on the stubs because they hold behaviour that does not change:
`TheSidecarSaysItsForm(phone)` (the stub read every sidecar as phone), `NotForThisDeviceIsSettled` (only `Failed`
was ever unsettled), and `APhoneApkKeepsItsName` (a phone APK still goes up under its own name). The 49:
`WatchFormTests` 32 of 34, `ApkStagingTests` 14 of 15, `CiHygieneTests` 2 (`EveryWatchApkSaysWatch`,
`TheWatchRuleBites`) and `ApkSidecarTests` 1. First errors:
- `APhoneNeverInstallsAWatchApk`: `Assert.Empty() Failure: Collection was not empty … InstallCandidate { Package =
  com.storyworthtelling.focusphone.communication, VersionCode = 1103500000, Form = Watch }, …` — all four watch
  apps chosen for the phone, which is R1 exactly.
- `NoFormIsPhone`: `Assert.Empty() Failure` — a phone APK chosen for the watch.
- `AnUnknownFormIsInstalledNowhere({})`: `Assert.Equal() Failure: Values differ … Expected: Unknown`.
- `SamePackageTwoForms`: `System.NotImplementedException … at SiblingUpdatePlan.Decide(…)`.
- `AWatchApkInAPhonePublishStopsIt`, `TwoApksOneNameStopsIt`: `Assert.NotEqual() Failure: Values are equal
  Expected: Not 0 Actual: 0` — the old action published both.
- `EveryWatchApkSaysWatch`: `Assert.Matches() Failure: Pattern not found` (the action had no `form` input).
- `TheWatchRuleBites`: `Assert.Empty() Failure … "build-watch.yml: a publish that doesn't say its fo"…`.

## Green

- **`Storyworthtelling.Updates.Tests`: 162 passed, 0 failed**, with `RealApksSayTheirForm` and
  `TheRealAapt2AgreesWithThePhone` running against `/opt/android-sdk-min` (aapt2 36.0.0, android-36): the real
  sidecars read `{"package": "com.example.probe", "versionCode": 1100000123, "form": "watch"}` and `"phone"`.
- `GitHubReleaseClientTests.APhoneAndAWatchApkOfOnePackageKeepTheirOwnSidecars` was added after: it passed on its
  first run, since the client already maps a sidecar by its APK's name. It stays as a guard.
- **`FocusPhone/Codex.Tests`: 3082 passed, 0 failed. `RepoGuards.Tests`: 386 passed, 0 failed.
  `Watch.Tests`: 132 passed, 0 failed.** `leakscan --ci` over the chunk's diff: no personal-data shapes.
- **Compiled here:** `Flyleaf.Android` (with `Codex.Ui.Android`) and `Score.Android`, 0 errors, no new warnings in
  the changed files.
- **The action's own shell, run here:** its `run:` block, with real aapt2 and a stand-in `gh`, published a phone
  APK as `…-Signed.apk` with a `phone` sidecar and a watch APK as `…-Signed.watch.apk` with a `watch` sidecar,
  and stopped (exit 1, nothing uploaded) for a watch APK under `form: phone` and a phone APK under `form: watch`.

## How each path is closed

1. and 2. **`UpdateScope.Decide(role, selfPackage, deviceForm, …)` skips a candidate of another form before any
   version is compared**, and `InstallCandidate` carries an `ApkForm` with no default. `ApkSidecar.Read` reads
   `form`: absent is phone (every sidecar before P14), exactly `"phone"` or `"watch"` is that form, anything else
   (another word, another case, `null`, a number) is `Unknown`. A downloaded APK's form comes from **`ApkArchive`**
   (`Codex.Ui.Android`): the platform's `ReqFeatures` (asked for with `PackageInfoFlags.Configurations`) and
   **`ApkManifest.FormOf`**, which reads the compiled `AndroidManifest.xml` out of the zip and looks for the
   feature in its string pool (UTF-16, as aapt2 writes it, or UTF-8). The two must agree.
3. **`SiblingUpdatePlan.Decide`** (it exists again; CLAUDE.md named it, audit 13 found it gone) decides **per
   asset**: install, `AlreadyCurrent` or the new **`NotForThisDevice`**, form first. Flyleaf keys every decision by
   asset id. Before download a sidecar's form must agree with the asset's name; after download the APK's own
   reading must agree with its name and its sidecar, or it is `Unknown`. `NotForThisDevice` is settled, so the asset
   is not fetched again, and is not a failure, so Settings still says "Everything is up to date"; its log line is
   `UPDATE <package> <code>: NotForThisDevice — form watch`.
4. **Score** reads through `ApkArchive`, agrees the form with the asset's name and asks `UpdateScope` for the phone
   form only.
5. **`ApkInstaller` refuses anything whose manifest is not a phone's** before it opens a session
   (`FailureIncompatible`, "This app is built for a watch, so it isn’t installed on a phone."). Anything but a file
   on disk (the picked file's content stream) is copied to the cache first, since the zip has to be read by seeking.
   This is the last line behind 1–4 and the only line in front of 5.
6. and 7. **`publish-rolling-release` requires `form:`** (all eight phone workflows now say `form: phone`), finds
   aapt2 or publishes nothing, and stages every APK through **`.github/scripts/stage-apks.py`**: the form from
   aapt2's badging (`uses-feature` or `uses-feature-not-required` naming the watch feature), which must be the
   declared form; a watch APK named `<name>.watch.apk` (`ApkForms.WatchAssetSuffix`); a phone APK named like a watch
   APK, two APKs with one published name, an unreadable badging or an unknown form each stop the publish with
   nothing staged; the sidecar `{"package", "versionCode", "form"}`.
8. **`CiHygieneTests.EveryWatchApkSaysWatch`:** the action asks for the form and has no sidecar-less path; the
   script names the same five literals as `ApkForms`; every publish says `phone` or `watch`, a publish from
   `Watch/` and every publish in a workflow that builds a `Watch/` project or a FaceStudio `--apk` says `watch`, and
   one that says `watch` builds one; every app project under `Watch/` (one with an `ApplicationId`), and
   `Watch/Point.Face`, declares the watch feature in a manifest or a `UsesFeature` attribute. It holds now (no
   watch workflow, no watch app project yet; the face declares it). **`TheWatchRuleBites`** shows it fails on a
   `build-watch.yml` that publishes Dash with no form or `form: phone`, on a phone workflow publishing from
   `Watch/`, and on a `Watch/Dash.Wear` project with no feature, and passes once the attribute is there. A
   `Watch/` change now runs `Storyworthtelling.Updates.Tests` in CI (`affected-tests.py` READS, and `Watch/**` in
   `build-shared-libs.yml`'s filter).

## Decisions made in this chunk

- **An unknown form is installed nowhere, on both ends, and settled.** A sidecar's form that is there but is not
  exactly `phone` or `watch` was written by somebody and was not "phone": reading it as phone could install a watch
  APK whose writer misspelt one word, and reading it as "no sidecar" would leave the decision to the APK reading
  alone. Refusing is safe on the phone and on the watch. It is marked processed (the same asset never changes), and
  its log line says `form unknown`; a new build replaces the asset and is read afresh.
- **Readings must agree** (`ApkForms.Agree`): sidecar, asset name, the platform's `ReqFeatures` and the manifest
  scan. Any disagreement is `Unknown`. This turns "one reading is wrong on some Android version" into a refusal
  rather than an install.
- **`uses-feature android:required="false"` is still a watch app.** Wear OS's own marker either way, and the
  phone is never its device.
- **A watch APK's asset is `<name>.watch.apk`,** chosen by the action from the form it read, so a watch workflow
  cannot forget it; a phone APK keeps the name it has always had, so no phone asset moves.
- **No aapt2, no publish.** The old fallback (publish without sidecars) is gone: a sidecar-less watch APK under its
  phone app's name is exactly the case this chunk exists to prevent.
- **Flyleaf's and Score's device form is a constant, phone:** both are phone heads; the watch's installer is its own
  APK (W-3.1.2).
- **The phone marks a watch asset processed.** W-3.1.3 (Flyleaf sends watch updates) must keep its own record of
  what it has sent, or the phone's record would hide a watch APK from it.

## Not done, not verified

- **The publish action has not run in CI.** Its shell ran here against aapt2 36.0.0 with a stand-in `gh`; the
  runner's aapt2 is whatever `ANDROID_HOME` holds, and no real upload was made.
- **`PackageInfo.ReqFeatures` from `GetPackageArchiveInfo(…, Configurations)` is unverified on a device.** If a
  device left it empty, a watch APK's two readings would disagree and it would be refused (safe); a phone APK would
  read phone twice.
- **Built, device check pending:** Flyleaf's per-asset run, Score's, the installer's refusal and the Downloads copy,
  on the locked phone and on hers.
- **The watch end is pure only:** `TheWatchNeverInstallsAPhoneApk` holds in `UpdateScope` and `SiblingUpdatePlan`
  with `ApkForm.Watch`; the Point installer (W-3.1.2) must call them with the watch form, read the form with
  `ApkManifest`, and carry its own refusal of a phone APK.
- **No band-11 entry in `SuiteVersionBands`:** the table is keyed by package and the watch apps share packages;
  W-3.2.1 decides how it records a second band for one package.
- The form literals are frozen in `WatchFormTests.TheWordsAreFrozen`; plan §4's `WatchInvariantsTests` can hold
  them too once `Watch.Domain` references `Storyworthtelling.Updates` (W-3.1.2).

## Chunk A4 totals

- `Storyworthtelling.Updates.Tests`: **162 passed, 0 failed** (111 before). `FocusPhone/Codex.Tests`: **3082
  passed, 0 failed**. `RepoGuards.Tests`: **386 passed, 0 failed**. `Watch.Tests`: **132 passed, 0 failed**.

---

# Chunk A5 · the Point installer (W-3.1.2)

Every figure below is from a real run on 21 Nov 2026. As before, the red runs were taken with the tests written and the
code present only as stubs: methods threw `NotImplementedException`, and `WatchIdentity`'s constants were empty strings.

## What it is

`Watch/Point.Installer`: a Wear OS app (`net10.0-android`, `SupportedOSPlatformVersion` 34, the suite's one minimum:
`USER_ACTION_NOT_REQUIRED` needs API 31 and both watches run Wear OS 6, API 36, so nothing asks for more and
`OneBuildSettingTests` is untouched) under Flyleaf's package, `com.storyworthtelling.focusphone.launcher` (PC1, PC8). It
has no activity and no launcher entry (WD §11), declares `uses-feature android.hardware.type.watch` and
`com.google.android.wearable.standalone` = false (it is nothing without Flyleaf), and asks for three permissions:
`REQUEST_INSTALL_PACKAGES`, `UPDATE_PACKAGES_WITHOUT_USER_ACTION` and `POST_NOTIFICATIONS`. Its `<queries>` name the other
four watch packages, so it can see their versions. Label "Point installer". The Data Layer comes from Microsoft's
`Xamarin.GooglePlayServices.Wearable` **120.0.1.2** (published 2 Sep 2026; CLAUDE.md dependency rule 2), version in
`Directory.Packages.props`; every member name was read from the package's own assembly with `MetadataLoadContext` before a
call was written (the override pairs on `WearableListenerService` take `ChannelClient.IChannel`, not the old
`ChannelApi` channel; `ChannelClient.ActionChannelEvent`, `MessageClient.ActionMessageReceived` and the close reasons are
`const`, so the intent filters name them).

**The pure half, in `Watch.Domain`** (which now references `Storyworthtelling.Updates`, BCL only):
- `WatchIdentity`: the frozen identifiers of plan §4 as literals: the five packages, the two Data Layer paths
  (`/update/apk`, `/update/installed`), the capability (`point-installer`), the three registered component names
  (`…launcher.point.UpdateListenerService`, `…InstallStatusReceiver`, `…ReplacedReceiver`), the preferences file
  (`point-installer`) and key (`results`), and the notification channel (`point-install`).
- `update/apk` (`UpdateApkFrame`, `UpdateApkHeader`, `UpdatePaths`): Flyleaf opens a channel on `/update/apk/<transfer>`
  (1–64 of `a–z 0–9 -`, chosen by the sender) and writes four bytes of header length (big-endian), a UTF-8 JSON header
  `{v, package, versionCode, form, length, sha256}`, then exactly the APK. `Unpack` refuses a newer `v` or an unreadable
  header and copies nothing; otherwise it copies at most `length` bytes, hashing them, and the APK is `Intact` only if it
  was exactly that long, nothing followed, and the hash matched. A header with no `form` is `Unknown` (this contract is
  new; unlike a sidecar, nothing was ever written without one).
- `update/installed` (`InstalledCodec`): `{v, kind: "ask"}` from the phone; `{v, kind: "report", apps, results}` from the
  watch: each suite app's package and versionCode, and the last outcome of each app's install with the platform's status
  word for a failure. A newer `v` is refused before anything else is read; one unknown outcome or status word refuses the
  whole message.
- `WatchUpdatePlan.Decide(offered, installed)`: the form first (anything but watch is `NotForThisWatch`), then the suite's
  namespace (`NotOurs`), then A4's own rule, `SiblingUpdatePlan.Decide(Custodian, installer, ApkForm.Watch, …)` (not newer
  is `AlreadyCurrent`); then **FirstInstall** (not on the watch: the wrist is asked once), **AskedUpdate** (installed by
  another installer, by hand with adb: asked once, after which this installer is of record) or **SilentUpdate** (this
  installer is its installer of record, or the package is the installer itself, which may update itself).
  `Verify(received, reading, installed)` decides on what arrived: a transfer that is not intact, an APK the platform
  cannot read, or a package or versionCode other than the header's is `Damaged`; the form is
  `ApkForms.Agree(header, manifest scan, platform's uses-feature list)`, so **the installer refuses a phone APK itself**
  whatever the header or the phone said; then `Decide` again on the APK's own package and version.
- `WatchInstallOutcomes`: a platform status as the watch's words (`InstallerStatus`, mapped from `PackageInstallStatus` by
  name in the Android half, so no platform number is copied) → `Waiting` (pending user action), `Installed`,
  `InstalledAfterAsking` (success after the same version had been waiting: how R2 is answered), `Declined` (aborted),
  `Failed` (everything else, with its status word). `SendAgain` is true only for `Damaged` and `Failed`: a prompt waiting
  on the wrist, or one turned down, is not answered by sending the same APK again. `InstallResults` keeps the last outcome
  per app, never letting an older build's result replace a newer one's (a newer build abandons the older one's waiting
  session, whose end can be reported after it).
- `UpdateLines`: the log lines: `UPDATE <package> <versionCode>: <Outcome>[ — <status>]`, `… Waiting — asked although
  silence was requested` (R2), `… Waiting — notifications are off, so nothing reached the wrist`, and `CONTRACT
  update/apk: received v1` / `refused v2, newer than this watch reads` / `refused, unreadable` / `transfer ended,
  disconnected` / `sent v1` / `not sent, ApiException`. Never an APK's bytes, a message's body or the platform's free-text
  status message.
- `InstallPrompt`: the one sentence the wrist is asked, "Tap to install Dash." / "Tap to update Dash.", through
  `WatchText`'s rules; one notification id per app.

**The Android half** (thin: it carries bytes, asks the platform and reports):
- `UpdateListenerService` (`WearableListenerService`, exported, two intent filters: `CHANNEL_EVENT` with
  `pathPrefix=/update/apk` and `MESSAGE_RECEIVED` with `path=/update/installed`). On a channel it asks Play services to
  `ReceiveFile` into `cache/update-apk/<transfer>.part` (so a process that dies mid-transfer loses nothing, and the
  close callback finds the file again after a restart); on `OnInputClosed` with a normal close it installs; on an ask it
  answers the asking node.
- `WatchInstaller`: unpacks the frame into `<transfer>.apk`, decides on the header, reads the APK itself
  (`GetPackageArchiveInfo(…, Configurations)` for package, versionCode and `ReqFeatures`, and `ApkManifest.FormOf`),
  verifies, abandons any session of its own still waiting for the same app (one prompt per app), then opens a
  `PackageInstaller` session with `SetRequireUserAction(NotRequired)` for a silent update and `Required` otherwise, writes
  the APK and commits with a status `PendingIntent` to `InstallStatusReceiver`. Files are deleted once the session holds
  its copy.
- `InstallStatusReceiver`: classifies the status, records it and reports to every connected node. "Pending user action"
  puts **one silent card** on the wrist (`InstallCard`: channel importance Low, no vibration, no sound) whose tap opens the
  system's own prompt; a settled outcome clears it.
- `ReplacedReceiver` (`MY_PACKAGE_REPLACED`): after the installer updates itself, which ends its process before its own
  session's status can arrive, it reports what the watch has.
- Logs go to logcat under the tag `Point`.

## Tests written first

- `Watch/Watch.Tests/Updates/WatchUpdatePlanTests.cs` (29 cases; 26 in the red run, see below): `FirstInstallAsksOnce`, `UpdateOfRecordIsSilent`,
  `UpdateOfAnotherInstallerAsks`, `TheInstallerUpdatesItselfSilently`, `TheFaceInstallsLikeAnApp`,
  `TheWatchNeverInstallsAPhoneApk` ×4 (Post's, Score's, Constitution's and Flyleaf's packages), `AnUnknownFormIsRefused`,
  `OnlyTheSuite` ×3, `TheFormComesFirst`, `NotNewerIsCurrent` ×2, `OtherAppsDoNotCount`, `VerifyInstallsWhatAgrees`,
  `ADamagedTransferIsRefused`, `WhatThePlatformCannotReadIsRefused`, `AnotherPackageIsRefused`, `AnotherVersionIsRefused`,
  `APhoneApkCalledAWatchApkIsRefused`, `ReadingsMustAgree` ×3, `AHeaderThatSaysPhoneIsRefused`,
  `VerifyStillComparesVersions`, `VerifyKeepsTheSilentRule`.
- `Watch/Watch.Tests/Updates/InstallOutcomeTests.cs` (60 cases, 56 at the first red run): `StatusMeansOneOutcome` ×11, `EveryStatusIsClassified`,
  `SuccessAfterAskingIsSaid`, `PendingIsWaitingEitherWay`, `AFailureAfterAskingIsAFailure`, `OnlyFailuresAreSentAgain` ×9,
  `TheOutcomeWordsAreFrozen`, `TheStatusWordsAreFrozen`, `WordsRoundTrip`, `AnyOtherWordIsNoOutcome` ×5,
  `OneResultPerApp`, `WaitingIsForTheSameVersion`, `ResultsRoundTrip`, `ADamagedRecordReadsEmpty` ×5, the line tests
  (`TheLineIsTheOutcome`, `AFailureNamesItsStatus`, `AnAskedSilentUpdateIsSaid`, `TheLinesSayWordsAndNumbersOnly`,
  `AReceiptSaysTheVersion`, `ANewerOneIsRefusedByVersion`, `AnUnreadableOneSaysSo`, `ATransferThatDidNotFinishSaysHow`),
  `TheWristIsAskedOneSentence` ×7, `EachAppHasItsOwnPrompt`; added in two later rounds (below): `ACardThatCannotShowIsSaid`,
  `TheInstallersOwnWords`, `AnOlderResultDoesNotReplaceANewerOne`, `ASendSaysTheVersion`.
- `Watch/Watch.Tests/Updates/UpdateApkTests.cs` (51 cases): `TheHeaderDescribesTheApk`, `TheApkFollowsTheHeader`,
  `EveryHeaderCarriesAnIntegerV`, `ANewerVersionIsRefused`, `AMalformedHeaderIsUnreadable` ×13, `ABrokenFrameIsUnreadable`
  ×6, `AnyOtherFormIsUnknown` ×4, `NoFormIsUnknownHere`, `AShortApkIsNotIntact`, `TrailingBytesAreNotIntact`,
  `AChangedByteIsNotIntact`, `TheCopyNeverRunsPastTheLength`, `TheFieldNamesAreFrozen`, `TheFormIsTheSidecarsWord`,
  `APathCarriesItsTransfer` ×3, `AnyOtherPathHasNoTransfer` ×11, `ABadTransferIsRefused` ×3.
- `Watch/Watch.Tests/Updates/InstalledMessageTests.cs` (22 cases): `AnAskRoundTrips`, `AReportRoundTrips`,
  `AnEmptyReportRoundTrips`, `EveryMessageCarriesAnIntegerV`, `ANewerVersionIsRefused`, `AMalformedMessageIsUnreadable`
  ×14, `NothingIsUnreadable`, `TheReportSaysNothingElse`, `TheWordsAreFrozen`.
- `Watch/Watch.Tests/Guard/WatchInvariantsTests.cs` (plan §4): `PackagesAreFrozen`, `EveryPackageIsTheSuites`,
  `TheInstallerIsFlyleafsPackage` (csproj and manifest), `TheFaceHasItsOwnPackage`, `PathsAreFrozen`,
  `TheCapabilityIsFrozen`, `TheInstallerAdvertisesItsCapability`, `TheListenerHearsBothPaths`, `ComponentsAreFrozen`,
  `EachComponentIsRegisteredByItsName` ×3, `StoredNamesAreFrozen`, `TheIdentityIsLiterals` (no `nameof`, every const a
  literal), `TheIdsAreNotCopiedIntoTheInstaller`, `FormWordsAreFrozen` (A4's five words, now held on the watch's side too),
  `TheInstallerIsAWatchApp`, `TheInstallerAsksForThreePermissions`, `TheInstallerSeesTheWatchApps`,
  `TheInstallerHasNoScreen`, `TheInstallerNeverBuzzes`, `FrozenSetsAreNotEmpty`.

## Red

**Watch.Tests, first attempt, not counted:** 309 tests, 173 failed, 136 passed. Two guards passed for the wrong reason,
over empty sets: `EveryPackageIsTheSuites` (the stub's package list was empty) and `TheIdsAreNotCopiedIntoTheInstaller`
(no installer sources yet). Each was given an `Assert.NotEmpty`; the code was not touched.

**Red run: `Watch.Tests` 309 tests, 175 failed, 134 passed.** The 134 are A1–A4's 132 and two that pass on the stubs
because they hold what does not change: `FormWordsAreFrozen` (A4's constants) and `TheIdentityIsLiterals` (the stub's
constants were literals). The 175: `WatchUpdatePlanTests` 26 of 26, `InstallOutcomeTests` 56 of 56,
`InstalledMessageTests` 22 of 22, `UpdateApkTests` 51 of 51, `WatchInvariantsTests` 20 of 22. First errors:
`System.NotImplementedException : The method or operation is not implemented.` (149 of them, the first
`InstallOutcomeTests.TheWristIsAskedOneSentence`); `Assert.Equal() Failure: Strings differ` (8, the frozen ids stubbed
empty, first `ComponentsAreFrozen`); `Watch/Point.Installer/Properties/AndroidManifest.xml isn’t there.` (4);
`Assert.NotEmpty() Failure: Collection was empty` (3); `Assert.Throws() Failure: Exception type was not an exact match`
(3, `ABadTransferIsRefused`); `The installer has no UpdateListenerService.` The suite reads 312 once the ids are real:
with every package stubbed as `""`, xUnit folded `TheWatchNeverInstallsAPhoneApk`'s four identical rows into one.

**Second red (two words the Android half needed, `UpdateLines.CannotAsk` and `InstallPrompt.InstallerLabel` /
`ChannelName`):** 314 tests, 14 failed: the 12 installer guards still waiting for the Android half, `ACardThatCannotShowIsSaid`
(`NotImplementedException`) and `TheInstallersOwnWords` (`Strings differ`). Green on the Domain: 302 of 314.

**Third red (`UpdateLines.Sent`/`NotSent`, and the older-result rule):** 316 tests, 5 failed: `ASendSaysTheVersion`
(`NotImplementedException`), `AnOlderResultDoesNotReplaceANewerOne` (the older build's `Declined` replaced the newer one's
`Waiting`), and three installer guards (`TheInstallerNeverBuzzes`, `EachComponentIsRegisteredByItsName` for the two
receivers). Green on the Domain: 313 of 316.

**`Storyworthtelling.Updates.Tests`, with the new project in place: 162 tests, 3 failed:**
`CiHygieneTests.EveryPathFilterCoversEveryProjectItsWorkflowBuilds` ("A change here runs none of the tests that read it:
test-tools.yml: Storyworthtelling.Updates": FaceStudio builds `Watch.Domain`, which now builds on the Updates library),
`CiHygieneTests.EveryWatchApkSaysWatch` ("Watch/Point.Installer: declares no android.hardware.type.watch", before the
manifest existed: A4's guard biting on its first real watch app), and
`ProjectReferenceTests.EveryProjectThatNamesTheUpdateLibraryReferencesIt` ("Watch/Watch.Tests/Watch.Tests.csproj").

**`FocusPhone/Codex.Tests` (`FrozenInvariantsTests`, 49 tests): 2 failed**, both correct under PC1 and both wrong as
written: `LauncherCsprojDeclaresFrozenApplicationId` ("Exactly one csproj must declare the frozen launcher ApplicationId
'com.storyworthtelling.focusphone.launcher'; found 2") and `EveryOwnerHeadIsPublishedToTheRollingRelease` ("These heads
declare an owner-namespace ApplicationId but no workflow publishes their signed APK … : Watch/Point.Installer").

**`RepoGuards.Tests`, first run after the code: 386 tests, 1 failed:** `FixedWordingTests` flagged "cannot" in a log line
(`WatchInstallOutcome.cs:206`, "spelled out where the copy contracts"); the line was reworded ("so nothing reached the
wrist").

## Green

- **`Watch.Tests`: 316 passed, 0 failed.**
- **`Storyworthtelling.Updates.Tests`: 162 passed, 0 failed**, after `test-tools.yml`'s filter gained
  `Storyworthtelling.Updates/**`, `Watch.Tests` gained a direct reference, and the manifest declared the watch feature.
- **`FocusPhone/Codex.Tests`: 3082 passed, 0 failed.** The two guards were changed, not loosened:
  `LauncherCsprojDeclaresFrozenApplicationId` now holds exactly one *phone* csproj to the launcher's id and allows the
  id under `Watch/` only to `Watch/Point.Installer/Point.Installer.csproj`; a probe `Watch/ProbeWear/ProbeWear.csproj` with
  the id failed it ("Only the Point installer may carry the launcher's package on the watch"). The publish guard exempts
  a `Watch/` head **only while `.github/workflows/build-watch.yml` does not exist**: a probe empty `build-watch.yml` made it
  fail on `Watch/Point.Installer`, so the exemption ends by itself when W-3.2.1 lands. Both probes were deleted.
- **`RepoGuards.Tests`: 386 passed, 0 failed.** `FaceStudio.Tests`: 19 passed (the committed face is unchanged).
  `leakscan --ci` over the chunk's diff: "no personal-data shapes in the added lines".
- **Compiled here:** `dotnet build Watch/Point.Installer/Point.Installer.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min
  -p:AndroidUseApkSigner=false`. dl.google.com was reachable, so Xamarin.Build.Download fetched the play-services aars. The
  first build failed with two errors: `CS0104` (`Environment` ambiguous between `Android.OS.Environment` and
  `System.Environment`) and `CS0121` (`PackageManager.PackageInfoFlags.Of(0)` ambiguous between `Of(long)` and
  `Of(PackageInfoFlagsLong)`); both fixed. **Build succeeded, 0 warnings, 0 errors**, Debug and Release; `Watch.Ui.Wear`
  still builds with 0 warnings.
- **The built APK:** `aapt2 dump badging` reads `package: name='com.storyworthtelling.focusphone.launcher'`,
  `application-label:'Point installer'`, `uses-feature: name='android.hardware.type.watch'`. The Release merged manifest
  requests **4 permissions**: the three above and androidx.core's `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` (no
  `INTERNET`: Play services carries the Data Layer); its one `<activity>` is Play services' own non-exported
  `GoogleApiActivity`. A4's `stage-apks.py`, run on it with real aapt2, staged
  `com.storyworthtelling.focusphone.launcher-Signed.watch.apk` with the sidecar `{"package":
  "com.storyworthtelling.focusphone.launcher", "versionCode": 1, "form": "watch"}` under `--form watch`, and refused it
  under `--form phone` (exit 1, nothing staged). `ApkManifest.FormOf` reads it as `Watch` (and Flyleaf's APK as `Phone`).
  `check-dependency-graph.py` over its restored graph: no package missing.

## Decisions made in this chunk

- **The prompt is a silent card, because the installer has no screen.** WD §11 gives the installer "only the system's
  own install prompt". A first install comes back from the session as "pending user action" with that prompt as an
  intent, and Android does not let a background app put an activity in front of anyone, so the prompt waits behind one
  notification card per app ("Tap to install Dash."), importance Low, no vibration, no sound (WD §7: none of the four
  buzzes means this), cleared when the session settles. Its tap opens the system's prompt directly. The owner may
  overturn the wording or the card.
- **The owner's one-time adb step becomes `adb install -g`** (A78): `POST_NOTIFICATIONS` is a runtime permission and the
  installer has no screen to ask from; `-g` grants it at install. Without it every first install waits unseen, and the
  log says so (`… Waiting — notifications are off, so nothing reached the wrist`). "Install unknown apps" is still allowed
  once on the wrist, by the system's own flow (or ahead of time with `adb shell appops set
  com.storyworthtelling.focusphone.launcher REQUEST_INSTALL_PACKAGES allow`).
- **Each APK is received by Play services into a file named for its transfer** (`ChannelClient.receiveFile`, the Data
  Layer's own way to move a file), on the path `/update/apk/<transfer>`: the frozen prefix plus a name the sender
  chooses. A 10–60 MB APK takes minutes over Bluetooth; nothing of the installer's needs to stay alive for it, and the
  close callback finds the file again in a new process. W-3.1.3 writes the frame with `UpdateApkFrame.Pack` and names
  the transfer with `UpdatePaths.ApkPathFor`, so both ends share one layout.
- **The installer reads every APK twice and refuses on any disagreement**, the A4 rule carried to the watch: header,
  manifest scan and platform must all say watch, and the platform's package and versionCode must be the header's.
- **Silence is asked for only where the platform may grant it** (installer of record, or itself) and `Required` is set
  otherwise; the outcome says which way it went (`Installed` against `InstalledAfterAsking`), and the log line says when
  silence was asked for and the wrist was asked anyway, which is risk R2 found on the first update.
- **`update/installed` is one path with a `kind`**, since the plan names one contract for both directions; a report goes
  to the node that asked, and to every connected node after each settled outcome and after the installer replaces
  itself.
- **What is logged:** outcomes, contract lines and the platform's status *word*, never the platform's free-text status
  message (which names packages and paths, and is not needed to tell a signature clash, `conflict`, from a full disk,
  `storage`), and never an APK's bytes. The watch has no log loop yet, so the lines are in logcat and the outcomes reach
  the phone in the report, where W-3.1.3 can log them.
- **The FocusPhone guards learnt PC1** as described above, rather than being exempted wholesale.

## Not done, not verified

- **Built, device check pending: all of it.** Play services delivering a channel and a message to the installer;
  `receiveFile` writing into the installer's cache and `onInputClosed` arriving, including after the process died;
  a 10–60 MB APK over Bluetooth and over the cloud relay; the card on Wear OS 6 and its tap opening the system's prompt;
  the "install unknown apps" step; `GetInstallSourceInfo` naming this installer after a first install; the self-update and
  its `MY_PACKAGE_REPLACED` report; the `<queries>` visibility.
- **Whether `USER_ACTION_NOT_REQUIRED` works on Wear OS 6 is unverified** (risk R2; documented for phones since Android
  12). If it does not, each update asks once on the wrist and the log says so.
- **The stopped state:** an app installed with adb and never started is in Android's stopped state; whether Play services
  binds the listener of an app that has never run is unverified. If it does not, the one-time adb step needs one command
  that starts a component.
- **A card swiped away without an answer leaves its session waiting** (`Waiting`, not sent again); sending the same build
  again, or a newer one, replaces it. W-3.1.3 decides whether to re-offer a waiting build.
- **A full disk while the frame is unpacked** is logged as `UPDATE work stopped: System.IO.IOException` with no outcome
  recorded; the phone sees the version unchanged.
- **Not in this chunk:** Flyleaf's sender and its own sent-record (W-3.1.3); the watch workflow, signing with the suite's
  keystore (the Data Layer delivers only between apps of one signature, so a debug-signed installer will not hear the
  release-signed Flyleaf), band 11, publishing, and the APP ID / FORM / ICON / FONT / DEPENDENCY checks (W-3.2.1). No CI
  workflow builds the installer or runs `Watch.Tests` yet. The installer has **no launcher icon resource** (it has no
  launcher entry; the system shows its default in the watch's settings); an IconStudio icon is W-3.2.1's if the ICON CHECK
  wants one. CLAUDE.md has no P14 section with the installer's build command; left to the release.

## Chunk A5 totals

- `Watch.Tests`: **316 passed, 0 failed** (132 before). `Storyworthtelling.Updates.Tests`: **162 passed, 0 failed**.
  `FocusPhone/Codex.Tests`: **3082 passed, 0 failed**. `RepoGuards.Tests`: **386 passed, 0 failed**. `FaceStudio.Tests`:
  19 passed. `Watch/Point.Installer` and `Watch/Watch.Ui.Wear` compile here, 0 warnings.

---

# Chunk A6 · Flyleaf sends watch updates (W-3.1.3)

Every figure below is from a real run on 21 Nov 2026. As before, the red runs were taken with the tests written and the
code present only as stubs: methods threw `NotImplementedException` and the phone's new `WatchIdentity` constants were
empty strings.

## What it is

**The pure half, in `Watch.Domain/Updates`** (Flyleaf now references `Watch.Domain`, so the frame codec and the
`update/installed` codec the installer reads are the very ones Flyleaf writes; nothing was copied or moved, and
`Watch.Domain` gained a reference to the BCL-only `Storyworthtelling.Time` for the line):
- `WatchSendPlan.Decide(offered, answer, sent, now, force)`: nothing at all unless the watch answered (`WatchAnswer`:
  `NoWatch`, `Unreachable`, `NoAnswer`, `Unreadable`, `Newer`, or `Answered` with its report); only the suite's
  `form: watch` assets; A4's own rule (`SiblingUpdatePlan`, custodian, watch form) for newer-or-absent against the
  report's apps; then, for a due build, the watch's own result for that very build first and the phone's sent-record
  second: installed (any of the three success words) is `AlreadyCurrent`, `NotForThisWatch`/`NotOurs` is
  `RefusedByTheWatch`, `Declined` is not sent again, `Waiting` is `WaitingOnTheWrist` until its send is three days old,
  `Failed`/`Damaged` is sent again, and no word yet is `InFlight` for an hour after the send. The installer itself may be
  sent and always goes **last**. `TransferName(assetId, nonce)` names each channel.
- `WatchSentRecord` (one entry per app, the latest send), `WatchRunRecord` (when, reach, what was due, sent, not sent, the
  newest report; `WithReport` for a report that comes later) and `WatchRunCodec`, all JSON for Flyleaf's preferences.
- `WatchUpdateLine.Say`: Settings' one sentence, or nothing when no run has happened or no watch is paired.
  "Watch checked 2h ago: Wave didn’t install (conflict), Beat is waiting for a tap and Dash installed.",
  "…: everything is up to date.", "…: it was out of reach, so nothing was sent." Counts go through `Quantities`
  ("2 apps installed."), the time through `RelativeTime`, names through `InstallPrompt.NameOf`.
- `WatchSendLines`: `WATCH run: Unreachable`, `WATCH <package> <code>: WaitingOnTheWrist`, `…: not sent, IOException`,
  `…: left for the next run`, and `Heard` for a received `update/installed` (`CONTRACT update/installed: received v1` /
  `refused v2, newer than Flyleaf reads` / `refused, unreadable`). Packages, numbers and words only.
- `WatchIdentity` gained Flyleaf's frozen names as literals: `PhoneReportListenerName`
  (`com.storyworthtelling.focusphone.launcher.WatchReportListener`), `PhonePreferences` (`watchUpdates`), `SentKey`
  (`sent`), `LastRunKey` (`lastRun`).

**The Android half in Flyleaf** (`FocusPhone/Flyleaf.Android/WatchUpdates.cs`, thin):
- `UpdateService.SendToWatchAsync(context, force)`, under its own gate, called by `UpdateWorker` **after** the phone's run
  has finished, been recorded (`UpdateStatusStore`) and logged, and after the ratings refresh; and by Settings' "Install
  from GitHub now" (with `force`) after the phone's line is on screen, which then redraws for the watch's line.
  `WatchUpdates.RunQuietlyAsync` catches anything unforeseen and logs its shape (`HeadLog.Quiet`), so it cannot break,
  delay or change the phone's result.
- Finding the watch: `CapabilityClient.GetCapabilityAsync(WatchIdentity.InstallerCapability, FilterAll)` (none, or an
  `ApiException` because the phone has no Wear OS, is `NoWatch`), then `FilterReachable` (none is `Unreachable`; a nearby
  node is preferred). Asking: `MessageClient.SendMessageAsync(node, /update/installed, InstalledCodec.Encode(new
  InstalledAsk()))`, then up to `WatchSendPlan.AnswerWait` (30 s) for the report.
- `WatchReportListener` (`WearableListenerService`, exported, filter `MESSAGE_RECEIVED` on `wear://*/update/installed`,
  registered by the frozen name): decodes, logs `Heard`, writes a report into the stored last run, and wakes the run
  waiting on it (`WatchReports`, an in-process ticket and signal). It also hears the report the installer sends every
  connected phone after each settled install, so the line learns of a tap on the wrist without waiting six hours.
- Sending: the release (the same `GitHubReleaseClient` and token), each `.watch.apk` asset's sidecar with its form agreed
  against the name; for each `Send`, the APK downloads by asset id (`UpdateService.DownloadAsync`, now internal) into the
  cache, is read with `ApkArchive` (it must read as a watch APK of the sidecar's package and version, or it is not sent),
  and goes down `ChannelClient.OpenChannelAsync(node, UpdatePaths.ApkPathFor(transfer))` as `UpdateApkFrame.Header`
  (length and SHA-256 from `UpdateApkFrame.Describe`) then exactly the APK, in 64 KB writes; the output is closed, the run
  waits up to a minute for a report naming that build, then closes the channel and deletes the file. A completed send is
  recorded (`WatchUpdateStore.RecordSent`); a failure (HTTP, IO or a Java exception) is logged by type and counted as not
  sent. No new send starts after `SendBudget` (5 min); the rest wait for the next run.
- The binding is Microsoft's `Xamarin.GooglePlayServices.Wearable` (already central, 120.0.1.2). Member names were read
  from the package's own assembly with `MetadataLoadContext` (`GetCapabilityAsync(string, int)`, `FilterAll` /
  `FilterReachable`, `ICapabilityInfo.Nodes`, `INode.IsNearby`, `OpenChannelAsync(string, string)`,
  `GetOutputStreamAsync` returning `Java.IO.OutputStream`, `CloseAsync`, and `ApiException.StatusCode` in Basement).

## Tests written first

- `Watch/Watch.Tests/Updates/FlyleafWatchUpdateTests.cs` (66 cases): `SendsOnlyNewer`, `AnOlderBuildIsNotSent`,
  `WhatIsNotThereIsSent`, `APhoneApkIsNeverSent`, `OnlyTheSuite`, `TheFaceIsSentLikeAnApp`, `TheInstallerMayUpdateItself`,
  `TheInstallerGoesLast`, `WaitsForTheWatch` ×5, `AnAnswerNeedsAReport`, `ASendStillLandingIsNotSentAgain`,
  `ALostSendIsSentAgain`, `AnOlderSendDoesNotHoldANewerBuild`, `AResultForAnotherBuildDoesNotCount`,
  `AWaitingCardIsLeftAlone`, `ACardLeftUnansweredIsOfferedAgain`, `AWaitingCardWithNoRecordIsOfferedAgain`,
  `ADeclinedBuildIsNotSentAgain`, `ANewerBuildFollowsADecline`, `AFailureIsSentAgain` ×2, `ARefusalIsNotSentAgain` ×2,
  `AReportedInstallCounts` ×3, `TheOwnerCanAskAgain`, `TheRecordKeepsOneSendPerApp`, `TheRecordRoundTrips`,
  `ADamagedRecordReadsEmpty` ×6, `ATransferHasItsOwnName`, `NoRunSaysNothing`, `NoWatchSaysNothing`,
  `SaysWhyNothingWent` ×4, `SaysHowItWent`, `NothingDueIsUpToDate`, `ManyAreCounted`, `ManyWaitingAreCounted`,
  `ASendNotYetReportedIsOnItsWay`, `ASendThatFailedOnThePhoneDidNotInstall`, `ADeclineIsSaid`,
  `ALaterReportUpdatesTheLine`, `TheRunKeepsWhatWasDue`, `TheRunRoundTrips`, `ARunWithNoReportRoundTrips`,
  `ADamagedRunReadsAsNone` ×6, `TheLogSaysOutcomesOnly`, `WhatThePhoneHeardIsLogged`, `TheWaitsAreTheDecidedOnes`.
- `Watch/Watch.Tests/Guard/WatchInvariantsTests.cs`, Flyleaf's half: `ThePhonesNamesAreFrozen`,
  `FlyleafRegistersItsListenerByName`, `FlyleafAsksForTheInstallerByItsCapability`, `TheIdsAreNotCopiedIntoFlyleaf`,
  `FlyleafReadsTheSameCodec` (Flyleaf's csproj references `Watch.Domain` and the Wear binding, and no Flyleaf source writes
  its own frame), and `FlyleafsBuildWatchesTheWatchsContracts`.

## Red

**Red run: `Watch.Tests` 386 tests, 69 failed, 317 passed.** The 317 are A1–A5's 316 and `TheWaitsAreTheDecidedOnes`,
which passes on the stubs because the waits were declared with the stub. The 69: `FlyleafWatchUpdateTests` 64 of 65,
`WatchInvariantsTests` 5 of 5 new. First errors: `System.NotImplementedException : The method or operation is not
implemented.` (63, the first `NothingDueIsUpToDate`); `Assert.Throws() Failure: Exception type was not an exact match …
Expected: typeof(System.ArgumentException)` (`AnAnswerNeedsAReport`); `Assert.Equal() Failure: Strings differ`
(`ThePhonesNamesAreFrozen`); `Assert.Single() Failure: The collection was empty` (`FlyleafRegistersItsListenerByName`);
`Assert.All() Failure: 2 out of 5 items in the collection did not pass. [3]: Item: ""` (`TheIdsAreNotCopiedIntoFlyleaf`,
the stubbed names); and `Assert.Contains() Failure: Sub-string not found … "WatchIdentity.InstallerCapability"` and
`… "..\..\Watch\Watch.Domain\Watch.Domain.csproj"` (`FlyleafAsksForTheInstallerByItsCapability`,
`FlyleafReadsTheSameCodec`).

**After the pure half: 386 tests, 3 failed** (the three Flyleaf source guards, waiting for the Android half).

**Second red (`WatchSendLines.Heard`, which the listener needed):** `WhatThePhoneHeardIsLogged` 1 of 1 failed,
`NotImplementedException`.

**Third red (`FlyleafsBuildWatchesTheWatchsContracts`):** 1 of 1 failed, `Assert.Contains() Failure: Sub-string not found
… "- 'Watch/Watch.Domain/**'"`. `CiHygieneTests.EveryPathFilterCoversEveryProjectItsWorkflowBuilds` follows `.csproj`
paths named in a workflow, and `build-flyleaf.yml` publishes its heads by folder name, so that guard passed with Flyleaf
building a project its filter did not watch; this guard holds the one case.

**The first Flyleaf build failed at restore:** `error NU1107: Version conflict detected for
Xamarin.AndroidX.Lifecycle.LiveData.Core`: `Work.Runtime 2.10.2 → Lifecycle.LiveData 2.9.1 → LiveData.Core (>= 2.9.1 &&
< 2.9.2)` against `GooglePlayServices.Wearable 120.0.1.2 → Basement 118.10.0.3 → AndroidX.Fragment 1.9.0 → LiveData.Core
(>= 2.11.0.1)`. Every Play services release from Basement 118.10.0.1 on asks for a LiveData.Core above 2.9.1 (Fragment
1.8.9.2 needs 2.10.0.1), so an older Wear binding would not have avoided it.

## Green

- **`Watch.Tests`: 388 passed, 0 failed.**
- **`Storyworthtelling.Updates.Tests`: 162 passed, 0 failed. `RepoGuards.Tests`: 386 passed, 0 failed.
  `FocusPhone/Codex.Tests`: 3082 passed, 0 failed. `FaceStudio.Tests`: 19 passed, 0 failed.**
- **Compiled here:** `dotnet build FocusPhone/Flyleaf.Android/Flyleaf.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min
  -p:AndroidUseApkSigner=false`: **Build succeeded, 0 errors**; the 35 warnings are all in files this chunk did not touch
  (`GmailApiClient`, `BookendChipView`, `HomeActivity`, `DeviceAccountTokens`' doc comments), none in `WatchUpdates.cs`,
  `UpdateService.cs` or `LauncherSettingsActivity.cs`. The merged manifest declares
  `com.storyworthtelling.focusphone.launcher.WatchReportListener`, exported, `MESSAGE_RECEIVED` with `scheme wear`,
  `host *`, `path /update/installed`. `dotnet build Watch/Point.Installer/Point.Installer.csproj …`: **0 warnings, 0 errors.**
- **`check-dependency-graph.py FocusPhone/Flyleaf.Android` over the restored graph: "Checked 1 dependency graphs.", no
  package missing.** The graph holds `Concurrent.Futures` 1.3.0.4 (and `.Ktx` 1.2.0.7), `Work.Runtime` 2.10.2,
  `Lifecycle.LiveData` and `LiveData.Core` 2.11.0.1, and `Wearable` 120.0.1.2.
- `leakscan --ci` over the chunk's diff: "no personal-data shapes in the added lines".

## Decisions made in this chunk

- **The codec stays in `Watch.Domain` and Flyleaf references it.** `Watch.Domain` is BCL only (Folds, Updates, Time), so
  the phone and the installer read one `UpdateApkFrame` and one `InstalledCodec` with nothing moved; the cost is a few
  watch-only types (tokens, face geometry) in Flyleaf's APK, which nothing on the phone calls.
- **Flyleaf takes LiveData 2.11.0.1 with a `VersionOverride`**, and the central version is 2.9.1, exactly what WorkManager
  2.10.2 already brings every head, so no other head's graph moves. WorkManager asks for LiveData 2.9.1 or newer, so it is
  satisfied. `OneBuildSettingTests.AllowedOverrides` names it with the reason, beside Plate's AppCompat. Raising
  WorkManager suite-wide to 2.11 (which brings LiveData 2.11) was the alternative; it would move every head's Lifecycle
  graph, including the MAUI heads', which AppCompat's note says breaks, so it waits for a release of its own.
- **The watch half runs after the phone's, under its own gate, and never inside the phone's run.** The phone's result and
  line are recorded before the watch is looked for; the worker's result is the phone's whatever the watch did.
- **The sent-record keeps one entry per app, and the watch's own result for that build wins over it.** The phone's
  processed set marks every watch asset `NotForThisDevice` (A4), so the watch half never reads it.
- **In flight for an hour.** A 60 MB APK over Bluetooth takes minutes and an install seconds; a dropped channel or a full
  disk leaves no result on the watch (A5), so after an hour with no word the send is taken as lost. Runs are six hours
  apart, so in practice this only stops "Install from GitHub now" doubling a send that is still landing; it applies even
  when the owner asks.
- **A card left unanswered is offered again after three days, and at once when the owner asks.** A swiped card leaves its
  session waiting; Android's package installer destroys an abandoned session after three days, after which nothing is
  left on the wrist to tap, so the build is sent again and the installer puts up a fresh card. A declined build is not sent
  again until a newer build or the owner's ask. Both follow the installer's own `SendAgain` rule otherwise.
- **A reported install of this build counts, even if the app is gone from the watch's list**, so an app the owner took off
  the watch by hand, or one the installer cannot see, is not pushed back on every run.
- **The installer is sent last**, since replacing itself ends its process and whatever came after would land on nobody.
- **The channel stays open until the installer reports on that build (at most a minute)**, then is closed, rather than
  being closed the moment the output is, which could cut off bytes still in Play services' buffers.
- **No sidecar, no send:** a watch asset with no readable sidecar is logged and waits, rather than downloading 10–60 MB
  to learn its version; A4 makes the sidecar mandatory.
- **Five minutes of sends per run**, so the watch half with the phone's own installs stays inside WorkManager's ten.
- **A phone with no Wear OS (an `ApiException` from the capability lookup) is the same as no watch:** the line says
  nothing, and the log names the status code.

## Not done, not verified

- **Built, device check pending: all of the Data Layer behaviour.** The capability lookup finding the installer with
  `FilterAll` and `FilterReachable`; the ask and the report arriving through `WatchReportListener` within 30 seconds; the
  live report after an install; a 10–60 MB APK down `ChannelClient`'s output stream, over Bluetooth and the cloud relay;
  closing the output ending the watch's input normally; and Play services running on the locked phone. The two ends will
  only hear each other once both are signed with the suite's keystore (W-3.2.1); a debug-signed installer will not.
- **Whether Android really drops an unanswered install session after three days on Wear OS 6** is from the platform's
  documented phone behaviour, not seen on a watch; if sessions last longer, a second card replaces the first (one per app).
- **WorkManager's ten-minute ceiling** with a slow first transfer of several apps: a send that starts near the budget can
  still run past it, and the worker's stop is then logged by `OnStopped`.
- **LiveData 2.11 under WorkManager 2.10 in Flyleaf** compiles and resolves with nothing missing; it has not run.
- Not in this chunk: the watch workflow, band 11 and signing (W-3.2.1).

---

# Chunk A7 · the watch workflow (W-3.2.1)

Every figure below is from a real run on 21 Nov 2026. The red run was taken with the tests written, no
`build-watch.yml`, FaceStudio's tests still in `test-tools.yml`, and the band table stubbed: `AppVersionBand` had its
new `Form` member (every entry `Phone`), `SuiteVersionBands.WatchBand` was 0, and there were no watch entries.

## What it is

`.github/workflows/build-watch.yml`, on `main`, `claude/**` and **Run workflow**, filtered to `Watch/**` (less
`Watch/**/*.md` and `Watch/design/**`), `Tools/FaceStudio`, `Tools/FaceStudio.Tests`, what `Watch.Domain` builds on
(`Storyworthtelling.Updates`, `.Time`, `.Folds`), the dotnet action and itself. Concurrency as every app workflow (a stale
branch run is cancelled, `main` never).

- **`test` (branch pushes only):** `dotnet test Watch/Watch.Tests/Watch.Tests.csproj` and
  `dotnet test Tools/FaceStudio.Tests/FaceStudio.Tests.csproj`, no workload, no SDK, no secrets.
- **`build` (`main` or by hand):** the dotnet action with the `android` workload, JDK 21, `sdkmanager "platforms;android-36"
  "build-tools;36.0.0"`, both test projects (FaceStudio's with `FACESTUDIO_APK_REQUIRED=1`), the keystore decoded from
  `KEYSTORE_BASE64`, versionCode at **band 11**, then:
  - **Build the signed watch apps:** one `dotnet publish` per line of the job's `WATCH_HEADS` table (`<project> <package>`;
    today only `Watch/Point.Installer/Point.Installer.csproj com.storyworthtelling.focusphone.launcher`), signed with the
    suite keystore like every phone head, `ApplicationVersion` the band-11 code and `ApplicationDisplayVersion`
    `1.0.<run_number>`; a project that leaves other than one `*-Signed.apk` fails the step. Each APK is written to
    `$RUNNER_TEMP/watch-apks.txt` (APK, package, kind), the list every check reads.
  - **Build the signed face:** FaceStudio writes the face (and `git diff --exit-code -- Watch/Point.Face` fails if that
    differs from what is committed) and packs it unsigned with the run's versionCode; `zipalign -f -p 4`, then `apksigner
    sign` (passwords as `env:` arguments, v4 off so no `.idsig` sits beside it) into
    `Watch/Point.Face/bin/Release/com.storyworthtelling.watch.point-Signed.apk`, and `apksigner verify`.
  - Fatal, in this order and all before anything leaves: **DEPENDENCY CHECK** (`check-dependency-graph.py .`), **APP ID
    CHECK** (badging's first line must be `package: name='<package>' versionCode='<this run's code>'`), **FORM CHECK**
    (`uses-feature` or `uses-feature-not-required` naming `android.hardware.type.watch`), **ICON CHECK**, **FONT CHECK**.
  - The artifact **Watch-Android** (installer and face), and on `main` the publish action with **`form: watch`**.

## Decisions made in this chunk

- **`SuiteVersionBands` is keyed by package and form.** `AppVersionBand` gained `ApkForm Form`, with no default (as
  everywhere the form is read since A4), and every existing entry says `Phone`. The installer is then two entries for one
  package, Flyleaf's at band 1 as a phone APK and the installer's at band 11 as a watch APK, and the face is one watch
  entry. Rejected: a second table for the watch (two places to look, and nothing to say a package in both is one app in two
  forms), and keying by package with a list of bands (no way to say which band is which device's). Every existing guard
  keeps its meaning: "a package appears twice" became "a package appears twice in one form"; "every band but band 1 holds
  one app" became "every band but band 1 and the watch band" (band 11 is the watch's band 1: one band for every watch APK,
  per PC4); `NoTwoAppsShareABandUnlessTheyShareAWorkflow` lists band 11 with its two APKs, which do share a workflow; the
  FocusPhone band guards (`EveryFocusPhonePackageInTheBandTableMatchesTheDomainConstant`, `EveryPublishedFocusPhoneHeadHasABand`)
  read the phone entries only; `ConstitutionIsBandTen` selects the phone entry, so Beat's watch entry will not break it.
  New: `WatchBand` = 11 is one constant the workflow test reads; no phone entry may take it; every watch entry has it;
  every APK `build-watch.yml` builds has a watch entry.
- **A new watch app is two lines, not one.** The table drives the build and every check; the upload and publish lists
  spell each head's publish folder, as build-flyleaf.yml spells its heads (a folder glob there stopped matching on a
  rename), and because the FocusPhone publish guard reads a publish folder by name. `MainBuildsEveryWatchAppAndTheFace`
  fails if any `Watch/` project with an `ApplicationId` is not in the table with the package it declares, or if its publish
  folder is not in both lists. Dash, Wave and Beat are one table line and one line in each list.
- **The ICON CHECK is scoped to what each APK shows, not faked.** The installer has no activity and no launcher entry (WD
  §11), so no launcher ever draws an icon for it; giving it one to pass a check would be decoration. The check: an APK with a
  `launchable-activity` must declare an `application-icon` (so Dash, Wave and Beat must carry one, and the check bites when
  they land); one without passes and says so; the face, which the system lists by its preview, must carry
  `res/xml/watch_face_info.xml` and its `preview.png`. Shown to fail on a probe APK with a launcher entry and no icon, and
  on the installer listed as a face.
- **The FONT CHECK covers the face only.** It compares the face's `res/font/plexmono.ttf` with
  `Watch/Fonts/IBMPlexMono-Medium.ttf` by SHA-256. The installer draws no text; the kit's own Plex Mono asset is packaged by
  the first app that draws with it (Dash), and the check gains that line then.
- **FaceStudio's tests moved from `test-tools.yml` to `build-watch.yml`,** with the watch paths of test-tools' filter, so a
  watch change runs them once (`FaceStudiosTestsRunOnceOnABranchPush`). `EveryToolsTestProjectIsRunByAWorkflow` still holds.
- **The publish-guard exemption is gone.** `FrozenInvariantsTests.EveryOwnerHeadIsPublishedToTheRollingRelease` no longer
  skips `Watch/` heads, and `WatchWorkflowExists` is deleted: `Watch/Point.Installer` is now held like every phone head.
- **The secrets reach the signing steps through `env:`** rather than being pasted into the script's text by the
  expression (the phone workflows' way); `dotnet publish` still takes them as `-p:` properties, while the face's apksigner
  reads its two passwords from the environment (`--ks-pass env:…`), so they are never in its arguments.
- **No `--print-certs` in the workflow:** the face is verified, not described; the run log stays free of the certificate.

## Tests written first

- `Storyworthtelling.Updates.Tests/CiHygieneTests.Watch.cs` (a second part of `CiHygieneTests`, now `partial`, 8 cases):
  `BranchPushesRunTheWatchTestsAndNothingElse`, `FaceStudiosTestsRunOnceOnABranchPush`, `MainBuildsEveryWatchAppAndTheFace`,
  `EveryWatchApkIsSignedWithTheSuitesKeystore`, `TheWatchIsBandEleven`, `TheWatchChecksAreFatalAndComeBeforeAnyApkLeaves`,
  `TheWatchPublishesAsWatchFromMainOnly`, `TheWatchFilterCoversWhatTheWatchTestsRead`.
- `SuiteVersionBandsTests`: `BandsAreUnique` (per form; band 11 shared by watch APKs), `EveryEntrySaysItsForm`,
  `EveryWatchApkIsInTheWatchBand`, `NoPhoneAppIsInTheWatchBand`, `TheInstallerKeepsFlyleafsPhoneBand`, `ConstitutionIsBandTen`
  (the phone entry); `VersionCodeTests.NoTwoAppsShareABandUnlessTheyShareAWorkflow` (band 11's two).
- `Watch.Tests`: `WatchInvariantsTests.TheWatchWorkflowChecksEveryFrozenPackage` (the table's installer line and
  `FACE_PACKAGE` are `WatchIdentity`'s frozen ids).
- `FocusPhone/Codex.Tests`: the exemption removed from `EveryOwnerHeadIsPublishedToTheRollingRelease`; the two band guards
  read the phone form.

## Red

- **`Storyworthtelling.Updates.Tests`: 174 tests, 12 failed, 162 passed.** Failed: the eight `CiHygieneTests.Watch` cases
  (seven with "build-watch.yml doesn't exist, so no watch APK is built, signed or published.";
  `FaceStudiosTestsRunOnceOnABranchPush` with `Assert.Equal() Failure: Collections differ … (pos 0)`, test-tools.yml being
  the only runner), `TheInstallerKeepsFlyleafsPhoneBand` (`Collections differ … (pos 1)`), `NoPhoneAppIsInTheWatchBand`
  (`Assert.InRange() Failure: Value not in range Range: (1 - 20)`, the stub's band 0), `EveryWatchApkIsInTheWatchBand`
  (`Assert.Contains() Failure: Filter not matched in collection Collection: []`) and
  `NoTwoAppsShareABandUnlessTheyShareAWorkflow` (`Collections differ … (pos 1)`). Passing on the stub, for what does not
  change: `BandsAreUnique`, `EveryEntrySaysItsForm`, `ConstitutionIsBandTen`.
- **`Watch.Tests`: 389 tests, 1 failed:** `TheWatchWorkflowChecksEveryFrozenPackage`
  (".github/workflows/build-watch.yml isn’t there.").
- **`FocusPhone/Codex.Tests`: 3082 tests, 1 failed:** `EveryOwnerHeadIsPublishedToTheRollingRelease` ("These heads declare
  an owner-namespace ApplicationId but no workflow publishes their signed APK … : Watch/Point.Installer"): the exemption
  removed and no workflow yet.

## Green

- **First green attempt, `Storyworthtelling.Updates.Tests`: 2 failed.** `EveryPathFilterCoversEveryProjectItsWorkflowBuilds`
  ("build-watch.yml: Storyworthtelling.Time", "… Storyworthtelling.Folds": `Watch.Domain` builds on both, which
  test-tools.yml had covered by accident, through IndexHistorySeal's filter) and `BranchPushesRunTheWatchTestsAndNothingElse`
  (a comment in the test job said "workload:"). The two filters were added and the comment reworded.
- **`Storyworthtelling.Updates.Tests`: 174 passed. `Watch.Tests`: 389 passed. `FocusPhone/Codex.Tests`: 3082 passed.
  `RepoGuards.Tests`: 386 passed. `FaceStudio.Tests`: 19 passed** (and 19 with `FACESTUDIO_APK_REQUIRED=1`, the APK
  built). Both YAML files parse (`yaml.safe_load`).
- **Shown to bite:** with the installer's publish lines deleted from `build-watch.yml`, `EveryOwnerHeadIsPublishedToTheRollingRelease`
  failed on `Watch/Point.Installer` and `MainBuildsEveryWatchAppAndTheFace` failed; restored.

## Dry run (here, not in CI)

The build job's own `run:` blocks were taken out of the YAML and run in order with bash, from the repository root, against
an SDK put together in the session's scratch folder (the minimal SDK's android-36 and platform-tools, and build-tools 36.0.0
from dl.google.com for `apksigner`, which the minimal SDK lacks), with the version-code action's own script for the code,
and a **throwaway keystore generated for the run** (invented alias and passwords, deleted with the scratch folder; nothing
from the suite's secrets):
- *Decode keystore*, *Build the signed watch apps* (a real `dotnet publish` of the installer, signed by .NET's apksigner
  path: `com.storyworthtelling.focusphone.launcher-Signed.apk`, versionCode 1103623845), *Build the signed face*
  (FaceStudio, `git diff` clean, zipalign, apksigner sign and verify; `zipalign -c 4` passes on the signed face), then
  **DEPENDENCY CHECK** ("Checked 34 dependency graphs."), **APP ID**, **FORM**, **ICON** and **FONT**: all exit 0, the
  installer reading "no launcher entry, so no launcher icon to carry" and "draws no text of its own", the face "the picker's
  preview is packaged" and "Plex Mono Medium, byte for byte". Both APKs carry one signer, the same certificate.
- Each check refused a bad entry, exit 1: FORM and ICON on a probe phone APK (aapt2-linked, a launcher activity and no
  icon); APP ID on the face listed under the installer's package (and, by accident, on a versionCode from a second
  computation a minute later); FONT and ICON on the installer listed as a face.
- `stage-apks.py --form watch` staged `com.storyworthtelling.focusphone.launcher-Signed.watch.apk` and
  `com.storyworthtelling.watch.point-Signed.watch.apk` with `"form": "watch"` sidecars at the run's versionCode; `--form phone`
  refused (exit 1, nothing staged).
- **The publish action's own `run:` block**, with the workflow's `apks:` and `form: watch` and a stand-in `gh`: staged both
  and called `gh release upload apps-latest <both .watch.apk and their .json> --clobber`. No real upload was made.

## Not done, not verified

- **Only CI can show:** that the secrets decode and sign (the dry run used a throwaway keystore), that the runner's
  `sdkmanager` installs android-36 and build-tools 36.0.0 as written, that `ANDROID_HOME`'s apksigner signs the face, that
  dl.google.com serves the Play services aars to the runner, and the upload to `apps-latest`. The first `build-watch.yml` run
  on `main` is the verification.
- **Signature match with Flyleaf is by construction, not checked:** both are signed from the one keystore secret, so the
  Data Layer should deliver; no step compares the installer's certificate with Flyleaf's.
- **The installer has no icon resource** and the check says why; if the owner wants one in the watch's app list, it is an
  IconStudio spec, not done.
- **The kit's Plex Mono asset** is not packaged by anything yet; the FONT CHECK covers the face only.
- Not verified on a device: everything the workflow publishes.

## Chunk A7 totals

- `Storyworthtelling.Updates.Tests`: **174 passed, 0 failed** (162 before). `Watch.Tests`: **389 passed, 0 failed**.
  `FocusPhone/Codex.Tests`: **3082 passed, 0 failed**. `RepoGuards.Tests`: **386 passed, 0 failed**. `FaceStudio.Tests`:
  **19 passed, 0 failed**.

---

# Release A fix round

An independent review of release A (A1–A7, draft PR #385) found seven faults and some nits, and CI found one more (the
log-loop gate). Every figure below is from a real run on 21 Nov 2026. As before, the red runs were taken with the tests
written and the new code present only as stubs: methods threw `NotImplementedException`, and new constants were empty
strings or zero.

## The findings, and what each fix is

1. **CRITICAL — the phones already deployed would have installed the watch's APKs.** Every Flyleaf and Score on `main`
   reads `releases/latest` (which is `apps-latest`), lists its assets by `.apk` and installs by package, whatever the
   form. A7's `build-watch.yml` published to `apps-latest`, so `…launcher-Signed.watch.apk` (band 11) would have replaced
   Flyleaf on the locked phone for good, and the face (`com.storyworthtelling.watch.point`, in the suite's namespace and
   not installed) would have been installed on both phones. A4's form rule only protects phones running A4's code.
   **Fix:** watch APKs never go to `apps-latest`.
   - `Storyworthtelling.Updates/ReleaseChannels`: `Phone = "apps-latest"`, `Watch = "watch-latest"`, frozen literals,
     `For(form)` (none for Unknown).
   - `publish-rolling-release` takes a `release` input (default `apps-latest`) and refuses, before it reads an APK or
     asks `gh` anything, every pair but `phone`/`apps-latest` and `watch`/`watch-latest` (so a watch publish that forgets
     to name its release stops). It now runs under `set -euo pipefail`.
   - **Creating the watch's release needed what `apps-latest` never did.** GitHub makes a newly created release the
     repository's latest unless told otherwise, and the deployed phones read `releases/latest`, so `watch-latest` is
     created `--prerelease --latest=false` (a prerelease is never latest), and an existing one is put back to that with
     `gh release edit watch-latest --prerelease --latest=false` before anything is uploaded to it. Creating it needs only
     `contents: write`, which the build job has; the tag is made at `main`'s head, as `apps-latest`'s was.
   - `build-watch.yml` publishes with `form: watch` and `release: watch-latest`.
   - `GitHubReleaseClient.GetReleaseAsync(tag)` reads `releases/tags/<tag>`; `GetLatestReleaseAsync` is gone. Flyleaf's
     phone half and Score read `ReleaseChannels.Phone` only; Flyleaf's watch half (`WatchUpdates`) reads
     `ReleaseChannels.Watch` only. Nothing in FocusPhone asks for `releases/latest` any more, so a second release made
     latest by mistake is never installed from. The form rule and the `.watch.apk` naming stay behind all of it.
2. **HIGH — the installer had no 32-bit ARM code.** Its APK carried `lib/arm64-v8a/` and `lib/x86_64/` only (.NET for
   Android's default), and a watch whose system runs 32-bit userspace refuses it. **Fix:** `Point.Installer.csproj` sets
   `<RuntimeIdentifiers>android-arm;android-arm64</RuntimeIdentifiers>`; `WatchInvariantsTests.EveryWatchAppCarries32BitArm`
   holds every `Watch/` project with an `ApplicationId` to it; `build-watch.yml` gained a fatal **ABI CHECK** (every app
   APK must carry `lib/armeabi-v7a/` and `lib/arm64-v8a/`; the face has no code). `Watch.Ui.Wear` is a library: its code is
   compiled into the app's APK, whose identifiers decide the ABIs, so it was left alone.
3. **MEDIUM-HIGH — "Install from GitHub now" ran the watch half on the main thread.** `UpdateService.SendToWatchAsync` now
   runs it in `Task.Run` (on the application context); `WatchUpdates` hashes with `UpdateApkFrame.DescribeAsync`
   (`SHA256.HashDataAsync`), reads the APK with an async `FileStream` and writes each chunk with the Java stream's
   `WriteAsync`. `ApkArchive.Read` stays synchronous, now on the pool thread.
4. **MEDIUM — the watch half could outlast WorkManager's ten minutes, and hang holding `WatchGate`.** `Watch.Domain`'s
   `RunDeadline` (pure): `From(started)` = started + 10 min − 1 min margin; `Left`, `Passed`, `Allows(now, needs)`,
   `Cut(now, wait)`. `UpdateWorker.DoWork` takes it as it starts and hands it down (the owner's tap takes one from the tap);
   the watch half starts nothing past it (a send needs `WatchSendPlan.SendNeeds`, 3 min, left; `SendBudget` is gone);
   every Data Layer await (`GetCapabilityAsync` ×2, `SendMessageAsync`, `OpenChannelAsync`, `GetOutputStreamAsync`,
   `CloseAsync`) goes through `BoundedAsync` (`DataLayerWait` 30 s, `CloseWait` 10 s, cut to what is left), the report
   waits are cut too (`AnswerWait`, `ReportWait`), the download and the stream stop at the deadline (a
   `CancellationTokenSource` on what is left; a stream still held in a write is abandoned and its channel closed), and the
   gate is waited for only as long as the deadline leaves (`UpdateRunGate.TryRunAsync`, pure and tested in Codex). A call
   the run stopped waiting for is observed, so its later fault is a log line, not an unobserved exception.
5. **MEDIUM-LOW — the installer recorded its own abandoned session as a decline.** Sending a build again (or a newer one)
   abandons the session still waiting, and the platform reports it aborted: that was recorded as `Declined` over the new
   build's `Waiting`, cleared the new build's card, and stopped the phone sending the build again. **Fix:**
   `Watch.Domain`'s `InstallSessions` (pure, with its codec): each app's current session (package, build, id, when) and
   the ids the installer abandoned (newest 32 kept), stored under the installer's frozen `sessions` key (`SessionStore`).
   `WatchInstaller` records a session as abandoned before abandoning it and as begun before committing it; the status
   intent carries the session id (`WatchIdentity.StatusSessionExtra`, with the platform's `EXTRA_SESSION_ID` as the
   fallback). `InstallSessions.Judge` says: **Ignore** a status for an abandoned session or one that is not the app's
   current session (nothing recorded, no card cleared); **LetGo** an abort on the current session three days (less an
   hour) after it began, which is the platform dropping a session nobody answered (`PackageInstallerService`'s
   `MAX_AGE_MILLIS`, read from AOSP, not seen on a watch): the card goes, nothing is recorded, and the build stays
   `Waiting`, so `WatchSendPlan`'s own three-day rule offers it again; **Settle** everything else as before (and a status
   with no session id, from an installer before this fix, as it always was). The extras' names are frozen in
   `WatchIdentity` and the action goes as its frozen word (`WatchUpdateActions`: `refuse`, `first-install`,
   `asked-update`, `silent-update`), not `Action.ToString()`.
6. **LOW-MEDIUM — a Flyleaf-only push ran none of the watch guards that read Flyleaf's source.** `build-watch.yml`'s
   filter now takes `FocusPhone/Flyleaf.Android/**` and `.github/workflows/build-flyleaf.yml` (both read by
   `WatchInvariantsTests`), and `TheWatchWorkflowRunsWhenAnythingTheWatchTestsReadChanges` collects every path outside
   `Watch/` that a watch test names from the repository's root and fails if the filter misses one. The cost, accepted:
   a Flyleaf change on `main` rebuilds and republishes the watch APKs.
7. **LOW — `[MINUTE] / 59`** divided two whole numbers. FaceStudio writes `[MINUTE] / 59.0`; the face was regenerated with
   the tool (485 lines of `watchface.xml`, the divisor only) and `TheFaceIsValidWff` validated it against the v4 XSD.
8. **CI — the log-loop gate never woke for the watch workflow.** `LogLoopReader.Tests.LogLoopWorkflowTests.TheGateWaitsForEveryAppWorkflowThatHasATestJob`
   failed on PR #385: "build-watch.yml (Build watch APKs) finishing does not wake the gate". **Fix:** `Build watch APKs`
   joins `log-loop-automerge.yml`'s `workflow_run` list.

**Nits:** CLAUDE.md now says Flyleaf, like Plate, compiles only while dl.google.com is reachable (the Wearable binding's
aars), and describes the two releases, the deadline and the installer's sessions. Score passes over an asset named as a
watch APK before downloading it. `FaceStudio.Apk.FindAapt2` orders build-tools and the workload's packs by number
(`Apk.NewestFirst`: "9.0.0" no longer beats "36.0.0"; a pre-release after its release; a non-version folder last).

**Found by the new tests, also fixed:** the publish action had no `set -e`, so a staging refusal (a watch APK in a phone
publish) went on to `gh release upload apps-latest --clobber` with no files, and the step's outcome was whatever `gh`
said. `PublishActionTests.AWatchApkInAPhonePublishStillStops` caught it against a stand-in `gh` that answers 0.

## Tests written first

- `Storyworthtelling.Updates.Tests/ReleaseChannelTests.cs` (3): `TheChannelsAreFrozen` (values, literals not `nameof`),
  `EachFormHasItsOwnRelease`, `AnUnknownFormHasNoRelease`.
- `Storyworthtelling.Updates.Tests/PublishActionTests.cs` (14): the action's own `run:` block, its expressions filled in as
  GitHub fills them, run with bash against a stand-in `gh` (logs each call; `release view <tag>` answers from a fixture)
  and a stand-in aapt2: `PhoneApksGoToTheirOwnReleaseOnly`, `WatchApksGoToTheirOwnReleaseOnly`,
  `AWatchPublishToThePhonesReleaseStopsBeforeAnything` (named, and by the default), `APhonePublishToTheWatchsReleaseStopsBeforeAnything`,
  `AnyOtherPairStops` ×4, `TheStepReadsItsInputsFromItsEnvironment`, `TheReleaseDefaultsToThePhones`,
  `TheWatchsReleaseIsCreatedAsAPrereleaseThatIsNeverLatest`, `AnExistingWatchReleaseIsKeptOffLatestBeforeAnythingGoesUp`,
  `ThePhonesReleaseIsCreatedAsTheLatest`, `AWatchApkInAPhonePublishStillStops`.
- `CiHygieneTests`: `NoWorkflowPublishesAWatchApkToThePhonesRelease`, `TheReleaseRuleBites`; `EveryWatchApkSaysWatch` and
  `PublishFormGaps` gained the release rule (watch to `watch-latest`, phone to `apps-latest`, unnamed is the default);
  `TheWatchRuleBites` names the release; `TheWatchPublishesAsWatchFromMainOnly` asks for `release: watch-latest`;
  `TheWatchChecksAreFatalAndComeBeforeAnyApkLeaves` has the ABI CHECK; `TheWatchFilterCoversWhatTheWatchTestsRead` asks
  for Flyleaf's folder and workflow.
- `FocusPhone/Codex.Tests`: `GitHubReleaseClientTests` read the phone's release by its tag throughout, plus
  `EachReleaseIsReadByItsOwnTag` and `ARepositorysLatestReleaseIsNeverAsked`; `Guard/ReleaseChannelGuardTests` (4):
  `ThePhoneReadsOnlyThePhonesRelease`, `OnlyTheWatchHalfNamesTheWatchsRelease`, `NothingAsksForTheRepositorysLatestRelease`,
  `ScoreSkipsAWatchAssetBeforeDownloadingIt`; `UpdateRunGateTests` (5 new): `AFreeGateRunsAtOnce`,
  `AWaitThatRunsOutRunsNothing`, `AGateHeldOnlyBrieflyIsWaitedFor`, `AThrowingTryFreesTheGateAndTheExceptionReachesTheCaller`,
  `ANegativeWaitIsNoWait`.
- `Watch.Tests`: `Updates/RunDeadlineTests.cs` (7), `Updates/InstallSessionTests.cs` (37 cases: the judge, the record and
  its codec, the action words, and `ALetGoBuildIsOfferedAgain` through `WatchSendPlan`), `UpdateApkTests` (2:
  `DescribingAsyncIsDescribing`, `DescribingAsyncStopsWhenCancelled`), `FlyleafWatchUpdateTests` (`TheWaitsAreTheDecidedOnes`
  with the new waits, `ASendNeedsRoomForItsOwnWaits`, `TheRunsTimeIsSaidInWordsOnly`), and `Guard/WatchInvariantsTests`
  (`StatusExtrasAreFrozen`, `StoredNamesAreFrozen` + `sessions`, `EachStatusIsJudgedByItsSession`,
  `EveryWatchAppCarries32BitArm`, `TheWatchWorkflowRunsWhenAnythingTheWatchTestsReadChanges`, `TheWatchHalfRunsOffTheMainThread`,
  `EveryDataLayerWaitIsBounded`, `TheWorkerHandsItsDeadlineDown`).
- `FaceStudio.Tests`: `TheMinuteIsReadAsAFraction`, `TheNewestToolsComeFirstByNumber`, `AFolderThatIsNoVersionComesLast`.

## Red

- **`Storyworthtelling.Updates.Tests`: 192 tests, 23 failed, 169 passed** (with `ReleaseChannels` stubbed): the 3
  `ReleaseChannelTests` (`NotImplementedException`, `Strings differ`), 13 `PublishActionTests` (the stub's empty channel
  made the fixture's path a folder, `UnauthorizedAccessException`, and `The publish action has no release input with a
  default.`) and 7 `CiHygieneTests` (`build-watch.yml: a watch publish to `, `Pattern not found … release:`,
  `ABI CHECK must run after the build …`, `Item not found … FocusPhone/Flyleaf.Android`).
- **Second red, the channels real and the action as A7 left it: 193 tests, 18 failed, 175 passed** (the env-mapping check
  moved into its own test first, so each run test reached the action's behaviour). This is finding 1 shown:
  `WatchApksGoToTheirOwnReleaseOnly`: `String: "release upload apps-latest /tmp/…" Expected start: "release upload watch-latest "`;
  `TheWatchsReleaseIsCreatedAsAPrereleaseThatIsNeverLatest`: `Collection: ["release view apps-latest", "release upload apps-latest …"]`;
  `AnExistingWatchReleaseIsKeptOffLatestBeforeAnythingGoesUp`: `release create apps-latest … --latest …`; the four
  `AnyOtherPairStops` and the two cross-release tests published (`Expected: Not 0 Actual: 0`); and
  `AWatchApkInAPhonePublishStillStops` exited 0 (no `set -e`). Failed: 13 of 14 `PublishActionTests` (the one that
  passed, `ThePhonesReleaseIsCreatedAsTheLatest`, holds what did not change: A7's action created `apps-latest` with
  `--latest`) and 5 `CiHygieneTests`; `TheReleaseRuleBites` and `TheWatchRuleBites` passed once the channels were real,
  since they test the guard's own helpers.
- **`FocusPhone/Codex.Tests`: 3093 tests, 19 failed, 3074 passed:** the 10 `GitHubReleaseClientTests` reading by tag
  (`NotImplementedException`), the 5 new `UpdateRunGateTests` (`NotImplementedException`; the two throwing tests
  `Exception type was not an exact match`), and the 4 `ReleaseChannelGuardTests` (`FocusPhone/Flyleaf.Android/UpdateService.cs
  doesn't read the phone's release by its tag.`, `Reads releases/latest: FocusPhone/Codex.Infrastructure/Updates/GitHubReleaseClient.cs`,
  `Score doesn't pass over a watch asset by its name.`, `Collections differ`).
- **`Watch.Tests`: 446 tests, 59 failed, 387 passed:** every new test (57) and the two changed ones
  (`TheWaitsAreTheDecidedOnes`, `StoredNamesAreFrozen`). 45 `NotImplementedException`; the guards: `Watch/Point.Installer/Point.Installer.csproj
  doesn't build for android-arm and android-arm64.`, `WatchUpdates.cs:135: var paired = await capabilities.GetCapabilityAsync(…)`
  (unbounded), `build-watch.yml doesn't run when these change, though the watch tests read them:` (Flyleaf's folder and
  workflow), `Sub-string not found … Task.Run(` and `… WatchIdentity.StatusSessionExtra`. A first attempt did not compile
  (`InstallSessionTests` lacked `using Watch.Tests.Guard;` for `WatchFiles`) and is not counted.
- **Third red (`UpdateLines.Ignored`/`LetGo`, which the receiver needed):** `TheLinesSayWhatHappenedInWordsAndNumbersOnly`
  1 of 1 failed, `NotImplementedException`.
- **`FaceStudio.Tests` (`FACESTUDIO_APK_REQUIRED=1`): 22 tests, 3 failed, 19 passed:** `TheMinuteIsReadAsAFraction`
  (`485 out of 485 items in the collection did not pass`) and the two `NewestFirst` tests (`NotImplementedException`).
- **`LogLoopReader.Tests`: 115 tests, 1 failed, 114 passed:** `TheGateWaitsForEveryAppWorkflowThatHasATestJob`,
  "build-watch.yml (Build watch APKs) finishing does not wake the gate, so a PR it tests would wait for another event."
  (`LogLoopWorkflowTests.cs:61`), the same failure as PR #385's CI.

## Green

- **On the way:** `Watch.Tests` first read 446 with 6 failed: the five Flyleaf and installer source guards, waiting for the
  Android half, and `ASendNeedsRoomForItsOwnWaits`, which caught the first `SendNeeds` (2 min) as shorter than a send's own
  waits (2 × 30 s + 1 min + 10 s): it is 3 min. `EachStatusIsJudgedByItsSession` was pointed at `SessionStore.Abandoning`
  and `SessionStore.Began` (the store wraps `InstallSessions`, and the guard now also reads the store), and
  `TheWorkerHandsItsDeadlineDown` at an expression-bodied `DoWork`, which `RepoGuards.Tests/EveryWorkerIsTrailedTests`
  requires. `RepoGuards.Tests` first read 386 with 1 failed, `DependencyGraphCheckTests.EveryWorkflowThatPublishesAnApkChecksTheGraphBeforeItLeaves`
  ("build-watch.yml runs … check-dependency-graph.py before its build or after the APK has left"): the filter had gained
  `.github/actions/publish-rolling-release/**`, which the guard reads as the APK leaving. No other workflow filters on
  the action (its behaviour is `PublishActionTests`', which a `.github` change runs), so the line went.
- **`Watch.Tests`: 447 passed, 0 failed. `Storyworthtelling.Updates.Tests`: 193 passed, 0 failed. `RepoGuards.Tests`: 386
  passed, 0 failed. `FocusPhone/Codex.Tests`: 3093 passed, 0 failed. `LogLoopReader.Tests`: 115 passed, 0 failed
  (`TheGateWaitsForEveryAppWorkflowThatHasATestJob` green). `FaceStudio.Tests` with `FACESTUDIO_APK_REQUIRED=1`: 22
  passed, 0 failed** (`TheCommittedFaceIsCurrent` on the regenerated face, `TheFaceIsValidWff` against the v4 XSD, the APK
  built).
- **Compiled here** with `-p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false` (dl.google.com was
  reachable): `Watch/Point.Installer` 0 warnings, 0 errors; `Watch/Watch.Ui.Wear` 0 warnings, 0 errors;
  `FocusPhone/Flyleaf.Android` 0 errors (34 warnings, none in a changed file); `FocusPhone/Score.Android` 0 errors (219
  warnings, none in a changed file). A Release `dotnet publish` of the installer carries **`lib/armeabi-v7a/` (47
  entries) and `lib/arm64-v8a/` (47)**, and no longer `x86_64`. `check-dependency-graph.py .`: "Checked 37 dependency
  graphs.", nothing missing. The three changed YAML files parse (`yaml.safe_load`): the publish step's `with` is
  `{form: watch, release: watch-latest, apks: …}` and the action's `release` input defaults to `apps-latest`.
- **The ABI CHECK, run here** (its `run:` block taken from the YAML): the installer "ok: lib/armeabi-v7a/ and
  lib/arm64-v8a/", the face (FaceStudio's `--apk`) "ok: no code"; a probe, the installer with `lib/armeabi-v7a/*` deleted,
  exit 1 ("carries no lib/armeabi-v7a/").
- **The publish action's own shell, run here** (its `run:` block from the YAML, real aapt2 36.0.0, a stand-in `gh`): the
  installer and the face with `form: watch`, `release: watch-latest` and no such release → `gh release create watch-latest …
  --prerelease --latest=false …launcher-Signed.watch.apk …point-Signed.watch.apk` (and their sidecars); with the release
  there → `gh release edit watch-latest --prerelease --latest=false`, then `gh release upload watch-latest … --clobber`.
  Flyleaf's phone APK with `form: phone` → `gh release upload apps-latest …launcher-Signed.apk …json --clobber`. `watch` to
  `apps-latest` and `phone` to `watch-latest`: exit 1, `gh` never called. The installer in a phone publish and Flyleaf in a
  watch publish: refused by `stage-apks.py`, exit 1, nothing uploaded. No watch asset went near `apps-latest`, and no
  phone asset near `watch-latest`.

## Decisions made in this round

- **A second release, not a second filter.** The phones already deployed cannot be taught anything before they read the
  next release, so the only fix that holds for them is that the release they read never carries a watch APK.
- **Every release is read by its tag**, so a release made latest by mistake (by hand, or a create that forgot
  `--latest=false`) is never installed from by the new code either; the old phones' safety rests on `watch-latest` being a
  prerelease, which the action now re-asserts before every upload.
- **A platform-expired session stays `Waiting`**, rather than gaining a new outcome word: `update/installed`'s words are
  frozen, and `WatchSendPlan` already re-offers a waiting build after three days. The three days (less an hour) are AOSP's,
  from memory of `PackageInstallerService`; if the watch keeps sessions longer, the platform's abort comes later and is
  still let go.
- **The deadline covers the owner's tap too**, from the tap, so "Install from GitHub now" cannot hold the watch's gate past
  the next six-hourly run's want of it.
- **`Watch.Ui.Wear` sets no `RuntimeIdentifiers`:** a library's code is compiled into the app's APK, whose identifiers
  decide its ABIs; the guard holds the apps.
- **The watch's filter takes Flyleaf's folder**, as briefed; on `main` that means a Flyleaf change rebuilds and republishes
  the watch APKs (a new versionCode, so the installer updates itself and the face). Accepted for now; a narrower trigger
  would need the build job to ask which paths a push touched.

## Not done, not verified

- **Only CI can show** that `gh release create … --prerelease --latest=false` and `gh release edit … --latest=false` are
  accepted by the runner's `gh` and by GitHub as written, and that `releases/latest` stays `apps-latest` once
  `watch-latest` exists. Worth checking by hand after the first `main` run: `gh api repos/{repo}/releases/latest --jq
  .tag_name` must print `apps-latest`.
- **On a device:** `releases/tags/<tag>` with the update token (the same permission as `releases/latest`); the Data Layer
  waits and the deadline under a real slow transfer; whether the platform sends a status for a session it drops at its
  age limit (if it sends none, the record stays `Waiting` anyway); the 32-bit userspace on the Pixel Watch.
- **The next CI failure on PR #385, found here and not fixed (it needs a decision):** with the gate test green,
  `log-loop-guard.yml` reaches its later step, `leakscan --ci` over `git diff origin/main...HEAD`, which the failing
  reader tests had stopped it reaching. Run here over the branch before this round (`origin/main...39fa23ec`) it reports
  **78 findings**: 77 `card-number` in `Watch/Point.Face/res/raw/watchface.xml` and 1 `coordinates` in
  `Watch/Watch.Tests/Design/SrgbTests.cs:14`, all from chunks A1–A3. The face's are false positives: a colour list is
  space-separated `RRGGBB`, and a run of all-digit colours (`003230 003233 003236`) matches the card rule
  (`\b(?:\d[ -]?){12,18}\d\b`) and can pass Luhn. This round's commit alone has 77 (the regenerated face's lines are
  "added" again). The options are a narrower card rule, a named exemption for the generated face, or a different colour
  format in the face (unverified on a watch); each changes a privacy guard or the face, so it is left to the owner.
- **The installer's own Data Layer awaits** (`Reporter`'s `GetConnectedNodesAsync`/`SendMessageAsync`) are not bounded:
  they run inside a broadcast's `GoAsync`, which holds no gate, and the finding was the phone's gate.

## Release A fix round totals

- `Watch.Tests`: **447 passed, 0 failed** (389 before). `Storyworthtelling.Updates.Tests`: **193 passed, 0 failed** (174).
  `FocusPhone/Codex.Tests`: **3093 passed, 0 failed** (3082). `RepoGuards.Tests`: **386 passed, 0 failed**.
  `LogLoopReader.Tests`: **115 passed, 0 failed** (114 of 115 on PR #385). `FaceStudio.Tests`: **22 passed, 0 failed** (19).

### Release A fix round, follow-up: the CI leak scan

With the gate test green, `log-loop-guard.yml` reached `leakscan --ci`, which found 78 hits over the branch: 77 "card-number" in the generated `Watch/Point.Face/res/raw/watchface.xml` (runs of six-digit minute colours such as `202943 232843 262743` pass Luhn) and 1 "coordinates" in `Watch/Watch.Tests/Design/SrgbTests.cs` (an OKLCH triple shaped like a latitude and longitude). Neither is personal data.

- **Red:** `LogLoopReader.Tests` 117 tests, 1 failed: `CiDoesNotReadAColourListAsACard` (`Assert.Empty() Failure: Collection was not empty`). `CiStillFlagsACardWithOneLongGroup` (a 4-6-5 card) passed before and after.
- **Fix:** `CiLeakScan` no longer reads a run made only of six-digit groups as a card; no card is written in groups of six, and a card with one six-digit group is still flagged. The test's OKLCH triple is written with named arguments, so it no longer reads as a coordinate pair. No path is exempted.
- **Green:** `LogLoopReader.Tests` 117 of 117, `Watch.Tests` 447 of 447, `RepoGuards.Tests` 386 of 386; `leakscan --ci` over the branch's diff from main: "no personal-data shapes in the added lines".

### After the merge: the first main run (21 Nov 2026)

The merge of PR #385 built Flyleaf, Score, Post, Pulp and Constitution and published them to `apps-latest` (which GitHub still reports as the latest release; no watch asset reached it). Five workflows failed, for two causes; nothing from them was published.

1. **Plate, Index, Folio and Foreword failed to restore.** A6 added a central `Xamarin.AndroidX.Lifecycle.LiveData` 2.9.1 so Flyleaf could override it. With transitive pinning that version was forced on every project reaching LiveData: Plate failed NU1109 (CameraView needs 2.10) and the MAUI heads NU1107 (MAUI needs LiveData.Core.Ktx 2.9.2.1, which LiveData 2.9.1 refuses). The branch's CI never built these heads, so it showed nothing.
   - **Reproduced here:** with the central entry, `dotnet restore` of Index failed NU1107 and Plate NU1109, as on main.
   - **Fix:** the central entry is gone; Flyleaf keeps its `VersionOverride` 2.11.0.1 and still resolves LiveData, Core and Core.Ktx at 2.11.0.1. `EveryPackageVersionIsCentral` names LiveData as the one package a head owns without a central version (`OneHeadsOwn`), and the new `LiveDataIsNotPinnedForEveryone` guard failed with the entry present (1 of 1) and passes without it.
   - **Green:** Plate, Index, Folio and Foreword restore here; RepoGuards 387 of 387.
2. **The watch build failed `TheCommittedFaceIsCurrent`:** `preview.png` differed on the runner. The 20 shape masks, which use no colour maths, matched byte for byte; the preview's colours go through the platform's pow and cbrt, whose last bit differs between machines, so a pixel rounds one step differently. The build's own `git diff` check after packing would have failed for the same reason.
   - **Fix:** `FaceFiles.Same` compares a picture by its pixels, every channel within one step (`PictureTolerance`), and every other file byte for byte. `FaceFiles.Write` keeps a committed file that is the same, so the runner packs and leaves the committed preview untouched. `PngPixels` reads and writes FaceStudio's one PNG shape (8-bit RGBA, filter 0), and `Raster.Png` goes through it; the regenerated face is byte-identical to the committed one here.
   - **Red:** `SamePictureTests` 6 tests, 6 failed (`NotImplementedException` from the stubs). **Green:** FaceStudio.Tests 28 of 28 (APK required).
   - **Not verified:** that the runner's preview is within one step; if it is not, the test's message names the file and the next run says so.

### The first face on the watch (21 Nov 2026): black, and why

Point installed and was selectable on the owner's Pixel Watch 2 (Wear OS 7), and drew nothing, date included; the picker's preview (a packed PNG) showed. The watch's log, taken with `adb logcat` as the face was chosen, held one error and nothing else from the renderer: `DWF:GradientUtil: Wrong parameter for colors E36556 E26849 … colors should not be null or empty`, once for every `extractColorFromColors` list. The lists were written as bare `RRGGBB`, as Google's reference example writes them; the renderer reads the list as it reads a gradient's colours, `#AARRGGBB`, so every colour came back empty.

- **Red:** FaceStudio.Tests 29 tests, 4 failed: the new `EveryMinuteColourIsWrittenAsTheRendererReadsIt` and the three tests that read the lists (`ColourFollowsTheMinute`, `AlwaysOnIsOutlineWithoutDate`, `TheDateIsWeekdayAndDay`), against the expected `#FFRRGGBB`.
- **Fix:** `FaceColours.ByMinute` writes each minute's colour through `Hex`, `#AARRGGBB`; the face is regenerated by the tool.
- **Green:** FaceStudio.Tests 29 of 29 (APK required, the v4 schema check included), Watch.Tests 447, RepoGuards 387, leak scan clean.
- **Not verified:** that the face draws on the watch; the next install says.

---

# Release B · Dash

# Chunk B1 · the Dash contracts and Post's relay, the pure half (W-4.1.1)

Every figure below is from a real run on 22 Nov 2026. As before, the red runs were taken with the tests written and the
code present only as stubs: methods threw `NotImplementedException`, and new constants were empty strings or zero. No
Android code was written in this chunk; `MailboxArea` was changed only to call the pure pieces taken out of it.

## What it is

**The contracts, in `Watch/Watch.Domain/Dash`** (BCL only, beside the update codecs, and read by both ends):
- `WatchIdentity` gained the nine Data Layer paths as literals: `/dash/people`, `/dash/thread`, `/dash/send`,
  `/dash/file`, `/dash/delete`, `/dash/undo`, `/dash/read`, `/dash/call-note`, `/dash/mail` (`DashPaths`).
- `DashMessages`: one message family per contract, each knowing its path. `dash/people`: an ask, and the answer's
  `DashPerson` (key, name, one line, `at` as an instant, unread, group; no photo). `dash/thread`: an ask by key, and the
  answer (title, group, members' names, the last messages: id, `at`, incoming, type `text`/`call`/`voicemail`/`note`,
  words, the sender's name for an incoming group message, and attachments by id and type `photo`/`video`/`file`, never
  their bytes). `dash/send`: a reply by key or a new text to numbers (never both), and Post's answer (`sent`, `no-route`,
  `nowhere`, `failed`) on the same path. `dash/file`, `dash/delete`: a whole correspondence by key or some messages by
  id (never both), with the undo window the wrist is showing. `dash/undo`: the act's id. `dash/read`: key and the newest
  message the wrist showed. `dash/call-note`: a call by its message id, or the number of a call the watch's own phone app
  had, and the note. `dash/mail`: an ask, and lines (id, sender, subject, `at`).
- `DashCodec`: UTF-8 JSON, `{v, kind, …}`, read for the path it arrived on (an ask is whatever its path asks for). A newer
  `v` is refused before anything else is read; a missing, mistyped, over-long, out-of-place or both-or-neither field
  refuses the whole message, as does anything over `DashLimits.MaxBytes`. Characters are written as themselves
  (relaxed escaping: JSON between two of the suite's apps, never HTML), so a message keeps to its budget.
- `DashIds`: Post names a correspondence, a message and an attachment by `c`/`m`/`a` and sixteen hex digits of a SHA-256
  over the letter and its own key, so no number, address or Post source id is what the watch holds, logs or sends back,
  and the same thing keeps its id. Not secrecy: a number's digest can be searched for. The watch names its own acts and
  sends (1–64 of `a–z 0–9 -`, like a transfer).
- `DashLimits`: 24 people, 16 messages, 16 mail lines, 12 members, 20 numbers, 8 attachments; 60 characters for a name,
  120 for a line or subject, 640 for a message's words, 4,000 for a text or note from the wrist, 40 for a number; a window
  of at most 2 minutes; 64 KB a message. Chosen, not measured.
- `DashLogLines`: `CONTRACT dash/send: received v1` / `refused v2, newer than Post reads` / `refused, unreadable` /
  `sent v1` / `not sent, ApiException`, and `DASH dash/file: Filed 3`, `DASH dash/send: NoRoute`. Every method takes a
  Dash path and throws on anything else, so a payload cannot stand in for one, and a failure that is not a type name is
  written as `Exception`.

**Post's relay, in `FocusPhone/Codex.Infrastructure/Dash`** (pure, tested in `Codex.Tests`): `DashRelay` over a
`DashSnapshot` (the timeline as the Mailbox reads it, the hidden set, spam, the delete buffer, tombstones, the identity
resolver, read state, group names and the address book's name lookup; the head reads them in B2):
- `People()`: the Mailbox's own waiting correspondences (`MailboxView`), less anything tombstoned, that have a text, call
  or voicemail waiting; one row per group; newest first; the line is a text's own words (the mockup's rows, not the
  Mailbox's "Text: …"), "Photo" for a picture with none, a call or voicemail as the Mailbox describes it; unread is the
  phone's unread over what the row shows. Mail is not on People waiting (A11).
- `Thread(key)`: the last 16 messages, oldest first, without mail, trimmed from the oldest until it fits the budget.
- `Mail()`: incoming mail Post sorted as `Human`, newest first. Marketing, newsletters, transactional mail, bills,
  unjudged mail and the owner's own sent mail never reach the wrist.
- `Send(send, bridgeConnected, canSendSms)`: the phone's own road. A reply's recipients and conversation are
  `ReplyTarget.For(thread)`, a new text's are its numbers (each must be digits and the usual separators); then
  `TextSend.Choose` says whether to try the bridge and `TextSend.WithoutBridge` what follows if it is down or refuses
  (a plain text to a person, one MMS to a group). An unknown key, a correspondence with no number, or a group known only
  from notifications is `Nowhere`; neither route is `NoRoute`.
- `File` / `Delete`: the items (a correspondence is what its row showed; ids are any waiting messages, mail lines
  included), the window held (`HoldFor`: the longer of the wrist's and the phone's, each through `FoldTiming.WindowFor`,
  plus `TransportGrace` 10 s), and when the source sync may run (`SyncMoment.Delay(Filed|Deleted, window)`). B2 files each
  through `Filing.File` (ghost, tombstone, hide) and holds each delete in the Mailbox's `DeleteBuffer`. The act is recorded
  in `DashActs` by the watch's id; the same id again is `Again`.
- `Undo`: `DashActs.Undo` takes back exactly what the act took (unfile, or let go from the delete buffer) inside the
  window, once; after it, `TooLate`.
- `Read`: the thread's key and the stamp: the newest message at or before the millisecond the wrist sent, through
  `ReadState.Through`, so a read never moves back and a later arrival stays unread on both.
- `CallNote`: `CallNote.For` on the call's number (found by its message id) or the number the watch sent.

**Taken out of `MailboxArea` into `Codex.Domain`, behaviour identical, so the relay calls the phone's own rules rather
than a copy:**
- `MailboxView.Build`: the reload's hidden, spam and delete-buffer filters, the marketing split, the merge and the read
  stamp, exactly as `ReadPageAndBuild` did them (its plain-merge fallback is unchanged).
- `ThreadTitles.ForRow`: the row title (a group's own name, else three names and "+n", else "Group"; a person's name).
- `ReplyTarget`: `PhoneOf` (the first all-digit key), `GroupRecipients` (raw addresses, else participants),
  `MembershipKnown` (no notification-only group), `ConversationOf` (the latest bridge conversation), and `For`, the whole
  target. `MailboxArea`'s own `IsPhone` went with it.
- `TextSend.WithoutBridge` and the new `TextSendPath.GroupMms`: what `SendWithoutBridge` decided inline.
- `ReadState.Through`, and `CallNote.SourcePrefix` / `CallNote.IsNote` (so the relay does not copy the `note:` prefix).

## Tests written first

- `Watch/Watch.Tests/Dash/DashCodecTests.cs`: `EveryMessageRoundTrips` and `EveryMessageCarriesAnIntegerV` over 20
  fixtures (`DashFixtures`, invented people and literal ids), `PeopleKeepEveryField`, `AThreadKeepsEveryField`,
  `ActsKeepTheirTargetAndWindow`, `AnInstantIsMilliseconds`, `PeopleCarryNoPhoto`, `WordsAreWrittenAsThemselves`,
  `ANewerVersionIsRefused`, `ANewerVersionIsRefusedBeforeItsKindIsRead`, `AMalformedMessageIsUnreadable` (55 rows),
  `NothingIsUnreadable`, `AKindIsReadOnlyOnItsOwnPath`, `TooLargeIsUnreadable`, `PastALimitIsUnreadable`,
  `TheFullestMessagesFitTheBudget`, `TheLimitsAreTheDecidedOnes`, `TheWordsAreFrozen`, `TheTypeWordsAreFrozen`.
- `Watch/Watch.Tests/Dash/DashIdsTests.cs`: `AnIdIsALetterAndSixteenHexDigits`, `TheDigestIsTheFrozenOne`,
  `TheSameThingKeepsItsId`, `KindsNeverShareADigest`, `NoNumberIsInAnId`, `TheShapesAreChecked`, `AnActIsTheWatchsOwnName`,
  `AnActIsAtMostSixtyFour`.
- `Watch/Watch.Tests/Dash/DashLogLinesTests.cs`: `TheLinesSayContractAndOutcomeOnly` (every message with a sentinel name,
  words and number through every line), `AReceiptSaysTheContractAndVersion`, `ANewerOneIsRefusedByVersion`,
  `AnUnreadableOneSaysSo`, `ASendingSaysTheContract`, `AFailureThatIsNoTypeNameIsNotWritten`, `AnOutcomeIsAWordAndACount`,
  `TheContractIsThePathWithoutItsSlash`, `OnlyADashPathIsAContract`.
- `Watch/Watch.Tests/Guard/WatchInvariantsTests.cs`: `DashPathsAreFrozen`, `EveryDashPathIsItsOwn`.
- `FocusPhone/Codex.Tests/Dash/DashRelayTests.cs` (52): the brief's five, `PeopleWaitingNewestFirst`,
  `ReplyGoesThroughPostsSendPath`, `FileKeepsTheTombstoneRule`, `ReadOnTheWatchIsReadOnThePhone`, `MailIsPeopleOnly`, and
  `AGroupIsOneRow`, `ThePeopleAreTheMailboxsCorrespondences`, `NothingFiledOrTombstonedIsWaiting`,
  `WhatTheMailboxHidesIsNotWaiting`, `MailIsNotOnPeopleWaiting`, `UnreadIsThePhonesUnread`, `APhotoSaysPhoto`,
  `ALineIsOneLineAndANameFitsTheRow`, `AtMostTheNewestTwentyFour`, `PeopleWaitingEncodes`, the thread's six
  (`AThreadIsItsLastMessagesOldestFirst`, `AGroupThreadNamesItsSendersAndMembers`, `AThreadCarriesNoMail`,
  `ACallAVoicemailAndANoteAreEachTheirOwn`, `APhotoIsNamedNotSent`, `AnUnknownKeyHasNoThread`, `ALongThreadIsTrimmedToFit`),
  `MailLinesAreNewestFirst`, `FiledMailIsNoLine`, the send's eight (`WithTheBridgeDownAPersonGetsAPlainText`,
  `AGroupReplyIsOneMmsToEveryone`, `AGroupKnownOnlyFromNotificationsIsNotAnswered`, `NothingCanCarryItSaysSo`,
  `WithOnlyTheBridgeItIsTried`, `ANewTextGoesToItsNumbers`, `ANumberThatIsNoNumberIsNowhere` ×2,
  `AReplyWithNobodyToReachIsNowhere`), the acts' (`FilingMessagesTakesOnlyThose`, `FilingWhatPostDoesNotHaveIsUnknown`,
  `TheSameActTwiceIsDoneOnce`, `AnUndoInsideTheWindowTakesTheFilingBack`, `AnUndoAfterTheWindowIsTooLate`,
  `AnUndoOfNothingIsUnknown`, `ADeleteIsHeldForTheUndoWindow`, `AnUndoTakesTheDeleteBack`, `TheWatchsUndoUsesTheSameWindow`),
  the read's (`ALaterTextStaysUnread`, `AReadNeverMovesBack`, `AReadIsToTheMillisecondTheWireCarries`,
  `AnUnknownReadIsUnknown`), the note's (`ACallNoteIsKeptWherePostKeepsThem`, `ANoteOnTheWatchsOwnCallGoesByItsNumber`,
  `ANoteOnNoCallIsUnknown`), and the book's (`TheBookKeepsTheNewest`, `AnUndoTwiceIsAgain`, `TheBookComesBackFromItsRecords`).
- `FocusPhone/Codex.Tests/Post`: `MailboxViewTests` (4), `ThreadTitlesTests` (4), `ReplyTargetTests` (11),
  `TextRoutingTests.WithoutTheBridgeAGroupIsOneMms` (5), `CallNoteTests.ANoteIsKnownByItsSourceId`; `Timeline/ReadStateTests.ThroughNeverMovesBack`.
- `FocusPhone/Codex.Tests/Guard/DashRelayGuardTests.cs`: `TheMailboxUsesTheSharedRules` (8), `TheRelayUsesTheSameRules` (9),
  `TheMailboxNoLongerBuildsItsOwn`, `TheDashPathsAreNotCopied` (Post, Codex.Infrastructure and Codex.Domain name a Dash
  path only through `WatchIdentity`).
- `RepoGuards.Tests/DashLogsCarryNoPayloadTests.cs` (after `RuntimeLogsCarryNoValuesTests`): `TheCodecAndTheRelayNeverLog`
  (no logger call in either folder), `TheLinesAreFilledFromNoPayload` (every interpolation hole in `DashLogLines` is a
  path, version, word, count or checked type name), `TheLinesTakeNoMessage` (its methods take no message), and the guard's
  own self-tests, `TheGuardSeesALogCall` ×7 and `TheGuardLeavesTheLinesAlone` ×3.

## Red

- **`Watch.Tests`: 607 tests, 160 failed, 447 passed** (the 447 are release A's). The 160: `DashCodecTests` 120,
  `DashIdsTests` 24, `DashLogLinesTests` 14, `WatchInvariantsTests` 2 (`DashPathsAreFrozen`, `EveryDashPathIsItsOwn`).
  First errors: `System.NotImplementedException` (150), `Assert.Throws() Failure: Exception type was not an exact match`
  (4, `OnlyADashPathIsAContract`), `Assert.Equal() Failure: Values differ` (3: the limits, `MaxActLength`, the paths'
  distinct count), `Strings differ` (1), `Collections differ` (1), and `Assert.All() Failure: 5 out of 5 items` (the
  budget test). A first attempt did not compile (`CS0229`: the fixture `Thread` was ambiguous with `System.Threading.Thread`
  under implicit usings; renamed `GroupThread`) and is not counted.
- **`FocusPhone/Codex.Tests`: 3206 tests, 95 failed, 3111 passed.** The 95: `DashRelayTests` 52, `DashRelayGuardTests` 18,
  `ReplyTargetTests` 11, `TextRoutingTests` 5, `MailboxViewTests` 4, `ThreadTitlesTests` 4, `ReadStateTests` 1. First errors:
  `NotImplementedException` (76), `Assert.Contains() Failure: Sub-string not found` (16: `MailboxArea` and the stub relay
  call none of the shared rules), `Assert.DoesNotContain() Failure: Sub-string found` (`TheMailboxNoLongerBuildsItsOwn`:
  `recipients.Count > 1`), `Assert.All() Failure: 9 out of 9 items` (`TheDashPathsAreNotCopied`, the stub paths empty),
  `Values differ` (`TransportGrace` zero).
- **`RepoGuards.Tests`: 400 tests, 1 failed:** `TheLinesAreFilledFromNoPayload` (`Assert.NotEmpty`: the stub makes no line).
  The other two pass on stubs because the stubs log nothing, so a probe was planted for one run: `Probe.cs` in
  `Codex.Infrastructure/Dash` calling `Console.WriteLine(person.Name)`, and `Leak(DashSend send) => $"DASH sent {send.Text}"`
  in `DashLogLines`. `DashLogsCarryNoPayloadTests`: 13 tests, 3 failed: `TheCodecAndTheRelayNeverLog`
  ("FocusPhone/Codex.Infrastructure/Dash/Probe.cs: Console.WriteLine("), `TheLinesAreFilledFromNoPayload` ("send.Text") and
  `TheLinesTakeNoMessage` ("DashSend send"). The probe was removed.
- **`Storyworthtelling.Updates.Tests`: 193 tests, 1 failed:** `CiHygieneTests.EveryPathFilterCoversEveryProjectItsWorkflowBuilds`,
  "build-plate.yml: Watch/Watch.Domain, build-post.yml: Watch/Watch.Domain, test-focusphone.yml: Watch/Watch.Domain"
  (each builds `Codex.Tests`, whose closure now holds `Watch.Domain` through `Codex.Infrastructure`).
- **Second red (`CallNote.IsNote`, added during green so the relay would not copy the `note:` prefix):** `CallNoteTests`
  9 tests, 1 failed, `ANoteIsKnownByItsSourceId`, `NotImplementedException`.

## Green

- **On the way:** `Watch.Tests` first read 607 with 1 failed, `AKindIsReadOnlyOnItsOwnPath("people ask", "/dash/mail")`,
  and the test was wrong: an ask carries only its kind, so its bytes on `/dash/mail` are a mail ask. The row went and
  `AnAskIsWhatItsPathSays` holds the rule. `Codex.Tests` read 3207 with 8 failed (the Mailbox guards) until `MailboxArea`
  called the extracted rules.
- **`Watch.Tests`: 607 passed, 0 failed. `FocusPhone/Codex.Tests`: 3207 passed, 0 failed. `RepoGuards.Tests`: 400 passed,
  0 failed. `Storyworthtelling.Updates.Tests`: 193 passed, 0 failed** (`test-focusphone.yml`, `build-post.yml` and
  `build-plate.yml` gained `Watch/Watch.Domain/**`; `build-flyleaf.yml` had it). `FaceStudio.Tests`: 29 passed.
  `LogLoopReader.Tests`: 117 passed. `leakscan --ci` over the chunk's diff: "no personal-data shapes in the added lines".
- **Compiled here:** `dotnet build FocusPhone/Post.Android/Post.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min
  -p:AndroidUseApkSigner=false`: **Build succeeded, 0 errors**; no warning names a changed file (every warning is in a file
  this chunk did not touch, as on main).

## Decisions made in this chunk

- **The relay lives in `Codex.Infrastructure`, not `Codex.Domain`.** Watch.Domain builds on `Storyworthtelling.Updates`,
  which `Codex.Domain` deliberately does not reference, and `Codex.Domain` is referenced by Pulp and Constitution, so a
  Domain reference would have put Watch.Domain in their builds and their workflows' filters, and rebuilt and republished
  them on every watch change. Infrastructure already references Updates and is built by Flyleaf, Post, Plate and Score
  only. The pieces of Post's own rules the relay needed went to `Codex.Domain` with no watch dependency. Post.Android
  will name Watch.Domain directly when its listener lands (B2).
- **Ids are digests.** The wire never carries Post's own keys or source ids (a notification's source id holds the number).
- **Mail reaches the wrist only as mail lines (A11).** People waiting and conversations carry texts, calls, voicemails
  and notes; a correspondence whose only waiting message is mail is not on People waiting, and filing a correspondence
  from the wrist files what its row showed, not its mail.
- **People only means `Human`.** Unjudged mail waits for Post's judgement. The cost: with no Claude key the rules never
  say `Human`, so the wrist shows no mail lines.
- **An undo names its act.** The brief had undo by source id, but a correspondence filed from People waiting is filed by
  key, and the watch never had all of its source ids. So file and delete carry the watch's own act id and either a key or
  message ids, and `DashActs` (64 kept, locked) takes back exactly what that act took, once, inside its window.
- **The wrist's window is held, not trusted.** Each device follows `FoldTiming` on its own settings, so the act carries the
  wrist's window and Post holds the longer of the two plus 10 s for the undo to cross, and waits that long before the
  source sync (`SyncMoment`). The 10 s is not evidenced.
- **A read is through what the wrist showed**, never "now" (the phone's own read stamps now on opening a thread): a text
  that arrived after the wrist drew the conversation stays unread on both, and a read never moves a stamp back
  (`ReadStateStore.MarkRead` overwrites, so `ReadState.Through` takes the later).
- **The send result travels on `dash/send`** (`kind: result`), so "or says why it can't" has a wire; the words are outcome
  words, and the watch says them its own way.
- **Photos are named, never sent, here.** A message lists its attachments by id and type; how the wrist asks for one, and
  receives it, is W-5.1.3's.
- **A call note may name a number** as well as a call Post has, since a call taken on the watch's own phone app may never
  reach Post's call log.
- **A text's line on the wrist is its words,** not the Mailbox's "Text: …", as the approved People waiting screen draws it.

## Not done, not verified

- **Nothing here has met a watch or the Data Layer.** B2 builds Post's listener (the paths, a capability, reading the
  stores into a `DashSnapshot`, carrying the plans out through `Filing`, the Mailbox's `DeleteBuffer` and commit path,
  `ReadStateStore`, the timeline cache and the send cascade, which today lives in `MailboxArea` and should be shared with
  the listener), the watch-unreachable rule, and a persisted `DashActs`.
- **The limits and the 64 KB budget are chosen, not measured;** whether a `MessageClient` message has a documented limit
  was not found. The 10 s grace is unevidenced.
- **`DashActs.Knows` then `Record` is not one step:** two deliveries of the same act at the same instant on two threads
  could both act. B2 should handle one message at a time.
- **The relay reads whatever items B2 hands it:** like the Mailbox, it sees only the cache pages read, so a correspondence
  older than them is not on the wrist.

# Chunk B2 · Post's listener for Dash (the rest of W-4.1.1)

Every figure below is from a real run on 22 Nov 2026. As in B1, the red runs were taken with the tests written and the
new pure code present only as stubs (methods threw `NotImplementedException`; new constants were empty strings or zero;
the two new `DashOutcome` members existed so the tests compiled), and with none of the head's code written yet. Nothing in
this chunk has met a watch, the Data Layer or a phone.

## What it is

**The phone's send road, in one place.** The cascade that lived in `MailboxArea` (`DeliverText`, `SendWithoutBridge`,
`SendGroupMms` and the picture path in `OnActivityResult`) is now:
- `Codex.Domain/Post/TextCascade` (pure): `Text(routes, recipients, conversation, words, bridgeConnected, canSendSms)` tries
  the bridge when `TextSend.Choose` says so (the conversation given, else one found or started), then takes
  `TextSend.WithoutBridge`: a plain text to a person, one MMS to a group, or `Unavailable`. `Media(...)` is the bridge,
  then an MMS to one person or a group (as the Mailbox always sent pictures, with no SMS permission check). It returns a
  `TextDelivery` (the route, whether the bridge was tried, and the failure kept whole) over an `ITextRoutes` seam.
- `Post.Android/TextCourier`: the routes (`GmBridgeClient`, `SmsSender`, `MmsSender`) and the same key actions and failure
  lines the Mailbox had (`TextSending.FromTap()` for a tap; `FromWrist()` is `KeyAction.Unwatched`, since a wrist's send is
  not a tap). `MailboxArea` reads the permission and starts the key action on the UI thread as before, runs
  `TextCourier.Deliver` off it and says what happened in the same dog-ears (`Delivered`). The one difference: the plain
  text or MMS fallback is now handed to the platform from the background thread too, not the UI thread (neither
  `SmsSender` nor `MmsSender` needs the UI thread; the listener has none).

**Also out of `MailboxArea`, behaviour identical, so the listener uses the Mailbox's own code:**
- `SourceDeletes`: `Held`, the process's one `DeleteBuffer` (the Mailbox's was per area), and `Commit`, the channel-specific
  delete that was `CommitDelete` and its five helpers. A delete held from the wrist is gone from the Mailbox at once, and
  the other way round.
- `MailboxSources`: the live tail (`ReadLiveTail`, and the same read in `LoadOpenThread`) and `CachePageSize`.
- `Syncs.Later(context, delay)`, which `Syncs.After` now calls.

**Post's listener, in `Post.Android/Dash`:**
- `DashListener`, a `WearableListenerService` registered as `com.storyworthtelling.focusphone.communication.DashListener`
  (`WatchIdentity.DashListenerName`), with one filter per Dash path from `WatchIdentity`. It takes each message off Play
  services' thread (`Task.Run`) and waits for it at most `DashReplies.TurnWait`, so the process stays while an act is
  carried out; were it ever called on the main thread it would not block.
- `Resources/values/wear.xml`: Post advertises `post-relay` (`WatchIdentity.PostCapability`), so Dash can find the phone.
- `DashCarrier`: decodes for the path (`DashCodec`), logs the receipt, then one message at a time (the `UpdateRunGate`
  Flyleaf's update runs use, waited for at most `DashReplies.GateWait`, else `DASH …: Busy`) asks `DashInbox` what to do and
  carries it out: answers from the snapshot; a send through `TextCourier.Deliver` (waiting up to `BridgeWait` for a paired
  bridge to come up, since the message may itself have started Post); a filing through `Filing.File`; a delete held in
  `SourceDeletes.Held` (`DashHolds`); an undo through `Filing.Unfile` or the buffer's `Undo`; a read through
  `ReadStateStore.MarkRead`; a note through the cache's `Upsert`; and the sync each plan owes through `Syncs.Later`.
- `DashSnapshots.Relay`: the snapshot from the stores `ReadPageAndBuild` reads (live tail and first cache page, hidden,
  spam, the shared buffer, tombstones, identity links, read stamps, group names, the address book's names).
- `DashBook`: the book in `dashRelay`/`acts` (`WatchIdentity.DashPreferences`, `DashActsKey`), read once a process and
  written whole after every change (`Commit`, not `Apply`). A book that will not read starts a new one, said once.
- `DashHolds`: a held delete commits when its window closes, through `SourceDeletes.Commit`, and is settled in the book;
  `Resume` (from `PostApplication`, once a process, off the main thread) holds again every delete the book still owes, or
  carries it out if its window has closed.

**The pure half's new pieces (`Codex.Infrastructure/Dash`, tested in `Codex.Tests`):**
- `DashInbox.Turn(message, relay, acts, conditions)`: each message becomes a `DashAnswer` (people, a thread, mail), a
  `DashSendTurn`, a `DashActTurn`, a `DashUndoTurn`, a `DashReadTurn`, a `DashNoteTurn` or a `DashNoTurn` (an unknown
  thread, or the watch's half of a contract arriving at Post). The Mailbox is read only for a message that needs it.
  `Delivered` puts a send's outcome in the book; `OutcomeOf` says it as the watch hears it.
- `DashActs`: a send is an act (`DashActKind.Send`): recorded before it is carried out, answered from the book if heard
  again, never undone. `Owed()` / `Settle()` for deletes across a restart. `ActedItem` keeps a mail's message id.
- `DashActsCodec`: the book as JSON with an integer `v`, frozen words, a timeline kind as the cache's own number.
- `DashReplies`: the watch-unreachable rule and the waits.

## Tests written first

- `Codex.Tests/Post/TextCascadeTests.cs` (14): `TheBridgeLeads`, `ANewConversationStartsOnTheBridge`,
  `ARefusedBridgeFallsToAPlainText`, `NoConversationFallsBack`, `ARefusedBridgeToAGroupIsOneMms`,
  `WithTheBridgeDownAPersonGetsAPlainText`, `WithTheBridgeDownAGroupIsOneMms`, `NothingCanCarryItSaysSo`,
  `ARefusedBridgeWithNoTextIsUnavailableAfterTrying`, `AFailedTextKeepsItsCause`, `AFailedMmsKeepsItsCause`,
  `APictureGoesByTheBridgeFirst`, `APictureWithoutTheBridgeIsAnMms`, `AFailedPictureKeepsItsCause`.
- `Codex.Tests/Dash/DashInboxTests.cs` (26 with theory rows): the asks (`PeopleWaitingIsAnswered`, `AConversationIsAnswered`,
  `AnUnknownConversationIsNotAnswered`, `MailLinesAreAnswered`, `AnAnswerArrivingAtPostIsLeftAlone` ×4), the send
  (`ASendIsCarriedOutOnce`, `ASendHeardWhileItIsCarriedOutIsNotSentAgain`, `ASendNothingCanCarryIsAnsweredAtOnce`,
  `ASendToNobodyIsAnsweredAtOnce`, `TheAnswerIsWhatThePhoneDid` ×7), the acts (`AFileFromTheWristIsAFiling`,
  `ADeleteFromTheWristIsHeld`, `ADeletedMailKeepsWhatItsDeleteNeeds`, `AnUndoNeedsNoMailbox`, `TheSameActTwiceIsDoneOnce`),
  `AReadIsMarked`, `ANoteIsKept`.
- `Codex.Tests/Dash/DashActsTests.cs` (9): `ADeleteIsOwedUntilSettled`, `ATakenBackDeleteIsNotOwed`,
  `ALateUndoLeavesTheDeleteOwed`, `AFilingIsNeverOwed`, `ASendKeepsItsOutcome`, `ASendThatNeverFinishedIsAnsweredFailed`,
  `ASendCannotBeUndone`, `AnActIsNoSend`, `AnActedItemIsWhatADeleteNeeds`.
- `Codex.Tests/Dash/DashActsCodecTests.cs` (20 with rows): `TheBookRoundTrips`, `TheBookComesBackAfterARestart`,
  `AnOwedDeleteIsStillOwedAfterARestart`, `NothingStoredIsAnEmptyBook` ×2, `ADamagedBookIsUnread` ×13, `ANewerBookIsUnread`,
  `TheWordsAreFrozen`.
- `Codex.Tests/Dash/DashRepliesTests.cs` (5): `AnAnswerIsTriedOnce`, `AMissedAnswerIsCounted`,
  `ALandedAnswerStartsTheCountAgain`, `MissesFromManyThreadsAreEachCounted`, `EveryWaitIsBoundedByTheTurn`.
- `Codex.Tests/Guard/DashRelayGuardTests.cs`: `TheMailboxUsesTheSharedRules` now asks for `TextCourier.Deliver(`,
  `TextCourier.DeliverMedia(`, `SourceDeletes.Held`, `SourceDeletes.Commit(` and `MailboxSources.LiveTail(` in place of the
  two `TextSend` rows (the cascade calls those now); new: `TheMailboxNoLongerDeletesItself`, `ThePhoneSendsTextsInOnePlace`
  (no route called outside the courier; the courier runs the cascade; the cascade calls `TextSend`),
  `PostRegistersItsListenerByName` (the service, the action, all nine paths), `PostAdvertisesItsCapability`,
  `TheListenerCarriesThePlansThroughPostsOwnCode` ×14, `EveryDataLayerWaitIsBounded`, `AnAnswerIsSentOnceAndAMissIsCounted`
  (one `SendMessageAsync` in all of Post), `TheListenerTakesOneMessageAtATime`, `TheListenerWorksOffTheMainThread`,
  `TheActsSurviveARestart`, `PostReadsTheWatchsCodec`; `TheDashPathsAreNotCopied` now also covers the listener's name, the
  capability and the preferences file.
- `Codex.Tests/Guard/FrozenInvariantsTests.cs`: `DashListenerComponentNameIsFrozen` (the value, and exactly one Post file
  registering it by `Service(Name = WatchIdentity.DashListenerName`).
- `Watch.Tests/Guard/WatchInvariantsTests.cs`: `PostsHalfIsFrozen`. `Watch.Tests/Dash/DashLogLinesTests.cs`:
  `AMissedAnswerIsACount`.
- `RepoGuards.Tests/DashLogsCarryNoPayloadTests.cs`: `TheListenerLogsOnlyDashLines` (every logger call in
  `Post.Android/Dash` is `HeadLog.Info`/`Warn` with a `DashLogLines` line or a plain literal, or `HeadLog.Quiet` with a plain
  literal and the exception; nothing interpolated, no other logger), and its own `TheListenerRuleSeesAPayload` ×8,
  `TheListenerRuleLeavesItsLinesAlone` ×4, `TheCallsAreReadWhole`.

## Red

- **`Watch.Tests`: 609 tests, 1 failed, 608 passed:** `PostsHalfIsFrozen`, `Assert.Equal() Failure: Strings differ` (the
  stub's empty name). `AMissedAnswerIsACount` passed on the stubs: the two outcomes had to exist for it to compile, and the
  line's shape is B1's `Outcome`.
- **`FocusPhone/Codex.Tests`: 3309 tests, 105 failed, 3204 passed.** The 105: `DashRelayGuardTests` 30, `DashInboxTests`
  26, `DashActsCodecTests` 20, `TextCascadeTests` 14, `DashActsTests` 9, `DashRepliesTests` 5,
  `FrozenInvariantsTests.DashListenerComponentNameIsFrozen` 1. First errors: `NotImplementedException` 70 (and one inside an
  `AggregateException`, the parallel count), `Assert.Contains() Failure: Sub-string not found` 21 (the Mailbox and the
  missing head), `Assert.Equal() Failure: Values differ` 4 (the zero waits, `Attempts`, and `ALateUndoLeavesTheDeleteOwed`:
  B1's book marked a late undo taken, so a second one said `Again`), and one each of `FileNotFoundException` (no
  `wear.xml`), `DirectoryNotFoundException`, "The listener has no OnMessageReceived", `Assert.Single()` (no file registers
  the listener), `Assert.NotEmpty()` (no Data Layer call), `Assert.Matches()` (no gate), `Strings differ`,
  `Assert.DoesNotContain()` (`CommitDelete` still in the Mailbox) and `Assert.All() Failure: 3 out of 12` (the empty names).
- **`RepoGuards.Tests`: 414 tests, 1 failed:** `TheListenerLogsOnlyDashLines`, `DirectoryNotFoundException`
  (`Post.Android/Dash` did not exist). Once green, a probe was planted for one run: a method in `DashBook.cs` calling
  `HeadLog.Info(Tag, $"DASH sent {words}")`, `HeadLog.Warn(Tag, "DASH stopped", e)` and `PostDiagnostics.LogInfo(Tag, words)`.
  `DashLogsCarryNoPayloadTests`: 27 tests, 1 failed, `TheListenerLogsOnlyDashLines`, naming all three calls. The probe was
  removed.
- **`Storyworthtelling.Updates.Tests`: 193 passed, 0 failed** (no workflow filter needed a change: `build-post.yml` already
  watches `Watch/Watch.Domain/**`, which Post now names directly).

## Green

- **On the way:** `Codex.Tests` read 3309 with 1 failed, `NativeTextAttachTests.WithoutTheBridgeAOneToOneTextGoesThroughSmsSender`
  ("MailboxArea.cs has no method named SendWithoutBridge"): an older guard that read the send from the Mailbox. It now reads
  the Mailbox's `DeliverText` handing the text, the key action and the permission to `TextCourier.Deliver`, and the
  courier's `SendText` calling `SmsSender.Send(context, number, text, sending)`; which route is taken is `TextCascadeTests`'.
- **`Watch.Tests`: 609 passed, 0 failed. `FocusPhone/Codex.Tests`: 3309 passed, 0 failed. `RepoGuards.Tests`: 414 passed,
  0 failed. `Storyworthtelling.Updates.Tests`: 193 passed, 0 failed.** `LogLoopReader.Tests`: 117 passed. `FaceStudio.Tests`:
  29 passed. `leakscan --ci` over the chunk's diff: "no personal-data shapes in the added lines".
- **Compiled here:** `dotnet build FocusPhone/Post.Android/Post.Android.csproj -p:AndroidSdkDirectory=/opt/android-sdk-min
  -p:AndroidUseApkSigner=false`: **Build succeeded, 0 errors**, with the Wearable aars fetched. No warning names a file this
  chunk added or changed. Two new NU1608 warnings (Lifecycle.Service 2.9.1 beside Lifecycle.Runtime 2.11, Tracing.Ktx 1.3
  beside Tracing 2.0) come with the Wearable binding and LiveData 2.11; Flyleaf's restored graph has the same three
  versions, and `.github/scripts/check-dependency-graph.py FocusPhone/Post.Android` finds nothing missing. Flyleaf was not
  rebuilt: no central version moved.

## Decisions made in this chunk

- **Post sends the wrist only answers.** People waiting, a conversation, mail lines and a send's outcome go back on the
  node that asked, once, waited for at most 15 s. An answer that does not land is logged by contract and type
  (`CONTRACT dash/people: not sent, TimeoutException`) and counted (`DASH dash/people: Unreached 3`, since the last that
  landed), and dropped: no queue, no retry. The wrist asks again when it is near; a send asked again is answered from the
  book. So "nothing goes to the watch while it is unreachable" holds by construction.
- **A send is in the book before it goes.** Heard again, it is answered as it went and never sent twice. A send the
  process lost while carrying it out is answered `failed`: Post cannot tell whether it went, and a second copy of a text
  is worse than a "didn't go" the owner can redo. Overturnable.
- **An unknown conversation is not answered** (logged `DASH dash/thread: Unknown`); the wrist's own wait says it.
- **A send waits up to 10 s for a paired bridge,** nudging it with `ConnectIfPaired`, because the message may itself have
  started Post's process; past that it takes the phone's fallback as the phone would. Unmeasured.
- **The waits:** a turn 60 s, the gate 30 s, the bridge 10 s (looked at every 500 ms), an answer 15 s. Chosen, not measured.
- **One delete buffer for the process.** The Mailbox's buffer was per area; it is now `SourceDeletes.Held`, shared by the
  Mailbox's two views and the wrist. A side effect: a delete held in the window survives the Mailbox being rebuilt (a
  rotation), where it used to reappear until committed.
- **A held delete outlives the process through the book.** The act records each item's kind, source id and (for mail) its
  message id; `DashHolds.Resume` holds it again or carries it out at the next start. A late undo no longer marks an act
  taken, so a delete whose window closed before it was carried out stays owed.
- **The wrist's MMS "sent" is the platform's acceptance**, as on the phone: a carrier refusal later is the Mailbox's
  existing dog-ear (`PendingMmsSendFailureStore`), and the wrist is not told.
- **Acts are carried out, then the book is written; a send's book is written first.** A filing or delete repeated after
  a crash is harmless; a send repeated is not.
- **`WearableListenerService`, not a `MessageClient` listener,** so a message from the wrist starts Post when it is not
  running, as Flyleaf's report listener does.
- **The fallback send now runs off the UI thread** in the Mailbox too (see above).

## Not done, not verified

- **Nothing here has met a watch, the Data Layer or a phone:** whether Play services binds the listener for the nine
  paths, whether a reply on `SourceNodeId` lands, the capability being found, the bridge coming up within the wait, and
  the hold and commit across a real process death. There is no Dash app on the watch yet (release B's later chunks).
- **Pre-existing, not changed:** `SourceDeletes.Commit`'s failure line names the item's source id (as `CommitDelete` did),
  and a notification's source id can hold a number. A wrist's delete now reaches that line too. Worth its own fix.
- **W-4.1.2 (tokens and the vault on the watch) is not started.**
- **The open Mailbox does not redraw after an act from the wrist;** it shows it at its next reload.

### B2 follow-up · the delete commit names no source id

`SourceDeletes.Commit`'s failure line logged `{item.Kind}:{item.SourceId}`, and a notification's source id can hold a phone number; deletes from the wrist reach it now. Red: `DashLogsCarryNoPayloadTests.ADeleteFailureNamesNoSourceId`, RepoGuards filter 1 total, 1 failed. Green: the line names the kind only (logcat the exception's type name), RepoGuards 415, 0 failed; Post builds.

# Chunk B3 · tokens and the vault on the watch (W-4.1.2)

Every figure below is from a real run on 22 Nov 2026. As in B1 and B2, the red runs were taken with the tests written and
the new code present only as stubs: methods threw `NotImplementedException`, and new constants were empty strings or zero
(`WatchIdentity.GoogleKeysPath` was `""`). No Android code was written in this chunk, and nothing in it has met a watch.

## What W-4.1.2 asked, and what B3 did with each part

The plan (§2.4, A38, A61) has two halves: the **vault passphrase** typed once on the watch in Dash and kept with the
watch's Keystore, and **Google tokens** handed over from the phone (Post a Gmail token, Score a Drive token, each hourly,
held in memory and in Keystore-encrypted storage). Its named tests are `KeyHandoffTests.TokenNeverLogged` and
`PassphraseKeptInKeystoreOnly`. The brief for B3 was to build the narrowest version that works and to stop and report
rather than put the passphrase on the watch.

**The passphrase: stopped, not built, for the owner to rule on.** What one passphrase opens is not "what lives in the
vault for the watch" (TF §4) but the whole of it: the shared vault holds every app's keys (Claude, Gemini, Plaid, NYT,
reMarkable, Pulp's three, the GitHub update token, TIDAL's app credentials), the Gmail app password and the Messages
bridge session, and the Sleep Number login; the personal vault holds the TIDAL refresh token and per-install choices.
Reading either also takes a Drive token, which W-6.1.2 puts on the wrist, so the two together leave every household
key one download from the watch. And since the technical finding, every use TF §4 named for it has gone: tokens are
handed over (A61), TIDAL plays through the phone (A62, A73), the Rolodex is Post's (PC6), zone limits are computed on
the phone and only the numbers travel (A24, W-8.1.3), and the phone is always on (A67). The music folder id
(`MusicSecrets.FolderId`) and the vault's Drive file ids are plain facts the phone can send. So nothing on the watch
needs the vault. **Asked of the owner:** retire A38's passphrase on the watch, and with it W-5.1.8 (Dash's first-run
passphrase screen, drawn in `design/screens-r1.html`) and the plan's `PassphraseKeptInKeystoreOnly`; or confirm A38 and
say what on the watch it is for. Until then `TheWatchHoldsNoVaultTests` keeps the vault off the watch, so building it is
a decision rather than a drift. The plan, `WATCH-DESIGN.md` §11 and the mockup are not edited: they wait for the ruling.

**The Gmail token: not handed over.** Dash needs none: Post fetches the mail and relays people-only lines on
`dash/mail` (B1), the phone doing the network work. So the contract has no Gmail scope and Post's side never names it.

**The Drive token: its contract built, the pure half, in `Watch/Watch.Domain/Keys`** (BCL only), for Wave's streaming
(W-6.1.2) in release C, where the phone's sender and the watch's holder will be wired. Its pieces:
- `WatchIdentity.GoogleKeysPath` = `/keys/google`, frozen. The Data Layer joins a package only to itself, so Score's
  messages reach Wave and Post's reach Dash; one path serves any phone app and its watch app.
- `AccessToken`: a token of a bearer token's shape (RFC 6750's characters, 20 to 2048), whose `ToString()` is
  `[access token]`, so interpolation, a record's own text, an exception or a joined list never prints it (the rule
  Foreword's prayer text uses). Its value is `internal` (the codec writes it) and leaves only as `BearerHeader()`.
  Value equality, so a refused token can be let go without touching one handed over since.
- `KeyMessages`: `TokenAsk(scope)` from the watch; `TokenHandoff(scope, token, life)` or `TokenNone(scope, why)` from the
  phone. `TokenScope` has one member, `DriveReadOnly`. `why` is `sign-in` or `failed`. The life is a duration, not an
  instant, so the two clocks need not agree.
- `KeyCodec`: UTF-8 JSON `{v, kind, …}` on `/keys/google` only. A newer `v` is refused first; each kind carries exactly
  its own fields once each (stricter than Dash's codec: nothing rides beside a token by any name); a scope other than
  `drive.readonly`, a token of the wrong shape, or a life outside 1 to 2,700 whole seconds refuses the whole message, and
  the phone cannot write a life past it (`ArgumentOutOfRangeException`, whose message names the seconds, not the token).
- `KeyLimits`: 4 KB a message; the longest hold 45 minutes, equal to the phone's own reuse window
  (`CachedAccessToken.DefaultLifetime`, held equal by a guard); ask again 5 minutes before the end; asks a minute apart.
- `TokenHold`: the watch app's one token, in memory only, behind a lock. `Take` holds a handoff for its life; `Current`
  lets the token go once its life is over (gone, not hidden, so a clock stepping back does not bring it back);
  `Refused` lets it go when the phone has none; `Forget` lets it go when Google refuses it, unless another has been handed
  over since; `AskDue` says to ask when none is held or the one held is nearly done, and not within a minute of the last
  ask, so a phone handing over the short end of its own token does not start a storm.
- `KeyLogLines`: `CONTRACT keys/google: received v1` / `refused v2, newer than Watch reads` / `refused, unreadable` /
  `sent v1` / `not sent, TimeoutException`, and `KEYS keys/google: Held` (asked, answered, held, sign-in, failed,
  expired, forgotten). The failure line takes the exception and writes only its type's name: a message can hold whatever
  the call was given, the token included.

## Tests written first

- `Watch/Watch.Tests/Keys/KeyHandoffTests.cs` (35 with theory rows): the plan's **`TokenNeverLogged`** (one token through
  its own text, a handoff's, a read's, the hold's, an interpolation, a joined list, every receipt from both ends, every
  outcome, and two failures whose messages hold the token and its header; none says the token, its digits or `Bearer`,
  and every line has the contract's shape), `ATokenSaysItIsAToken`, `TheTokenLeavesAsAHeader`, `ATokenIsATokensShape` ×8,
  `ALongTokenIsATokenToItsLimit`, `ATokenIsItsValue`, the hold's (`AHandedTokenIsHeldForItsLife`,
  `PastItsTimeTheTokenIsLetGo`, `NothingIsHeldAtFirst`, `ANewerTokenReplacesTheOld`, `ARejectedTokenIsForgotten`,
  `ATokenHandedSinceIsNotForgotten`, `ThePhoneSayingNoneLetsTheTokenGo`), the asking (`AskWhenThereIsNone`,
  `NoAskWhileTheTokenIsGood`, `AskWhenTheTokenIsNearlyDone`, `AnAskIsNotRepeatedWithinAMinute`,
  `AShortAnswerDoesNotStartAStorm`), `TheHoldKeepsNothingButMemory` (no property, and no member hands out text, bytes or a
  stream), `TheTokensValueIsNotPublic`, and the lines' (`AReceiptSaysTheContractAndVersion`, `AnOutcomeIsAWord`,
  `AFailureIsItsTypeOnly`, `OnlyTheKeysPathIsAContract` ×4, `TheContractIsThePathWithoutItsSlash`).
- `Watch/Watch.Tests/Keys/KeyCodecTests.cs` (79 with rows): `EveryMessageRoundTrips` ×5, `EveryMessageCarriesAnIntegerV` ×5,
  `AHandoffKeepsItsTokenAndLife`, `ARefusalKeepsWhy`, `ALifeIsWholeSeconds`, `ANewerVersionIsRefused`,
  `ANewerVersionIsRefusedBeforeItsKindIsRead`, `AMalformedMessageIsUnreadable` ×44 (a Gmail scope, a wider Drive scope, a
  token beside an ask or a refusal, a duplicated field, every wrong token shape and life among them), `NothingIsUnreadable`,
  `AnUnknownFieldRefusesTheMessage`, `OnlyAReadOnlyDriveTokenCrosses`, `OnlyTheKeysPathIsRead` ×8, `TooLargeIsUnreadable`,
  `TheFullestMessageFitsTheBudget`, `ALifeOutsideTheHoldIsNotSent` ×4, `TheWordsAreFrozen`, `TheLimitsAreTheDecidedOnes`,
  `EveryMessageTravelsOnTheKeysPath`. Fixtures in `KeyFixtures.cs`, every token saying it is invented.
- `Watch/Watch.Tests/Guard/WatchInvariantsTests.cs`: `KeysPathIsFrozen`.
- `RepoGuards.Tests/TheWatchHoldsNoVaultTests.cs` (new, 22 with rows): `ThePassphraseNeverReachesTheWatch` (no shipping
  file under `Watch/`, comments aside, names the vault, its passphrase or `Storyworthtelling.Voice`),
  `NoWatchProjectReachesTheVaultsLibrary` (no watch project's ProjectReference closure reaches `Storyworthtelling.Voice`),
  `ThePhonesWatchHalvesNameNoVault` (Post's relay and listener, Flyleaf's watch half), `DashIsHandedNoToken` (nothing on
  Post's side names the keys contract), `ATokenLeavesOnlyAsAHeader` (`BearerHeader(` read nowhere but its own folder and
  the tests; release C adds Wave's one HTTP seam to the list), `TheTokensValueStaysInItsAssembly` (no `InternalsVisibleTo`
  on the watch), `TheWristKeepsATokenNoLongerThanThePhone` (45 = 45), and the guard's own `TheGuardSeesTheVault` ×7,
  `TheGuardLeavesTheWatchAlone` ×3, `TheGuardSeesAHandoff` ×4, `CommentsAreNotCode`.
- `RepoGuards.Tests/DashLogsCarryNoPayloadTests.cs`: `TheCodecAndTheRelayNeverLog` now reads `Watch/Watch.Domain/Keys`
  too (and asks to see `KeyCodec.cs` and `TokenHold.cs`); new `TheKeyLinesAreFilledFromNoPayload` and
  `TheKeyLinesTakeNoToken` (a keys line takes a path, a read, an end, an outcome or an exception, and nothing else).

## Red

- **`Watch.Tests`: 718 tests, 107 failed, 611 passed** (B2's 609, and `TheHoldKeepsNothingButMemory` and
  `TheTokensValueIsNotPublic`, which read the stubs' members by reflection and the stubs already had the final shape). The
  107: `KeyCodecTests` 71 (all), `KeyHandoffTests` 35 of 37, `WatchInvariantsTests.KeysPathIsFrozen` 1. First errors:
  `NotImplementedException` 79, `TypeInitializationException` 15 (the fixtures' tokens come from the stub
  `AccessToken.From`, so each test reading them failed in the fixture, and each `MemberData` theory counted once rather
  than per row: 8 rows more in green), `Assert.Throws() Failure: Exception type was not an exact match` 8 (`ALifeOutsideTheHoldIsNotSent` ×4,
  `OnlyTheKeysPathIsAContract` ×4), `Assert.Equal() Failure` 3 (the path, the limits, the version), and
  `ArgumentOutOfRangeException` 2 (the zero `LongestLength` made the longest-token fixture's length negative).
- **`RepoGuards.Tests`: 439 tests, 2 failed:** `TheWristKeepsATokenNoLongerThanThePhone` (45 expected, 0) and
  `TheKeyLinesAreFilledFromNoPayload` (`Assert.NotEmpty`: the stub makes no line). The others pass on stubs because the
  stubs name nothing, so a probe was planted for one run: `Watch/Watch.Domain/Keys/Probe.cs` (an `InternalsVisibleTo`, a
  `VaultPassphraseAlias` constant, `Console.WriteLine(token.BearerHeader())`), a `Leak(TokenHandoff handoff)` line in
  `KeyLogLines` writing the header, `FocusPhone/Post.Android/Dash/KeysProbe.cs` (`WatchIdentity.GoogleKeysPath`, a
  `BearerHeader()` call, a `VaultPassphraseStore`), and `Watch/Probe/Probe.csproj` referencing `Storyworthtelling.Remarkable`
  (which reaches `Storyworthtelling.Voice` one step on, so only the closure walk can see it). The two guard files: 52 tests,
  **10 failed**, each naming its probe: `ThePassphraseNeverReachesTheWatch`, `NoWatchProjectReachesTheVaultsLibrary`,
  `ThePhonesWatchHalvesNameNoVault`, `DashIsHandedNoToken`, `ATokenLeavesOnlyAsAHeader`, `TheTokensValueStaysInItsAssembly`,
  `TheCodecAndTheRelayNeverLog`, `TheKeyLinesTakeNoToken`, `TheKeyLinesAreFilledFromNoPayload` and the still-stubbed
  `TheWristKeepsATokenNoLongerThanThePhone`. The probes were removed.
- **Second red (during green, the failure line taking the exception rather than a string, so no message can reach it):**
  `KeyHandoffTests` 35 tests, 3 failed (`TokenNeverLogged`, `AnOutcomeIsAWord`, `AFailureIsItsTypeOnly`) with `NotSent`
  stubbed; the three-row `AFailureThatIsNoTypeNameIsNotWritten` it replaced had been in the first red (so green has 724:
  718, 8 rows more, 2 fewer).

## Green

- **`Watch.Tests`: 724 passed, 0 failed. `RepoGuards.Tests`: 439 passed, 0 failed** (`NoKeysTests` and
  `InventedDataTests` over the new files included). **`FocusPhone/Codex.Tests`: 3309 passed, 0 failed.
  `Storyworthtelling.Updates.Tests`: 193 passed, 0 failed** (no workflow filter needed a change: every workflow that
  builds `Watch.Domain` already watches `Watch/Watch.Domain/**`, and `log-loop-guard.yml`, which runs RepoGuards, has no
  path filter). `FaceStudio.Tests`: 29 passed. `leakscan --ci` over the chunk's diff: "no personal-data shapes in the
  added lines".
- **Compiled here** (`-p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`): `Watch/Watch.Ui.Wear`,
  `Watch/Point.Installer` and `FocusPhone/Post.Android`, each **Build succeeded, 0 errors**. No head's own file changed;
  they were built because each compiles `Watch.Domain`.

## Decisions made in this chunk (each the owner's to overturn)

- **The vault passphrase stays off the watch until the owner rules** (above). Nothing in B3 asks for it, stores it or
  sends it, and the guard holds both ends.
- **No Gmail token:** Dash reads mail only as Post's relayed lines (A11, B1), so the narrowest is none.
- **Only a read-only Drive token crosses.** Score's own token is full `drive` (it writes the taste summary back); the
  phone should ask Google for a `drive.readonly` token to hand over. The cost, unverified: a token type the app has not
  asked for before may bring one more consent screen on the phone (Android keys a consent by the token-type string).
- **Asked for, not pushed.** The plan has each phone app hand its token over hourly while the phone is on; here the watch
  app asks when it needs one and the phone answers once, on the node that asked, as Post's answers already go (B2). A
  token is on the wrist only while something there uses it. The cost: the first stream after the watch app starts waits
  for one round trip. With the phone off neither works (A67).
- **Memory only.** The plan also keeps the token in Keystore-encrypted storage; here it is never written down, and a
  watch app that restarts asks again. Within its life of at most 45 minutes a stored copy saves one round trip and
  leaves a token at rest.
- **The life is the phone's reuse window, carried as a duration:** at most 45 minutes, so the wrist never uses a token
  longer than the phone would, and no clock agreement is needed. Renew 5 minutes before, asks a minute apart, 4 KB a
  message: chosen, not measured.
- **W-4.1.2 lands as the contract only.** The phone's answer (Score) and the watch's ask and hold (Wave) are Android code
  with nothing to use them until W-6.1.2, so they are release C's, where they can be exercised. B3 adds no Data Layer
  call, so there is no wait to bound.

## Not done, not verified

- **W-5.1.8 (Dash's first-run passphrase screen) is blocked** on the owner's answer about A38, and the plan's
  `PassphraseKeptInKeystoreOnly` is not written. The plan (§2.4, §4's Keystore alias, W-4.1.2, W-5.1.8), `WATCH-DESIGN.md`
  §11 and the screens mockup still describe the passphrase on the watch and the hourly Gmail token.
- **Nothing asks and nothing answers yet:** no head sends or receives on `/keys/google`. Score's half (fetching a
  read-only token, answering an ask, `sign-in` when Google wants consent) and Wave's (asking, holding, `Forget` on a 401,
  the one `BearerHeader()` reader) come with W-6.1.2.
- **Unverified:** whether AccountManager hands Score a `drive.readonly` token beside its `drive` consent without another
  screen, Google's token lifetime on the watch's clock, and a keys message's round trip over Bluetooth or the cloud relay.

### Owner's ruling (27 Nov 2026): the watch never holds the vault passphrase

Asked in B3's report, answered by the owner: keep the passphrase off the watch. A38 is retired, and with it
W-5.1.8 (Dash's first-run passphrase screen) and the plan's `PassphraseKeptInKeystoreOnly`;
`TheWatchHoldsNoVaultTests` is the standing guard. The plan (§0.3, §2.4, W-4.1.2, W-5.1.8) and `WATCH-DESIGN.md`
are updated; the retired mockup screen in `design/screens-r1.html` is left as drawn, superseded by this ruling.

# Chunk B4 · Dash on the watch: People waiting, a conversation, a reply (W-5.1.1, W-5.1.2)

Every figure below is from a real run on 27 Nov 2026. As before, each red run was taken with the tests written and the
new code present only as stubs: methods threw `NotImplementedException`, new constants were empty strings or zero, and
the two new `DashOutcome` members existed so the tests compiled. (A first attempt at B4 was lost with its container;
nothing of it survived, and this chunk started clean.)

## What it is

**The pure half, in `Watch.Domain`** (BCL only, tested in `Watch.Tests`):
- `Dash/PeopleWaiting`: Post's `DashPeople` as rows (`PersonRow`: key, name, line, when, unread, group), newest first
  whatever order they crossed in, ties in Post's order.
- `Dash/Conversation`: a `DashThread` as `Bubble`s, oldest first, the owner's marked `Mine`, a group's senders named
  (never in a person's conversation), a message with no words said by what it is ("Call", "Voicemail", "Photo",
  "Video", "Attachment"); `ReadFor` (the `DashRead` through the newest message drawn, none for an empty conversation);
  `ReplyLabel` (Reply, or Reply to all in a group); and the replies sent here (`SentHere`), shown at once and dropped
  once Post's answer carries the owner's own text with the same words stamped no more than `ClockSlack` (2 min) before
  the wrist sent it.
- `Dash/DashExchange`: what an ask waits for and what an answer is for (`DashTicket`: the path, and a conversation's key
  or a send's id), so People waiting is answered by people, a conversation by its own key and a send by its own name; a
  read, a file, a delete, an undo and a note wait for nothing. `DashWaits`: find the phone 5 s, hand it a message 10 s,
  an answer 20 s, a send's answer 80 s (Post's 60 s turn and its 15 s answer wait, B2). Chosen, not measured.
- `Dash/SendAttempt`: a reply into a correspondence, named `send-` and 32 hex digits by the watch; `Check` (words, at
  most 4,000 characters); `Again`: with no answer the same name goes again, since Post answers a send it has seen from
  its book and never sends it twice (B2); once Post has answered, trying again is a new send.
- `Dash/RelayNodes`: the phone to ask, near before through the cloud, the same one each time among equals.
- `Dash/DashWords`: the fixed words (status sentences, the four troubles, the send outcomes with success silent, the
  mockups' labels, "To Lee", and a row as TalkBack reads it). No app is named (A48), so the phone is "your phone".
- `Dash/DashLayout`: the mockups' fractions of the radius (head, rows, bubbles, pills, the reply's choices), each
  shown to sit inside the round screen.
- `Kit/WhenWords`: the watch's relative times as the mockups write them ("just now", "2 min", "3 h", "Yesterday", the
  weekday, "Nov 3"), in the watch's zone.
- `Design/WatchContrast`: the WCAG ratio, and a pill's words in whichever of the near-black and the ink reads better on
  its glass. `WatchTokens` gained `Neutral.Card`, `Neutral.OnGlass`, `Light.Mine` (0.35) and `Icon` (the rim, its
  stroke, the glow's alpha). `DashOutcome` gained `NoPhone` and `NoAnswer`, the wrist's own outcomes; `WatchIdentity`
  gained Dash's three registered names (`DashComponents`).

**The kit (`Watch.Ui.Wear`):** `WatchPaints` is public (an app's own views paint the same way) and gained `SizeOf`,
`Words` (a `TextPaint` in Plex Mono at a token size) and `Rounded`; `WatchFonts` reads IBM Plex Mono from the app's
`assets/fonts/`, falling back to the family by name for an app that carries none (the installer); `WatchTextView` takes
a `Weight` and a `Body` (a person's own words: as many lines as they take, never cut, not held to the one-sentence rule,
since they are not the watch's copy); `WatchPillView` (the pinned action: a capsule of the app's glass, its words in the
better ink, at least the mockup's height, the view a whole touch target, quiet when disabled); `WatchTimeView` (the
12-hour time at the top, A37).

**Dash, `Watch/Dash.Wear`** (Post's package, PC1; band 11; android-arm and android-arm64; no permissions of its own;
not standalone):
- `PeopleActivity` (registered `…communication.dash.PeopleActivity`, the one launcher entry): the time and Dash's glyph,
  "Finding who’s waiting." until the first answer, the rows (`PersonRowView`, drawn on the card: the name in the ink when
  unread and the grey when not, when it came, the line, the unread rule), the faint glyph alone when nobody waits, and
  a quiet line over the last rows when the phone cannot be asked again. Asked each time the screen comes back.
- `ConversationActivity` (`…dash.ConversationActivity`): the name, a group's members, "Opening the conversation.",
  the bubbles (the owner's in Dash's glass at 0.35 on the right, everyone else's on the card on the left), Reply (or
  Reply to all) pinned at the foot. Once drawn it tells Post `dash/read` through the newest message drawn, once per newer
  message, and adds a sent reply at once.
- `ReplyActivity` (`…dash.ReplyActivity`): opens on the system's dictation (`RecognizerIntent`, prompted "To Lee");
  the words are read back under "To Lee" with Send pinned and Keyboard (the system's wearable input screen) and Again
  either side; "Sending." while Post carries it; when it went the screen goes and the conversation has the text; when
  it did not, the quiet line says why ("Your phone can’t send a text right now.", "There’s nobody to send this to.",
  "The text didn’t go.", "Your phone didn’t answer.", "Your phone isn’t in reach.") and the words stay for Send again.
  Words and the send's name survive the screen being rebuilt.
- `PostLink`: finds Post by `post-relay` (reachable, through `RelayNodes`), sends with `DashCodec`, and listens only
  while an ask waits, registering before the ask goes; matches each answer by `DashExchange`; a refused answer settles
  every ask on its path as unreadable. Every Data Layer call is bounded (`BoundedAsync`, `WaitAsync`), every ask and
  tell runs on the pool, and the answer returns to the screen only if it is still there.
- `DashLog`: one tag; `DashLogLines` lines or plain literals; a failure by its type and frames. `SystemInput`: the two
  system intents (the wearable input action and extra written out as the platform reads them, not a package).
- The icon: FaceStudio's `DashIcon` draws the kit's dash in Dash's glass at 4:20 pm (the mockups' hour) on the black
  disc with its rim, at the five launcher densities, into `Watch/Dash.Wear/Resources/mipmap-*`; committed, held to the
  tool by `DashIconTests`, and checked by the workflow's `git diff` beside the face.

**CI:** `build-watch.yml`'s `WATCH_HEADS` has Dash's line, the upload and the publish carry its folder, the face step's
`git diff` covers Dash's icon, and the FONT CHECK reads Dash's three Plex Mono assets back byte for byte (and fails if
Dash carries none). `SuiteVersionBands` records Dash at band 11 as a watch APK beside Post's phone band.

## Red 1 · the pure half (Watch.Domain)

- **`Watch.Tests`: 822 tests, 95 failed, 727 passed** (B3's 724 and three that pass on the stubs: `TheWristSaysWhatWentMissing`,
  whose two members had to exist to compile and whose shape is B1's `Outcome`, and `ThePillsSitInsideTheCircle` and
  `TheQuieterChoicesSitInsideTheCircle`, which the zero stubs pass by putting every point at (1, 0), on the circle's
  edge). The 95: `ConversationTests` 19, `DashExchangeTests` 18, `SendAttemptTests` 12, `WhenWordsTests` 12,
  `DashWordsTests` 9, `NeutralTokensTests` 6, `PeopleWaitingTests` 6, `RelayNodesTests` 6, `DashLayoutTests` 6,
  `WatchInvariantsTests.DashsNamesAreFrozen` 1. First errors: `NotImplementedException` 82, `Assert.True() Failure` 4,
  `Strings differ` 2, "A box at (0, 0) of 0 by 0 runs off the round screen" 2, `Values are not within 9 decimal places` 2,
  and one each of `Assert.Throws() Failure: Exception type was not an exact match`, `Assert.InRange() Failure` and
  `Values differ`.
- **On the way to green:** 822 with 1 failed, `AnOlderTextWithTheSameWordsIsNotTheOneSentHere`: a reply sent here was
  matched to the owner's own text five minutes older, since the slack for the phone's clock was five minutes and the
  match included its edge. The slack is now two minutes (`Conversation.ClockSlack`).
- **Green: `Watch.Tests` 822 passed, 0 failed.**

## Red 2 · the head's guards, the kit, the workflow, the band table and the icon

Written with no `Watch/Dash.Wear` folder, no `WatchPillView` in the kit, Dash's line missing from the band table and
the workflow, and FaceStudio's `DashIcon` a stub.

- **`Watch.Tests`: 843 tests, 23 failed, 820 passed.** The 23: `WatchInvariantsTests` 21 (the Dash head's guards, and
  `TheWatchWorkflowChecksEveryFrozenPackage` asking for Dash's line in `WATCH_HEADS`) and `WatchA11yTests` 2
  (`TheGuardSeesTheKit`, no `WatchPillView`; `TheGuardSeesDash`). First errors: `Values differ` 5, "Watch/Dash.Wear has
  no sources." 4, "…AndroidManifest.xml isn’t there." 3, "…Dash.Wear.csproj isn’t there." 3, `Assert.Single()` 3,
  `Assert.Matches()` 1, `Assert.All()` 2, "Dash has no mdpi icon." 1, `Assert.NotEmpty()` 1.
- **`FaceStudio.Tests`: 38 tests, 9 failed, 29 passed:** `DashIconTests`, all nine, `NotImplementedException`.
- **`RepoGuards.Tests`: 452 tests, 4 failed:** `TheWristLogsOnlyDashLines` and `TheWristsLogWritesTheLineAndTheShapeOnly`
  (`DirectoryNotFoundException`), `TheWatchHoldsNoVaultTests.ThePassphraseNeverReachesTheWatch` (it now asks to see
  Dash's project), and `NoKeysTests.NoKeyShapedLiteralInTheRepository`, which was a real finding against this chunk's own
  test: `SendAttemptTests` named its two Guids by 32-digit hex literals. They are now made from counted bytes. The wrist
  rule's own self-tests (11 rows) pass, being pure.
- **`Storyworthtelling.Updates.Tests`: 194 tests, 4 failed:** `MainBuildsEveryWatchAppAndTheFace` (no `git diff` over
  Dash's icon), `TheWatchChecksAreFatalAndComeBeforeAnyApkLeaves` (no check of Dash's font assets),
  `SuiteVersionBandsTests.EveryWatchApkIsInTheWatchBand` and `DashKeepsPostsPhoneBand`.
- **Probes, once green:** a `Probe.cs` planted in `Watch/Dash.Wear` with `Android.Util.Log.Warn("Dash", words)`, a
  `"vault"` literal and a tappable `ProbeView` with no description failed `TheWristLogsOnlyDashLines`,
  `ThePassphraseNeverReachesTheWatch` and three `WatchA11yTests` (`EveryViewDescribesItself`, `EveryViewSaysWhatItIs`,
  `ATappableViewCanBeTappedByTalkBack`), each naming the probe; with the platform call taken out,
  `TheWristLogsOnlyDashLines` named `DashLog.Info($"DASH sent {words}")` and `DashLog.Quiet(words, e)`. The probe was
  removed.

## Green

- **On the way:** two existing guards went red as the head landed, each a deliberate edit:
  `VersionCodeTests.NoTwoAppsShareABandUnlessTheyShareAWorkflow` (194, 1 failed) records band 11's members and now
  names Dash, and `Codex.Tests` `FrozenInvariantsTests.CommunicationCsprojDeclaresFrozenApplicationId` (3309, 1 failed)
  allowed one csproj Post's package; it now allows the phone's one and `Watch/Dash.Wear` on the watch, as
  `LauncherCsprojDeclaresFrozenApplicationId` allows the installer. Building the head first failed on three `CS1503`/
  `CS0019` (a helper named `Height` hid `View.Height`); renamed `LineOf`.
- **`Watch.Tests`: 843 passed, 0 failed. `Tools/FaceStudio.Tests`: 38 passed. `FocusPhone/Codex.Tests`: 3309 passed.
  `RepoGuards.Tests`: 452 passed. `Storyworthtelling.Updates.Tests`: 194 passed.** `leakscan --ci` over the chunk's diff:
  "no personal-data shapes in the added lines".
- **Compiled here** (`-p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`): `Watch/Dash.Wear`,
  `Watch/Watch.Ui.Wear` and `Watch/Point.Installer`, each **Build succeeded, 0 warnings, 0 errors** (the Wearable aars
  fetched from dl.google.com). Dash's debug APK, read back with aapt2 and unzip: package
  `com.storyworthtelling.focusphone.communication`, `uses-feature android.hardware.type.watch`, the launchable activity
  `…communication.dash.PeopleActivity`, an icon at all five densities, `lib/armeabi-v7a/` and `lib/arm64-v8a/`, and
  `assets/fonts/IBMPlexMono-{Regular,Medium,SemiBold}.ttf`. Its permissions are none of its own: the debug build adds
  INTERNET for the debugger, and AndroidX's manifest adds its `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`, as on the
  installer.
- **After review** (one more commit): the wrist now waits, bounded, for its listener to be registered before it sends an
  ask (an answer can come back at once); a screen opened with no correspondence clears its key so it asks nothing
  after finishing; the theme is `Theme.DeviceDefault.NoActionBar`; the scroll takes the crown's focus as soon as the
  screen is built. `EveryDashDataLayerWaitIsBounded`'s file now also asks for the bounded registration. Re-run:
  `Watch.Tests` 843 passed, `RepoGuards.Tests` 452 passed (`FixedWordingTests` caught a log literal with a straight
  apostrophe and "was not"; it is curled and contracted), Dash built again with 0 warnings and 0 errors, and
  `check-dependency-graph.py Watch/Dash.Wear` checked its graph and found nothing missing.

## Decisions made in this chunk (each the owner's to overturn)

- **Relative times are the mockups' words** ("2 min", "1 h", "Yesterday"), not WD §5's example ("5 min ago") nor the
  phones' ("2m ago"); the watch's own `WhenWords`, since the watch shares no copy with Paper & Ink (A33).
- **A pill's words take the ink on Dash's glass.** The mockup labels its pills near-black, which on Dash's navy-to-maroon
  glass is about 2:1; WD §12 keeps words at 4.5:1, so the label takes whichever of the near-black and the ink reads
  better at that hour (the ink, for Dash, all day). This differs visibly from the mockup.
- **The unread rule is drawn,** as the approved People waiting mockup draws it, though WD §1 says "no unread marks"; it
  is a rule in the app's colour, not a count or a badge.
- **Dash listens only while it waits.** No `WearableListenerService` and no registered listener name: an answer for a
  screen that has gone is dropped, and the screen asks again when it comes back. A send whose answer comes after the
  owner left the reply is not told to them.
- **A reply opens straight into dictation** (the mockup's "Replying by voice"); Keyboard opens the system's wearable
  input screen, not a keyboard directly (the keyboard is one choice there). With no dictation on the watch it goes to
  the keyboard; with neither, a quiet line.
- **A send with no answer keeps its name** (Post answers it from its book, B2); after an answer it is a new send.
- **A reply sent here is kept for the process only,** until Post's copy of it comes, matched by the same words within
  two minutes of the wrist's clock.
- **A read is told once the conversation is drawn,** through its newest message, as A14 asks.
- **Group conversations say "Reply to all" and name their senders** (both already on the wire); the photos of
  W-5.1.3 are not shown.
- **The icon's colour is Dash's at 4:20 pm,** the hour the mockups were approved at, since an icon cannot follow the
  day; FaceStudio, the watch's generator, draws it rather than IconStudio (Paper & Ink's).
- **No Mail entry on People waiting** yet: the mail lines are W-5.1.6.

## Not done, not verified

- **Nothing here has met a watch.** Unverified: Play services finding `post-relay` and delivering both ways between
  Dash and Post (same package, same signature), the live listener registering in time, `RecognizerIntent` and the
  `android.support.wearable.input.action.REMOTE_INPUT` screen on Wear OS 6 (the action is the one androidx.wear.input
  writes; that the platform still answers it is unchecked), the theme's swipe-to-dismiss, the crown scrolling a focused
  `ScrollView`, Plex Mono loading from assets, `TextClock` at the top, the layout on the 41 mm and 45 mm screens, the
  rows and bubbles against the mockups on a real round screen, TalkBack, and the first `build-watch.yml` run with Dash
  (signing, the FONT CHECK's new branch, the publish of `…communication-Signed.watch.apk`). Flyleaf will send Dash to the
  installer once it is on `watch-latest`; the first install asks once on the wrist.
- **W-5.1.2 is replies only.** New texts "from the Rolodex or by voice" need the Rolodex from Post (PC6), and no Dash
  contract carries it yet (`dash/send` already takes numbers); that is a contract to add, with the owner, before the
  screen.
- **The waits are chosen, not measured** (5 s, 10 s, 20 s, 80 s), as is the two-minute clock slack.
- **Not started from release B:** W-5.1.3's photos, W-5.1.4 (filing and deleting with the undo ring), W-5.1.5 (the call
  note), W-5.1.6 (mail lines), W-5.1.7 (the tile), W-5.1.9 (overnight callers); W-5.1.8 is retired. No buzz yet: the
  haptic plan (W-1.3.1) is not wired to anything.

## Chunk B4 totals

- `Watch.Tests`: **843 passed, 0 failed.** `Tools/FaceStudio.Tests`: 38 passed. `FocusPhone/Codex.Tests`: 3309 passed.
  `RepoGuards.Tests`: 452 passed. `Storyworthtelling.Updates.Tests`: 194 passed.
- `Watch/Dash.Wear`, `Watch/Watch.Ui.Wear` and `Watch/Point.Installer` compile here with 0 warnings and 0 errors.

# Chunk B5 · filing and deleting, undo-first; group texts and photos (W-5.1.4, W-5.1.3)

Every figure below is from a real run on 27 Nov 2026. As before, each red run was taken with the tests written and the
new code present only as stubs: methods threw `NotImplementedException`, new constants were empty strings or zero.

## What it is

**Filing and deleting, undo-first (W-5.1.4).**
- A long press on a People waiting row opens `ActActivity` (registered `…communication.dash.ActActivity`,
  `WatchIdentity.DashActActivityName`) on the choice: the time and Dash's glyph, the name, **File** pinned and
  **Delete** under it, on After the call's layout (no mockup draws this screen). TalkBack names the row's long press
  ("File or delete").
- A choice is the act, with no confirmation (WD §9): `WristAct` names it `act-` and 32 hex digits, takes the watch's
  own undo window (`UndoRingView.WindowFor`, at most the wire's two minutes), and `ActCarrier` hands Post the
  `dash/file` or `dash/delete` for the whole correspondence on the pool. The screen becomes the mockup's Filed, with
  Undo: the glyph and its emptying ring at 0.62 of the radius (`UndoRingView.GlyphCentre`, new in the kit), "Lee filed"
  or "Lee deleted", and "Tap to undo" in Dash's colour where it reads at 4.5:1 and in the ink where it does not
  (`WatchContrast.Accent`; for Dash, all day). A tap anywhere takes it back: the undo goes once the phone has the act,
  so it never arrives first. When the ring empties, or the screen is left, the act is final and the screen goes, and
  People waiting drops the row at once.
- Nothing is claimed that did not happen: an act that never reached the phone leaves the row and says why in the quiet
  line ("Your phone isn’t in reach."); an undo that never reached it says "The undo didn’t reach your phone." and the
  act stands. The act and its undo are carried on the pool whether the screen is still there or not.
- **Post's half:** an undo heard before its act is kept in the book as taken back (and saved), so the act that follows
  is `Again` and never done, across a restart too.

**Group texts and photos (W-5.1.3).**
- `dash/photo` (`WatchIdentity.DashPhotoPath`, the tenth path, frozen and in Post's listener): the wrist asks
  `{v, kind: ask, id}` with the attachment id B1 already lists; Post answers `{v, kind: photo, id, jpeg}`, the JPEG as
  base64, at most `DashLimits.PhotoBytes` (40 KB, so the message stays under the 64 KB budget). The codec refuses a
  photo that is not a JPEG by its first bytes, is past its budget, or carries another id.
- Post finds the photo among the conversations the wrist is shown (never a video or file, a mail's picture, or anything
  filed), fetches it through `AttachmentCache.Bytes`, the one fetch the Mailbox opens pictures with (Google Messages'
  bridge, or the phone's own MMS store), and `DashPhotos.Shrink` decodes it no larger than needed, stands it upright by
  its EXIF orientation, scales it so its shorter side is at most 240 px and its longer 640 (`PhotoFit`), and writes a
  JPEG at the first quality from 80 down to 40 that fits, halving the size if none does. Written again, never passed
  on, so the picture's own metadata stays on the phone; held in memory only. One that will not fetch or fit is not
  answered (`DASH dash/photo: Unfetched`), and the wrist's 30 s wait says so. A picture's stand-in words ("Photo",
  "Photo +1") no longer cross on a conversation; the row's line still says "Photo".
- On the wrist, each photo is drawn in the mockup's picture (1.0 by 0.6 of the radius, its corners at 0.06, filled about
  its centre and never stretched) with "Robin · 1 h" under it (who, in a group, and when); its words, if it has any of
  its own, follow in a bubble. Until it comes the card says "Opening the photo.", and one that did not come says why.
  The conversation asks one photo at a time, newest first, and holds a dozen for the process (`HeldPhotos`), so a
  conversation opened again shows them at once. A group's Reply to all sits at 1.52 of the radius, where the mockup
  draws it, under the caption (B4 had it at the person's 1.62).

## Owner's ruling (27 Nov 2026): People waiting draws no unread mark

B4 drew the approved mockup's rule in Dash's colour down an unread row's edge, against WD §1's "no unread marks", and
asked. The owner's answer: the design doc wins. The rule is gone from `PersonRowView`, `DashLayout` has no `UnreadRule`
or `UnreadInset` (the gap between a row's name and its line is `RowGap`, the same 0.014), and `design/screens-r1.html`
no longer draws it (its row keeps the name in the ink when unread and the grey when not; its caption cites the ruling).

- **Red:** `Watch.Tests` **845 tests, 2 failed, 843 passed**: `WatchInvariantsTests.PeopleWaitingDrawsNoUnreadMark`
  (`Assert.DoesNotContain() Failure: Filter matched in collection`, `Double UnreadRule`) and `TheMockupDrawsNoUnreadMark`
  (`Sub-string found`, the row's `fillRect(`).
- **Green:** `Watch.Tests` **845 passed, 0 failed.**
- **Left as it was, for the owner to say:** the name of an unread row is still in the ink and a read one in the grey (the
  mockup draws both, and the ruling named the mark), and TalkBack still says "new" for an unread row, as the ink does.

## Red 1 · the pure half (Watch.Domain)

Tests written first: `Dash/DashUndoTests` (24 with rows: the act goes at once under the watch's name with the wrist's
window; an undo inside it takes back that act, once; an undo tapped before the phone has the act waits for it; an act
that never reached the phone, or an undo that never did, is said, and nothing is claimed), `Dash/PhotoFitTests` (29),
`Dash/HeldPhotosTests` (5), photo rows in `DashCodecTests` (`APhotoKeepsItsBytes`, `APhotoIsItsIdAndItsJpeg`,
`APhotoPastItsBudgetIsUnreadable`, `AFullPhotoFitsTheBudget`, nine malformed rows, two wrong-path rows, the frozen words
and limits) and in `DashFixtures` (so every round-trip, `v` and log-line theory takes them), `ConversationTests` (seven:
a photo drawn as the mockup draws it, with or without words of its own, newest first, equal by content),
`DashExchangeTests` (two), `DashWordsTests` (three), `DashLayoutTests` (four), and `WatchInvariantsTests`
(`DashPathsAreFrozen` with `/dash/photo`, `DashsNamesAreFrozen` with the act screen). `Codex.Tests`'
`DashRelayGuardTests.PostRegistersItsListenerByName` asks for the tenth path.

- **`Watch.Tests`: 941 tests, 98 failed, 843 passed.** The 98: `PhotoFitTests` 29, `DashUndoTests` 24, `DashCodecTests`
  12, `ConversationTests` 7, `DashWordsTests` 7, `HeldPhotosTests` 5, `DashLayoutTests` 5, `DashExchangeTests` 4,
  `WatchInvariantsTests` 3, `DashLogLinesTests` 2 (the stub's empty path counted as one of Dash's). First errors:
  `NotImplementedException` 62, `Assert.Throws() Failure: Exception type was not an exact match` 9, `Values differ` 5,
  `Collections differ` 4, `Assert.False() Failure` 3, `ArgumentException: Destination array was not long enough` 2,
  `Strings differ` 2, `Assert.All() Failure` 5, and one each of `Nullable object must have a value`,
  `AggregateException`, `Assert.True() Failure`, `Assert.NotEqual() Failure`, `not within 9 decimal places` and
  "A box at (0.55, -0.1) of 0.9 by 0.2 runs off the round screen".
- **`FocusPhone/Codex.Tests`: 3309 tests, 2 failed, 3307 passed:** `TheDashPathsAreNotCopied` (the stub's empty path)
  and `PostRegistersItsListenerByName` ("DashPhotoPath" not found).
- **Green: `Watch.Tests` 941 passed, 0 failed; `Codex.Tests` 3309 passed** (Post's listener hears `/dash/photo`; until
  the next step it does nothing with it); `RepoGuards.Tests` 452 passed; `Storyworthtelling.Updates.Tests` 194 passed.

## Red 2 · Post's half: the photo, and an undo that comes first

Tests written first, in `FocusPhone/Codex.Tests`: `DashRelayTests` (`APhotoIsFoundByItsId`, `AVideoIsNoPhoto`,
`AFiledPhotoIsNotFound`, `AMailsPictureIsNotAPhotoOnTheWrist`, `APhotosStandInIsNoWordsOnTheWire` ×2,
`APhotosOwnWordsCross`), `DashInboxTests` (`APhotoIsFetchedForTheWrist`, `AnUnknownPhotoIsNotAnswered`,
`AShrunkPhotoIsTheAnswer`, `APhotoThatWouldNotShrinkIsNotAnswered`, `AnUndoHeardBeforeItsActKeepsItFromHappening`, and
the photo row in `AnAnswerArrivingAtPostIsLeftAlone`), `DashActsCodecTests.AnEarlyUndoIsKeptAcrossARestart`, and
`DashRelayGuardTests` (`TheListenerCarriesThePlansThroughPostsOwnCode` ×6 more: `AttachmentCache.Bytes(`,
`DashInbox.Photo(`, `PhotoFit.SampleFor(`, `PhotoFit.Sizes(`, `PhotoFit.TurnFor(`, `PhotoFit.Qualities`;
`TheWristsPhotoIsFetchedAsTheMailboxOpensOne`; `APhotoLeavesThePhoneShrunkAndReEncoded`).

- **`FocusPhone/Codex.Tests`: 3331 tests, 20 failed, 3311 passed.** The 20: `DashRelayGuardTests` 8, `DashRelayTests` 6,
  `DashInboxTests` 5, `DashActsCodecTests` 1. First errors: `NotImplementedException` 8, `Assert.Contains() Failure:
  Sub-string not found` 7, `Strings differ` 2 (the stand-in still crossing as "Photo +1"), and one each of
  `Sequence contains no matching element` (no `DashPhotos.cs`), `Assert.True() Failure` (no `Bytes` in
  `AttachmentCache`) and `Values differ` (a late act carried out after its undo). `APhotosOwnWordsCross` passed on the
  stubs: a message's own words already crossed.
- **Green: `Codex.Tests` 3331 passed, 0 failed. `Watch.Tests` 941 passed. `RepoGuards.Tests` 452 passed** (the
  listener's new log calls are `DashLogLines` lines and plain literals). **Compiled here:** `FocusPhone/Post.Android`,
  **Build succeeded, 0 errors**; none of the 65 warnings names a file this step touched.

## Red 3 · Dash on the watch: the act screen, the Undo ring, photos

Tests written first, with no `ActActivity`, `ActCarrier` or `PhotoView` in `Watch/Dash.Wear`, the kit's
`UndoRingView` unchanged, and `WatchContrast.Accent` a stub: in `WatchInvariantsTests`,
`EachDashScreenIsRegisteredByItsName` (the act screen's row), `DashWaitsOffTheMainThread` (four screens),
`DashFilesAndDeletesUndoFirst`, `AnUndoIsToldEvenWhenTheScreenHasGone`, `ARowNamesItsLongPress`,
`TheUndoRingOpensForTheActsWindow`, `DashShowsPhotosAsTheMockupDrawsThem`; `WatchA11yTests.TheGuardSeesDash` (with
`PhotoView`); and `Design/AccentTests` (5 with rows: words in an app's colour keep 4.5:1 on black, or take the ink).

- **`Watch.Tests`: 952 tests, 13 failed, 939 passed.** The 13: `WatchInvariantsTests` 7, `AccentTests` 5,
  `WatchA11yTests` 1. First errors: `NotImplementedException` 5, "Watch/Dash.Wear/ActActivity.cs isn’t there." 2,
  "Watch/Dash.Wear/PhotoView.cs isn’t there." 1, `Values differ` 2 (three screens, not four), `Sub-string not found` 2,
  `Assert.All() Failure: 1 out of 2 items` (no `PhotoView`).
- **On the way:** two of the new guards were changed after the red run, each to read the code where it belongs rather
  than to pass: `DashFilesAndDeletesUndoFirst` asked the act screen for `.Told(` and `.UndoTold(`, which are
  `ActCarrier`'s (held by `AnUndoIsToldEvenWhenTheScreenHasGone`, rewritten to read the carrier, since
  `DashWaitsOffTheMainThread` wants every tell from a screen on one `Task.Run(` line); it now asks the screen for
  `.Undo(`. `DashShowsPhotosAsTheMockupDrawsThem` then read 952 with 1 failed (`DashWords.Photo(` not in `PhotoView`):
  the view now says whose photo it is itself.
- **Green: `Watch.Tests` 952 passed, 0 failed.** `Tools/FaceStudio.Tests` 38 passed. `FocusPhone/Codex.Tests` 3331
  passed. `RepoGuards.Tests` 452 passed (the new screens log only `DashLogLines` lines and plain literals, and every
  copy literal is curled and contracted). `Storyworthtelling.Updates.Tests` 194 passed. `leakscan --ci` over the
  chunk's diff: "no personal-data shapes in the added lines".
- **Compiled here** (`-p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false`): `Watch/Dash.Wear` and
  `Watch/Watch.Ui.Wear`, each **Build succeeded, 0 warnings, 0 errors**; `FocusPhone/Post.Android` **Build succeeded,
  0 errors** (65 warnings, none in a file this chunk touched).

## Decisions made in this chunk (each the owner's to overturn)

- **The way in to filing is a long press on a People waiting row,** opening a choice screen no mockup draws, laid out
  as After the call: File pinned, Delete under it. The conversation keeps Reply as its one action, as drawn. Only a
  whole correspondence is filed or deleted from the wrist; the contract also takes single messages, and nothing on the
  wrist asks for that yet.
- **"Tap to undo" is in the ink, not Dash's colour,** whenever Dash's colour reads under 4.5:1 on black, which for
  Dash's navy-to-maroon range is all day: the mockup draws it in Dash's colour (B4 made the same call for the pills).
- **An undo is told, not answered,** as B1 and B2 built it: if the undo lands after Post's hold has closed (the
  wrist's window, or the phone's if longer, plus 10 s), Post says `TooLate` in its own log and the act stands, while
  the wrist believes it took it back. An answered undo would need a new answer kind on `dash/undo`.
- **An undo heard before its act is kept,** so the act that follows is never done (Post's book). Two messages over the
  Data Layer can cross; the wrist only sends the undo once the act was handed over, but that is not proof it landed.
- **A row whose act stands leaves People waiting at once,** before Post is asked again, so a filed row never flashes
  back; an act that failed, or was taken back, leaves the row where it was.
- **Photos cross as one Data Layer message each** (base64 in the same JSON codec), not over `ChannelClient` or as an
  asset: the narrowest addition. 40 KB of JPEG, 240 px on the shorter side, the five qualities and the 30 s wait are
  chosen, not measured.
- **A photo is cropped to the mockup's 5:3 box,** so a tall photo loses its top and foot. There is no tap to see the
  whole picture, and videos and files are named ("Video", "Attachment"), not shown.
- **Photos are held in memory for the process only, a dozen at most,** never written to the watch.
- **Owner's ruling folded in:** People waiting draws no unread mark (see the top of this chunk).

## Not done, not verified

- **Nothing here has met a watch or a phone.** Unverified: the long press on a row under Wear OS's own gestures, the
  ring's tap and its window against Android's "Time to take action", the act and undo crossing in order over Bluetooth
  and the cloud relay, the result reaching People waiting when the act screen is swiped away, `BitmapFactory` and
  `ExifInterface` on real MMS and Google Messages pictures, the bridge's `DownloadMedia` answering inside the 30 s wait,
  a 40 KB message crossing as one `MessageClient` message, the cropped picture on the 41 mm and 45 mm screens, and
  TalkBack on all of it.
- **Not built from release B:** new texts from the Rolodex (the next chunk; the owner's answer is the whole Rolodex,
  names and digest ids, searchable on the watch, and nothing here stands in its way), W-5.1.5 (the call note),
  W-5.1.6 (mail lines), W-5.1.7 (the tile), W-5.1.9 (overnight callers). No buzz yet.

## Chunk B5 totals

- `Watch.Tests`: **952 passed, 0 failed.** `Tools/FaceStudio.Tests`: 38 passed. `FocusPhone/Codex.Tests`: 3331 passed.
  `RepoGuards.Tests`: 452 passed. `Storyworthtelling.Updates.Tests`: 194 passed.
- `Watch/Dash.Wear` and `Watch/Watch.Ui.Wear` compile here with 0 warnings and 0 errors; `FocusPhone/Post.Android`
  with 0 errors.

# Chunk B6 · new texts from the Rolodex or by voice (the rest of W-5.1.2)

Every figure below is from a real run on 27 Nov 2026. As before, each red run was taken with the tests written and the
new code present only as stubs: methods threw `NotImplementedException`, new constants were empty strings or zero, and
the new `DashTrouble.Changing` and `DashSend.Contact` existed so the tests compiled. Nothing in this chunk has met a
watch, a phone or the Data Layer.

## What it is

**The owner's ruling, built:** Post sends the whole Rolodex to the watch, each contact its display name and a digest
id, never a number, an address or a photo; the watch searches it (letters typed or dictated through the system's input)
or scrolls it as a plain list; picking a contact and dictating sends a new text, which Post maps back to the number and
sends by the phone's one road.

**The contract (`Watch.Domain/Dash`):**
- `dash/rolodex` (`WatchIdentity.DashRolodexPath`, the eleventh path, frozen, in Post's listener): the wrist asks
  `{v, kind: ask, page}`; Post answers `{v, kind: rolodex, page, pages, edition, contacts: [{id, name}]}`, in Post's
  order (by name), at most `DashLimits.RolodexPage` (200) contacts a page and `RolodexPages` (20) pages, so 4,000
  contacts at most; the fullest page of the longest three-byte names still fits 64 KB. A contact is exactly an id
  (`DashIds.Contact`, `p` and sixteen hex digits of a digest of Post's device contact id) and a name: a contact carrying
  any other field (a number beside a name, say), a page past its count, more than 20 pages, a wrong id or edition shape
  refuses the whole page. `edition` (`DashIds.Edition`, `e` and a digest of every id and name shown) tells pages of one
  state of the book from another. A newer `v` is refused first, as everywhere.
- `dash/send` gains a third target, `contact` (`DashSend.Contact`, an init property so every existing send stands):
  exactly one of a correspondence, numbers or a contact. Post's answer is B1's `result` on the same path.
- `DashExchange` matches a page's answer to its ask by its page number; the wait is the ordinary 20 s.
- `Rolodex`: the contacts, deduplicated by id, and `Find(query)`: every word of the query begins a word of the name,
  whatever the case, accents, apostrophes ("junipers" finds "Juniper’s school") or dictation's punctuation ("Robin."),
  names that begin with the query first, then the rest, each in Post's order; nothing asked is everyone.
- `RolodexGathering.GatherAsync`: page 0, then each in turn; a page of another edition or page count means the book
  changed between pages, so the wrist starts again from page 0, at most `Restarts` (2) times, then says "Your phone’s
  contacts kept changing." (`DashTrouble.Changing`); anything standing in the way of a page stops the asking and is said.
- `HeldRolodex`: the last whole Rolodex, in memory, locked; nothing else.
- `SendAttempt.NewText(contact, words, name)`: a new text named `send-` and 32 hex digits like a reply, its message
  naming the contact; `Again` keeps B4's rule (no answer: the same name again; an answer: a new send).
- `DashWords`: New text, Say a name, Everyone, "Who to?" (the dictation's prompt), "Opening your contacts.", "There’s
  nobody in your phone’s contacts.", and "No one by that name." (the phone's Rolodex says the same). `DashLayout.EntryGap`.

**Post's half:**
- `Codex.Domain/Post/RolodexNumbers`: a contact's numbers in one order: the one the contacts app marks primary
  (super-primary or primary), else a mobile (`Phone.TYPE_MOBILE` or `TYPE_WORK_MOBILE`), else the contacts app's own
  order; a number listed twice is listed once, primary if either is. `ContactPicker.People` now reads each phone row's
  `is_super_primary`, `is_primary` and `data2` and orders a person's phones by it, so the phone's own Rolodex (its Text,
  its Call, the number under a name) and the wrist reach the same first number.
- `Codex.Infrastructure/Dash/RolodexRelay` (pure): the pages and `NumberOf(id)`. A contact is on the wrist only if it
  has a number (the first of its phones that is a number) and a name of its own: the phone names a nameless contact
  after its number or address, so a name with no letter, an `@`, or a run of seven digits or more (with the usual
  separators) is left out. Names are one line, cut to 60 with an ellipsis, without control characters or half a
  surrogate pair. The edition is over what the wrist is shown, so a changed number changes nothing there and is found
  again at send time.
- `DashInbox.Turn` takes the Rolodex beside the Mailbox (`Func<RolodexRelay>`): a page ask reads only the address book
  (a page past the last is not answered, `DASH dash/rolodex: Unknown`); a send naming a contact reads only the address
  book and becomes `DashRelay.NewText(number, …)`: the phone's road to that number (bridge first when it is up, finding
  or starting its conversation; else a plain text), or `Nowhere` for an id Post no longer has. The book's same-id rule
  holds: in the book before it goes, answered from the book if heard again.
- `Post.Android/Dash/DashContacts`: the relay over `ContactPicker.People`; without the contacts permission the wrist is
  shown an empty book, said once in the log. `DashListener` hears `/dash/rolodex` and hands the carrier the Rolodex.
  Logs: `DASH dash/rolodex: Answered 200`, a count.

**Dash on the watch (`Watch/Dash.Wear`; no mockup draws the flow, so it follows the nearest drawn screens and the
mockup file is not edited):**
- People waiting has **New text** at its foot, where the mockup draws Mail: a quieter word in the grey (Mail's weight),
  under the rows as they follow each other, and there with nobody waiting too.
- `RolodexActivity` (registered `…communication.dash.RolodexActivity`): "New text" at the top as Mail and a
  conversation draw their names; the contacts as cards (`ContactRowView`: the name in the ink, a whole touch target,
  TalkBack reads the name as a button) as People waiting's rows; **Say a name** (the system's dictation, prompted "Who
  to?") and **Keyboard** (its input screen) pinned at the foot as Replying by voice draws Keyboard and Again. The
  letters found are read back in the ink with **Everyone** under them. The last Rolodex gathered shows at once and a
  fresh one is gathered on the pool each time the screen opens ("Opening your contacts." until the first); a failure
  keeps what is shown and says why. The list draws 40 at a time and grows as the crown nears the end. The screen keeps
  only the search across a rebuild, never a contact.
- A contact picked opens B4's reply screen on the contact's id ("To Lee", dictation first, Send, Keyboard, Again); when
  it went, both screens go and People waiting is where the owner was; when it did not, the reply screen says why and
  keeps the words, as for a reply.
- `RolodexCarrier`: the gathering, each page through `PostLink` within its bounds, on the pool; `RolodexBook.Held`.

## Tests written first

- `Watch/Watch.Tests/Dash/DashCodecTests.cs`: `ARolodexPageKeepsEveryField`, `AContactIsItsIdAndItsName`,
  `ARolodexAskIsItsPage`, `ANewTextNamesItsContact`, `AReplyNamesNoContact`, `TheFullestRolodexPageFitsTheBudget`,
  `APagePastItsSizeIsUnreadable`, 20 malformed rows (an ask with no page or a page of -1, "0", 1.5 or 20; a page with no
  contacts, 0 pages, its page at its count, 21 pages, no or a wrong edition, a correspondence's id, an empty or missing
  name, a number beside a name, a contact that is a string; a contact beside a correspondence or numbers, a contact that
  is a correspondence's id, a contact with no words), three wrong-path rows, the frozen words and limits; and four
  fixtures in `DashFixtures` (so every round-trip, `v` and log-line theory takes them).
- `DashIdsTests` (`AContactAndAnEditionAreDigestsToo`, `AContactIsNeverACorrespondence`, `AContactsShapeIsChecked` ×8),
  `DashExchangeTests` (`ARolodexPageIsAnsweredOnlyForItsPage`, `ANewTextIsAnsweredByItsId`, two rows),
  `SendAttemptTests` (`ANewTextGoesToItsContact`, `ANewTextIsNamedAsAReplyIs`, `ANewTextNeedsAContact` ×3,
  `ANewTextNeedsWords`, `ANewTextTravelsOnTheSendPath`), `DashWordsTests.TheRolodexsWordsAreItsOwn` and the sentence and
  label lists, `DashLayoutTests.NewTextSitsWhereTheMockupDrawsMail`, `DashLogLinesTests` (a Rolodex and a new text with
  sentinels), and the new `RolodexTests` (29 with rows: the search's twelve, the gathering's ten, the hold's three).
- `WatchInvariantsTests`: `DashPathsAreFrozen` and `DashsNamesAreFrozen` with the new path and screen; then
  `EachDashScreenIsRegisteredByItsName` (the Rolodex's row), `DashWaitsOffTheMainThread` (five screens),
  `DashStartsNewTextsFromTheRolodex`, `TheWristHoldsTheRolodexInMemoryOnly` (nothing in Dash names a store; the screen
  saves only the search) and `TheWristNeverSendsANumber` (no `new DashSend(` or `new DashCallNote(` in Dash: every text
  is a `SendAttempt`); `WatchA11yTests.TheGuardSeesDash` with `ContactRowView`.
- `FocusPhone/Codex.Tests/Post/RolodexNumbersTests.cs` (6), `Dash/RolodexRelayTests.cs` (29 with rows, among them
  `NoNumberCrosses`: every page as it crosses, read for every number in every spelling and for an `@`;
  `ANameThatIsNoNameIsLeftOut` ×8; `TheFullestPageFitsTheBudget`), `DashInboxTests` (`TheRolodexIsAnswered`,
  `APagePastTheLastIsNotAnswered`, `ANewTextGoesToTheContactsNumber`, `ANewTextTriesTheBridgeFirst`,
  `ANewTextToSomeoneUnknownIsNowhere`, `ANewTextWithNothingToCarryItIsNoRoute`, `ANewTextIsSentOnce`, the Rolodex row in
  `AnAnswerArrivingAtPostIsLeftAlone`; every other turn now passes a Rolodex that throws if read), and
  `DashRelayGuardTests` (`PostRegistersItsListenerByName` with the eleventh path,
  `TheListenerCarriesThePlansThroughPostsOwnCode` ×3 more, `TheWristsRolodexIsThePhones`, `TheWristIsShownNoNumberFromTheBook`).

## Red 1 · the contract and the wrist's pure half (Watch.Domain)

- **`Watch.Tests`: 1043 tests, 78 failed, 965 passed.** The 78: `RolodexTests` 29, `DashCodecTests` 18, `DashIdsTests`
  10, `SendAttemptTests` 7, `DashWordsTests` 6, `DashLogLinesTests` 3, `WatchInvariantsTests` 3 (`DashPathsAreFrozen`,
  `DashsNamesAreFrozen`, and B1's `EveryDashPathIsItsOwn` over the stub's empty path), `DashExchangeTests` 1,
  `DashLayoutTests` 1. First errors: `TypeInitializationException` 25 (`RolodexTests`' book is built by the stub's
  constructor), `NotImplementedException` 18, `ArgumentException: … is not a Dash message` 13 (`DashRolodex` 10,
  `DashRolodexAsk` 3), `Assert.Equal() Failure` 7, `Assert.Throws() Failure` 5, `ArgumentOutOfRangeException: Not a
  trouble Dash knows` 5 (B4's sentence and trouble tests meeting `Changing`), `Assert.All() Failure` 2, and one each of
  `Assert.IsType()`, `Assert.NotEqual()` and "new text to a contact: Unreadable". Passing on the stubs: the malformed
  `/dash/rolodex` rows (the stub reads nothing there), the asks no stub answers, and `TheHoldKeepsNothingButMemory`
  (it reads the stub's members, which had the final shape).
- **On the way to green:** 1043 with 3 failed, `DictationsPunctuationIsIgnored` ×3: the test expected "Robin." to find
  Robin alone, but "robin" begins Lee Robinson's "Robinson", as the rule says; the test was wrong, and now expects both.
- **`Codex.Tests` (Post's listener must hear the new path): 3331 tests, 1 failed, 3330 passed**,
  `PostRegistersItsListenerByName` (`DataPath = WatchIdentity.DashRolodexPath` not found). Green once the filter was in.
- **Green: `Watch.Tests` 1043 passed, 0 failed. `Codex.Tests` 3331 passed. `RepoGuards.Tests` 452 passed.**

## Red 2 · Post's half

- **`FocusPhone/Codex.Tests`: 3379 tests, 47 failed, 3332 passed.** The 47: `RolodexRelayTests` 29, `DashInboxTests` 7,
  `RolodexNumbersTests` 6, `DashRelayGuardTests` 5. First errors: `NotImplementedException` 35,
  `InvalidOperationException: The Mailbox was read for a message that needs none.` 5 (a new text went to the Mailbox's
  relay), `Assert.Contains() Failure: Sub-string not found` 4, and one each of `Assert.IsType()` (the Rolodex ask not
  answered), `Sequence contains no matching element` (no `DashContacts.cs`) and `Assert.Equal()` (a page past the last
  had no outcome).
- **On the way:** 3379 with 6 failed: `ANewTextTriesTheBridgeFirst` compared a list inside a tuple, by reference (the
  test was wrong; two asserts now), and the five head guards. `TheWristsRolodexIsThePhones` first asked the picker for
  `RolodexNumbers.IsMobile(`, which `Order` calls itself; it now asks for the columns the picker reads and the
  `ContactNumber` it builds.
- **Green: `Codex.Tests` 3379 passed, 0 failed. `Watch.Tests` 1043 passed. `RepoGuards.Tests` 452 passed. Compiled
  here:** `FocusPhone/Post.Android`, **Build succeeded, 0 errors**; none of the 65 warnings names a file this step touched.

## Red 3 · Dash on the watch

- **`Watch.Tests`: 1047 tests, 5 failed, 1042 passed:** `DashStartsNewTextsFromTheRolodex` and
  `TheWristHoldsTheRolodexInMemoryOnly` ("Watch/Dash.Wear/RolodexActivity.cs isn’t there."),
  `EachDashScreenIsRegisteredByItsName("DashRolodexActivityName")` (`Values differ`, 0 for 1), `DashWaitsOffTheMainThread`
  (four screens, not five), `WatchA11yTests.TheGuardSeesDash` (no `ContactRowView`). `TheWristNeverSendsANumber` passed
  on B5's head, which already sent only through `SendAttempt`.
- **On the way:** red 1's `DashLayout.EntryCentre(rows)` placed New text by the mockup's fractions, which a column of
  rows taller than drawn (each a whole touch target) cannot keep; it is now `EntryGap`, the gap the rows keep between
  them, and the layout test shows a row's height of entry after it under the third row centred at the mockup's 1.7.
- **Probe, once green:** `DashLog.Info($"DASH first contact {…Name}")` in `RolodexCarrier`, `Console.WriteLine(name)` in
  `RolodexRelay` and `HeadLog.Info(Tag, $"DASH reading {context.PackageName}")` in `DashContacts`:
  `DashLogsCarryNoPayloadTests` 43 tests, 3 failed (`TheWristLogsOnlyDashLines`, `TheCodecAndTheRelayNeverLog`,
  `TheListenerLogsOnlyDashLines`), each naming its probe. The probes were removed.
- **Green: `Watch.Tests` 1047 passed, 0 failed.** **Compiled here:** `Watch/Dash.Wear`, **Build succeeded, 0 warnings,
  0 errors**.

## Decisions made in this chunk (each the owner's to overturn)

- **One number per contact, chosen on the phone; the wrist is shown no labels and no choice.** Post's Rolodex keeps no
  labels (`ContactPicker` read numbers without their type and texted the first), so "by label" has nothing to pick
  from. Post texts the contacts app's primary, else a mobile, else the first number that is a number. To keep one
  address book, the phone's Rolodex now orders a person's numbers the same way: its Text and Call, and the number
  under a name, are the primary or mobile one where they were the provider's first row. A visible change on the phone.
- **A contact is named by a digest of the device contact id.** Stable while the contacts provider keeps the id; a
  contact it re-aggregates (an account sync merging two) gets a new id, and a new text to the old one is "There’s
  nobody to send this to." until the Rolodex is gathered again.
- **A contact without a name of its own is left out,** since the phone names such a contact after its number or
  address: no letter, an `@`, or a run of seven digits or more. Such a contact cannot be texted from the wrist. Two
  contacts with one name both appear, and cannot be told apart there.
- **200 a page, at most 20 pages (4,000 contacts), cut in Post's order past that; two restarts.** Chosen, not measured.
- **Gathered afresh each time the screen opens,** the last one shown meanwhile; held for Dash's process only (the
  session), never written down; a rebuilt screen keeps only the search.
- **B1's numbers form of `dash/send` stays in the contract,** unused: the wrist is handed no number, and a guard keeps it
  sending none.
- **New text is on People waiting with nobody waiting too,** which amends B4's "the faint glyph and nothing else".
- **Say a name opens dictation directly** and Keyboard the system's input screen, as the reply screen does; the
  letters are read back with Everyone to leave the search. "By voice" is the name said into the search, as asked.
- **A new text that went shows nowhere on the wrist at once:** both screens go, and the correspondence appears on
  People waiting when Post shows it (a reply, by contrast, shows in its conversation at once, B4).
- **Without the contacts permission the wrist is shown an empty book** and says nobody is in the phone's contacts; it
  cannot tell that from a book with nobody in it. An answer carrying why would be a new field.
- **40 contacts drawn at a time,** more as the crown nears the end, so a large book does not build thousands of views.

## Not done, not verified

- **Nothing here has met a watch or a phone.** Unverified: the contacts provider's `is_super_primary`, `is_primary` and
  `data2` on this phone's accounts; how long a large book takes to cross page by page over Bluetooth or the cloud
  relay; the dictation prompt and the input screen for a name on Wear OS 6; the list growing as the crown scrolls
  (`ScrollChange`); the cards and choices on the 41 mm and 45 mm screens; TalkBack; the contacts permission on the
  locked phone; and Google Messages' bridge finding or starting a conversation for a number sent from the wrist (B2's
  cascade, as unverified from the phone).
- **No mockup draws the new-text flow;** `design/screens-r1.html` is unchanged. W-5.1.9's favourites (starred in the
  Rolodex) do not cross: the contract carries names and ids only.
- **Not built from release B:** W-5.1.5 (the call note), W-5.1.6 (mail lines), W-5.1.7 (the tile), W-5.1.9 (overnight
  callers). No buzz yet.

## Chunk B6 totals

- `Watch.Tests`: **1047 passed, 0 failed.** `Tools/FaceStudio.Tests`: 38 passed. `FocusPhone/Codex.Tests`: 3379 passed.
  `RepoGuards.Tests`: 452 passed. `Storyworthtelling.Updates.Tests`: 194 passed. `leakscan --ci` over the chunk's diff:
  "no personal-data shapes in the added lines".
- `Watch/Dash.Wear` and `Watch/Watch.Ui.Wear` compile here with 0 warnings and 0 errors; `FocusPhone/Post.Android`
  with 0 errors (65 warnings, none in a file this chunk touched).

# Chunk B7 · the call note and the mail lines (W-5.1.5, W-5.1.6)

Every figure below is from a real run on 27 Nov 2026. As before, each red run was taken with the tests written and the
new code present only as stubs: methods threw `NotImplementedException`, and new constants were empty strings.

## First, the owner's report: Post's own call note on the phone doesn't work

Asked while B7 was under way, since the watch's call note uses the same rules. The owner gave no symptom, so the
whole path was read link by link.

**The path, and what each link was found to do:**
- **How Post learns a call ended:** `CallStateReceiver`, a manifest receiver for `android.intent.action.PHONE_STATE`
  (one of the implicit-broadcast exceptions), keeps the OFFHOOK sighting in preferences and, on the IDLE after it,
  hands off to `CallEndedNotePrompt` on the pool. Android delivers that broadcast only to an app holding
  READ_PHONE_STATE. Post asks for it in its launch dialog, but **Flyleaf's policy did not grant it**: the device owner
  force-grants READ_CALL_LOG, READ_SMS, WRITE_CALL_LOG, WRITE_CONTACTS, READ_PHONE_NUMBERS and SEND_SMS, and not this
  one. On the locked phone the offer depended on that dialog having been answered yes, and nothing would have said
  otherwise. No notification route or call-log observer is involved; the call log is read once per call end.
- **Finding the call that just ended (the cause):** `CallEndedNotePrompt.FindJustEndedCall` read the newest row and
  accepted it only if `now − CallLog.Calls.Date ≤ 15 s`. **The call log's DATE is when the call began** (Telecom's
  `CallLogManager` writes the call's creation time, when it was placed or began to ring, with the connected time as
  DURATION). So a call that lasted more than about fifteen seconds, which is every real conversation, was "not fresh
  enough" and silently offered nothing. Only a call of a few seconds could ever reach the prompt. This one is certain
  from the code alone.
- **The prompt behind the dog-ear, the writing screen, saving and showing:** read and found sound.
  `PostActivity.CheckPendingCallNotes` raises the notice on every resume; its action (run from the Waiting page)
  clears the slot and opens the correspondence with `WriteCallNote`, an `ActionCard.AskText` on the page's overlay,
  which a re-render does not clear; Keep writes `CallNote.For(number, text, now)` to the timeline cache (keyed by kind
  and source id, `note:<ms>`), which no reconcile touches (only mail is reconciled), and the open thread reloads and
  shows it as a call item on the same correspondence (the number normalised as the call's own key). None of these
  could be tested end to end here; they are not changed.
- **Quiet failures found on the way:** nothing on the path logged anything when no note was offered, so the weekly
  export could not show it. The blocked-numbers check now always throws on this phone (Google Messages holds the
  texting role, and only the default SMS or dialer app may read the list); it was caught, and logged with the
  exception's words. The receiver's failure line put the whole exception into logcat.

**The fix (`Codex.Domain/Post/JustEndedCall`, pure):** the row is read with its DURATION and judged by its end: when it
began, plus how long it was connected, plus up to `RingAllowance` (90 s, chosen, not measured) of ringing the log does
not record, must fall within `Freshness` (15 s) of now (`Fresh`). A call someone was on (`Answered`) is an incoming or
outgoing call that connected, or one answered on another device (type 7: rung on the phone, taken on the watch); a
missed, declined, voicemail or blocked call, an unknown type, and an outgoing call nobody picked up (which the old
rule offered) are not. `Judge` gives one `CallNoteVerdict`; a row not there yet, or the one before it, is read again
(`ReadAgain`), as the old retries did. Flyleaf's plan now grants Post READ_PHONE_STATE.

**The log lines (none carries a number, a name or a word of the call):** `CALL NOTE a call ended` (the broadcast
arrived), then one of `CALL NOTE offered` / `not offered: the call log had no call` / `the newest call ended too long
ago` / `nobody answered` / `the number was withheld` / `the number is blocked` / `the call log can’t be read`; and
`PERMISSION READ_PHONE_STATE …` once a process when Post resumes without it (`HeadLog.Denied`), and the same for
READ_CALL_LOG. The receiver's and the blocked check's failures are logged by their shape only (`HeadLog.Quiet`).

- **Tests first:** `Codex.Tests/Post/JustEndedCallTests.cs` (21 with rows: `ASixMinuteCallIsJustEnded`,
  `AnHourLongCallIsJustEnded`, `TheRingBeforeItConnectedIsAllowed`, `ACallThatEndedLongAgoIsNot`,
  `ACallStillGoingIsNot`, `AShortCallIsStillJustEnded`, `ACallNobodyWasOnIsNotAnswered` ×5,
  `AnOutgoingCallNobodyAnsweredIsNotAnswered`, `AConversationIsAnswered` ×2, `ACallAnsweredElsewhereIsAnswered`,
  `NoRowIsNoCall`, `OnlyAMissingRowIsReadAgain` ×5); `PostLogLinesTests.ACallNoteSaysWhatBecameOfIt` ×7 and
  `ACallNoteLineCarriesNoNumber`; `Post/CallNotePathTests.cs` (the head, read by content: the prompt judges through
  `JustEndedCall` and reads the duration and has no `FreshnessWindow`, every call end and a refused call log say so,
  a refused READ_PHONE_STATE is said, the receiver logs a failure by its shape); and
  `PolicyPlanBuilderTests.Plan_GrantsReadPhoneStateSoPostHearsACallEnd`.
- **Red: `FocusPhone/Codex.Tests` 3413 tests, 34 failed, 3379 passed.** The 34: `JustEndedCallTests` 21,
  `PostLogLinesTests` 8, `CallNotePathTests` 4, `PolicyPlanBuilderTests` 1. First errors: `NotImplementedException` 29,
  `Assert.Contains() Failure: Sub-string not found` 3 (the head untouched), `Strings differ` 1 (the empty permission),
  `Assert.DoesNotContain() Failure: Sub-string found` 1 (`{e}` in the receiver).
- **Green: `Codex.Tests` 3413 passed, 0 failed. `RepoGuards.Tests` 452 passed.** `FocusPhone/Post.Android` builds
  with 0 errors and no warning in a file this fix touched.
- **What only a device can confirm:** that Telecom writes DATE as the call's start on this phone (it is AOSP's, and the
  first real call's log lines will say: `offered` after a long call where the old rule would have said nothing);
  whether READ_PHONE_STATE had been granted by the launch dialog (if the export shows no `a call ended` line after a
  call, it was not, and the next policy pass now grants it); whether the phone goes off hook at all for a call rung
  here and answered on the watch; and the note card's keyboard opening when the card is raised from the Waiting page.
