#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check that the C# SDK modules that must be fully tested are: 100 % of the lines and branches of each module's assembly, measured
by its own tests (Microsoft code coverage, which the test SDK brings: dotnet test --collect "Code Coverage;Format=cobertura").

  python tools/check_csharp_coverage.py                  # every module below
  python tools/check_csharp_coverage.py --module core    # one module

Prints every line that is not covered and every line with a branch not taken, and fails when there is one.
"""

import argparse
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
# Module: its assembly (the cobertura package) and the test project that must cover it
MODULES = {
    "core": ("MB.FramePacing", ROOT / "sdk" / "csharp" / "core" / "UnitTest" / "MB.FramePacing.UnitTest.csproj"),
}


class Arguments(argparse.Namespace):
    """The parsed command line."""

    module: str = "all"


def collect(project: Path, results: Path) -> Path:
    command = ["dotnet", "test", str(project), "-c", "Release", "--collect", "Code Coverage;Format=cobertura", "--results-directory", str(results)]
    print("> " + " ".join(command), flush=True)
    completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if completed.returncode != 0:
        print(completed.stdout + completed.stderr)
        sys.exit(f"The tests of {project.name} failed")
    reports = sorted(results.rglob("*.cobertura.xml"))
    if len(reports) != 1:
        sys.exit(f"Expected one coverage report in {results}, found {len(reports)}")
    return reports[0]


def uncovered(report: Path, assembly: str) -> tuple[int, int, list[str]]:
    """The assembly's line count, branch count and the lines that are not fully covered."""
    packages = [package for package in ET.parse(report).getroot().iter("package") if package.get("name") == assembly]
    if len(packages) != 1:
        sys.exit(f"{report.name} has no package {assembly}")
    # A line appears under its method and again under its class: count each file's line once
    lines: dict[tuple[str, int], tuple[int, str | None]] = {}
    for cls in packages[0].iter("class"):
        filename = cls.get("filename", "")
        for line in cls.iter("line"):
            key = (filename, int(line.get("number", "0")))
            hits = int(line.get("hits", "0"))
            condition = line.get("condition-coverage") if line.get("branch", "False").lower() == "true" else None
            previous = lines.get(key)
            lines[key] = (max(hits, previous[0]) if previous else hits, condition or (previous[1] if previous else None))
    branches = 0
    problems: list[str] = []
    for (filename, number), (hits, condition) in sorted(lines.items()):
        where = f"{Path(filename).relative_to(ROOT).as_posix() if Path(filename).is_relative_to(ROOT) else filename}:{number}"
        if condition is not None:
            # "50% (1/2)"
            taken, total = (int(part) for part in condition.split("(")[1].rstrip(")").split("/"))
            branches += total
            if hits > 0 and taken < total:
                problems.append(f"{where}: {taken} of {total} branches taken")
        if hits == 0:
            problems.append(f"{where}: not covered")
    return len(lines), branches, problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--module", choices=["all", *MODULES], default="all", help="check one module only (default: all)")
    args = parser.parse_args(namespace=Arguments())
    ok = True
    for name, (assembly, project) in MODULES.items():
        if args.module not in ("all", name):
            continue
        with tempfile.TemporaryDirectory(prefix="mb-framepacing-coverage-") as temporary:
            line_count, branch_count, problems = uncovered(collect(project, Path(temporary)), assembly)
        for problem in problems:
            print(problem)
        print(f"{assembly}: {line_count} lines, {branch_count} branches, {'100 % covered' if not problems else f'{len(problems)} not fully covered'}")
        ok = ok and not problems
    print("C# coverage: OK" if ok else "C# coverage: FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
