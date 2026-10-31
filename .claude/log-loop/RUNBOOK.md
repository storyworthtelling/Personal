# Log loop — the weekly reader's runbook

This is the procedure the scheduled Sunday session follows (LOG-LOOP-PLAN.md §5–§8). It is committed so it
is reviewed like code. The plan is the specification; where this runbook and the plan disagree, the plan
wins and this file is the bug.

**The session runs in the dedicated `log-loop` environment**, which holds two secrets and nothing else:

| Variable | What it is |
|---|---|
| `SWT_LOG_KEY` | The log key, derived from the vault passphrase with `Tools/LogKeyDerive` (§6.2). Never the passphrase. |
| `SWT_DRIVE_READ_TOKEN` | An *authorized user* JSON — `client_id`, `client_secret`, `refresh_token` — for the dedicated OAuth client granted `drive.readonly` only (O13). |

The connector is Google Drive only. It is used to list the folder (a cross-check) and to trash processed
files. Nothing else.

---

## The rules that hold whatever happens

1. **Plaintext never enters the working tree.** It lives under `$TMPDIR/log-loop/`. `LogLoopReader`
   refuses any path inside the repository; do not work around it (no copying, no `cat > ./…`).
2. **Synthetic reproductions only.** Describe a failure's *shape* in your own words first, then build the
   test from invented values: `example.com` addresses, `555-01xx` numbers, fictional names and lenders,
   made-up amounts. Never edit a test out of a copy of a real input, a real line or a real stack.
3. **The sanitizer is the only path from a log line to GitHub.** Anything quoted in a PR, an issue or a
   commit message goes through `LogLoopReader sanitize` first. Fingerprints are safe to publish; raw lines
   are not.
4. **The leak scan runs before every push**, and any hit blocks it (§6.1 layer 4).
5. **Never push to `main`, never merge.** The `log-loop-automerge` workflow decides auto-merge; the session
   only opens PRs. Never close and reopen a PR, never push an empty commit, never rewrite history on a
   branch you did not create.
6. **Access tokens, refresh tokens, PKCE verifiers, client secrets and passwords are never written
   anywhere** (A3), even when a log line seems to carry one.
7. **Trash, never delete** (A1). Trash is the last step of the run.

---

## 1. Fetch

```bash
dotnet run --project Tools/LogLoopReader -- fetch          # → $TMPDIR/log-loop/plain + manifest.json
```

It prints counts only (`Decrypted`, `WrongKey`, `Corrupt`). Then cross-check with the connector:
`search_files` for files in the `storyworthtelling-logs` folder whose name starts `swt-log.` — the counts
should agree. A disagreement goes in the report.

- `WrongKey`: another key, or an older `logKeyEpoch`. Report it; **never trash it**.
- `Corrupt`: not an envelope, or not a batch inside one. Report it; **never trash it**.
- A file that has failed to decrypt for **four weeks** is listed for the owner by name.

## 2. Digest

```bash
dotnet run --project Tools/LogLoopReader -- digest          # → $TMPDIR/log-loop/digest.md + digest.json
```

Read `digest.md` first. It groups every line by fingerprint (an exception's type and top three own
frames; a tap verdict's screen, control and verdict; a sign-in's flow kind and diagnosis; otherwise a
message template), with counts, phones and first/last seen. Open raw lines under `$TMPDIR/log-loop/plain/`
only for the groups you triage.

## 3. Last week first

- Find open `claude/logfix-*` PRs. Their `log-loop` blocks name fingerprints; do not open a second PR for
  one. If an app's PR from last week is still open (CI red, say), add to that branch.
- For every fingerprint merged since the last run, decide: **Gone** (absent this week), **Still present**
  (the fix failed — reopen it as a new fix on the same fingerprint), or **New since merge** (a fingerprint
  first seen after a fix merged in that app — a possible regression, listed with the merge it followed).
  A regression traced to a merged fix is itself a log-derived bug fix: its fix, or a revert of the merged
  commits, goes into that app's next PR.

## 4. Triage (§7.1)

Every fingerprint gets exactly one outcome: **Fix**, **Report** or **No action**.

| Class | Evidence | Outcome |
|---|---|---|
| C1 Crash in our code | Crash entry, ANR, exit `CRASH`/`ANR` with our frames | Fix |
| C2 Logic error | Broken own contract, impossible state, own-format parse failure | Fix |
| C3 Unexpected third-party input | Feed or PDU shape we don't handle | Fix with a synthetic reproduction; else Report asking for a capture |
| C4 Retry or backoff wrong | Re-sent with no wait, budget unspent, re-billing | Fix |
| C5 Transient network | DNS, timeout, offline, reset | No action, unless ≥ 20% of a source's attempts that week fail, then Report |
| C6 Upstream outage or rate limit | 429, 5xx, Plaid `INSTITUTION_DOWN` | No action; > 3 days running, Report |
| C7 Carrier or platform rejection | MMS refused, role lost, system kill | Report; fix only if our handling is wrong (then C2) |
| C8 Credential rejected | 401/403, `ITEM_LOGIN_REQUIRED` | Report (owner action) |
| C9 Owner action pending | No passphrase, no profile | No action, listed once |
| C10 Logging defect | Missing context, or sensitive data where it shouldn't be | Fix |
| C11 Cost or performance | Token counts, retry storms | Report with numbers |
| C12 Unclassifiable | | Report |
| C13 Dead or failing control | `DEAD TAP` / `ACTION FAILED` / `TAP THREW` with a code cause | Fix; if the cause can't be found from code, Report with screen + control |
| C14 Repeated presses on a working control | `REPEATED TAP` where each press did act | Report as UX — not a bug, never a loop fix |
| C15 Presses on non-controls | `TAPS ON NON-CONTROL` | Report as UX — not a bug, never a loop fix |
| C16 Sign-in did not return a token | `NoReturn`, `ProviderError`, `StateMismatch`, `ExchangeFailed`, blocked `Hop` | Our bug → Fix; provider or portal configuration → Report with the exact owner checklist; user cancelled → No action |

