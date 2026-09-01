#!/usr/bin/env bash
#
# Makes the MAUI Android head (Storyboarder.Android, net10.0-android) COMPILE in a fresh web/CI
# container, so UI work isn't written blind. This does NOT produce a runnable app — there is no
# emulator or display here — it gets `dotnet build` to succeed through XAML + C# + resource linking.
#
# Why this is needed: the container has the .NET SDK (via .claude/hooks/session-start.sh) but no
# Android workload and no Android SDK. The official Android SDK platform (android.jar) lives on
# dl.google.com, which the network policy BLOCKS. nuget.org and github are reachable, so:
#   - the maui-android workload comes from nuget,
#   - android.jar (API 36) comes from a public GitHub mirror,
#   - aapt2/r8 ship inside the workload; zipalign comes from Ubuntu's apt.
#
# Idempotent. Run from anywhere. Takes a few minutes on first run (the workload is a big download).
#
# After it finishes, build the head with the command it prints, e.g.:
#   dotnet build Storyboarder/Storyboarder.Android/Storyboarder.Android.csproj \
#     -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false
set -euo pipefail

API=36
SDK=/opt/android-sdk-min
# A public mirror of the per-API android.jar (dl.google.com is blocked). Swap if it ever moves.
JAR_URL="https://raw.githubusercontent.com/Sable/android-platforms/master/android-${API}/android.jar"

echo "==> Installing the maui-android workload (from nuget)…"
# The apt-built SDK installs workloads under ~/.dotnet and its GC can drop the packs on first
# install; 'repair' re-fetches any that went missing, which is what actually leaves a buildable set.
dotnet workload install maui-android
dotnet workload repair

echo "==> Locating the workload's bundled aapt2…"
AAPT2="$(find "${HOME}/.dotnet" /usr/lib/dotnet -path '*Microsoft.Android.Sdk.Linux*/tools/Linux/aapt2' 2>/dev/null | sort -V | tail -1)"
if [[ -z "${AAPT2}" ]]; then
  echo "ERROR: could not find aapt2 in the installed Android workload." >&2
  exit 1
fi
chmod +x "${AAPT2}"
echo "    aapt2: ${AAPT2}"

echo "==> Installing zipalign (apt; the workload doesn't ship it)…"
if ! command -v zipalign >/dev/null 2>&1; then
  apt-get install -y zipalign
fi

echo "==> Assembling the minimal Android SDK at ${SDK}…"
mkdir -p "${SDK}/platforms/android-${API}" "${SDK}/platform-tools" "${SDK}/build-tools/${API}.0.0"
if [[ ! -s "${SDK}/platforms/android-${API}/android.jar" ]]; then
  echo "    downloading android-${API} android.jar…"
  curl -fsSL -o "${SDK}/platforms/android-${API}/android.jar" "${JAR_URL}"
fi
# The .NET Android build resolves aapt2/zipalign by scanning $(AndroidSdkDirectory)/build-tools/*.
ln -sf "${AAPT2}" "${SDK}/build-tools/${API}.0.0/aapt2"
ln -sf "$(command -v zipalign)" "${SDK}/build-tools/${API}.0.0/zipalign"
# A stub adb satisfies any tool that probes platform-tools; we never talk to a device.
printf '#!/bin/sh\nexit 0\n' > "${SDK}/platform-tools/adb"
chmod +x "${SDK}/platform-tools/adb"

echo
echo "Done. Build the Android head with:"
echo "  dotnet build Storyboarder/Storyboarder.Android/Storyboarder.Android.csproj \\"
echo "    -p:AndroidSdkDirectory=${SDK} -p:AndroidUseApkSigner=false"
