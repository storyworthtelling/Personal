# Cross-app contract audit: nine Android apps, one keystore

This was a read-only audit. Nothing in the repo was edited, and the report contains no personal data.

Most of the names agree between the two sides. Every action, component, extra key and file name I compared matches on both ends. But all six broadcast contracts that are protected by a signature permission fail at delivery, because the receiving app never asks for (`uses-permission`) the permission it declares. Five are dropped outright. The sixth (the locked hand-off) gets refused every time, so Pulp always falls back to the plain hand-off. Android drops a refused broadcast without telling the sender, and every courier swallows errors, so no app log will ever show this. The content-read guards only check that the names agree, so they all pass.

Severity: **C** critical, **H** high, **M** medium, **L** low.

## Summary table

| # | Contract (sender → receiver) | Names agree | Content guard on both sides | Absent receiver handled (silent + logged) | Payload versioned | Exported / queries / permission correct |
|---|---|---|---|---|---|---|
| 1 | Post → Index balance (`IndexHandoff` / `BalanceReceiver`) | ✓ | ✗ Index side only; Post side retypes a literal (`MoneyMailTests:143`) | ✓ silent; ✗ a refusal is never observable | ✗ (optional `sender` key added compatibly) | ✗ **C1, C2** |
| 2 | Plate statement read → Index (`READ_STATEMENT`) | ✓ | ✗ | n/a | ✗ | ✗ nothing calls it (M3) |
| 3 | Post card read → Plate (`READ_CARD`) | ✓ | ✗ | n/a | ✗ | ✗ nothing calls it (M3) |
| 4 | Pulp → Index usage broadcast (`UsageReceiver`) | ✓ | ✓ (Pulp's test reads the Index manifest; Index's `PulpUsageContractTests` reads Pulp) | ✓ silent | ✗ | ✗ **C1**, no `<queries>` (M1) |
| 4b | Pulp → Index usage file in Drive (`storyworthtelling-pulp-usage.json`) | ✓ | ✗ Index side only (`PulpUsageFileGuardTests`) | ✓ | ✓ v1, strict equality | n/a; last writer wins (M6) |
| 5 | Pulp → Flyleaf hidden apps | ✓ | ✓ | ✓ | ✗ | ✗ **C1** |
| 6 | Flyleaf → Pulp playback sessions | ✓ | ✓ | ✓ | ✗ | ✗ **C1** |
| 7 | Pulp → Flyleaf locked hand-off (ordered broadcast) | ✓ | ✓ | ✓ falls back to plain hand-off | ✗ | ✗ **C1** (always refused, so always falls back) |
| 8 | Index → Constitution budget | ✓ | ✓ | ✓ checks installed first | ✗ | ✗ **C1**, and a guard *enforces* the bug |
| 9 | Constitution → Plate `READ_RECIPE` (activity) | ✓ | ✓ (Constitution reads Plate; Plate pins literals) | ✓ catches not-found and SecurityException | ✗ | ✓ caller holds the permission; install order (M2) |
| 10 | Constitution `NOTICE_ROLE` extra → Flyleaf | ✓ | ✗ Flyleaf side only; Flyleaf CI doesn't watch `Constitution/**` | ✓ ribbon falls back | ✗ | ✓ |
| 11 | `LockedOpenCatalogue` = Pulp `ServiceCatalogue.Public` | ✓ | ✓ | n/a | n/a | n/a |
| 12 | Keep package (Constitution ↔ Flyleaf allowlist) | ✓ `com.google.android.keep` | ✓ (`GroceryShareTests:105` reads `PolicyContext`) | ✓ ActivityNotFound becomes `KeepNotInstalled` | n/a | ✓ |
| 13 | `PolicyContext` package ids vs each app's identity | ✓ | ✓ partial (`ShelfCoverArtTests`, contract tests) | n/a | n/a | n/a |
| 14 | Update channel version bands | ✓ | ✗ table vs workflow checked only for Pulp and Constitution (L5) | n/a | n/a | n/a |
| 15 | Post/Plate → Flyleaf restricted browser (`RestrictedBrowserHandoff`, **undocumented**) | ✗ **H2** | ✗ | ✓ falls back to a plain VIEW intent | n/a | ✗ |
| 16 | Secrets vault / personal vault in Drive | ✓ | partial | ✓ | envelope only, not schema | ✗ **H1** (older apps drop newer fields) |
| 17 | Household files (Pulp, Constitution); Index household snapshot | ✓ | ✓ | ✓ | ✓ format + schema, newer refused | ✓ re-read after write; one writer for the snapshot |

