#!/usr/bin/env python3
"""Which of the shared libraries' test projects a change reaches (build-shared-libs.yml).

A test project is affected when a changed file sits inside anything it builds from: its own folder,
every project it reaches through ProjectReference, and every folder a <Compile Include="..\\..."> or
similar item pulls in from outside. Some suites also read other folders by content (the design
guards read the heads' settings pages, the workflow guards read the workflows); READS names those.

Run with the base and head commits; prints a JSON list of csproj paths. With no usable base (a new
branch, a manual run, a rewritten history) every project is affected: guessing small is the
failure this exists to avoid, since a skipped suite reports nothing at all.
"""
import json
import os
import re
import subprocess
import sys

# Folders a suite reads by content rather than by building them, relative to the repository.
READS = {
    "Storyworthtelling.Maui.Tests": ["FocusPhone/Codex.Domain/Design", "Foreword", "Ledger", "Storyboarder"],
    "Storyworthtelling.Paper.Tests": ["FocusPhone", "Foreword", "Ledger", "Storyboarder", "Fonts"],
    # The CI and update-channel guards read every workflow, every project file and CLAUDE.md.
    "Storyworthtelling.Updates.Tests": [".github", "CLAUDE.md", "**/*.csproj"],
}

# A change to the machinery itself runs everything.
EVERYTHING = [".github/workflows/build-shared-libs.yml", ".github/scripts/affected-tests.py", ".github/actions/dotnet/"]

INCLUDE = re.compile(r'Include="([^"]+)"')


def test_projects(root):
    found = []
    for name in sorted(os.listdir(root)):
        project = os.path.join(name, name + ".csproj")
        if name.startswith("Storyworthtelling.") and name.endswith(".Tests") and os.path.isfile(os.path.join(root, project)):
            found.append(project)
    return found


def folders(root, project):
    """Every folder the project builds from, as repository-relative paths."""
    seen, result, pending = set(), set(), [project]
    while pending:
        current = pending.pop()
        if current in seen or not os.path.isfile(os.path.join(root, current)):
            continue
        seen.add(current)
        here = os.path.dirname(current)
        result.add(here)
        with open(os.path.join(root, current), encoding="utf-8-sig") as handle:
            text = handle.read()
        for include in INCLUDE.findall(text):
            include = include.replace("\\", "/")
            if not include.startswith(".."):
                continue
            target = os.path.normpath(os.path.join(here, include)).replace("\\", "/")
            if target.endswith(".csproj"):
                pending.append(target)
            else:
                # A single file, or a wildcard: the folder it lives in, or the folder the wildcard walks.
                prefix = target.split("*")[0]
                result.add(prefix.rstrip("/") if prefix.endswith("/") else os.path.dirname(prefix))
    return result


def covers(folder, path):
    if folder == "**/*.csproj":
        return path.endswith(".csproj")
    return path == folder or path.startswith(folder.rstrip("/") + "/")


def affected(root, changed):
    projects = test_projects(root)
    if changed is None or any(covers(e, p) or p == e for e in EVERYTHING for p in changed):
        return projects
    chosen = []
    for project in projects:
        name = os.path.dirname(project)
        reach = folders(root, project) | set(READS.get(name, []))
        if any(covers(folder, path) for folder in reach for path in changed):
            chosen.append(project)
    return chosen


def changed_files(root, base, head):
    if not base or set(base) == {"0"}:
        return None
    try:
        out = subprocess.run(["git", "diff", "--name-only", base, head], cwd=root, check=True, capture_output=True, text=True).stdout
    except subprocess.CalledProcessError:
        return None
    return [line for line in out.splitlines() if line]


def main():
    root = os.getcwd()
    base = sys.argv[1] if len(sys.argv) > 1 else ""
    head = sys.argv[2] if len(sys.argv) > 2 else "HEAD"
    print(json.dumps(affected(root, changed_files(root, base, head))))


if __name__ == "__main__":
    main()
