# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""Sizes, placement, symbol versions and the quad walk, as the C# library's GeometryTests; and fill_quads."""

import unittest

from .. import (
    MAX_ENCODED_PAYLOAD_BYTE_COUNT,
    MAX_FRAME_QUAD_COUNT,
    MAX_QR_MODULE_COUNT,
    MAX_QUAD_COUNT,
    MAX_QUIET_ZONE_MODULES,
    MarkerKind,
    MarkerSlot,
    Options,
    Payload,
    Point,
    Quad,
    StartMetadata,
    Vertex,
    fill_quads,
    generate_indexed,
    generate_modules,
    generate_quads,
    generate_triangles,
    marker_size_px,
    max_marker_size_px,
    minimum_module_size_px,
    quads_to_indexed,
    quads_to_triangles,
    recommend_module_size_px,
    recommended_origin,
)


class GeometryTests(unittest.TestCase):
    def test_buffer_sizes_match_the_cpp_library(self) -> None:
        self.assertEqual(MAX_FRAME_QUAD_COUNT, 326)
        self.assertEqual(MAX_QUAD_COUNT, 862)
        self.assertEqual(MAX_ENCODED_PAYLOAD_BYTE_COUNT, 97)

    def test_marker_size(self) -> None:
        self.assertEqual(marker_size_px(Options()), 198)
        self.assertEqual(marker_size_px(Options(3)), 99)
        self.assertEqual(marker_size_px(Options(12)), 396)
        self.assertEqual(marker_size_px(Options(1, 0)), 25)
        self.assertEqual(max_marker_size_px(Options()), 294)

    def test_module_size_recommendations_match_the_documentation(self) -> None:
        self.assertEqual(minimum_module_size_px(1080, 1080), 2)
        self.assertEqual(recommend_module_size_px(1080, 1080), 3)
        self.assertEqual(recommend_module_size_px(1080, 1080, mjpeg=True), 4)
        self.assertEqual(minimum_module_size_px(1440, 1080), 3)
        self.assertEqual(recommend_module_size_px(1440, 1080), 4)
        self.assertEqual(minimum_module_size_px(1080, 540), 4)
        self.assertEqual(recommend_module_size_px(1080, 540), 6)
        self.assertEqual(recommend_module_size_px(2160, 1080), 6)
        self.assertEqual(recommend_module_size_px(1080, 540, mjpeg=True), 8)
        self.assertEqual(minimum_module_size_px(1080, 360), 6)
        self.assertEqual(recommend_module_size_px(1080, 360), 9)
        self.assertEqual(minimum_module_size_px(2160, 540), 8)
        self.assertEqual(recommend_module_size_px(2160, 540), 12)
        self.assertEqual(recommend_module_size_px(540, 1080), 3)
        self.assertEqual(recommend_module_size_px(0, 1080), 3)

    def test_recommended_origins(self) -> None:
        options = Options()
        self.assertEqual(recommended_origin(MarkerSlot.TOP_LEFT, 1920, 1080, options), Point(32, 32))
        self.assertEqual(recommended_origin(MarkerSlot.MIDDLE_LEFT, 1920, 1080, options), Point(32, 441))
        self.assertEqual(recommended_origin(MarkerSlot.BOTTOM_LEFT, 1920, 1080, options), Point(32, 1080 - 32 - 198))
        self.assertEqual(recommended_origin(MarkerSlot.TOP_LEFT, 1920, 1080, options, 3), Point(33, 33))
        self.assertEqual(recommended_origin(MarkerSlot.MIDDLE_LEFT, 1920, 1080, options, 3), Point(33, 441))
        self.assertEqual(recommended_origin(MarkerSlot.BOTTOM_LEFT, 1920, 1080, options, 3), Point(33, 849))
        self.assertEqual(recommended_origin(MarkerSlot.TOP_LEFT, 1920, 1080, options, 4), Point(32, 32))
        # A frame lower than the marker: C# truncates toward zero, Python's // would floor
        self.assertEqual(recommended_origin(MarkerSlot.MIDDLE_LEFT, 100, 100, options), Point(32, -49))
        self.assertEqual(recommended_origin(MarkerSlot.MIDDLE_LEFT, 100, 100, options, 3), Point(33, -48))

    def test_symbols_frame_and_end_are_version_2_start_grows_with_the_name(self) -> None:
        self.assertEqual(generate_modules(Payload(1, 2, 3)).size, 25)
        self.assertEqual(generate_modules(Payload(1, 2, 3, MarkerKind.SEQUENCE_END)).size, 25)
        self.assertEqual(generate_modules(Payload(1, 2, 3, MarkerKind.SEQUENCE_START)).size, 29, "33 bytes do not fit version 2-M")
        start = Payload(1, 2, 3, MarkerKind.SEQUENCE_START)
        self.assertEqual(generate_modules(start, StartMetadata(0, "x" * 64)).size, MAX_QR_MODULE_COUNT)
        with self.assertRaises(ValueError):
            _ = generate_modules(start, StartMetadata(0, "x" * 65))

    def test_quads_are_pixel_aligned_background_first(self) -> None:
        quads = generate_quads(Payload(5, 6, 7), Options(3, 4), Point(10, 20))
        self.assertTrue(2 <= len(quads) <= MAX_FRAME_QUAD_COUNT)
        self.assertEqual(quads[0], Quad(10, 20, 10 + 99, 20 + 99, False))
        for quad in quads[1:]:
            self.assertTrue(quad.dark)
            self.assertEqual(quad.height, 3)
            self.assertEqual((quad.left - 22) % 3, 0, "left edges on module boundaries")
            self.assertEqual((quad.top - 32) % 3, 0, "top edges on module boundaries")

    def test_triangles_and_indexed_equal_the_converted_quads(self) -> None:
        payload, options, origin = Payload(42, 1_234_567, 3), Options(2, 4), Point(7, 9)
        quads = generate_quads(payload, options, origin)
        self.assertEqual(generate_triangles(payload, options, origin), quads_to_triangles(quads))
        self.assertEqual(generate_indexed(payload, options, origin, 50), quads_to_indexed(quads, 50))

    def test_vertex_order_matches_the_cpp_library(self) -> None:
        quads = [Quad(0, 0, 10, 10, False), Quad(2, 3, 4, 5, True)]
        triangles = quads_to_triangles(quads)
        self.assertEqual(len(triangles), 12)
        light = [Vertex(0, 0, 255), Vertex(10, 0, 255), Vertex(0, 10, 255), Vertex(0, 10, 255), Vertex(10, 0, 255), Vertex(10, 10, 255)]
        self.assertEqual(triangles[:6], light)
        self.assertEqual(triangles[6], Vertex(2, 3, 0))
        self.assertEqual(triangles[11], Vertex(4, 5, 0))

        vertices, indices = quads_to_indexed(quads, 100)
        self.assertEqual(len(vertices), 8)
        self.assertEqual(vertices[4:], [Vertex(2, 3, 0), Vertex(4, 3, 0), Vertex(4, 5, 0), Vertex(2, 5, 0)])
        self.assertEqual(indices, [100, 101, 103, 103, 101, 102, 104, 105, 107, 107, 105, 106])

    def test_invalid_options_raise(self) -> None:
        payload = Payload(1, 2, 3)
        for options in (Options(0), Options(6, -1), Options(6, MAX_QUIET_ZONE_MODULES + 1), Options(1025)):
            with self.subTest(options), self.assertRaises(ValueError):
                _ = generate_quads(payload, options, Point(0, 0))

    def test_frame_markers_fit_the_frame_quad_count(self) -> None:
        for frame in range(0, 500, 7):
            self.assertLessEqual(len(generate_quads(Payload(frame * 7919, frame * 166_667, 9), Options(), Point(0, 0))), MAX_FRAME_QUAD_COUNT)


