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

# Rebuild graphify's knowledge graph so it's there from the first tool call instead of only after
# someone remembers to run it. graphify-out/ is gitignored (see CLAUDE.md's Graphify section — the
# built graph isn't checked in, since community-ID reshuffling made every commit a ~30MB diff), so a
# fresh checkout has no graph at all until this runs; the graphify skill's own trigger condition
# ("especially when graphify-out/ exists") depends on this file being here. Local CPU only — no LLM,
# no network for --code-only — so this costs wall-clock time, not tokens or API spend: ~25s cold,
# ~1-2s on a warm container where the AST cache already covers most files. Never fails the session:
# graphify might not be installed yet in some environments, and a build hiccup here shouldn't block
# dotnet setup below.
if command -v graphify >/dev/null 2>&1; then
  (
    cd "${CLAUDE_PROJECT_DIR:-$(pwd)}" && \
    graphify update . --no-cluster >/dev/null 2>&1 && \
    graphify cluster-only . --no-label --no-viz >/dev/null 2>&1
  ) || echo "graphify graph rebuild failed or skipped (non-fatal)"
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
