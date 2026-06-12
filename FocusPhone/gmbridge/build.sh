#!/bin/sh
# Builds gmbridge.aar for the .NET binding project.
# Prerequisites: Go 1.23+, Android NDK, and gomobile:
#   go install golang.org/x/mobile/cmd/gomobile@latest
#   gomobile init
set -eu
cd "$(dirname "$0")"
go mod tidy
gomobile bind -target=android -androidapi 34 -o gmbridge.aar .
echo "Built gmbridge.aar — referenced by FocusPhone.GmBridge.Binding."
