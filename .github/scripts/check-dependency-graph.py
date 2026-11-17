#!/usr/bin/env python3
"""Fails if any restored project's dependency graph is missing a package that another package in it needs.

On 17 Nov 2026 transitive pinning put Concurrent.Futures outside the exact range Concurrent.Futures.Ktx asks for,
and NuGet left it out of four heads' graphs without an error. The APKs shipped without androidx.concurrent.futures
and each app died on open. A version outside a range (NU1608) is not a failure here: the package is there, so its
classes are in the APK. An absent one is.

Usage: check-dependency-graph.py <folder>...   reads every obj/project.assets.json under each folder.
Held by RepoGuards.Tests/DependencyGraphCheckTests.
"""
import json
import pathlib
import sys


def missing_in(assets_path):
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    problems = []
    for target, libraries in assets.get("targets", {}).items():
        present = {key.split("/", 1)[0].lower() for key in libraries}
        for key, library in libraries.items():
            for dependency in (library.get("dependencies") or {}):
                if dependency.lower() not in present:
                    problems.append(f"{target}: {key} needs {dependency}, which isn't in the graph")
    return problems


def main(folders):
    files = sorted({path for folder in folders for path in pathlib.Path(folder).rglob("obj/project.assets.json")})
    if not files:
        print("FAIL: no obj/project.assets.json found; run this after the build, from the repo root.")
        return 1

    failed = False
    for path in files:
        problems = missing_in(path)
        if problems:
            failed = True
            print(f"FAIL: {path}")
            for problem in problems:
                print(f"  {problem}")
    print(f"Checked {len(files)} dependency graphs.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:] or ["."]))
