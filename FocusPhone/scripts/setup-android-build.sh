#!/usr/bin/env bash
#
# Makes the FocusPhone Android heads COMPILE in a fresh web/CI container, so UI work isn't
# written blind. This does NOT produce a runnable app — there is no emulator or display here —
# it gets `dotnet build` to succeed through C# + resource linking.
#
# The sibling of Storyboarder/scripts/setup-android-build.sh, with two differences: the
# FocusPhone heads are plain .NET-for-Android rather than MAUI, so the lighter 'android'
# workload is enough; and they target API 36.
#
# Why this exists: for a long stretch the only compile check on these heads was CI, and when CI
# stopped answering, four heads had no verification at all — which is how DogEar.cs shipped a
# CS0104 and sat there uncompiled. A local build is the check that doesn't depend on anything.
#
# Idempotent. Run from the repo root. First run takes a few minutes (the workload is a big
# download).
#
# After it finishes, build a head with the command it prints, e.g.:
#   dotnet build FocusPhone/Flyleaf.Android/Flyleaf.Android.csproj \
#     -p:AndroidSdkDirectory=/opt/android-sdk-min -p:AndroidUseApkSigner=false
#
# Known limit: FocusPhone.Camera.Android does NOT build here. Its ML Kit / Play-services
# bindings download .aar payloads from dl.google.com at build time, which the network policy
# blocks. Build that one in CI or on a normal dev machine.
set -euo pipefail

API=36
SDK=/opt/android-sdk-min
# A public mirror of the per-API android.jar (dl.google.com is blocked). Swap if it ever moves.
JAR_URL="https://raw.githubusercontent.com/Sable/android-platforms/master/android-${API}/android.jar"

echo "==> Installing the android workload (from nuget)…"
dotnet workload install android
# The workload's own GC can drop packs during install, leaving 'dotnet workload list' claiming
# android is present while every build fails NETSDK1147. 'repair' re-fetches what went missing
# and is what actually leaves a buildable set — it is not optional.
dotnet workload repair

echo "==> Locating the workload's bundled aapt2…"
# Search only the roots that exist. find returns non-zero for a missing directory even
# with stderr silenced, and under `set -euo pipefail` that killed this assignment — and
# with it the whole script, before the error message below could ever run. A machine
# without one of these three dotnet roots got a silent exit 1 and no explanation.
ROOTS=()
for root in "${HOME}/.dotnet" /usr/lib/dotnet /usr/share/dotnet; do
  [[ -d "${root}" ]] && ROOTS+=("${root}")
done
AAPT2=""
if [[ ${#ROOTS[@]} -gt 0 ]]; then
  AAPT2="$(find "${ROOTS[@]}" \
    -path '*Microsoft.Android.Sdk.Linux*/tools/Linux/aapt2' 2>/dev/null | sort -V | tail -1 || true)"
fi
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
echo "Done. Build the heads with:"
for head in Codex.Ui.Android Flyleaf.Android Score.Android Post.Android; do
  echo "  dotnet build FocusPhone/${head}/${head}.csproj -p:AndroidSdkDirectory=${SDK} -p:AndroidUseApkSigner=false"
done
