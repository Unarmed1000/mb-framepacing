# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The error the readers raise for a file that is not in the format: another kind of file, a newer format version, a required field or
column missing, or a value that is not of its type or outside its range. It is the only error they raise for a file's content (a file
that cannot be opened is an OSError)."""


class DataFormatError(ValueError):
    """A file is not in a format this library reads (a newer format version says so: update the tools or the library)."""
