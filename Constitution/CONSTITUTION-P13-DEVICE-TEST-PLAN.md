# Constitution P13 — on-device test plan (C-13.1.1)

The owner runs this with the two phones and two tablets. Every check here exists because the tests
proved it only against fakes ("built and tested against fakes" is not "works", finding section 6, plan
section 11). Each check brings back **a number or a plain observation**, never an impression.

## How to use this document

- Print it or keep a copy in your Drive; **write results in the Record lines of your own copy, not in
  the repository**. The repository copy stays blank and uses invented data only (Sam, Lee, Robin,
  Juniper, `example.com`, `555-0100` numbers). Never write a real reading, balance, address, login or
  the vault passphrase anywhere in a copy that is committed.
- Each check has: **Needs** (precondition), **Steps**, **Expect**, **If it fails** (what that means and
  which PBI reopens), **Record**.
- Run the groups in order: later groups assume earlier ones passed. Mark each result P (pass), F (fail)
  or S (skipped, with the reason). A failure is reported with the check id (for example `B3`) and the
  PBI it names, and that PBI goes back to "reopened" in the progress file (C-13.1.2).
- Capture shapes and outcomes, not content. The Diagnostics export (Settings) is the evidence to attach;
  it holds outcome words and counts only.

## Devices and roles

| Name | What | Role |
|---|---|---|
| Phone O | the owner's locked phone (Flyleaf as device owner) | owns the household file and the shared vault; may write both |
| Phone P | the partner's unrestricted phone | household reader/planner standby; reads the shared vault, never writes it |
| Tablet O | the owner's reMarkable | receives the owner's documents |
| Tablet P | the partner's reMarkable | receives hers |

Which tablet is a Paper Pro and which an rM2 is recorded in section A.

## Group index

| Group | Device / setup | Checks |
|---|---|---|
| A | Install, identity, update channel | A1 to A5 |
| B | Vault, sign-in and consent | B1 to B5 |
| C | Phone O under the lockdown | C1 to C7 |
| D | Health Connect and the bed | D1 to D8 |
| E | Calendar | E1 to E5 |
| F | Tablets: delivery, pen, read-back | F1 to F9 |
| G | Two phones: lease and household file | G1 to G7 |
| H | Workers and schedules | H1 to H7 |
| I | Recipes, Plate, camera, Keep | I1 to I8 |
| J | Index budget contract | J1 to J3 |
| K | Notices, notifications, Flyleaf shelf | K1 to K5 |
| L | Live Claude lines and judge | L1 to L4 |
| M | Look and feel | M1 to M4 |
| N | Constants nobody has evidence for | N1 to N5 |
| O | First real week's cost (C-13.1.2) | O1 |

---

## Group A — Install, identity, update channel

**A1 · First install and the frozen app id** (C-1.1.1)
- Needs: a signed build from the `apps-latest` release; Phone O and Phone P.
- Steps: install on each phone. Open Settings, read the foot line. Open the system app info page.
- Expect: the app is labelled Constitution; the package id ends `.constitution`; the foot line shows
  `1.0.` and a run number.
- If it fails: a different package id means the invariant moved and no update will ever replace it.
  Reopens C-1.1.1.
- Record: package id ______ · version ______.

**A2 · Update channel at band 10** (C-12.1.1)
- Needs: A1 done on Phone O (locked); a newer build published.
- Steps: wait for or force "Install from GitHub now" in Flyleaf Settings; read the status sentence.
- Expect: Constitution updates in place, data intact, no second install.
- If it fails: the versionCode band or the signature is wrong. Reopens C-12.1.1.
- Record: versionCode before ______ after ______ · Flyleaf's status sentence ______.

**A3 · SelfOnly update on Phone P** (C-12.1.1)
- Needs: Phone P, unrestricted.
- Steps: with a newer build published, open Constitution on Phone P.
- Expect: it updates itself only; it does not try to install other suite apps.
- If it fails: the update scope is wrong for an unrestricted phone. Reopens C-12.1.1.
- Record: yes / no · what else it tried ______.

**A4 · Launcher icon and cover** (C-1.3.3)
- Steps: look at the home icon at its real size and the cover when the app opens.
- Expect: two runners on a pyramid, recognisable at 48 px.
- If it fails: the crop or contrast needs another pass. Reopens C-1.3.3.
- Record: recognisable at a glance? yes / no.

