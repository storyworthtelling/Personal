#!/bin/bash
# Installs the .NET SDK so `dotnet test` works in Claude Code on the web.
#
# Every project in this repo is .NET (FocusPhone, Storyboarder, Ledger, Tools/IconStudio), and the
# remote container ships without an SDK — which means changes get written blind and only compile for
# the first time in CI. That turns each mistake into a full CI round trip.
#
# Why apt instead of the official https://dot.net/v1/dotnet-install.sh script: this environment's
# network policy blocks builds.dotnet.microsoft.com and dotnetcli.azureedge.net, so the installer
# script has nowhere to fetch an SDK from. Ubuntu's own archive carries dotnet-sdk-10.0 and is
# reachable, so that is the way in.
#
# Note this installs the SDK only. The Android heads additionally need the Android SDK plus the MAUI
# workload, which are large and not needed to run any of the test suites — those stay a CI concern.
set -euo pipefail

# Local machines have their own toolchain; this is only for the remote container.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Quieter, faster builds. Appended on every start (including warm ones where the SDK is already
# there), guarded so repeated starts don't stack duplicate lines into the env file.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  for line in 'export DOTNET_NOLOGO=1' 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'; do
    grep -qxF "$line" "$CLAUDE_ENV_FILE" 2>/dev/null || echo "$line" >> "$CLAUDE_ENV_FILE"
  done
fi

# Idempotent: the container is cached once the hook completes, so a warm start finds the SDK already
# installed and exits immediately.
if command -v dotnet >/dev/null 2>&1; then
  echo "dotnet $(dotnet --version) already installed"
  exit 0
fi

export DEBIAN_FRONTEND=noninteractive

# The image's package index is stale — its dotnet10 versions have already been superseded in the
# pool, so installing without refreshing first fails with a 404 on every .deb.
apt-get update -qq
apt-get install -y --no-install-recommends dotnet-sdk-10.0

echo "installed dotnet $(dotnet --version)"