## Critical

### C1 — Every permissioned broadcast is dropped: the receiving app does not hold its own permission
- **Mechanism.** `Context.sendBroadcast(intent, receiverPermission)` and `sendOrderedBroadcast(…, receiverPermission, …)` deliver only to a receiver whose app **holds** that permission. Declaring a `<permission>` does not grant it to the declaring app; it needs `<uses-permission>` too.
- **Every sender passes the receiver's own permission:**
  - `FocusPhone/Post.Android/IndexCourier.cs:34`: `SendBroadcast(intent, IndexHandoff.Permission)`
  - `Pulp/Pulp.Android/Usage/IndexUsageCourier.cs:42`, `Pulp/Pulp.Android/Hidden/HiddenAppsCourier.cs:37`, `Pulp/Pulp.Android/LockedOpen/LockedOpenCourier.cs:75-82`
  - `FocusPhone/Flyleaf.Android/PlaybackSessionWatcher.cs:187`
  - `Ledger/Ledger.Android/ConstitutionBudgetSender.cs:63`
- **No receiver requests its own permission.** Each one only declares it:
  - Index: `Ledger/Ledger.Android/Platforms/Android/AndroidManifest.xml:18,22`, with no `uses-permission` for `RECEIVE_BALANCE` or `RECEIVE_USAGE`.
  - Flyleaf: `FocusPhone/Flyleaf.Android/HiddenAppsReceiver.cs:10` and `LockedOpenReceiver.cs:10`. `ManifestPermissions.cs` doesn't request either.
  - Pulp: `Pulp/Pulp.Android/Sessions/SessionReceiver.cs:11`, with no request.
  - Constitution: `Constitution/Constitution.Android/Budgets/BudgetReceiver.cs:12`. Its comment says "Declared, not requested", and `Constitution.Tests/Contracts/BudgetManifestTests.cs:57-63` and `:91-92` assert that it is never requested. So the guard locks the bug in.
- **What happens on a phone:**
  - Balances, usage counts, hidden-app sets and watch sessions never arrive. The system log shows only "Permission Denial … requires X due to sender".
  - Locked hand-off: the final receiver runs with its starting code `ResultRefused`, so Pulp always falls back to the plain hand-off and locked mode never starts.
  - Budget: `BudgetSendPlan.AdvancesRecord` advances `ConstitutionBudgetLastSent` (`ConstitutionBudgetSender.cs:32-35`) even though nothing arrived. Even after a fix, Index won't re-send until the figures change.
- **Fix:**
  - Each receiver app adds `[assembly: UsesPermission(<its own permission>)]`: Index (`RECEIVE_BALANCE`, `RECEIVE_USAGE`), Flyleaf (`RECEIVE_HIDDEN`, `RECEIVE_LOCKED_OPEN`), Pulp (`RECEIVE_SESSIONS`), Constitution (`RECEIVE_BUDGET`).
  - Update `BudgetManifestTests`, and Constitution's 17-permission count, to 18.
  - Clear the stored `ConstitutionBudgetLastSent` once.
  - Add one guard: for every `SendBroadcast(intent, P)`, the target app's head requests P.
  - Keep the `receiverPermission` argument rather than dropping it. It is what stops a differently signed app squatting on an absent package id from receiving the data.

### C2 — Post → Index balance: the sender doesn't hold the permission either
- `BalanceReceiver.cs:35-39` sets `Permission = …RECEIVE_BALANCE`, so the **sender** must hold it. Post's `ManifestPermissions.cs` has no such `uses-permission`; the only reference anywhere in Post is `IndexCourier.cs:34`.
- `IndexHandoffContractTests.cs:56-62` even says the permission is "never grant[ed] … to anyone, including Post", but no test checks that Post requests it.
- **Fix:** add `[assembly: UsesPermission(IndexHandoff.Permission)]` to Post, plus a guard. Without both C1 and C2 fixed, no balance from mail ever reaches Index.

## High

