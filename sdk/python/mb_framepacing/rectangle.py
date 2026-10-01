# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""Rectangle: the SDK's integer pixel rectangle, as the C++ and C# cores have it."""

from dataclasses import dataclass


@dataclass(frozen=True, slots=True, init=False)
class Rectangle:
    """An integer pixel rectangle covering x <= px < x + width and y <= py < y + height (left <= px < right, top <= py < bottom). Origin at
    the top-left corner, +x to the right, +y down. Always valid: a negative width or height is 0."""

    x: int
    y: int
    width: int
    height: int

    def __init__(self, x: int = 0, y: int = 0, width: int = 0, height: int = 0) -> None:
        object.__setattr__(self, "x", x)
        object.__setattr__(self, "y", y)
        object.__setattr__(self, "width", max(width, 0))
        object.__setattr__(self, "height", max(height, 0))

    @staticmethod
    def from_left_top_right_bottom(left: int, top: int, right: int, bottom: int) -> "Rectangle":
        """The rectangle between the edges: an edge before the opposite one gives a size of 0."""
        return Rectangle(left, top, right - left, bottom - top)

    @property
    def left(self) -> int:
        return self.x

    @property
    def top(self) -> int:
        return self.y

    @property
    def right(self) -> int:
        """The first pixel column right of the rectangle."""
        return self.x + self.width

    @property
    def bottom(self) -> int:
        """The first pixel row below the rectangle."""
        return self.y + self.height

    @property
    def is_empty(self) -> bool:
        """No pixels: a width or height of 0."""
        return self.width == 0 or self.height == 0

    def contains(self, x: int, y: int) -> bool:
        """Whether the pixel (x, y) is inside."""
        return self.left <= x < self.right and self.top <= y < self.bottom
