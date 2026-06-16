# FocusPhone Cutover Runbook

One-time provisioning of the Pixel 8a. **The order is load-bearing** — device
owner can only be set on an account-free device, and disabling debugging is
irreversible, so it comes absolutely last.

## Before the reset

1. Everything in the build order is done and soak-tested on the phone
   *unlocked* for several days (all four apps installed normally, remote
   install from play.google.com confirmed, auto-update confirmed, messaging
   bridge stable across re-auth).
2. **Build and escrow the signing keystore.** The four heads carry no signing
   config, so they sign with the dev machine's default debug keystore (same as
   DailyLoader) — `~/.local/share/Xamarin/Mono for Android/debug.keystore` on
   Linux, `%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore` on Windows.
   Build all four APKs on the **same machine/user** so they share one key (the
   device-owner silent-update channel rejects a signature change). That debug
   keystore is generated per-machine, so back it up off-device: losing it means
   no app can ever be updated again (signature mismatch), and recovery would be
   a factory reset.
3. Back up anything on the device.

## Provisioning

4. Factory reset the phone.
5. First-boot setup: **skip ALL Google sign-in** (zero accounts is a hard
   requirement for `dpm set-device-owner`). Enable Developer Options and USB
   debugging.
6. Install the four signed release APKs — `-g` whitelists the hard-restricted
   `READ_CALL_LOG` for the Communication app:

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
15. Verify each app end-to-end on the device.
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
