# Codex.Tidal.Binding

TIDAL's Player SDK, bound for Score.

## Why a binding exists at all

Playback has to go through TIDAL's own unmodified Player module — that is the only
sanctioned route, and TIDAL rejects quota requests that work around it. The module is
Kotlin, published to Maven Central as `com.tidal.sdk:player`, so reaching it from a
.NET-for-Android head means a binding project.

## What is bound, and what deliberately is not

Two entry points are bound: **`player`** (playback) and **`auth`** (sign-in). Everything
else in the tree is packaged with `Bind="false"` — present at runtime, absent from C#.
Binding the internal modules would surface a large API surface nobody calls and add
generator noise to keep quiet.

`Transforms/Metadata.xml` removes one package, `com.tidal.sdk.player.events.model` —
TIDAL's playback telemetry. Its models are a Kotlin sealed hierarchy whose members
narrow their inherited return types, which Java's generics express and C#'s override
rules cannot; every error the first build produced was in that one package. Removing it
takes it out of the *generated C#* only. The classes remain in the packaged .aar and
the SDK reports its telemetry at runtime as its terms require — a binding decision, not
a modification of the player.

## Why the artifacts are committed

The ten `.aar`/`.jar` files in `Jars/` total 1.6 MB. They are committed rather than
restored at build time because the player modules are versioned in lockstep (all at
0.0.71) and a build that silently resolved a different version of a transitive module
would fail as a crash on the phone rather than as a red build.

## What this does not answer

The binding compiling proves the API is reachable from C#. It proves nothing about
playback. Two questions are settled — the SDK pulls in **no Play Services**, and it is
built on **Media3/ExoPlayer**, the same player Score already runs, which is what makes
one media session across a merged library plausible. What remains is whether a
developer app can actually stream, which only a real call answers.

## Upgrading

Bump the version in `Codex.Tidal.Binding.csproj`, re-download every `com.tidal.sdk`
artifact at the matching version, and rebuild. Expect the metadata transform to need
revisiting: the removed package is where TIDAL's own model churn lands.
