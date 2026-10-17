# Constitution P13 — progress

One phase branch (D89): `ccr-0de01a0f-8dnn7q`. Releases land as labelled commits. Statuses: todo / in progress / review / done / blocked / reopened.
"Verify" = what has actually been verified; Device PBIs are "built, device check pending" until the owner runs them.

| PBI | Status | Commit | Red-run note | Verify |
|---|---|---|---|---|
| C-1.1.1 | in progress | | | |
| C-1.1.3 | in progress | | | |
| C-1.3.1 | review | (this commit) | 5 crop-validation cases failed "No exception was thrown"; CropReplacesTheCentreSquare failed red-vs-blue | IconStudio 34 pass; existing outputs byte-identical (guard + real regen clean) |
| C-1.3.2 | review | (this commit) | ConstitutionCoverIsTheApprovedSize failed: file absent | sha256 matches book cover.jpg, 864x1184 |
| C-12.1.2 | done | (this commit) | ConstitutionIsBandTen red: Assert.Single found no match; BandsAreUnique guards future collisions (asserts >=3 non-band-1 entries) | Updates.Tests 65 pass |
| C-11.4.1 | done | (this commit) | n/a (investigation) | Outcome PERMISSION NEEDED = NO; evidence second-hand, owner to confirm portal list |

## Open questions
- NYT: confirm no Cooking entry at developer.nytimes.com/apis, then decide whether to send the drafted permission request (spikes/nyt-recipes.md). Page 14 revision needed (paste row dropped).
- Spikes C-11.4.2/3/4 need the owner (phone, bed login, tablets); not runnable from the cloud.
