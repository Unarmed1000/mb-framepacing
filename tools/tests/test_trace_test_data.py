# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""measure/test-data/trace is what tools/trace_augment.py makes now: the tools' own join (measure/libs/MB.FramePacing.Trace) is
tested against that folder, so the two can not drift apart unseen.

It needs the dependency group 'trace' and the trace processor, as test_trace_roundtrip does: skipped where either can not be
had, and a failure instead where MB_REQUIRE_TRACE_PROCESSOR is set (CI)."""

import os
import unittest

import update_trace_test_data

REQUIRED = os.environ.get("MB_REQUIRE_TRACE_PROCESSOR", "") not in ("", "0")


class TraceTestDataTests(unittest.TestCase):
    def test_the_stored_files_are_what_the_script_makes_now(self) -> None:
        try:
            changed = update_trace_test_data.differences()
        except (ImportError, RuntimeError) as error:
            if REQUIRED:
                raise
            raise unittest.SkipTest(f"Perfetto's Python package or its trace processor could not be had: {error}") from error
        self.assertEqual(changed, [], "run: uv run --group trace tools/update_trace_test_data.py")


if __name__ == "__main__":
    _ = unittest.main()