**A5 · Fonts and paper**
- Steps: open each tab; check the headers are Serif Black and the owner's line is calligraphy.
- Expect: no system-font substitution anywhere.
- If it fails: a font alias is unregistered. Reopens C-3.1.3.
- Record: any tab in a system font ______.

---

## Group B — Vault, sign-in and consent

The **vault passphrase is typed by you at run time and is never written in this document or any copy.**

**B1 · First run sign-in and restore** (C-8.3.1, C-8.2.2)
- Needs: Phone O freshly installed; Google account with the Calendar and Drive scopes registered on the
  OAuth client.
- Steps: open Constitution; sign in; accept the consent screen; enter the passphrase in Settings.
- Expect: consent appears once with the calendar and Drive scopes; after the passphrase, connections
  (reMarkable, bed, Claude key) appear as "Working" without being typed again.
- If it fails: a consent that never appears means the scope is not registered; a restore that stays empty
  means the vault section or the wire name is wrong. Reopens C-8.3.1 / C-8.2.2.
- Record: consent screens seen ______ · connections restored ______ of ______.

**B2 · Vault ownership: the owner's install reads and writes** (C-8.2.2)
- Needs: Phone O signed into the account that owns the shared vault file in Drive.
- Steps: change one connection (for example re-enter a key); save; open the vault file's details in Drive.
- Expect: the file's modified time moves; Constitution's section carries the change; other apps' sections
  are untouched.
- If it fails: the owner cannot publish, or another section was lost. Reopens C-8.2.2.
- Record: modified time moved? yes / no · other sections intact? yes / no.

**B3 · Vault ownership: the shared-with phone reads and never writes** (C-8.2.2)
- Needs: the vault file shared to Phone P's account (shared, not owned); Phone P signed in.
- Steps: on Phone P, open Settings and enter the passphrase; confirm the Claude key and household
  connections appear. Change a connection field and save. Check the file in Drive.
- Expect: values restore; the save does **not** change the shared file (modified time and version
  unchanged); Settings says the vault is read-only on this phone, one plain sentence, no error.
- If it fails: a partner write can lose the owner's data. Reopens C-8.2.2 (highest severity).
- Record: file modified time before ______ after ______ · sentence shown ______.

**B4 · Vault never overwritten when unreadable (G14)** (C-7.4.1, C-6.1.1)
- Steps: on Phone P, enter a wrong passphrase and save a connection.
- Expect: "wrong passphrase" shown; nothing written to Drive; no crash.
- If it fails: reopens C-8.2.2.
- Record: file modified time unchanged? yes / no.

**B5 · Claimable vault (no file yet)**
- Needs: a spare test account with no vault file.
- Steps: sign in and publish once.
- Expect: the file is created and that account becomes its owner.
- If it fails: reopens C-8.2.2.
- Record: owner shown in Drive ______.

---

## Group C — Phone O under the lockdown

**C1 · Calendar scope and consent** (C-5.3.2)
- Steps: see E1. Confirm Chrome and the Google consent flow are not blocked by the lockdown.
- If it fails: the OAuth allowlist is missing a host. Reopens C-8.3.1.
- Record: consent reachable? yes / no.

**C2 · Keep's share sheet** (C-10.1.4, D113)
- Needs: Keep installed and allowlisted; a week with groceries.
- Steps: Meals tab, "Share the list".
- Expect: Keep's own share sheet opens, a new note is created, the page says "The list went to Keep."
- If it fails: Keep suspended or the package id differs (NotFound / Refused sentence). Reopens C-10.1.4.
- Record: Keep's package id ______ · sentence shown ______.

**C3 · Watch and scale data under the lockdown** (C-10.1.3)
- Steps: take a weight on the scale and a walk with the watch; open each companion app.
- Expect: the companion apps run, and the reading reaches Health Connect within an hour.
- If it fails: allowlist lacks a package id (open question OQ3). Reopens C-10.1.3.
- Record: companion package ids ______ · minutes to arrive ______.

**C4 · Notification alerts under the lockdown** (C-9.1.3, C-10.2.1)
- Steps: raise a Problem notice (turn the bed login wrong); look at the Flyleaf shelf.
- Expect: the alert is suppressed by the lockdown but the Constitution bookmark lights rose.
- If it fails: no ribbon. Reopens C-10.2.1.
- Record: ribbon colour ______.

