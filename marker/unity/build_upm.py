#!/usr/bin/env python3
"""Assemble the Unity package com.manabattery.framemarker from the repository.

The package is not kept as one folder on master (its core would duplicate marker/csharp). This script builds it:

  package.json            package.template.json with the version from marker/VERSION
  README.md               marker/unity/README.md
  LICENSE.md              the repository's LICENSE
  Third Party Notices.md  qrcodegen (MIT), ported in the core
  Runtime/Core/           marker/csharp/source/*.cs + MB.FrameMarker.asmdef (engine free)
  Runtime/Unity/          the Unity helpers + MB.FrameMarker.Unity.asmdef
  Samples~/               samples (imported on demand from the Package Manager)

Unity needs a .meta file for every asset of a package installed from git (the folder is read only), so they are generated here with
GUIDs derived from the package path: the same file always gets the same GUID.

  python marker/unity/build_upm.py --output <folder> [--check]

The release workflow publishes the result on the upm branch; install it in Unity with
  https://github.com/Unarmed1000/mb-framepacing.git#upm/v<version>
"""

import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path
from typing import cast

PACKAGE_NAME = "com.manabattery.framemarker"
SCRIPT_DIR = Path(__file__).resolve().parent
MARKER_DIR = SCRIPT_DIR.parent
REPOSITORY_ROOT = MARKER_DIR.parent

META_IMPORTERS = {
    ".cs": "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n",
    ".asmdef": "AssemblyDefinitionImporter:\n  externalObjects: {}\n",
    ".json": "PackageManifestImporter:\n  externalObjects: {}\n",
    ".md": "TextScriptImporter:\n  externalObjects: {}\n",
}
META_FOOTER = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


class Arguments(argparse.Namespace):
    """The parsed command line."""

    output: str = ""
    check: bool = False


def parse_args() -> Arguments:
    parser = argparse.ArgumentParser(description="Assemble the Unity package (with .meta files) into a folder.")
    _ = parser.add_argument("--output", required=True, help="Folder to write the package to (created, or replaced if it holds a package).")
    _ = parser.add_argument("--check", action="store_true", help="Validate the assembled package afterwards.")
    return parser.parse_args(namespace=Arguments())


def read_version() -> str:
    return (MARKER_DIR / "VERSION").read_text(encoding="utf-8").strip()


def guid_for(relative_path: str) -> str:
    return hashlib.md5(f"{PACKAGE_NAME}/{relative_path}".encode()).hexdigest()


def meta_text(path: Path, relative_path: str) -> str:
    header = f"fileFormatVersion: 2\nguid: {guid_for(relative_path)}\n"
    if path.is_dir():
        return header + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" + META_FOOTER
    importer = META_IMPORTERS.get(path.suffix, "DefaultImporter:\n  externalObjects: {}\n")
    return header + importer + META_FOOTER


def is_hidden_from_unity(relative: Path) -> bool:
    # Unity ignores folders ending in '~' (such as Samples~) and hidden files; they get no .meta
    return any(part.endswith("~") or part.startswith(".") for part in relative.parts)


def prepare_output(output: Path) -> None:
    if output.exists():
        if any(output.iterdir()) and not (output / "package.json").exists():
            sys.exit(f"'{output}' is not empty and does not hold a package; refusing to replace it.")
        shutil.rmtree(output)
    output.mkdir(parents=True)


def copy_sources(source: Path, destination: Path, pattern: str) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    for file in sorted(source.glob(pattern)):
        _ = shutil.copy2(file, destination / file.name)


def read_manifest(path: Path) -> dict[str, object]:
    return cast(dict[str, object], json.loads(path.read_text(encoding="utf-8")))


def assemble(output: Path, version: str) -> None:
    prepare_output(output)

    manifest = read_manifest(SCRIPT_DIR / "package.template.json")
    manifest["version"] = version
    _ = (output / "package.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    _ = shutil.copy2(SCRIPT_DIR / "README.md", output / "README.md")
    _ = shutil.copy2(REPOSITORY_ROOT / "LICENSE", output / "LICENSE.md")
    qrcodegen = (REPOSITORY_ROOT / "licenses" / "qrcodegen-MIT.txt").read_text(encoding="utf-8")
    notices = "\n".join(
        [
            "# Third Party Notices",
            "",
            "The QR encoder (Runtime/Core/QrEncoder.cs) is a port of the QR Code generator library by Project Nayuki",
            "(https://www.nayuki.io/page/qr-code-generator-library), MIT License:",
            "",
            "```",
            qrcodegen.strip(),
            "```",
            "",
        ]
    )
    _ = (output / "Third Party Notices.md").write_text(notices, encoding="utf-8")

    copy_sources(MARKER_DIR / "csharp" / "source", output / "Runtime" / "Core", "*.cs")
    _ = shutil.copy2(SCRIPT_DIR / "Runtime" / "Core" / "MB.FrameMarker.asmdef", output / "Runtime" / "Core" / "MB.FrameMarker.asmdef")
    copy_sources(SCRIPT_DIR / "Runtime" / "Unity", output / "Runtime" / "Unity", "*")
    _ = shutil.copytree(SCRIPT_DIR / "Samples~", output / "Samples~")

    for path in sorted(output.rglob("*")):
        relative = path.relative_to(output)
        if is_hidden_from_unity(relative) or path.suffix == ".meta":
            continue
        _ = path.with_name(path.name + ".meta").write_text(meta_text(path, relative.as_posix()), encoding="utf-8", newline="\n")


def check(output: Path, version: str) -> list[str]:
    problems: list[str] = []
    guids: dict[str, str] = {}
    for path in sorted(output.rglob("*")):
        relative = path.relative_to(output)
        if is_hidden_from_unity(relative):
            if path.suffix == ".meta":
                problems.append(f"{relative}: .meta inside a folder Unity ignores")
            continue
        if path.suffix == ".meta":
            asset = path.with_name(path.name[: -len(".meta")])
            if not asset.exists():
                problems.append(f"{relative}: .meta without its asset")
            continue
        meta = path.with_name(path.name + ".meta")
        if not meta.exists():
            problems.append(f"{relative}: missing .meta")
            continue
        guid = meta.read_text(encoding="utf-8").split("guid: ")[1].split("\n")[0]
        if guid in guids:
            problems.append(f"{relative}: GUID {guid} also used by {guids[guid]}")
        guids[guid] = relative.as_posix()

    manifest = read_manifest(output / "package.json")
    name, manifest_version = manifest.get("name"), manifest.get("version")
    if name != PACKAGE_NAME or manifest_version != version:
        problems.append(f"package.json: name/version {name}/{manifest_version}, expected {PACKAGE_NAME}/{version}")

    for source in sorted((MARKER_DIR / "csharp" / "source").glob("*.cs")):
        copy = output / "Runtime" / "Core" / source.name
        if not copy.exists() or copy.read_bytes() != source.read_bytes():
            problems.append(f"Runtime/Core/{source.name}: differs from marker/csharp/source")
    return problems


def main() -> int:
    args = parse_args()
    output = Path(args.output).resolve()
    version = read_version()
    assemble(output, version)
    print(f"{PACKAGE_NAME} {version} assembled in {output}")
    if args.check:
        problems = check(output, version)
        if problems:
            print("Package check failed:")
            for problem in problems:
                print("  " + problem)
            return 1
        print("Package check: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
