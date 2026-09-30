# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The package root's core types, Point and Rectangle: the same cases as the C++ and C# cores' tests."""

import unittest

from .. import Point, Rectangle

_INT32_MAX = 2**31 - 1


class PointTests(unittest.TestCase):
    def test_is_a_pixel_position_that_compares_by_value(self) -> None:
        self.assertEqual(Point(), Point(0, 0))
        self.assertEqual((Point(3, -4).x, Point(3, -4).y), (3, -4))
        self.assertNotEqual(Point(3, -4), Point(-4, 3))


class RectangleTests(unittest.TestCase):
    def test_its_edges_follow_from_its_position_and_size(self) -> None:
        rect = Rectangle(10, 20, 30, 40)
        self.assertEqual((rect.x, rect.y, rect.width, rect.height), (10, 20, 30, 40))
        self.assertEqual((rect.left, rect.top, rect.right, rect.bottom), (10, 20, 40, 60))
        self.assertTrue(rect.contains(10, 20) and rect.contains(39, 59))
        self.assertFalse(rect.contains(40, 20) or rect.contains(10, 60) or rect.contains(9, 20))
        self.assertEqual(Rectangle.from_left_top_right_bottom(10, 20, 40, 60), rect)
        self.assertEqual(Rectangle(), Rectangle(0, 0, 0, 0))

    def test_it_is_always_valid(self) -> None:
        # A negative size is 0
        self.assertEqual(Rectangle(5, 6, -3, -4), Rectangle(5, 6, 0, 0))
        self.assertEqual(Rectangle.from_left_top_right_bottom(40, 60, 10, 20), Rectangle(40, 60, 0, 0))
        self.assertTrue(Rectangle(5, 6, 0, 7).is_empty)
        # A size that would put an edge beyond a 32-bit int is cut to fit, as C++ and C# must
        self.assertEqual(Rectangle(_INT32_MAX - 10, _INT32_MAX - 5, 100, 100).right, _INT32_MAX)
        self.assertEqual(Rectangle(_INT32_MAX - 10, _INT32_MAX - 5, 100, 100).bottom, _INT32_MAX)
        self.assertEqual(Rectangle(-_INT32_MAX, 0, _INT32_MAX, 1).right, 0)
