#!/usr/bin/env python3
"""Stage signed APKs for their form's rolling release (apps-latest or watch-latest), each with the sidecar the phone reads (P14 W-3.1.1).

    stage-apks.py --aapt2 <aapt2> --form phone|watch --out <folder> <apk> [<apk> ...]

Each APK's badging is read with aapt2. Its form is "watch" when its manifest declares android.hardware.type.watch
(required or not) and "phone" otherwise, and it must be the form the workflow said it was publishing: a watch app
carries its phone app's package and a higher versionCode band, so a watch APK that went up as a phone APK would be
installed over the phone app, silently, and a watch app that forgot its uses-feature reads as a phone APK by every
measure. A watch APK is named with ".watch.apk" so it never shares an asset name with its phone app (.NET names
both <package>-Signed.apk, and the release replaces an asset by name). Into <folder> go the APK under its published
name and <name>.json beside it: {"package", "versionCode", "form"}.

Every APK is read and checked before any is staged, so a refusal stages nothing and the release is left as it was.
Exits 1 with the reason on any refusal.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys

PHONE = "phone"
WATCH = "watch"
WATCH_FEATURE = "android.hardware.type.watch"
WATCH_SUFFIX = ".watch.apk"
FORM_FIELD = "form"

# aapt2 prints a required feature as "uses-feature:" and an optional one as "uses-feature-not-required:", indented under
# a feature-group; either makes a watch app.
FEATURE_LINE = re.compile(r"^\s*uses-feature(?:-not-required)?: name='" + re.escape(WATCH_FEATURE) + r"'\s*$", re.MULTILINE)
PACKAGE_LINE = re.compile(r"^package: (.*)$", re.MULTILINE)


def refuse(message):
    sys.exit("stage-apks: " + message + "; nothing is published")


def read(aapt2, apk):
    run = subprocess.run([aapt2, "dump", "badging", apk], capture_output=True, text=True)
    if run.returncode != 0:
        refuse(f"aapt2 could not read {apk}: {run.stderr.strip() or run.returncode}")
    line = PACKAGE_LINE.search(run.stdout)
    package = re.search(r"\bname='([^']+)'", line.group(1)) if line else None
    version = re.search(r"\bversionCode='([0-9]+)'", line.group(1)) if line else None
    if not package or not version:
        refuse(f"{apk} has no readable package and versionCode")
    form = WATCH if FEATURE_LINE.search(run.stdout) else PHONE
    return package.group(1), int(version.group(1)), form


def published_name(apk, form):
    name = os.path.basename(apk)
    marked = name.lower().endswith(WATCH_SUFFIX)
    if form == PHONE:
        if marked:
            refuse(f"{apk} is a phone APK named like a watch APK")
        return name
    if marked:
        return name
    stem = name[:-4] if name.lower().endswith(".apk") else name
    return stem + WATCH_SUFFIX


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--aapt2", required=True)
    parser.add_argument("--form", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("apks", nargs="+")
    args = parser.parse_args()

    if args.form not in (PHONE, WATCH):
        refuse(f"the form must be {PHONE} or {WATCH}, not {args.form!r}")

    staged = []
    names = set()
    for apk in args.apks:
        package, version, form = read(args.aapt2, apk)
        if form != args.form:
            says = "declares" if form == WATCH else "does not declare"
            refuse(f"{apk} ({package}) is a {form} APK: its manifest {says} {WATCH_FEATURE}, but this publish is for {args.form}")
        name = published_name(apk, form)
        if name in names or name.lower() in {n.lower() for n in names}:
            refuse(f"two APKs would publish as {name}")
        names.add(name)
        staged.append((apk, name, {"package": package, "versionCode": version, FORM_FIELD: form}))

    os.makedirs(args.out, exist_ok=True)
    for _, name, _ in staged:
        for path in (os.path.join(args.out, name), os.path.join(args.out, name + ".json")):
            if os.path.exists(path):
                refuse(f"{path} is already staged")
    for apk, name, sidecar in staged:
        shutil.copyfile(apk, os.path.join(args.out, name))
        with open(os.path.join(args.out, name + ".json"), "w", encoding="utf-8") as handle:
            json.dump(sidecar, handle)
            handle.write("\n")
        print(f"{name}: {sidecar['package']} {sidecar['versionCode']} {sidecar[FORM_FIELD]}")


if __name__ == "__main__":
    main()
