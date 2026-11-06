# P15 · Rotating the vault passphrase — technical finding

**Status:** draft for the owner's review. Nothing is built until it is approved.
**Scope (owner's ruling, 6 Nov):** P15 is the passphrase rotation and nothing else. The
Sabbath-gated backlog is dropped, not deferred. Both phones use the same passphrase, so the
rotation covers both people's installs and both people's Drives. P15 runs alongside P14 (the
watch); §7 says where the two touch.

## 1. Why

Two published versions of mockup artifacts carry the current passphrase. On its own the passphrase
opens nothing: an attacker would also need the encrypted files, which sit in the two private
Drives. So the exposure is real but needs two failures, not one. Rotating it closes it. Nothing in
this finding needs or records the passphrase, old or new.

## 2. What the passphrase locks

Every file below is a `VaultEnvelope` (PBKDF2-SHA256, 210,000 iterations, AES-256-GCM) under the
one passphrase. They were found by searching the code for `VaultEnvelope`/`SecretsCrypto`, not
from memory.

| File | Lives in | Written by | Reads before writing? |
|---|---|---|---|
| `storyworthtelling-secrets.json` (shared vault) | owner's Drive, shared to her | owner's installs only (`VaultOwnership`) | yes: `SecretsVaultService` |
| `storyworthtelling-personal.json` | **each person's own Drive** | that person's Score / Foreword | yes: `PersonalVaultAccess` |
| `storyworthtelling-pulp-household.json` | owner's Drive, shared | both Pulp installs | yes: `DriveHouseholdStore` (G14) |
| `storyworthtelling-constitution-household.json` | owner's Drive, shared | both Constitution installs | yes: `DriveHouseholdStore` (G14) |
| `storyworthtelling-index-household.json` | owner's Drive, shared | owner's Index | yes: `HouseholdSnapshotPublisher` |
| `storyworthtelling-pulp-usage.json` | owner's Drive | Pulp | yes: `DriveIndexUsageStore` |
| `storyworthtelling-index-snowball-history.json` | owner's Drive | `Tools/IndexHistorySeal` only; Index reads | n/a (the app never writes it) |
| `storyworthtelling-prayer-archive.json` | **each person's own Drive** (if Foreword prays there) | Foreword | yes: `DrivePrayerArchive` |
| `storyworthtelling-constitution-body.json` | **each person's own Drive** | Constitution | yes: `BodyBackup` |
| log batches in `storyworthtelling-logs` | owner's folder, shared to her | every head | n/a: sealed under the **derived log key**, see §5 |

Not locked by it, so out of scope: the Post ghost-archive backup (plaintext by decision, see
`ArchiveSnapshot`), the voice profile, the taste profile, the Score library backup.

**The finding that makes this safe:** every writer reads the file with its own passphrase first and
refuses to write if it does not open. So once a file is re-locked, an install still holding the old
passphrase **cannot overwrite it**. It reports "wrong passphrase" (or its household notice) and
carries on from local data until someone types the new one. The one gap is a write already under
way when the file is re-locked: read under the old key, then written seconds later. §4's verify pass
catches it.

## 3. What a rotation has to do

1. Re-lock every file in §2 under the new passphrase, **in both Drives**. The tool cannot reach her
   three files from the owner's account, and the owner's account cannot reach hers.
2. Do it all-or-nothing per file, and prove each one by reading it back. The prayer archive is the
   only copy, so a file is never left in a state that neither passphrase opens.
3. Type the new passphrase into every app that holds one, on both phones: up to **nine apps × two
   phones**. Each head keeps its own copy in its own private storage (`VaultPassphraseStore` for
   the native heads, the `VaultPassphrase` setting in the MAUI ones), so nothing can spread it from
   one app to the others. This is the cost the roadmap meant by "cheapest done once".
4. Replace the log loop's `SWT_LOG_KEY` (§5).
5. Get rid of the old ciphertexts Drive still keeps (§6).

## 4. Two ways to build it, and the recommendation

**A. A one-off console tool (recommended).** `Tools/VaultRotate`, on the pattern of
`IndexHistorySeal` and `LogKeyDerive`: run on the owner's own computer, both passphrases read from
the terminal without echoing, nothing written to disk, nothing logged but file names and
outcomes. It signs in to Drive itself (a desktop OAuth client with the `drive` scope; the reader's
existing client is read-only and stays that way). For each file in §2 it finds by name:

- opens with the new passphrase → already done, skip;
- opens with the old passphrase → re-lock under the new one, write, read back, confirm it opens;
- opens with neither → **stop and report**, touching nothing more.

Because of that three-way rule, running it again is always safe. That is how the verify pass works:
run it once, re-type the passphrase on the phones, then run it again a day later to catch any write
that got in during the change. It is run twice in all: signed in as the owner, then as her (on the
same computer, through her own browser sign-in), since her three files are in her Drive. The
snowball history is re-locked like any other file, so the original export is not needed.

**B. Re-lock from inside the apps.** When the passphrase field changes, each app keeps the previous
one. Any store that gets "wrong passphrase" with the new one tries the old one and, if that works,
re-writes the file. No computer and no OAuth set-up. Against it: every store in nine apps changes,
the old passphrase stays on the phones for an open-ended time, nothing knows when the rotation is
finished, and the shared vault can only be re-locked by the owner's phone. It also changes how the
passphrase field behaves, which puts it under the §03 mockup gate.

**Why A:** the rotation is one event every few years. A puts all of it in one tested place, leaves
the nine apps unchanged (a re-typed passphrase is already handled), and finishes in an hour with
a report that says so. B spreads one-off code through the suite permanently.

## 5. The log loop

`LogKey.Derive(passphrase, epoch)` makes the log key from the passphrase, so a new passphrase means
a new key on its own. **No epoch bump is needed**; the runbook already says "changing the vault
passphrase rotates the key too". What changes:

- The owner derives the new key with `Tools/LogKeyDerive` and replaces `SWT_LOG_KEY` in the `log-loop`
  environment.
- Batches already sealed under the old key read as `WrongKey`. The reader reports them and never
  trashes them, so they would sit in the folder for good. **Proposal:** upload and clear logs
  ("Send logs now" on each phone, then let a Sunday run take them) *before* rotating, so the old
  key has nothing left to read. Any that slip through can be trashed by hand once listed.

## 6. Old copies Drive keeps

Overwriting a non-Google-Docs file in Drive keeps the previous contents as a **revision**, normally
for 30 days (or 100 revisions). The suite never sets "keep forever" (searched: no `keepRevisionForever`
anywhere), so the old-passphrase copies will disappear by themselves within a month. Until then,
anyone with the old passphrase and access to that Drive could open last month's copy.
**Proposal:** once a file is re-locked and verified, the tool deletes its older revisions, so the
exposure closes the same day. This is one Drive call per revision and only ever touches files the
tool has just re-locked.

## 7. Where P15 touches P14

- **Shared code:** P15 adds a tool and **does not change** `VaultEnvelope`, `SecretsCrypto` or any
  store. If the watch session changes how files are locked, or adds a new locked file, that file has
  to be added to the tool's list, which is one line. Before the rotation runs, the list is checked
  again against the code on `main`.
- **Timing:** if the watch will hold the passphrase, rotating **before** it is paired saves one
  entry. If the watch is already paired, it is one more install to re-type it on.

## 8. Risks

1. **A file neither passphrase opens.** The tool stops. Nothing is lost, because nothing was written.
2. **A wrong new passphrase typed on a phone.** The app shows "wrong passphrase" and writes
   nothing, as it does today.
3. **The rotation half done** (the computer is closed mid-run). Each file is either old or new and
   both open, so running the tool again finishes the job.
4. **Forgetting an app on her phone.** It degrades as in risk 2 until someone notices. The Diagnostics
   export already reports "logs are waiting for the vault passphrase" for every head.
5. **The new passphrase lost.** This is the standing cost of encryption (prayer archive, body
   backup). Nothing changes, but it is worth saying at the moment a new one is chosen.

## 9. Decisions for the owner

1. **A (the tool) or B (inside the apps)?** Recommended: A.
2. **Delete the old revisions straight away (§6), or let Drive drop them in 30 days?**
   Recommended: delete them.
3. **Delete or replace the two artifact versions that carry the old passphrase?** Once the
   passphrase is rotated they hold nothing usable, so it is housekeeping, not security.
4. **Rotate the API keys in the vault as well (Claude, Gemini, Plaid, NYT, TIDAL and the rest)?**
   Not needed on what is known: the leak was the passphrase, not the vault file. Rotating them would
   be a precaution against the vault file itself having leaked, and nothing suggests it has.
5. **Before or after the watch is paired?** (§7)

## 10. If A is approved: the build

TDD as everywhere else. Tests run against an in-memory Drive and use made-up passphrases only:

- `Tools/VaultRotate` + `Tools/VaultRotate.Tests` (`Tools/VaultRotate.sln`), referencing
  `Storyworthtelling.Voice` for `VaultEnvelope`. The Drive client comes from the same
  `Google.Apis.Drive.v3` package `LogLoopReader` already uses.
- The pure part is `RotationPlan.Decide(fileContent, old, new)`: skip, re-lock, or stop. Every case in
  §4, a truncated file, and an empty passphrase are tested.
- A list of locked files, held equal to the code by a guard test: every `FileName` constant that a
  `VaultEnvelope` store uses must be on it, so a new locked file cannot be missed.
- Commands are `rotate` and `verify` (read-only: reports which passphrase opens each file).
  `--dry-run` is the default for `rotate` until `--write` is given.
- A short runbook in the tool's README covering the steps in order: send logs, run as the owner, run
  as her, re-type on each phone, replace `SWT_LOG_KEY`, then `verify` the next day.
