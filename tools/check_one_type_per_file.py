#!/usr/bin/env python3
"""Check the "one class/struct/enum per file" convention (see CLAUDE.md).

- C#: every tracked .cs file declares at most one namespace-level type (nested types are fine).
- C++: every public header under marker/cpp/include declares at most one type; FrameMarker.hpp holds only functions.

Run from anywhere inside the repository: python tools/check_one_type_per_file.py
Exits with 1 and lists the offending files when the convention is broken.
"""

import re
import subprocess
import sys
from pathlib import Path

# Block namespaces are indented by two spaces (CSharpier), so namespace-level types start at column 2
CS_TYPE = re.compile(
    r"^  (?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|file|static|sealed|abstract|readonly|partial|unsafe|ref)\s+)*"
    r"(?:record\s+struct|record\s+class|record|class|struct|interface|enum)\s+\w+"
)
# Type definitions directly inside a namespace (not forward declarations, which end in ';')
CPP_TYPE = re.compile(r"^  (?:struct|class|enum\s+class|enum|union)\s+\w+(?:\s*:\s*[\w:<> ]+)?\s*$")


def tracked_files(root: Path, pattern: str) -> list[Path]:
    result = subprocess.run(["git", "ls-files", pattern], cwd=root, capture_output=True, text=True, check=True)
    return [root / line for line in result.stdout.splitlines() if line]


def count_types(path: Path, regex: re.Pattern) -> int:
    return sum(1 for line in path.read_text(encoding="utf-8").splitlines() if regex.match(line))


def main() -> int:
    root = Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip())
    problems = []
    for path in tracked_files(root, "*.cs"):
        if (n := count_types(path, CS_TYPE)) > 1:
            problems.append(f"{path.relative_to(root)}: {n} namespace-level types")
    for path in tracked_files(root, "marker/cpp/include/*.hpp"):
        if (n := count_types(path, CPP_TYPE)) > 1:
            problems.append(f"{path.relative_to(root)}: {n} types")
    if problems:
        print("One class/struct/enum per file (CLAUDE.md 'Conventions'):")
        for problem in problems:
            print("  " + problem)
        return 1
    print("One type per file: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
