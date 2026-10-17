# Constitution P13 — progress

One phase branch (D89): `ccr-0de01a0f-8dnn7q`. Releases land as labelled commits. Statuses: todo / in progress / review / done / blocked / reopened.
"Verify" = what has actually been verified; Device PBIs are "built, device check pending" until the owner runs them.

| PBI | Status | Commit | Red-run note | Verify |
|---|---|---|---|---|
| C-1.1.1 | review | (this commit) | 7 of 9 red against empty stub (AppIdIsFrozen, MainActivityNameIsFrozen, HeadUsesTheFrozenAppId, 3 set guards, FrozenSetsAreNotEmpty) | Tests 9 pass; head compiles (0 errors) |
| C-1.1.3 | review | (this commit) | FrozenSetsAreNotEmpty red on empty sets; PreferenceKeysAreLiterals proven by injecting nameof | Tests 9 pass. Worker/work/pref names chosen by agent, owner to confirm |
| C-1.3.1 | review | (this commit) | 5 crop-validation cases failed "No exception was thrown"; CropReplacesTheCentreSquare failed red-vs-blue | IconStudio 34 pass; existing outputs byte-identical (guard + real regen clean) |
| C-1.3.2 | review | (this commit) | ConstitutionCoverIsTheApprovedSize failed: file absent | sha256 matches book cover.jpg, 864x1184 |
| C-12.1.2 | done | (this commit) | ConstitutionIsBandTen red: Assert.Single found no match; BandsAreUnique guards future collisions (asserts >=3 non-band-1 entries) | Updates.Tests 65 pass |
| C-11.4.1 | done | (this commit) | n/a (investigation) | Outcome PERMISSION NEEDED = NO; evidence second-hand, owner to confirm portal list |
| C-1.1.2 | review | (ladder commit) | CS0117 PolicyContext.ConstitutionPackage missing; EverySuiteBook... failed until BookClothPalette bound | Codex.Tests 2470 pass |
| C-1.2.1 | review | (ladder commit) | wrong hexes: TokenParity (no DESIGN.md row), DesignTokensTests (#143B4C vs 4D), 4 Paper separation/parity failures | Codex 2470, Paper 140, IconStudio 34 pass |
| C-1.2.2 | review | (ladder commit) | ConstitutionDocumentHueIsTheMutedCerulean / ConstitutionClothInvertsToInk only compile-red (values were already right) | same suites |
| C-12.1.1 | review | 686ec8d | 4 ConstitutionWorkflowTests failed: workflow file absent | Updates.Tests 69 pass; workflow never run in CI |
| C-12.2.1 | review | (guards commit) | key detector tests green from the start of the resumed run; guard source holds no key-shaped literal (runtime concatenation) | Constitution.Tests 111 pass |
| C-12.2.2 | review | (guards commit) | 29 InventedDataTests failed against a stub detector (email 6, phone 9, address 4, person 6+2, planted fixtures) | Constitution.Tests 111 pass; people check only reaches Household*/Fixtures and markdown rosters |
| C-1.3.3 | review (Owner) | (icon commit) | CS0117 Glyphs.ConstitutionPlate missing; ConstitutionResourcesAreGenerated failed (no matching spec); 3 existing tests failed on the 8x8 seed until SeedGrounds grew | IconStudio 36 pass, Constitution 111 pass, head compiles. Icon at 144px shows two runners; 48px small but discernible (agent's view). Owner has not seen it. Assembly-level Application attribute not yet checked in merged manifest/APK (CI ICON CHECK will). |
| C-8.2.1 | review | (vault commit) | build failed CS1061/CS0117 (no Constitution section); mutation check: wire name changed to 'Constitutions' made 2 tests fail | Voice.Tests 156 pass. Fields SleepNumberLogin / SleepNumberPassword chosen by agent. Nothing run against a real vault. |
| C-10.1.1 | review (Owner) | (shelf commit) | EverySuiteBookOnTheShelfHasAWindowIllustration failed: Constitution fell back to the circular launcher icon; ConstitutionBindsItsCloth was never red (binding existed from C-1.1.2) | Codex.Tests 2471 pass; Flyleaf compiles (0 errors). Shelf not seen on a device; whether the tray wraps to a second row depends on phone width. |
| C-9.1.1 | review | (notice commit) | compile-red (no Notices namespace), then 41 of 41 red against throw-only stubs | Notices+Guard 152 pass. Reuses FoldRole and NoticeSeriousness. No UI, no wiring to real causes yet. |
| C-9.2.1 | review | (voice commit) | compile-red (no Voice namespaces); mutation checks: renamed id, Exception property in a Voice type, stray MessageRequest each made a guard fail | Constitution.Tests 191 pass. Fakes only. Word limits (20-35) and purposes are the agent's wording: owner to check against p.18. Guards cannot see target-typed new(). |
| C-3.1.1 | review (Owner) | (shell-tokens commit) | compile-red only (no AreaCloths / Areas namespace); D84 case is token-level, nothing behavioural was red under the old rule | Codex 2476, Constitution (non-Voice) 175 pass. Cloth hexes from book strip B; Today inactive cloth and Workouts/Meals/Body active inks are the agent's reading. Owner to see the tabs. |
| C-3.1.2 | review | (shell-tokens commit) | compile-red (no Pillars) | same suites |
| C-3.2.1 | review | (shell-tokens commit) | planted-literal test failed against a stub scanner (collection empty); leak guard then caught the agent's own quoted hexes; MaterialParityTests has no clean red | Codex 2476, Ledger 1754, Paper 140 pass. Ledger.Android NOT compiled here (maui-android workload missing); CI is its compiler, and no CI runs on this branch yet. |
| C-4.1.5 | review | (shell-tokens commit) | parity test failed 2 of 2 (Constitution copy absent) | Codex parity + Constitution time tests pass. 'Tuesday, Nov 17' is a Constitution-only TodayHeader helper, not in shared TimeFormats (owner may prefer shared). |
| C-9.2.2 | review | (seat-facts commit) | compile-red, then 68 of 290 red against throw-only stubs (FallbacksMatchPageEighteen, BodyStatesWorseningPlainly, NoFaithContent...); guard proven to fire on a planted word built at runtime | Constitution.Tests 290 pass. Steady bands per unit (0.25 h, 1 lb, 2 bpm...) are the agent's guess. No real model run. |
| C-9.2.3 | review | (seat-facts commit) | UTC-vs-local day mutation failed 2 tests | same. Store is an in-memory fake; prefs-backed store still to build; failure memory 10 min mirrors Index. |

## Open questions
- Ledger.Android cannot compile in this sandbox (maui-android workload missing) and CI does not run on branch ccr-0de01a0f-8dnn7q (workflows trigger on main and claude/** only). Index's re-point to shared card tokens is uncompiled; decide: manual Run workflow, or accept until PR/main.
- NYT: owner suggests Article Search may return Cooking content; wants it tested with their key from the vault. Vault passphrase NOT received and must not be taken in chat; offered environment secret route. Terms (D96) still bar storing recipes regardless.
- Frozen names CONFIRMED by owner (workers ReadBackWorker, BodySyncWorker; work names constitution-read-back, constitution-body-sync; prefs file constitution-settings).
- Spikes C-11.4.2/3/4 need the owner (phone, bed login, tablets); not runnable from the cloud.
