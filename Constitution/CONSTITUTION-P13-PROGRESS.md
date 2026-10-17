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

## Open questions
- NYT: owner suggests Article Search may return Cooking content; wants it tested with their key from the vault. Vault passphrase NOT received and must not be taken in chat; offered environment secret route. Terms (D96) still bar storing recipes regardless.
- Frozen names CONFIRMED by owner (workers ReadBackWorker, BodySyncWorker; work names constitution-read-back, constitution-body-sync; prefs file constitution-settings).
- Spikes C-11.4.2/3/4 need the owner (phone, bed login, tablets); not runnable from the cloud.
