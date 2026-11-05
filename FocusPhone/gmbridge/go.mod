module github.com/storyworthtelling/focusphone/gmbridge

go 1.25

// libgm lives inside the mautrix-gmessages module (it is not a module of its
// own), so require the parent module; `go mod tidy` fills in the transitive
// requires (protobuf, etc.).
require (
	github.com/google/uuid v1.6.0
	github.com/rs/zerolog v1.34.0
	go.mau.fi/mautrix-gmessages v0.6.4
)

require (
	github.com/mattn/go-colorable v0.1.14 // indirect
	github.com/mattn/go-isatty v0.0.20 // indirect
	go.mau.fi/util v0.8.8 // indirect
	golang.org/x/crypto v0.40.0 // indirect
	golang.org/x/exp v0.0.0-20250711185948-6ae5c78190dc // indirect
	golang.org/x/sys v0.34.0 // indirect
	google.golang.org/protobuf v1.36.6 // indirect
)
