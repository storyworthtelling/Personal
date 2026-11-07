# Docs drift + package/build drift audit (read-only)

The audit made no changes to tracked files and `git status` is clean. One side effect: `dotnet list package --vulnerable` restored packages into the gitignored `obj/` folders. Scripts and raw output are in `/tmp/claude-0/-home-user-Personal/b5742c71-73ba-5e61-9ad5-55fc006c45b7/scratchpad/`: `pkgs.py`, `ticks.py`, `paths.py`, `pkg-full.tsv` and `vuln.txt`. No personal data appears below.

## 0. The eight earlier findings, re-checked
- **StartPairing:** it does have callers now. `Post.Android/MessagesPairingActivity.cs:157,174` calls it, so CLAUDE.md's "no caller" line is stale.
- **Voice models:** the code uses `claude-sonnet-5-5` / `claude-haiku-5-5` (`Storyworthtelling.Voice/VoiceConfig.cs:9-10`, `VoiceOptions.cs:13`). CLAUDE.md still says sonnet-4-6 / haiku-4-5.
- **Folio welcome, Post's voice seat, Constitution "no figures", balance hand-off "balance and date only", "Foreword no longer writes the PersonalVault", Plate statement read presented as live, the FONT CHECK note:** I took these as already established and did not re-check them.

## A. Docs drift

**Backtick-identifier script** (`ticks.py`, plus a path-suffix check over `git ls-files`):
- CLAUDE.md has 34 unresolved tokens and DESIGN.md has 1. Almost all are intended:
  - things the docs themselves call retired (`AppTrayActivity`, `TrayAdapter`, `ThreadsActivity`, `ItemsFor`, `OwnerMark`, `MedallionImage`, `MedallionCrop`);
  - AOSP or platform names (`MmsService.writePduToContentUri`, `SetDefaultSmsApplication`, `DISABLE_NOTIFICATION_ALERTS`, `RESULT_CANCELED`);
  - graphify file names.
- The guard test names did resolve once I checked them by file name (the `*Tests.cs` files exist).
- Real misses:

