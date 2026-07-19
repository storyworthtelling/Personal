module github.com/storyworthtelling/focusphone/gmbridge

go 1.25

// libgm lives inside the mautrix-gmessages module (it is not a module of its
// own), so require the parent module; `go mod tidy` fills in the transitive
// requires (protobuf, etc.).
require (
	github.com/google/uuid v1.6.0
	github.com/rs/zerolog v1.33.0
	go.mau.fi/mautrix-gmessages v0.6.4
)
