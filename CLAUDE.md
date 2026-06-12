# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Personal projects repository — primarily background automation apps, with occasional MAUI mobile apps and Angular frontends. Projects are added incrementally; update this file with project-specific build/test commands when a new one lands.

## Projects

### FocusPhone

Distraction-free Pixel 8a: a device-owner launcher/DPC plus companion apps (see `FocusPhone/`).

- Tests: `dotnet test FocusPhone/FocusPhone.sln` (Domain + Infrastructure; no device needed)
- Android heads target `net10.0-android` and need the Android SDK. In environments without one (e.g. sandboxed CI), a minimal SDK works: `android.jar` under `platforms/android-36/` plus stub `platform-tools/adb`, then build with `-p:AndroidSdkDirectory=<sdk> -p:AndroidUseApkSigner=false` (workload bundles aapt2/r8; jarsigner signs debug builds)
- App IDs are `com.storyworthtelling.focusphone.*` and must match `PolicyContext` constants in Domain

## Languages & Frameworks

- **Primary**: C# / .NET
- **Frontend** (when needed): Angular
- **Mobile** (when needed): .NET MAUI
- **IDE**: Visual Studio

## Project Structure

Each project lives in a single `.sln` with multiple `.csproj` files — typically split by concern (e.g. `Domain`, `Infrastructure`, `API`, `Tests`). Keep projects coarse-grained until complexity actually demands splitting.

## Architecture Preferences

- Object-oriented; avoid functional-style abstractions that don't fit naturally in C#.
- Controllers handle HTTP; services handle logic; EF Core `DbContext` is the data access layer. Do not add a repository layer on top of `DbContext`.
- Do not access `DbContext` directly from controllers or above the service layer.
- Prefer fewer layers over more. Avoid redundant typecasts and pass-through classes that add no value.
- Use SignalR for pushing real-time updates to clients.
- Expose APIs via controllers with Swagger documentation.

## Code Style

Follow standard Microsoft C# naming conventions throughout. Do not use underscores in any identifier — this includes private fields (use `camelCase`, not `_camelCase`).

## Async

Use `async`/`await` only where I/O actually warrants it or where it will meaningfully improve expected performance. Do not make everything async by default.

## Error Handling

Handle errors where they occur. Propagate failures with a descriptive, specific message and preserve the stack trace for debugging. Do not swallow exceptions or replace them with vague messages.

## Testing

- Unit tests for all logic; integration tests at the API boundary.
- TDD strictly: tests are written before implementation.
- Do not skip or defer tests — untested code is not considered done.

## Dependencies

Only add a dependency if one of these is true:
1. It is already present in the project.
2. It is from Microsoft or another well-established, trusted publisher.
3. It is required to integrate with the specific tool or service being built.

When in doubt, implement it directly in C#.

## Git Workflow

- **Never push to `main`**. All changes go on a feature branch for review first.
- Open a pull request and wait for approval before merging.
