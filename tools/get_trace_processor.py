#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""EXPERIMENTAL (measure/doc/perfetto.md). Print the path of Perfetto's trace processor (trace_processor_shell), fetching it
first when it is not on this machine yet: the copy Perfetto's own Python package is pinned to, downloaded by that package,
checked against its SHA-256 and kept under the user's home folder.

    uv run --group trace tools/get_trace_processor.py

CI runs it before the .NET tests, whose trace tests read their traces with that program (MB_TRACE_PROCESSOR). The tools
themselves never download it: they find the user's own (an option, MB_TRACE_PROCESSOR, the configuration file, PATH, and this
folder of the Python package's)."""

import sys


def main() -> int:
    try:
        from perfetto.trace_processor.platform import PlatformDelegate  # noqa: PLC0415
    except ImportError as error:
        print(f"{error}\nThis needs Perfetto's Python package: uv run --group trace tools/get_trace_processor.py", file=sys.stderr)
        return 1
    print(PlatformDelegate().get_shell_path(bin_path=None))
    return 0


if __name__ == "__main__":
    sys.exit(main())
