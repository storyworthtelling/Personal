# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Personal projects repository — primarily background automation apps, with occasional Android apps and Angular frontends. Projects are added incrementally; update this file with project-specific build/test commands when a new one lands.

## Languages & Frameworks

- **Primary**: C# / .NET
- **Frontend** (when needed): Angular
- **Mobile** (when needed): Android

## Architecture Preferences

- Object-oriented; avoid functional-style abstractions that don't fit naturally in C#.
- Services sit on top of a database abstraction layer — do not access the database directly from controllers or higher layers.
- Prefer fewer layers over more. Avoid redundant typecasts and pass-through classes that add no value.
- API-level integration tests + unit-heavy test coverage. Follow TDD: write the test first.

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
