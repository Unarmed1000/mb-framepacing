# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""The error the readers raise for a file they cannot read: another kind of file, a newer format version, or damaged content."""


class DataFormatError(ValueError):
    """A file is not in a format this library reads (a newer format version says so: update the tools or the library)."""
