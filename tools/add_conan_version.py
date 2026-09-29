#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Add a released version to a Conan recipe (sdk/conan/recipes/<name>): config.yml gets the version, conandata.yml the URL and SHA-256
of its release archive, read from the SHA256SUMS the release published. Run it after a marker-v<version> or data-v<version> release,
then review and commit the change (see doc/releasing.md). The release workflows run it too, without committing, to test the release
through the recipe (tools/check_conan.py --released).

  python tools/add_conan_version.py marker 0.2.0
  python tools/add_conan_version.py marker 0.3.0-beta.1
  python tools/add_conan_version.py data 0.2.0 --repository <owner>/<name>
"""

import argparse
import http.client
import re
import sys
import urllib.request
from pathlib import Path
from typing import cast

ROOT = Path(__file__).resolve().parent.parent
LIBRARIES = {
    "marker": ("mb-framemarker", "marker-v", "mb-framemarker-cpp"),
    "data": ("mb-framepacingdata", "data-v", "mb-framepacingdata-cpp"),
}
VERSION = re.compile(r"^\d+\.\d+\.\d+(-(alpha|beta|rc)\.[1-9]\d*)?$")
# A pre-release sorts before its release, and alpha before beta before rc (as Conan orders them)
PRERELEASE_LABELS = ("alpha", "beta", "rc")
# The entries of both files: a quoted version, then its indented fields
ENTRY = re.compile(r'^  "(?P<version>[^"]+)":\n((?:    .*\n)+)', re.MULTILINE)
FIELD = re.compile(r'^    (?P<key>\w+): "?(?P<value>[^"\n]*)"?$', re.MULTILINE)


class Arguments(argparse.Namespace):
    library: str = ""
    version: str = ""
    repository: str = "Unarmed1000/mb-framepacing"


def read_entries(path: Path) -> tuple[str, dict[str, dict[str, str]]]:
    """The file's comment header and its entries (version -> fields)."""
    text = path.read_text(encoding="utf-8")
    header = "".join(line for line in text.splitlines(keepends=True) if line.startswith("#"))
    entries = {match["version"]: {field["key"]: field["value"] for field in FIELD.finditer(match[2])} for match in ENTRY.finditer(text)}
    return header, entries


def version_key(version: str) -> tuple[int, ...]:
    numbers, _, prerelease = version.partition("-")
    major, minor, patch = (int(part) for part in numbers.split("."))
    if not prerelease:
        return (major, minor, patch, 1, 0, 0)
    label, _, number = prerelease.partition(".")
    return (major, minor, patch, 0, PRERELEASE_LABELS.index(label), int(number))


def write_entries(path: Path, header: str, key: str, entries: dict[str, dict[str, str]], quoted: bool) -> None:
    lines = [header.rstrip("\n"), f"{key}:" if entries else f"{key}: {{}}"]
    for version in sorted(entries, key=version_key):
        lines.append(f'  "{version}":')
        lines.extend(f'    {name}: "{value}"' if quoted else f"    {name}: {value}" for name, value in entries[version].items())
    _ = path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def released_sha256(url: str, archive: str) -> str:
    with cast(http.client.HTTPResponse, urllib.request.urlopen(url)) as response:
        sums = response.read().decode("utf-8")
    for line in sums.splitlines():
        parts = line.split()
        if len(parts) == 2 and parts[1].lstrip("*") == archive:
            return parts[0].lower()
    raise SystemExit(f"error: {archive} is not in {url}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("library", choices=list(LIBRARIES))
    _ = parser.add_argument("version", help="the released version, MAJOR.MINOR.PATCH, optionally with -alpha.N, -beta.N or -rc.N")
    _ = parser.add_argument("--repository", help="the GitHub repository with the release (default: Unarmed1000/mb-framepacing)")
    args = parser.parse_args(namespace=Arguments())
    if not VERSION.match(args.version):
        parser.error(f"{args.version} is not MAJOR.MINOR.PATCH, optionally with -alpha.N, -beta.N or -rc.N")

    name, tag_prefix, archive_prefix = LIBRARIES[args.library]
    archive = f"{archive_prefix}-{args.version}.tar.gz"
    release = f"https://github.com/{args.repository}/releases/download/{tag_prefix}{args.version}"
    sha256 = released_sha256(f"{release}/SHA256SUMS", archive)

    folder = ROOT / "sdk" / "conan" / "recipes" / name
    header, versions = read_entries(folder / "config.yml")
    versions[args.version] = {"folder": "all"}
    write_entries(folder / "config.yml", header, "versions", versions, quoted=False)
    header, sources = read_entries(folder / "all" / "conandata.yml")
    sources[args.version] = {"url": f"{release}/{archive}", "sha256": sha256}
    write_entries(folder / "all" / "conandata.yml", header, "sources", sources, quoted=True)
    print(f"{name}/{args.version}: {release}/{archive} ({sha256})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
