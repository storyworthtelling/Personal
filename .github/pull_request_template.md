<!--
Repo-wide PR template. Delete any section that doesn't apply.
See CLAUDE.md for per-project build/test commands and DESIGN.md for the
Paper & Ink design bible.
-->

## Summary

<!-- One or two sentences: what this PR does. -->

## Context

<!-- Why this change is being made — the problem or need it addresses and the
     intended outcome. Link any related issue or PR. -->

## Changes

<!-- The notable changes, grouped by project/layer (Domain / Infrastructure /
     UI / Tests). Bullet points are fine. -->

-

## Testing & verification

<!-- How you verified this end-to-end. Paste the test command you ran and its
     result; note anything checked on-device or left unverified (and why). -->

- [ ] Tests written before implementation (TDD) and passing — e.g.
      `dotnet test <Project>/<Project>.Tests/... ` (see CLAUDE.md)
- [ ] Verified the change actually works, not just that tests pass
- [ ] CI is green (per-project `build-*.yml`, including the ICON CHECK where it applies)

## Design (Paper & Ink)

<!-- Only if this PR touches UI, generated artifacts, or copy. Otherwise delete. -->

- [ ] No color hex or type-size literals outside the registered token files
      (DESIGN.md §16); leak-guard + parity tests still pass
- [ ] Interaction rules honored (DESIGN.md §5–§11): inline success state (no
      success toasts), undo-first for destructive acts, status-sentence loading,
      pinned bottom bar, Serif in-page header, 12-hour/relative time
- [ ] Generated resources (icons, grain tile, medallion) were regenerated with
      `Tools/IconStudio`, not hand-edited

## Checklist

- [ ] Branched off `main` (never commit directly to `main`)
- [ ] Follows the C# conventions in CLAUDE.md (Microsoft naming, no underscores,
      services own logic, `DbContext` is the data layer, async only where I/O warrants)
- [ ] Only added dependencies that meet the CLAUDE.md dependency bar
- [ ] Updated `CLAUDE.md` / `DESIGN.md` if build/test commands or design tokens changed
