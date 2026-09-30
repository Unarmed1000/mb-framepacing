# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""Point: the SDK's integer pixel position, as the C++ and C# cores have it."""

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class Point:
    """A pixel position: origin at the top-left corner, +x to the right, +y down."""

    x: int = 0
    y: int = 0