class FillQuadsTests(unittest.TestCase):
    def test_fills_every_channel_in_order_clipped(self) -> None:
        pixels = bytearray([7]) * (4 * 3 * 3)
        fill_quads(pixels, 4, 3, [Quad(-1, -1, 3, 2, False), Quad(1, 1, 9, 9, True)], channels=3)
        rows = [pixels[y * 12 : (y + 1) * 12] for y in range(3)]
        self.assertEqual(rows[0], bytes([255] * 9 + [7] * 3))
        self.assertEqual(rows[1], bytes([255] * 3 + [0] * 9))
        self.assertEqual(rows[2], bytes([7] * 3 + [0] * 9))

    def test_stride_leaves_the_padding(self) -> None:
        pixels = bytearray([7]) * (3 * 2)
        fill_quads(pixels, 2, 2, [Quad(0, 0, 2, 2, True)], stride=3)
        self.assertEqual(pixels, bytearray([0, 0, 7, 0, 0, 7]))

    def test_rejects_a_small_buffer(self) -> None:
        with self.assertRaises(ValueError):
            fill_quads(bytearray(5), 2, 3, [])
        with self.assertRaises(ValueError):
            fill_quads(bytearray(12), 2, 2, [], channels=3, stride=5)


if __name__ == "__main__":
    _ = unittest.main()
