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
