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
