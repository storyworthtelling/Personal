#!/bin/sh
# Builds gmbridge.aar for the .NET binding project.
# Prerequisites: Go 1.25+, Android NDK, and gomobile:
#   go install golang.org/x/mobile/cmd/gomobile@latest
set -eu
cd "$(dirname "$0")"
go mod tidy
# gomobile bind (Go 1.25+) requires golang.org/x/mobile in the module graph; the
# tool directive records it so it survives `go mod tidy` and future builds.
go get -tool golang.org/x/mobile/cmd/gobind
gomobile bind -target=android -androidapi 34 -o gmbridge.aar .
echo "Built gmbridge.aar — referenced by FocusPhone.GmBridge.Binding."
