#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""Print the path of Perfetto's trace processor (trace_processor_shell), fetching it
first when it is not on this machine yet: the copy Perfetto's own Python package is pinned to, downloaded by that package,
checked against its SHA-256 and kept under the user's home folder.

    uv run --group trace tools/get_trace_processor.py

CI runs it before the .NET tests, whose trace tests read their traces with that program (MB_TRACE_PROCESSOR). The tools
themselves never download it: they find the user's own (an option, MB_TRACE_PROCESSOR, the configuration file, PATH, and this
folder of the Python package's).

The path is the last line of the standard output, and all this script writes there: what the package prints while it
downloads goes to the standard error, and a download program it starts may still write lines before it."""

import contextlib
import sys


def main() -> int:
    try:
        from perfetto.trace_processor.platform import PlatformDelegate  # noqa: PLC0415
    except ImportError as error:
        print(f"{error}\nThis needs Perfetto's Python package: uv run --group trace tools/get_trace_processor.py", file=sys.stderr)
        return 1
    with contextlib.redirect_stdout(sys.stderr):
        path = PlatformDelegate().get_shell_path(bin_path=None)
    print(path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