### H1 — Older app builds silently delete vault sections and fields they don't know
- `Storyworthtelling.Voice/Secrets/SecretsVault.cs` (`SecretsVault`, its section records, `PersonalVault`) has no `[JsonExtensionData]`. System.Text.Json drops unknown properties when it reads, and `SecretsVaultService.PublishAsync` (`:39-67`) writes `merge(result.Vault)` back.
- `SecretsEnvelope.CurrentVersion` versions the envelope only, not the vault's contents.
- **What happens:** an APK built before the Pulp or Constitution sections (or before any new field) saves its settings and erases those keys and logins from Drive. The same applies to the personal vault (Foreword `VaultSync.cs:60-64`, Score, Constitution).
- **Made worse by CI:** `build-flyleaf.yml`'s path filter lacks `Storyworthtelling.Voice/**`, but Flyleaf and Score ship it through `Codex.Ui.Android`. So a schema change never rebuilds or publishes them, and their old copy stays on the phone.
- **Fix:**
  - Add `[JsonExtensionData]` dictionaries on every vault record so unknown fields round-trip.
  - Add a vault `schema` number; refuse to write when the file's number is newer.
  - Add `Storyworthtelling.Voice/**` to the Flyleaf workflow's path filter.

### H2 — The explicit restricted-browser hand-off names a class that doesn't exist (undocumented contract)
- `FocusPhone/Codex.Domain/Policy/RestrictedBrowserHandoff.cs:16` uses `Activity = "Flyleaf.RestrictedBrowserActivity"`. But `FocusPhone/Flyleaf.Android/RestrictedBrowserActivity.cs:25` declares `[Activity(...)]` with no `Name`, and the repo sets no `AndroidPackageNamingPolicy`.
- .NET for Android therefore generates the class name as `crc64<hash>.RestrictedBrowserActivity`.
- Callers: `Post.Android/LinkOpener.cs:19`, `Plate.Android/ScanArea.cs:484`.
- **What happens:** opening the named activity throws ActivityNotFound, and the code falls back to a plain VIEW intent. That is exactly the route the class says verified App Links can steer to full Chrome, so the containment fix does nothing. No test covers it.
- **Fix:** add `Name = PolicyContext.LauncherPackage + ".RestrictedBrowserActivity"` (PolicyManager pins by `typeof`, so it re-pins the next time policy runs), plus a guard. Document the contract in CLAUDE.md.

## Medium

