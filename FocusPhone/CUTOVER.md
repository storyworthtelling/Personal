# FocusPhone Cutover Runbook

One-time provisioning of the Pixel 8a. **The order is load-bearing** — device
owner can only be set on an account-free device, and disabling debugging is
irreversible, so it comes absolutely last.

## Before the reset

1. Everything in the build order is done and soak-tested on the phone
   *unlocked* for several days (all four apps installed normally, remote
   install from play.google.com confirmed, auto-update confirmed, messaging
   bridge stable across re-auth). During this pre-lockdown testing the DPC has
   not pinned HOME yet, so make FocusPhone the launcher manually: open it and
   tap **Set as default launcher** (shown until it is the default), or Settings
   → Apps → Default apps → Home app. At provisioning (step 9) the DPC pins it
   automatically and the button disappears.
2. **Get the signed APKs from CI.** The canonical build path is the
   `Build FocusPhone APKs` workflow (`.github/workflows/build-focusphone.yml`):
   it builds all four heads and signs them with the shared repo release keystore
   (the same `KEYSTORE_BASE64` / `KEY_ALIAS` / `KEY_PASSWORD` /
   `KEYSTORE_PASSWORD` secrets DailyLoader and Storyboarder use), so all four
   carry one signature — required for the device-owner silent-update channel to
   accept updates across the suite. Trigger it (push to the branch or run it
   manually), then download the **FocusPhone-Android** artifact from the Actions
   run; it contains the four APKs. Keep an off-GitHub copy of that keystore: if
   the secret is ever lost, no app can be updated again (signature mismatch) and
   recovery would be a factory reset. (A local Visual Studio build works too, but
   it signs with that machine's debug keystore — fine for functional testing, not
   for the update channel unless you always rebuild on the same machine.)
3. Back up anything on the device.

> **Back up every app's secrets to the encrypted vault first.** All four
> FocusPhone heads and the owner's own apps (Storyboarder, Ledger, DailyLoader)
> now share one AES-256-GCM-encrypted file in the owner's Google Drive
> (`storyworthtelling-secrets.json`) that holds their keys and tokens — so the
> reset below no longer means re-entering everything by hand. In each app's
> settings, **set the same secrets-vault passphrase** and Save; each app writes
> its section (Claude/Gemini keys, Plaid credentials + every bank's access token,
> the NYT cookie/key, the reMarkable token, the Gmail app password +
> Messages-for-Web session, the GitHub update token, the music-folder id). The
> passphrase is the one thing the vault does **not** store — memorize it; it is
> all you re-enter after the reset. (The release keystore is separate and still
> lives in CI, not the vault — keep the off-GitHub copy per step 2.)

## Provisioning

4. Factory reset the phone.
5. First-boot setup: **skip ALL Google sign-in** (zero accounts is a hard
   requirement for `dpm set-device-owner`). Enable Developer Options and USB
   debugging.
6. Install the four signed release APKs — `-g` whitelists the hard-restricted
   `READ_CALL_LOG` and `READ_SMS` for the Communication app:

   ```
   adb install -g -r Flyleaf.apk
   adb install -g -r FocusPhone.Communication.apk
   adb install -g -r FocusPhone.Camera.apk
   adb install -g -r Score.apk
   ```

7. Make the launcher device owner (fails if any account exists):

   ```
   adb shell dpm set-device-owner com.storyworthtelling.focusphone.launcher/.FocusDeviceAdminReceiver
   ```

8. Grant notification-listener access while ADB is still alive:

   ```
   adb shell cmd notification allow_listener com.storyworthtelling.focusphone.communication/.MessageBridgeListener
   adb shell cmd notification allow_listener com.storyworthtelling.focusphone.launcher/.TileNotificationListener
   ```

   The Communication listener grant powers both text capture from Google
   Messages and voicemail capture (with transcription) from Google Phone.

9. Launch the launcher once — the DPC applies every policy **except**
   `DISALLOW_DEBUGGING_FEATURES`.

## Account and app setup (still reversible)

10. Add the Google account in Settings (permitted — `DISALLOW_MODIFY_ACCOUNTS`
    is never set). This enables Play remote install, Gemini, Drive, Maps, and
    YT Music.

> **Restore secrets from the vault.** In each app, sign in with Google (grant
> the Drive permission when prompted — Ledger, DailyLoader, and the FocusPhone
> heads now request full `drive` so they can read the vault) and enter the
> **same vault passphrase** you set before the reset. On the next launch each app
> refills its own missing keys/tokens from Drive automatically — so the manual
> re-entry in steps 11, 12, and 14 is only needed if the vault wasn't set up, or
> for the sensor permissions in step 13 (which can never be restored). Ledger
> re-creates every connected bank from the vault; no re-linking.

11. In the Communication app: enter the Gmail address + app password
    (myaccount.google.com/apppasswords; requires 2-Step Verification) — or let
    the vault restore them after you sign in and enter the passphrase.
12. Complete Google-account pairing for the messaging bridge (emoji confirm in
    Google Messages) if the restored Messages-for-Web session isn't accepted.
13. Tap through the CAMERA / RECORD_AUDIO runtime dialogs (sensor permissions
    cannot be silently granted; they persist for the life of the install).
14. In the launcher admin screen: set the GitHub token for the update channel —
    or let the vault restore it (enter the passphrase in the same admin prompt).
15. Verify each app end-to-end on the device. For Communication, check the
    feed intermixes all four sources: place a call, receive a text (exactly
    one feed item, not a provider/notification duplicate), and leave a
    voicemail (it should appear with its transcription).
16. Confirm a remote install from play.google.com lands and auto-update works.
    If this fails, all third-party apps must ship through the GitHub
    silent-install channel instead — decide before proceeding.

## The irreversible step

17. In the launcher: long-press the clock → admin mode → **Finalize cutover**.
    This applies `DISALLOW_DEBUGGING_FEATURES`. Also disable Developer
    Options. From here the lockdown can only be undone by a recovery-mode
    factory reset.

## After cutover

- App updates: push a GitHub Release with the four APK assets (tag contains
  the semver, e.g. `focusphone-v1.2.0`); the launcher's update worker
  silent-installs within ~6 hours.
- Admin access (unblock apps, rotate the app password, re-pair the bridge):
  long-press the clock, request unlock, wait out the 72-hour countdown.
