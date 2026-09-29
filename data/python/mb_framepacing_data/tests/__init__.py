# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The data library's tests. Decoding a marker needs mb_framemarker: when it is not installed, the copy in mb-framepacing's marker/python
(found above this file) is used."""

import sys
from importlib.util import find_spec
from pathlib import Path

if find_spec("mb_framemarker") is None:
    for _folder in Path(__file__).resolve().parents:
        if (_folder / "marker" / "python" / "mb_framemarker").is_dir():
            sys.path.append(str(_folder / "marker" / "python"))
            break
