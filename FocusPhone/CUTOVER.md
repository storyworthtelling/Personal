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

## Provisioning

4. Factory reset the phone.
5. First-boot setup: **skip ALL Google sign-in** (zero accounts is a hard
   requirement for `dpm set-device-owner`). Enable Developer Options and USB
   debugging.
6. Install the four signed release APKs — `-g` whitelists the hard-restricted
   `READ_CALL_LOG` and `READ_SMS` for the Communication app:

   ```
   adb install -g -r FocusPhone.Launcher.apk
   adb install -g -r FocusPhone.Communication.apk
   adb install -g -r FocusPhone.Camera.apk
   adb install -g -r FocusPhone.Audio.apk
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
11. In the Communication app: enter the Gmail address + app password
    (myaccount.google.com/apppasswords; requires 2-Step Verification).
12. Complete Google-account pairing for the messaging bridge (emoji confirm in
    Google Messages).
13. Tap through the CAMERA / RECORD_AUDIO runtime dialogs (sensor permissions
    cannot be silently granted; they persist for the life of the install).
14. In the launcher admin screen: set the GitHub token for the update channel.
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
