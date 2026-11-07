# Suite audit, November 2026

A read-only audit of the nine phone apps (Flyleaf, Score, Post, Plate, Pulp, Constitution, Folio, Index, Foreword) and the shared libraries. Separate agents produced each report by reading the code. Nothing was changed while auditing, and nothing here has been checked on a device. Treat each finding as something to confirm before fixing, especially the ones about how Android behaves.

| # | Report | What it covers |
|---|---|---|
| 01 | [Duplication and divergent strategies](01-duplication.md) | Copied code, and the same job done different ways in different apps |
| 02 | [Cross-app UI consistency](02-ui-consistency.md) | Similar features presented differently |
| 03 | [Orphaned and incomplete code](03-orphaned-incomplete.md) | Code that is built and tested but never called, half-wired features, dead packages |
| 04 | [Design-system and mockup fidelity](04-design-fidelity.md) | Places where DESIGN.md or the mockups aren't followed, and gaps in the leak guards |
| 05 | [Efficiency](05-efficiency.md) | Data, battery, paid-API and CI-minute waste |
| 06 | [Authorial voice inventory](06-voice.md) | Every voiced seat: its prompts, fallbacks, rendering and cost |
| 07 | Privacy and the public repo | Kept out of the repository on purpose. The owner holds it |
| 08 | [Logging thoroughness](08-logging.md) | Whether the log loop can actually diagnose failures |
| 09 | [Cross-app contracts](09-cross-app-contracts.md) | Broadcasts, intents and shared Drive files, checked from both sides |
| 10 | [Accessibility](10-accessibility.md) | Contrast, touch targets, screen readers, font scaling, timeouts |
| 11 | [Error handling and offline](11-error-handling.md) | What the user sees, and whether data survives, when things fail |
| 12 | [Fixed UI wording](12-wording.md) | §14 copy rules, and a glossary for using one word per concept |
| 13 | [Docs and package drift](13-docs-and-packages.md) | CLAUDE.md and DESIGN.md against the code, and NuGet versions and vulnerabilities |

## Fix first

These put data at risk, or stop a feature from working at all.

1. **Drive reads that fail are treated as empty files** (11 §1). `DriveTextFile.ReadAsync` ignores the download result. One failed read followed by a settings save can overwrite the shared vault with a single app's section.
2. **Every permissioned app-to-app broadcast is likely dropped** (09 C1/C2). Each receiving app declares its signature permission but never requests it for itself. Balances, usage, hidden apps, watch sessions and the budget would never arrive, and the locked hand-off would always be refused. Nothing is logged on either side. This needs a check on a device.
3. **Backups are overwritten with whatever the phone holds** (11 §2, §17). If the restore didn't run, the first new item replaces Post's archive backup, or Score's library backup, in Drive.
4. **Older app builds erase newer vault fields** (09 H1, 11 §3). The vault has no extension data and no schema check.
5. **Folio's vault save can blank the reMarkable token** that Foreword published (01 §2).
6. **The log reader can't parse the MAUI apps' logs** (01 §1, 08 §1). So the Sunday triage sees almost nothing from Index, Folio or Foreword, and missed workers are never detected (08 §2).
7. **The restricted-browser hand-off names a class that doesn't exist** (09 H2). Links from Post and Plate fall back to a plain VIEW intent.
8. **Flyleaf's home can't be used with a screen reader or switch access** (10 §1), and the lockdown leaves no other launcher.
9. **Index never purges old transactions** (03 §32). Pulp's mis-landing and sports-feed notices are never raised (03 §28–29).
10. **Vulnerable packages:** SQLitePCLRaw, System.Security.Cryptography.Pkcs, MailKit/MimeKit and AngleSharp (13 B).

## Themes across reports

- **Shared pieces that would remove most of the drift:** one Claude client, one sealed-Drive-file policy, one Google token and Drive factory, a MAUI action card / bottom bar / diagnostics page, one DogEar decision type, and time helpers in a BCL library (01, 02, 04).
- **Guards that only cover some apps:** the type-size, NoKeys, InventedData, worker-trail and time-parity guards each cover only part of the suite (04, 08, 13).
- **Pine where rose belongs:** the MAUI kit's `Accent` is now Action pine, but error styles still point at it (04 §1, 10 §12).
- **Toasts and dialogs:** about 70 remain, against §7 (02 §1, 04 §26).
- **The voice:** only about 15 of the roughly 40 voice-looking seats actually use the profile. Fallback, model tier and caching rules differ by app (06).