| Doc:line | Doc says | Reality | Sev | Fix |
|---|---|---|---|---|
| CLAUDE.md:75 | `SiblingUpdatePlan.Decide (Codex.Domain/Updates)` | There is no `Codex.Domain/Updates`; the type lives in `Storyworthtelling.Updates` (CLAUDE.md's own Self-update section says it was extracted) | Low | Repoint the path |
| DESIGN.md:431 (§19 registry) | Per-app `Resources/Styles/DesignTokens.xaml`; "Ledger and Canonizer still carry their own and adopt the kit at their own phases" | No `DesignTokens.xaml` exists anywhere; Index and Folio adopted the kit; Ledger/Folio `DesignTokens.cs` hold 0 hexes | Med | Rewrite the "Per-app" row |
| LOG-LOOP-PLAN.md:257 | `Storyworthtelling.Maui/Diagnostics/TapWatch` | The type is `MauiTapWatch.cs` | Low | Rename in the plan |
| LOG-LOOP-PLAN.md:84-88 | `PipelineFileLogger.cs` / `DeliveryFileLogger.cs` | Both are gone, replaced by `FileLogger` | Info | Mark them as replaced |

**Concrete claims checked by hand (37). Confirmed correct (26):**
- `LogOutbox.DefaultCapBytes` = 8 MB; `LogBatch.MaxPlaintextBytes` = 256 KB; `MaxBatchesPerRun` = 40; `UploadWindow` opens Saturday 18:00.
- Folio: `RemarkableMaxBytes` = 100 MB, `PassMaxTokens` = 16,000, `MaxTruncatedAttempts` = 2, `OfflineRetrySeconds` = 60, Effort and PassEffort = "medium", ceilings 24k / 32k, `MaxAttempts` = 6, `PictureQuality` = 80, page 595×842 with margins 52.8 / 48 / 62.4, `PaperTypeScale.Ratio` = 0.75.
- Post / Plate: `MmsComposer.MaxMediaBytes` = 15 MB, `FaceBook.SameFace` = 0.62, `FiledSearch` 120,000 characters and at most 5 picks, `MailJudgementStore` keeps 60 days, `MmsStubRetry` (3 attempts, 7 days), `TextTrail.LateAfter` = 2 min.
- Foreword `EventLanes.LegibleLanes` = 2; Score `MaxConcurrentReads` = 8.
- Constitution: lease 48 h, `RunGate` waits 5 min, workout run at 6:45, digest from 04:00, 7 record kinds, 6 unique work names, the 17-permission test exists and the CI allowlist (9 + 8 health reads) matches 17.
- Unlock delay 6 h and Admin session 60 min; `UpdateService` every 6 h.
- Exactly 8 TTFs in `/Fonts`; Folio has 12 frozen preference keys and Foreword 9 frozen (22 keys exist in total, but 9 are frozen, so the claim holds).
- Bands: Folio 8, Foreword 7, Pulp 9, Constitution 10, Index 1. The artifact names match. All 9 ApplicationIds match their frozen ids. `LoopUploadWorker` registered name and work name `log-upload` match.
- Every documented `dotnet test` / `build` path exists.

**Drift (11):**

| Where | Doc says | Reality | Sev | Fix |
|---|---|---|---|---|
| CLAUDE.md:45 | Score has "the **eleven** SharedPreferences file names" | `FrozenInvariantsTests.cs:257-262` freezes **15** | Med | Say 15 |
| CLAUDE.md:46 (Post), :66 (Plate) | "versionCode floor stepped to **200000**" / "floored at 200000" | Both use `./.github/actions/version-code` **band 1** (band × 1e8 + minutes since 2020). The 200,051 / 200,030 numbers survive only as the last-shipped values in `SuiteVersionBands` | Med | Describe the band formula |
| CLAUDE.md:75 | `build-focusphone`/`build-index`/`build-storyboarder` inject `ApplicationVersion=run_number`; `build-android` "injects **nothing**", Foreword's csproj computes a timestamp versionCode | `build-focusphone.yml` does not exist. Every workflow injects the band code. Foreword's csproj has `ApplicationVersion` 1 and the workflow passes band 7 | **High** (actively misleading on the upgrade channel) | Replace the paragraph with a pointer to Self-update / version-code |
| CLAUDE.md:348 | Foreword icon is spec `Foreword`, glyph `Glyphs.FoldedPaper` | `Tools/IconStudio/IconSpecs.cs:103` uses `Glyphs.ForewordPlate` + `Branding/foreword-cover.png` | Low | Update |
| CLAUDE.md:245 | `MealWeekWorker` runs "on the hour" plus 35 past | `MealPass.MinutePastTheHour = 5` (`MealPass.cs:188`) and `RecipeMinutePastTheHour = 35` | Low | Say ":05 and :35" |
| CLAUDE.md (Constitution) | `TheMergedReleaseManifestHasSeventeenPermissions` "reads the real merge" | `MealWeekGuardTests.cs:160-166` returns early when there is no `obj/Release` manifest. CI runs tests **before** the Release publish (`build-constitution.yml:60` vs `:101`), so it never asserts in CI; only the workflow's PERMISSION CHECK enforces it | Med | Say so, or run the test after the publish |
| CLAUDE.md:26 | "Owner photo swap" | `Branding/owner.png` is the drawn M mark, not a photo (checked visually; no personal image) | Info | Note it |
| Foreword/README.md | Tokens live in `Foreword.Android/Resources/Styles/DesignTokens.xaml`; app delivers "news plus the NYT crossword" | The file is gone (kit adopted); the description is pre-P9 | Med | Rewrite |
| Storyboarder/README.md:135 | `dotnet test Storyboarder/Folio.sln -p:FolioTfms=net10.0` | CLAUDE.md says target the Tests csproj because the .sln pulls in the Android head | Med | Change the command |
| PHASE5-HANDOFF.md (root) | "FocusPhone Audio" handoff, Phases 0–4b | Pre-Score-rename context, stale | Low | Archive or delete |
| DESIGN.md:3, :372-373 | Lists "Canonizer, Ledger, DailyLoader"; glossary uses "Canonizer"; "tile/tray" | Renamed to Folio / Index / Foreword. Pulp, Constitution, Post, Plate and Score are missing from the list; the tray is retired | Low | Update |

**DESIGN.md token values against the token files:**
- **Matching:**
  - All 7 roles × mark/text/deep/fill, and all 9 neutrals plus `InkBlue`, match `PaperColorTokens.cs`.
  - The type scale (sp), spacing ladder, radius 12, touch 48 / row 56, Quick 150 / Standard 200, undo 6 s, grain ~12.5% (32/255), wash ~30% (77) and dog-ear 40dp all match.
  - The identity ladders for Score, Post, Plate, Folio, Index, Pulp and Constitution match `Codex.Domain DesignTokens.Identity` and IconStudio `Tokens.cs`.
- **Mismatches:**
  1. **Terracotta "retired everywhere" (DESIGN.md:70) is not true** (Med):
     - `PaperColorTokens.Accent = "#C2542F"` / `AccentDeep` (lines 21-22) is still live. `PaperPalette.Resolve(PaperRole.Active)` returns it (`PaperPalette.cs:30`).
     - IconStudio still draws terracotta: `Tokens.AccentHex` is `#C2542F`, used by `Glyphs.AccentStroke`/`AccentFill` (Clock and others) and by `BrandingMedallion`. These glyphs now look unused by any `IconSpec`, but they are not deleted.
     - The legacy Sage / Slate / Ochre / Plum (P3 e-ink) hues are still in `PaperColorTokens` alongside the role table.
  2. **Name collision** (Med): `Paper.xaml` defines `Accent = #38734A` (pine) and `AccentDeep = #174025`, while `PaperColorTokens.Accent` is terracotta. Same token name, two colours.
  3. **Foreword deep tier** (Low): DESIGN.md's table says "—", but IconStudio and `AppIdentityTokens` both carry `ForewordDeepHex = #6E3512`.
  4. **§4 "Analysis PDF px" column** (72 / 46 / 32 / 30 / 26 / 22 / 24) (Med): this is the retired 150-DPI raster ladder. The printed ladder is now `PaperTypeScale` in points (33 / 21 / 13.5 / 12 / 10.5 / 9.75 / 10.5, plus Dose 54), which DESIGN.md's own §19 describes.
  5. **`Ink` 320 ms motion** (Low): DESIGN.md §6 names it, but no token exists in any Motion class.
  6. **§4 font list** (Low): it omits Serif **Italic**, yet the same section gives Italic a role and the file ships.

**Code with no docs:**
- `Storyworthtelling.Google`, `Storyworthtelling.Handwriting`, `Codex.Tidal.Binding`, `docs/` (privacy page) and `audits/` are not mentioned in CLAUDE.md at all.
- The Paper, Updates, Household, Google, Handwriting and IndexHistorySeal test projects have no exact documented test command (Household is written as `…`).

## B. Package / build drift

There is no `Directory.Packages.props` or `Directory.Build.props`. The 55 csproj files hold 43 distinct packages.

**Version-drift table:**

| Package | Versions → projects |
|---|---|
| Microsoft.Extensions.Options | **9.0.0**: Foreword.Infrastructure, Foreword.Tests, Handwriting(+Tests), Remarkable, Voice · **10.0.9**: Ledger.Infrastructure, Folio.Infrastructure |
| Microsoft.Extensions.Logging.Abstractions | **9.0.0**: Handwriting(+Tests), Maui.Tests, Voice · **10.0.9**: Ledger.Infrastructure, Folio.Infrastructure |
| Microsoft.Extensions.Http | **9.0.0**: Foreword.Infrastructure · **10.0.9**: Ledger, Folio Infrastructure |
| Microsoft.Extensions.Logging.Debug | **9.0.0**: Foreword.Android · **10.0.9**: Ledger.Android, Folio.Android |
| Microsoft.NET.Test.Sdk | **17.12.0**: Constitution, Codex, Foreword, Pulp, IconStudio tests · **17.14.1**: the other 13 |
| xunit.runner.visualstudio | **2.8.2**: same 5 · **3.1.4**: the other 13 |
| Xamarin.AndroidX.Concurrent.Futures | **1.3.0.1**: Foreword, Ledger, Folio Android · **1.3.0.4**: Constitution.Android |
| Microsoft.Data.Sqlite / EF Core | **10.0.2** (Codex.Infrastructure) vs EF Core Sqlite/Design **10.0.9** (Ledger, Folio) |
| Google.Apis.Drive.v3 | **`1.74.0.*` floating** in 7 projects, so restores are non-deterministic; the other Google.Apis packages are pinned (Auth 1.75.0, Calendar 1.74.0.4154, Tasks 1.74.0.3958, Docs 1.74.0.4134) |

- **Consistent:** xunit 2.9.3 (18), Moq 4.20.72 (12), coverlet 6.0.4 (11), PdfPig 0.1.10, PDFsharp 6.1.1, SkiaSharp + NativeAssets.Linux 3.119.4, Work.Runtime 2.10.2 (9), Media3 1.8.0.2, Maui.Controls `$(MauiVersion)`.
- **Gaps:** coverlet is missing from 7 test projects (Constitution, Codex, Foreword, Pulp, IconStudio, IndexHistorySeal, LogLoopReader). Moq is in 12 of 18.

**Vulnerabilities** (from `dotnet list package --vulnerable --include-transitive` against nuget.org, desktop-TFM projects only; Android heads not scanned):

| Package | Severity | Via | Projects |
|---|---|---|---|
| SQLitePCLRaw.lib.e_sqlite3 2.1.11 | **High** (GHSA-2m69-gcr7-jv3q) | transitive | Codex.Infrastructure/Tests, Ledger.Infrastructure/Tests, Folio.Infrastructure/Tests, IndexHistorySeal(+Tests). The Android heads that use these likely ship it too |
| System.Security.Cryptography.Pkcs 8.0.1 | **High** (GHSA-846f-f764-76mx) + Moderate | transitive (likely via MailKit/MimeKit) | Codex.Infrastructure/Tests |
| MailKit 4.14.1 | Moderate (GHSA-9j88-vvj5-vhgr) | direct | Codex.Infrastructure |
| MimeKit 4.14.0 | Moderate (GHSA-g7hc-96xr-gvvx) | transitive | Codex.Infrastructure |
| AngleSharp 1.1.2 | Moderate (GHSA-pgww-w46g-26qg) | direct | Foreword.Infrastructure/Tests |

- **Fix:** bump MailKit/MimeKit and AngleSharp. Pin a patched `SQLitePCLRaw.bundle_e_sqlite3` and `System.Security.Cryptography.Pkcs` directly (I'm not certain of the exact fixed versions; take them from the advisories).
- **Other observations:** Microsoft.Playwright 1.49.0 (desktop TFM only, Foreword) is old but has no listed advisory. Moq 4.20.72 is after the SponsorLink removal.

**TFM, SDK and compiler settings:**
- TFMs are `net10.0` (39) and `net10.0-android` (13), consistent with the docs.
- `SupportedOSPlatformVersion` is 34 on all native heads, Codex.Ui.Android, Ledger/Folio Android and Infrastructure, and Pulp/Constitution.
  - **Foreword.Android has no SupportedOSPlatformVersion**, only `AndroidMinSdkVersion` 26.
  - Ledger and Folio set **both** 34 and `AndroidMinSdkVersion` 26, which is contradictory. In .NET for Android, `SupportedOSPlatformVersion` normally drives minSdk; I'm not certain which wins (Med).
  - Fix: pick one minimum (34 suite-wide, since the device is a Pixel 8a) and drop `AndroidMinSdkVersion`.
- No targetSdk is set anywhere (no `uses-sdk`, no `AndroidTargetSdkVersion`), so it defaults to the net10-android default, which is consistent.
- `Nullable` and `ImplicitUsings` are enabled in all 55. No project sets `LangVersion` or `TreatWarningsAsErrors`; CPM could add warnings-as-errors suite-wide.
- `ApplicationDisplayVersion` defaults differ per csproj: a timestamp in Ledger/Folio, 0.1.0 in Flyleaf/Plate, 1.0.0 elsewhere, 2.0.0 in Foreword. CI overrides all of them, so only local builds show the difference (Low).

**Workflows:**
- **Uniform:** `actions/checkout@v4`, `setup-dotnet@v4` with `10.0.x` everywhere (floating SDK band), `setup-java@v4`, `upload-artifact@v4`, `github-script@v7`, `setup-go@v5` (go 1.25, Post), all on `ubuntu-latest`.
- **JDK 21** is pinned in Foreword, Flyleaf, Plate, Pulp and Constitution. Post, Index and Folio inherit the runner's JDK 17 (Low/Med; Flyleaf's comment explains a JDK-17 failure that came from Score's TIDAL binding).
- **Workloads** are installed unpinned (`dotnet workload install android` / `maui-android`, no rollback file), so CI and local builds can diverge silently (Med). Fix: commit a workload rollback / `--version`.
- **ICON CHECK:** `build-android.yml` (Foreword) has no ICON CHECK and no FONT CHECK. Flyleaf's and Post's ICON CHECK is non-fatal (as CLAUDE.md says).
- **Path-filter gaps** (computed from transitive ProjectReferences, `paths.py`). A change to these libraries or folders does not run that app's own tests on a branch push (Med):

| Workflow | Missing from its path filter |
|---|---|
| build-android | **Foreword/Foreword.Tests**, Paper, Handwriting, Updates |
| build-index | **Paper** (the worksheet uses `VectorPdfSink`), Remarkable, Handwriting, Updates |
| build-constitution | Paper, Remarkable, Google, Household, Handwriting |
| build-flyleaf | Voice, Paper, Remarkable |
| build-post, build-plate | Paper, Remarkable |
| build-storyboarder | Paper, Updates |

  `build-shared-libs` runs only the libraries' own tests, not app tests that depend on them.

**Recommendation: adopt Central Package Management.**
- Add a root `Directory.Packages.props` with `ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled`. Transitive pinning is what lets you pin the SQLitePCLRaw and Pkcs fixes in one place.
- Unify the drifted packages: Microsoft.Extensions.* (10.0.9 is net10-native; keeping 9.0.0 was only a compatibility choice), the test stack (Test.Sdk 17.14.1, runner 3.1.4, plus coverlet for all), Concurrent.Futures, and Sqlite with EF Core.
- Replace the `1.74.0.*` float with an exact version.
- Add a `Directory.Build.props` for shared Nullable/ImplicitUsings, an optional `TreatWarningsAsErrors`, and one Android minimum.
- MAUI's `$(MauiVersion)` can stay as is. The Xamarin binding packages are single-consumer and can stay project-local if CPM's `VersionOverride` is preferred for them.