**C5 · Android 13 notification permission** (C-9.1.3)
- Steps: first launch; answer the permission prompt Allow, then repeat on a reinstall answering Deny.
- Expect: Allow posts notices; Deny posts nothing and shows no error.
- If it fails: reopens C-9.1.3.
- Record: prompt appeared? yes / no.

**C6 · Photo errand under the lockdown** (C-11.2.1)
- Steps: "I ate something else" on Phone O; photograph a plate.
- Expect: Plate opens, returns to the card; "Estimating…" then "You had something else · about N kcal".
- If it fails: Plate is not reachable. Reopens C-11.2.1 / C-6.6.1.
- Record: seconds ______ · the Plate capture file gone from cache? yes / no.

**C7 · Camera permission is absent on Phone O**
- Steps: look at Constitution's permissions in system settings.
- Expect: Camera is listed (declared for Phone P's scans) but only asked on first scan; no write-health permissions.
- If it fails: reopens C-6.5.2.
- Record: permissions listed ______.

---

## Group D — Health Connect and the bed

**D1 · Permission dialog opens per signal** (C-7.1.1, C-7.3.5)
- Needs: a fresh install on both phones.
- Steps: open Body; open a signal; tap "Allow in Health Connect".
- Expect: Health Connect's dialog opens for that signal; the rationale page is reachable from Health
  Connect's own settings.
- If it fails: dialog never opens; the fallback to manage permissions is a guessed action string. Reopens
  C-7.1.1 and C-7.3.5. This is the single largest risk: Body is empty if it fails.
- Record: Phone O opens? yes / no · Phone P opens? yes / no · Android version ______.

**D2 · Background reads** (C-7.1.2, C-7.1.3)
- Steps: grant steps and sleep; close the app; wait a day.
- Expect: next morning's readings are present without opening the app.
- If it fails: the Body sync worker did not wake. Reopens C-7.1.3.
- Record: days with data after 3 days ______.

**D3 · Units and member names** (C-7.1.2)
- Steps: compare Constitution's steps, resting heart rate, sleep hours and weight to Health Connect's.
- Expect: identical within rounding.
- If it fails: a unit or field mapping is wrong. Reopens C-7.1.2.
- Record: Constitution ______ vs Health Connect ______ per signal.

**D4 · Revocation** (C-7.3.5)
- Steps: revoke one signal in Health Connect; reopen.
- Expect: that signal says it is waiting for permission; others unaffected; revoked while the app was
  closed is noticed on next open.
- Record: noticed on open? yes / no.

**D5 · SleepIQ sign-in and sleeper side** (C-7.2.1)
- Needs: the household bed login in Settings (stored in the vault only).
- Steps: sign in; pick the sleeper side; wait one night.
- Expect: next day a night appears; the unit is hours.
- If it fails: the unofficial endpoints changed or the side maps wrongly. Reopens C-7.2.1.
- Record: outcome phrase in Diagnostics ______ · hours shown vs bed's own app ______.

**D6 · Bed offline** (C-7.2.1)
- Steps: unplug the bed's network for two nights.
- Expect: the sleep line stops, "Waiting for the bed", and the error waits behind the dog-ear.
- Record: appeared after ______ nights.

**D7 · Body backup (G14)** (C-7.4.1)
- Steps: reinstall on Phone O, restore with the passphrase.
- Expect: history returns (merge: local days win).
- If it fails: reopens C-7.4.1.
- Record: days restored ______ of ______.

**D8 · Recovery thresholds** (C-5.2.4)
- Steps: for two weeks compare the app's "recovery" call to how you feel.
- Expect: the invented thresholds (sleep -1 h, HRV 80 percent, resting heart rate +5 bpm) rarely misfire.
- If it fails: tune the numbers; reopens C-5.2.4.
- Record: days it swapped a workout ______ · days you disagreed ______.

---

## Group E — Calendar

**E1 · Consent and scope** (C-8.3.1) — consent lists full calendar. Record: scope text ______.

**E2 · Workout placement on the owner's calendar** (C-5.3.2, C-5.3.3)
- Steps: run the weekly plan (open the app Sunday); look at the calendar.
- Expect: workouts placed in free gaps, titled plainly, tagged with a private property; none overlap.
- If it fails: a double-booking. Reopens C-5.3.2.
- Record: sessions placed ______ · overlaps ______.

**E3 · Partner's shared primary calendar** (C-5.3.2, C-6.4.1; R9)
- Needs: Lee shares her primary calendar with edit rights.
- Steps: run the meal plan; look for cook blocks on her calendar.
- Expect: her blocks appear on hers; an edit she made to a block is left alone.
- If it fails: no edit rights, or events land on the wrong calendar. Reopens C-6.4.1.
- Record: blocks created ______ · refused ______.

**E4 · Moves and deletes** (C-5.3.3, C-5.4.3)
- Steps: move a placed workout in Google Calendar; delete another.
- Expect: the move is accepted as the new time; the delete counts as a skip, not a replan.
- Record: skip recorded? yes / no.

**E5 · Swap and skip from the Workouts tab** (C-5.4.3, C-5.4.4)
- Steps: long-press a day; swap with another; skip one; undo within six seconds.
- Expect: calendar follows; undo restores; a swap while the meal run holds the gate shows the busy sentence.
- Record: seconds for the calendar to follow ______.

---

## Group F — Tablets

**F1 · Paper sizes** (F-2.1) — send a workout page to Tablet O (Paper Pro) and Tablet P (rM2).
- Expect: native 3:4, nothing scaled, type legible in greyscale.
- Record: model ______ · legible smallest type ______.

**F2 · Target sizes under a real pen** (C-2.1.2)
- Steps: tick the large and standard boxes, circle a word, write in the number box and "No, because".
- Expect: you can hit each without touching another.
- If it fails: enlarge the target. Reopens C-2.1.2 / C-2.3.1.
- Record: misses out of 20 ______.

**F3 · Does the cloud's PDF carry pen strokes** (C-11.4.4, R3, C-2.2.3)
- Steps: mark a page; sync; download the cloud PDF.
- Expect: the marks are present in the downloaded file.
- If it fails: the ink design is rebuilt, not tuned (accepted risk, D97). Reopens C-2.2.3.
- Record: marks present? yes / no.

**F4 · Vision read of marks** (C-2.2.3)
- Steps: tick, scribble, circle one word, circle across two lanes, write a number, write a reason.
- Expect: tick = done; scribble = tick (D18); two lanes = confirm card; number read; reason beats tick (D17).
- Record: read correctly ______ of ______.

**F5 · Delivery and the pairing** (C-2.2.2)
- Steps: run a morning; check each tablet; re-pair and watch waiting documents go out.
- Expect: the right document on the right tablet; a refused upload raises the reMarkable notice.
- Record: minutes from run to tablet ______.

**F6 · Three-day trash** (C-2.2.3, R4)
- Steps: leave a digest and a workout page unread three days.
- Expect: the tablet copies are trashed on day three; a page you started is not.
- Record: trashed on day ______.

**F7 · Rating page and "We didn't cook this tonight"** (C-2.5.4, C-2.5.5)
- Steps: cook a dinner; rate on both tablets; tick the not-cooked box on another.
- Expect: the next morning the ratings appear in the recipe box history; a doubtful tick is asked.
- Record: ratings read ______ of ______.

**F8 · Meal sheet line-by-line approval and the deadline** (C-2.4.1, C-2.4.5, D103)
- Steps: answer some lines, leave others; wait past Sunday 8:00 on the owner's clock.
- Expect: untouched lines approved; a denied dinner reads "Not approved." and adds a shopping line.
- Record: decided at ______ (D152 expects midday).

**F9 · Workout cover and exercise pages** (C-2.3.1) — illustrations match the exercise; Score tempo
  printed once. Record: any wrong figure ______.

---

## Group G — Two phones, lease and household file

**G1 · Lease hand-over** (C-6.1.3, D148 to D152)
- Needs: both phones with the household file.
- Steps: use only Phone O for a week; then stop Phone O's app and let a week pass on Phone P.
- Expect: exactly one phone plans a week; the standby takes over after the owner's heartbeat goes quiet.
- If it fails: two plans for one week, or none. Reopens C-6.1.3.
- Record: week planned by ______ · plans found ______.

**G2 · Claim settle time** (D152) — time from first claim to the deadline decision. Record: ______ hours.

**G3 · Sync convergence** (C-6.1.1) — edit the roster on Phone O and a rating on Phone P within a minute.
- Expect: both edits present on both phones after a sync.
- Record: minutes to converge ______.

**G4 · Household file unreadable (G14)** (D108, D109)
- Steps: change the household passphrase on one phone only.
- Expect: the recipe box shows the specific reason and refuses edits; a Problem notice appears; nothing is overwritten.
- Record: Drive file modified time unchanged? yes / no.

**G5 · Shared week for the other phone** (D126, D127)
- Expect: Tablet P receives the sheet and her recipes from the week Phone O published.
- Record: arrived? yes / no.

**G6 · Person sheet and undo-first removal** (C-8.1.2, D141 to D147)
- Steps: remove Robin on Phone O; undo inside the window; on Phone P confirm it is read-only.
- Expect: Remove folds the corner; undo restores; Phone P has Back only.
- Record: ______.

**G7 · Roster order of rights** (D100) — fresh phone with no role yet gets owner rights but cannot create the file.

---

## Group H — Workers and schedules

**H1 · Worker names survive an update** (C-1.1.3) — update over an existing install; check scheduled work still runs. Record: ______.

**H2 · Read-back worker** — morning read happens after Foreword's delivery. Record: time ______.

**H3 · Workout week worker** — plan present by 06:45. Record: time ______.

**H4 · Meal week pass, hourly at five past, and the second request at thirty-five past** (D114, D123)
- Expect: a pass roughly every half hour; nothing due writes no trail line.
- Record: passes in 6 hours ______.

**H5 · Recipe hour** (D120 to D125)
- Steps: plan a dinner; watch for its recipe page in the hour before the cook block.
- Expect: the page arrives inside the hour on the cook's tablet.
- Record: minutes before block ______ · missed? yes / no.

**H6 · Saturday digest worker** (D168) — arrives from 04:00 Saturday, once; a failed upload retries.
- Record: arrival time ______.

**H7 · Battery and Doze** — workers survive overnight on both phones. Record: missed passes ______.

---

## Group I — Recipes, Plate, camera, Keep

**I1 · READ_RECIPE round trip on Phone O** (C-11.2.1, C-6.5.1)
- Steps: add a recipe; scan a card with Plate (one or several pages).
- Expect: title, ingredients and steps come back as text; no photo stored.
- If it fails: reopens C-11.2.1.
- Record: pages ______ · words uncertain ______ of ______.

**I2 · Plate signature permission and install order** (C-11.1.1, C-11.2.1)
- Steps: on a spare device install Constitution first, then Index; repeat the other order.
- Expect: the budget broadcast works in the order that matches the signing; no permission denial in logs.
- If it fails: the signature-level permission must be declared before the sender installs. Reopens C-11.1.1.
- Record: working order ______.

**I3 · Camera on Phone P** (C-6.5.2) — one photo per page; file deleted after reading; kill the app mid-capture and reopen: leftover swept (D162).
- Record: files left in cache ______.

**I4 · Scan confirm** (C-6.5.3) — only uncertain words asked; a clean scan shows once. Record: uncertain words ______.

**I5 · Typed recipe and the box under ten** (C-6.5.4, C-6.2.5, D106) — empty-state "3 of 10", Type it in.
- Record: ______.

**I6 · Cuisine and the pile** (C-6.2.1) — cuisine assigned silently; move by long-press. Record: wrong cuisines ______.

**I7 · Recipe heuristics on real OCR** (C-11.2.1) — RecipeRead was tested only on invented text.
- Record: cards read correctly ______ of 5.

**I8 · "I ate something else" accuracy** (C-6.6.1, C-6.6.2) — compare the estimate for three known meals. Record: ______ kcal vs known ______.

---

## Group J — Index budget contract

**J1 · Broadcast between separately installed apps** (C-11.1.1)
- Steps: set a monthly grocery budget in Index.
- Expect: Constitution's groceries page shows an over-budget flag in words once exceeded.
- Record: received? yes / no.

**J2 · Rejection without permission** — an unsigned test sender is refused. Record: ______.

**J3 · Dropped budget** (D163) — a budget dropped stays dropped until Index re-sends. Record: re-sent when ______.

---

## Group K — Notices, notifications, Flyleaf shelf

**K1 · Flyleaf's second row** (C-10.1.1, C-10.1.2) — the fifth book wraps to a second row on its own full ledge.
- Record: wraps at phone width ______ dp.

**K2 · Ribbon rose and slate** (C-10.2.1, C-10.2.2) — Problem notice = rose, a waiting scan = slate; no count.
- Record: colours seen ______.

**K3 · Partner's notifications** (C-9.1.3, D83, D155, D172) — Phone P posts the five (and the two added) notices, one each, fixed wording.
- Record: posted ______ of 7.

**K4 · Notification small icon** — renders, not a blank square. Record: ______.

**K5 · Dog-ear and Waiting page** (C-9.1.1) — most serious notice colours the fold; undo outranks. Record: ______.

---

## Group L — Live Claude

**L1 · Six voice seats** (C-9.2.1, C-9.2.2) — each seat appears; neutral fallback with no key; never an error. Record: seats seen ______ of 6.

**L2 · Taste judge live** (D134 to D137)
- Steps: deny a dinner with a written reason ("too spicy for Juniper").
- Expect: a lasting lesson is recorded or the words stay pending with a typed reason; no crash.
- Record: verdict ______ · pending after 3 passes ______.

**L3 · Template author** (D110) — proposed breakfast and lunch lines are plain and short. Record: ______.

**L4 · Illustration check** (D21) — new exercise figures pass the second check.

---

## Group M — Look and feel

**M1 · Scrapbook ground and scraps** (C-3.2.2, C-3.2.3) — compare to book pp. 11 to 14 at 360 to 430 dp. Record: differences ______.

**M2 · Tab cloths** (C-3.1.1) — Today, Workouts, Meals, Body, Settings distinguishable. Record: ______.

**M3 · Recipe box and pile** (C-6.2.1) — tilt and overlap look. Record: ______.

**M4 · Digest on the glass** (C-2.6.1 to C-2.6.4, D167 to D170)
- Expect: nine Body charts with dotted gaps, "Patterns, not causes" (only with 8 weeks), four planner-note lines.
- Record: pages ______ · charts legible? yes / no.

**M5 · Planner notes handwriting** (D170) — write in the four lines; next day the notes are read.
- Record: lines read ______ of ______.

---

## Group N — Constants nobody has evidence for

| Id | Constant | Where | What to bring back |
|---|---|---|---|
| N1 | Recovery thresholds (C-5.2.4) | see D8 | disagreements per fortnight |
| N2 | Pattern strength 0.7 over 8 weeks (D159) | digest | patterns shown, sensible? |
| N3 | Steady bands per unit (C-9.2.2) | Body sentences | sentences that read wrong |
| N4 | Taste judge limits 6 per pass and 5 attempts (D136) | deadline step | questions asked per pass |
| N5 | Taste ranking position (D131) | planner | repeats of disliked dishes |

---

## Group O — First real week's cost (C-13.1.2)

**O1** — add up the week's Claude calls (Diagnostics counts) and compare with R11's estimate.
- Record: calls ______ · cost ______ vs estimate ______.

---

## PBI coverage index

Every PBI waiting on a device in the progress file, and the checks that cover it:
C-2.2.2 F5 · C-2.2.3 F3 F4 F6 · C-2.3.1 F2 F9 · C-2.4.1 F8 · C-2.4.5 F8 · C-2.5.1 F7 · C-2.5.4 F7 ·
C-2.5.5 F7 · C-5.2.2 D8 · C-5.2.4 D8 · C-5.3.2 E2 E3 · C-5.3.3 E2 E4 · C-5.4.3 E5 · C-5.4.4 E5 ·
C-6.1.3 G1 · C-6.2.1 I6 M3 · C-6.2.5 I5 · C-6.3.1 F8 · C-6.3.2 F7 L2 · C-6.4.1 E3 · C-6.5.1 I1 ·
C-6.5.2 I3 · C-6.5.3 I4 · C-6.5.4 I5 · C-7.1.1 D1 · C-7.1.2 D2 D3 · C-7.1.3 D2 · C-7.2.1 D5 D6 ·
C-7.3.5 D4 · C-7.4.1 D7 · C-7.4.3 K3 · C-8.1.2 G6 · C-8.2.2 B1 B2 B3 · C-8.3.1 B1 · C-9.1.3 K3 C5 ·
C-10.1.2 K1 · C-10.2.1 K2 · C-10.2.2 K2 · C-11.2.1 I1 I2.
Also covered: C-1.1.1 A1 · C-10.1.1 K1 · C-10.1.3 C3 · C-10.1.4 C2 · C-11.1.1 J1 I2 · C-12.1.1 A2 A3 ·
C-6.6.1 C6 I8 · C-6.6.2 I8.

`DeviceTestPlanCoversProgressTests` fails if a "review (Device)" PBI in the progress file is not named
in this document.