Record each file's progress in `$TMPDIR/log-loop/manifest.json`: `fetched` → `decrypted` → `triaged` →
`done`. A file is `done` when every fingerprint in it has an outcome.

## 5. Fix — one branch and one PR per app per week (O15)

```bash
git fetch origin main
git checkout -b claude/logfix-<yyyy-MM-dd>-<app> origin/main     # the Sunday's date, the app's name
```

For **each** Fix fingerprint, two commits, both ending with the trailer:

1. **The test**, written first and red: a synthetic reproduction of the shape (rule 2). For head code that
   cannot run here, a source-level guard or a pure-logic extraction (the repo's existing pattern).
2. **The fix**, which turns it green.

```
Log-Loop-Fingerprint: <the 64-hex fingerprint from the digest>
```

Every commit on the branch carries a trailer naming one of the PR's fingerprints. A commit without one — a
refactor riding along, a tidy-up — holds the whole PR for the owner. Never merge `main` into the branch;
rebase if you must (a merge commit holds the PR too).

Before pushing:

```bash
dotnet test <the app's test project>                               # CLAUDE.md names it per project
dotnet build <the app's head> -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false   # when head code changed
git diff origin/main...HEAD > "$TMPDIR/log-loop/branch.diff"
dotnet run --project Tools/LogLoopReader -- leakscan --plain "$TMPDIR/log-loop/plain" --base origin/main \
  --diff "$TMPDIR/log-loop/branch.diff" --title "$TMPDIR/log-loop/title.txt" --body "$TMPDIR/log-loop/body.md"
dotnet run --project Tools/LogLoopReader -- leakscan --ci --diff "$TMPDIR/log-loop/branch.diff" --branch claude/logfix-<date>-<app>
```

Then **reread** the test fixtures, the commit messages and the PR body against the sanitizer's categories
(numbers, addresses, phones, amounts, URLs and OAuth parameters, names) — the scan cannot catch a
paraphrase ("the owner's card at that bank"). Say in the PR body that you did.

### The PR

Title: `Log loop — <app> fixes, week of <yyyy-MM-dd>`. Body, per fix:

```
<!-- log-loop
fingerprint: <sha256>
class: C13
source: swt-log.<app>.<installId>.<range>.json
occurrences: 14
phones: owner
first-seen: 2026-10-24
-->
```

followed by, in prose: what the shape was (sanitized), what the fix does, its **verification mark**
(§8: *Test-verified*, *Compile-verified* or *Device-pending*), and — as information only (§6.4) — any
sensitive area it touches: the FocusPhone lockdown or the update channel, Index's money, prayer, the vault,
body data, a sign-in flow. If a fix edits one of the repo's guards (`FrozenInvariantsTests`,
`SnowballSheetTests`, `BodyStaysHomeTests`, `PrayerIsolationTests`, the Pulp and Constitution invariants,
`NudgeNeverWriteTests`), say so plainly. End with the attribution footer.

Do **not** merge, approve or enable auto-merge. The `log-loop-automerge` workflow does that once the report
issue (step 6) lists the fingerprints and every check is green.

## 6. Report — one issue per run (§5.5)

Title: `Log loop — week of <yyyy-MM-dd>` (the same date as the branches). Body:

- counts per app and class;
- the top dead or repeated taps by screen and control **label only, sanitized**;
- sign-in diagnoses by flow kind;
- PR links, and each fix's verification mark;
- last week's merged fingerprints: Gone / Still present / New since merge;
- untrashed files and why (`WrongKey`, `Corrupt`, still triaging);
- **no raw log line** — only sanitized shapes;

and the machine-readable block the gate reads, listing every fingerprint whose outcome is **Fix**:

```
<!-- log-loop-report
week: <yyyy-MM-dd>
fix: <sha256>
fix: <sha256>
-->
```

End with:

```

---
_Generated by [Claude Code](https://claude.ai/code)_
```

Run the leak scan over the issue body (`--body`) before posting it.

## 7. Trash — the last step

Only after the report issue is posted. Trash, through the connector's `trash_file`, every file the manifest
marks `done`. Never a `WrongKey` or `Corrupt` file. A crash before this step leaves the files in the folder
for next week, which is the intended failure.

Her phone's files (Phase 4) are owned by her and cannot be trashed by this session: write the names of the
processed ones to `swt-log-processed.json` in the folder instead, and her phone trashes its own.

## 8. Push summary

The trigger's push notification is one sentence: how many fixes, reports and files, e.g. *"Post: 2 fixes in
PR #N, 3 reports; 14 files read and trashed."*

---

## If personal data lands in a commit

Stop. Do **not** cover it with a follow-up commit — a later commit hides it from the tree, not from the
history. Disable auto-merge on that PR if it has not merged (the GitHub tools' `disable_pr_auto_merge`). Say so at the top of the report issue. Removing it from history is the
owner's decision (CLAUDE.md, "This repository is public").

## Rotating the key

`LogKey.Epoch` is in every batch header. To rotate: bump it in a reviewed PR (not a loop PR), ship, derive the
new key on the owner's own machine with `dotnet run --project Tools/LogKeyDerive -- --epoch <n>` (passphrase
on stdin), and replace `SWT_LOG_KEY`. Files sealed under the old epoch read as `WrongKey`: report them, do
not trash them. Changing the vault passphrase rotates the key too.