### M1 — Package visibility (Android 11+)
- Pulp's manifest (`Pulp/Pulp.Android/Properties/AndroidManifest.xml:4-6`) queries only Flyleaf. It does not hold `QUERY_ALL_PACKAGES` and has no `<package>` entry for `com.storyworthtelling.ledger`.
- Explicit broadcasts are resolved through `queryIntentReceivers`, which filters by the sender's visibility. So the usage broadcast to Index is likely dropped even after C1 is fixed. This needs checking on a device.
- Post, Flyleaf, Plate and Score hold `QUERY_ALL_PACKAGES`. Index queries Constitution (`AndroidManifest.xml:30`).
- Activity starts (Keep, Plate's recipe read) are not affected.
- **Fix:** add `<package android:name="com.storyworthtelling.ledger"/>` to Pulp. Declaring the query for every contract target costs nothing.

### M2 — Each permission is declared by one app only (install order and uninstall)
- Each permission has exactly one declaring app:
  - `RECEIVE_BALANCE` and `RECEIVE_USAGE`: Index
  - `RECEIVE_HIDDEN` and `RECEIVE_LOCKED_OPEN`: Flyleaf
  - `RECEIVE_SESSIONS`: Pulp
  - `RECEIVE_BUDGET`: Constitution
  - `READ_RECIPE`: Plate
- Plate's own comment (`Plate.Android/ManifestPermissions.cs:31-35`) states the trap: an app that requests a permission before its declaring app is installed doesn't get it until reinstalled. Whether that still holds varies by Android release, so it needs checking on the phone. Uninstalling the declaring app also revokes the grant from every user of it.
- **Fix:** declare each `<permission>` in **both** apps (same signer, allowed since API 21) as well as requesting it.

### M3 — Contracts documented as live that have no caller
- `READ_STATEMENT` and `READ_CARD` (`Codex.Domain/Plate/ReadRequest.cs:25-26`) have no sender anywhere. Neither Index nor Post starts them; `grep` finds only tests.
- CLAUDE.md still presents the statement read as Plate's half of the Index hand-off ("Index is P11, so no live counterpart"). P11 has shipped and there is still no counterpart.
- `BalanceReceiver.cs:28,88` mentions "an old Plate statement send", but Plate never broadcasts; it answers through `SetResult` (`ErrandActivity.cs:505-521`).
- `ErrandActivity` is exported with **no permission** on those filters (`:35-49`), so any app could start a statement read and get back the balance the owner confirmed.
- **Fix:** either wire Index (and Post) as callers or mark the contract unbuilt. Move the read filters to a signature-guarded activity, as `RecipeReadActivity` already does.

### M4 — CI path filters mean the one-sided guards often don't run
- `build-index.yml` doesn't watch `FocusPhone/Codex.Domain/**` (`MoneyMail.cs`), `Pulp/**` or `Constitution/**`. A change to Post's `IndexHandoff` runs only Codex.Tests, which has no test reading Index's files.
- `build-flyleaf.yml` doesn't watch `Constitution/**` or `Pulp/**`. Constitution's notice-role test is literal-only (`NoticeNotificationTests` says "literals here on purpose"), so changing `NOTICE_ROLE` passes Constitution's CI and Flyleaf's never runs.
- `build-constitution.yml` doesn't watch `Ledger/**`, `Storyworthtelling.Household/**`, `Remarkable`, `Paper` or `Handwriting`. Constitution builds against the household library, so a format bump can reach Pulp's APK but not Constitution's. It is safe, because a newer format is refused loudly, but the household data is stuck until Constitution rebuilds.
- `build-index.yml` likewise omits `Storyworthtelling.Updates`, `Remarkable`, `Paper` and `Handwriting`.
- **Fix:** add the partner app's contract files to each workflow's paths, or run every contract guard in `log-loop-guard.yml`.

### M5 — Index logs balance amounts and bank names
- `BalanceReceiver.cs:92` and `:117` log "received balance {balance:0.00} … from '{sender}'" into the file logger, which the weekly log loop uploads and a Sunday session reads.
- Constitution's budget receiver deliberately logs no amounts (`BudgetReceiver.cs:22-23`).
- **Fix:** log the shape only, e.g. "unroutable: no sender" / "no account matched".

### M6 — Shared Drive files: concurrent writes and newer versions
- `IDriveTextFile` (`Storyworthtelling.Voice/IDriveTextFile.cs:6-13`) has no conditional write. `DriveTextFile.WriteAsync` (`:69-104`) is a blind update, so every file is "last writer wins" at the transport.
- **Household files** handle this correctly: they merge, re-read and re-push (`HouseholdSync`, `HouseholdPublishOutcome.LeftForNextPass`) and refuse a newer format (`HouseholdDocumentCodec.cs:102`).
- **Pulp usage file** (`Pulp.Infrastructure/Usage/DriveIndexUsageStore.cs`) reads, merges and writes once with no re-read. Two phones publishing at once can roll back a month's count until the next hand-off. Both sides refuse anything but version 1 (`IndexUsageFile.cs:101`, `Ledger.Domain/Usage/PulpUsageFile.cs:31`). That is safe, but a v2 writer blinds every older Index until it updates.
- **Secrets vault:**
  - **Concurrent saves:** two apps on the owner's phone publishing at the same time can lose a section; there is no re-read.
  - **Second-copy risk:** if the vault reads as Empty (file not visible yet, search miss, wrong scope), a write creates a **second** vault. `DriveFileChooser` then prefers the newest owned copy, and the other secrets seem to vanish.
- **Single writer, low risk:** the Post archive (restore only onto an empty archive), the Index household snapshot (versioned; `HouseholdSnapshotReader.cs:47-60`), the voice profile (Folio writes), and the logs folder (never overwritten).
- **Fix:** have `IDriveTextFile` compare `headRevisionId` or `modifiedTime` before an update and retry the merge on a mismatch. Re-read after write for the usage file and the vault. Treat an Empty vault on an install that has seen one before as "do not create".

## Low
- **L1 — Sends can't report failure.** Every courier treats "no receiver" as success, because `SendBroadcast` reports nothing. This is why C1 was never noticed. Pulp's hidden-apps courier even returns `true` (`HiddenAppsCourier.cs:21-24`). Before sending, check `PackageManager.CheckPermission(P, targetPkg)` and that the target is installed, and log a refusal. Or use an ordered broadcast with a result code, as the locked hand-off does.
- **L2 — No version extra on any broadcast.** Payloads are forward-compatible only because new keys are optional and unknown ones are ignored (the balance `sender` key was added this way). A change in meaning, such as count 0 meaning "remove", cannot be negotiated. Add an integer `v` extra.
- **L3 — `BalanceReceiver` can lose the database write.** It writes to EF from `async void` without `GoAsync` (`:96-125`), so the process can be reclaimed mid-write. `SessionReceiver` already uses `GoAsync` correctly.
- **L4 — Pulp has no guard for the usage file.** Only Index's `PulpUsageFileGuardTests` holds the names equal, and Pulp's CI never runs it.
- **L5 — Band table vs workflows mostly unguarded.** `SuiteVersionBands` (`Storyworthtelling.Updates/VersionCode.cs:90-128`) is checked against its workflow only for Pulp (`PulpWorkflowTests:33`) and Constitution (`ConstitutionWorkflowTests:27`). The values agree today: Flyleaf, Score, Post, Plate and Index use band 1; Foreword 7; Folio 8; Pulp 9; Constitution 10. But a drift in the other six workflows would be unguarded, which is exactly how the Folio refusal happened.
- **L6 — CLAUDE.md is out of date in three places:**
  - It says the balance hand-off carries "balance and date only"; the code now also sends `sender` (`MoneyMail.cs:191-263`).
  - It says "Foreword no longer writes the PersonalVault at all"; it does (`Foreword.Android/VaultSync.cs:60-64`).
  - It presents the statement read as live (see M3).
- **L7 — Contracts in code with no documentation:**
  - `RestrictedBrowserHandoff` (H2).
  - Post → Plate attachment and Rolodex hand-offs (`Post.Android/Attachments.cs:81-83`, `RolodexArea.cs:168,255`).
  - Plate's scan delivery by package name (`Plate.Android/ScanArea.cs:388`).

## Where the two sides agree
Both sides' constants match byte for byte. Each contract below lists Side A, then Side B.

- **Balance:**
  - Package, receiver, action and permission are `com.storyworthtelling.ledger` / `….BalanceReceiver` / `….action.BALANCE` / `….RECEIVE_BALANCE`: `MoneyMail.cs:217-230` and `BalanceReceiver.cs:36-49`.
  - Extras are `balance` (decimal as text, `0.00`, invariant culture, parsed as `decimal`), `asOf` (`yyyy-MM-dd`) and an optional `sender`.
- **Usage:**
  - Package, receiver, action and permission: `IndexUsageContract.cs:94-97` and `UsageReceiver.cs:21-28`.
  - Extras are `service`, `month` (`yyyy-MM`), `count` (integer as text), `lastOpened` (`yyyy-MM-dd`) and `merchants` (separated by `|`): `IndexUsageContract.cs:99-103` and `UsageReceiver.cs:29-33`.
- **Hidden apps:** `HiddenApps.cs:23-32` and `Pulp…/HiddenAppsHandOff.cs:21-29`. The `packages` extra is a JSON string array, and the receiver refuses garbled input rather than clearing the stored set.
- **Sessions:** `PlaybackSessions.cs:20-45` and `SessionHandOff.cs:14-39`. Fields are `package`, `title`, `positionMs`, `durationMs`, `state` (`playing` / `paused` / `stopped`).
- **Locked hand-off:** `LockedOpen.cs:21-49` and `LockedOpenHandOff.cs:18-43`. Extras are `package` and `link` (max 2048). Result codes are −1 started, 0 refused.
- **Budget:**
  - Package, receiver, action and permission: `ConstitutionBudgetContract.cs:10-17` and `ConstitutionIdentity.cs:180-189`.
  - The amount is sent as `0.00` invariant text and parsed with `^[0-9]{1,15}(\.[0-9]{1,6})?$` (`BudgetPayload.cs:23-24`); the month is `yyyy-MM`.
- **Recipe:**
  - Action, activity and permission: `ReadRequest.cs:34-51` and `RecipeReadContract.cs:31-49`.
  - Extras are `title`, `ingredients` and `steps` (JSON arrays).
  - Plate's activity has `Exported=true` and `Permission=READ_RECIPE`; Constitution requests the permission (`RecipeScanPermission.cs:8`) and catches ActivityNotFound and SecurityException (`ScanRecipeLauncher.cs:32`).
- **Notice role:** the key `…constitution.NOTICE_ROLE` and the values `problem` / `reference`: `NoticeNotifications.cs:26-32` and `NotificationBookmark.cs:33-36`.
- **Receiver declarations:** every receiver is declared in the manifest, `Exported=true` is set explicitly (Android 12+ is satisfied), and every send names its component explicitly (Android 8+ is satisfied).
- **Background activity starts:** Flyleaf's locked-session start from a receiver is allowed because it is the device owner.
