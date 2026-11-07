# P14 · device spikes (A60, A63)

Two questions that decide P14's shape, answered on the owner's own Pixel Watch before any screen
is designed:

- **S1 (A60): texts on Fi.** Do texts reach the watch, and can the watch reply, with the phone
  switched off? Can a third-party app (Dash, stood in for here by the spike) be the watch's default
  SMS app?
- **S2 (A63): device owner.** Does a Pixel Watch accept a device owner, which Point needs to enforce
  the lockdown and to install updates silently?

The spike app (`WatchSpike.Android`, package `com.storyworthtelling.watchspike`) is a throwaway
instrument: one screen of readouts and six buttons, no design language, no tests, never published to
`apps-latest`, and deleted once both spikes have reported. It never reads, logs or shows a message's
words. It keeps only how many texts arrived, when, and the sender's last two digits. As a default SMS
app must, it files each text it receives into the watch's own message store, so nothing is lost while
it holds the role.

**Built here, device check pending:** it compiles against Android 16 (API 36). Nothing in it has run on
a watch.

---

## S0 · Getting the spike onto the watch

The APK is attached to the **`watch-spike`** pre-release on GitHub (never `apps-latest`, so Flyleaf
never installs it), built by `.github/workflows/watch-spike.yml`. It is signed with a throwaway debug
key, not the suite's keystore.

With no computer, use an ADB app on an **unrestricted** Android phone (the partner's), such as
Bugjaeger: it pairs with the watch's wireless debugging and can install an APK and run `adb shell`
commands. The locked FocusPhone cannot run it. With a computer, use Android's `adb` (the "SDK
Platform-Tools" download from developer.android.com).

1. On the watch: **Settings → System → About → Versions**, tap **Build number** seven times to turn on
   Developer options.
2. **Settings → Developer options**: turn on **ADB debugging** and **Debug over Wi-Fi** (the watch and
   computer on the same Wi-Fi). Then **Wireless debugging → Pair new device** shows a code and an address.
3. On the computer: `adb pair <address shown>` and enter the code, then `adb connect <watch address>`.
4. `adb install com.storyworthtelling.watchspike-Signed.apk`.
5. Open **Watch spike** from the watch's app list.

## S1 · Texts on Fi (A60) — answered, A67

The owner's own use answered S1a: with the phone on, texts and calls reach the watch anywhere; with
the phone off, nothing arrives until it is back on. On Fi the phone carries everything, so **S1b is
dropped** and Dash uses the Post relay. The steps are kept below for the record only.

**S1a, no app needed.** Run each with someone (Lee, say) texting and calling your number.

| # | Set-up | What to note |
|---|---|---|
| 1 | Phone on, Bluetooth off on the phone (watch on LTE) | Did the text reach the watch? Did a reply from the watch arrive? Did the call ring on the watch? |
| 2 | Phone **switched off** | the same three |
| 3 | Phone off, then back on | Did the texts from (2) appear in **Post** on the phone afterwards? |

**S1b, with the spike.** Phone switched off throughout.

1. Open the spike and read the **TEXTS** lines: telephony hardware, SMS capable, default SMS app, and
   whether an SMS role exists on the watch.
2. Tap **Ask for the SMS role**. Note whether the watch offers it at all, and what the dialog says.
3. If it was granted, have Lee text you. Tap **Check again**: does the count go up?
4. Tap **Reply to the last sender**. Did Lee receive "Test reply from the watch spike."?
5. **Give the role back** before you finish: Settings → Apps → Default apps → SMS app → Messages. Until
   you do, Google Messages on the watch will not get texts.

## S2 · Device owner (A63)

**S2a, no reset (safe; try first).** With the spike installed:

`adb shell dpm set-device-owner com.storyworthtelling.watchspike/.SpikeAdmin`

Copy the exact reply. On a phone this fails if any account is on the device, and a paired watch carries
your Google account, so a refusal is the likely answer. Its wording says why. If it succeeds, open the
spike: **Device owner: yes**. Then try **Hide Play Store**, **Show Play Store** and **Reinstall this app
silently**, and note what each reports. To remove it afterwards:
`adb shell dpm remove-active-admin com.storyworthtelling.watchspike/.SpikeAdmin`.

**S2b, with a reset: do not run yet.** If S2a is refused because of accounts, the only remaining route
is to reset the watch and set the owner before the account is added. That may remove the watch's LTE
profile and the shared number, which A30 rules out unless both survive. It needs its own go-ahead after
S2a has reported, with a check first on what a Pixel Watch reset does to its eSIM.

## Recording results

Send them back as plain notes or screenshots of the spike's screen (the Pixel Watch app on the phone can
take one). They show no message words and only two digits of a number. Results go into
`P14-TECHNICAL-FINDING.md` §0, with this file's table filled in:

| Spike | Result | Date |
|---|---|---|
| S1a-1 phone on, Bluetooth off | | |
| S1a-2 phone off | | |
| S1a-3 texts appear in Post afterwards | | |
| S1b SMS role offered on the watch | | |
| S1b text received and reply sent with the spike as SMS app | | |
| S2a `dpm set-device-owner` reply | | |
