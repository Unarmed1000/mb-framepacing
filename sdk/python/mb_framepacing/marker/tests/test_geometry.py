# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""Sizes, placement, symbol versions, the packed module matrix, the quad walk and the bitmap, as the C# library's GeometryTests."""

import unittest

from .. import (
    DEFAULT_MODULE_SIZE_PX,
    MAX_ENCODED_PAYLOAD_BYTE_COUNT,
    MAX_MODULE_SIZE_PX,
    MAX_QUAD_COUNT,
    MAX_QUIET_ZONE_MODULES,
    MIN_MODULE_SIZE_PX,
    QR_CAPACITY_BYTES,
    QR_MODULE_COUNT,
    QR_VERSION,
    RECOMMENDED_QUIET_ZONE_MODULES,
    SYNC_QR_MODULE_COUNT,
    SYNC_QR_VERSION,
    MarkerFlags,
    MarkerKind,
    MarkerQuad,
    ModuleMatrix,
    Options,
    Payload,
    PixelFormat,
    Point,
    Rectangle,
    SequenceId,
    StartMetadata,
    Vertex,
    generate_modules,
    grid_vertex_count,
    grid_vertices,
    modules_to_bitmap,
    modules_to_grid_indices,
    modules_to_indexed,
    modules_to_quads,
    packed_module_byte_count,
    qr_module_count_for,
)
from . import software_raster
from .markers import generate_indexed, generate_quads, generate_start_quads, generate_triangles, to_indexed, to_triangles


class GeometryTests(unittest.TestCase):
    def test_buffer_sizes_match_the_cpp_library(self) -> None:
        self.assertEqual(QR_VERSION, 6)
        self.assertEqual(QR_MODULE_COUNT, 41)
        self.assertEqual(QR_CAPACITY_BYTES, 106)
        self.assertEqual(SYNC_QR_VERSION, 2)
        self.assertEqual(SYNC_QR_MODULE_COUNT, 25)
        self.assertEqual(MAX_QUAD_COUNT, 862)
        self.assertEqual(MAX_ENCODED_PAYLOAD_BYTE_COUNT, 77)
        self.assertLessEqual(MAX_ENCODED_PAYLOAD_BYTE_COUNT, QR_CAPACITY_BYTES)

    def test_marker_size(self) -> None:
        self.assertEqual(Options().marker_size_px(), 294)
        self.assertEqual(Options(3).marker_size_px(), 147)
        self.assertEqual(Options(4).marker_size_px(), 196)
        self.assertEqual(Options(12).marker_size_px(), 588)
        self.assertEqual(Options(1, 0).marker_size_px(), 41)
        for kind in (MarkerKind.FRAME, MarkerKind.SEQUENCE_START, MarkerKind.SEQUENCE_END):
            self.assertEqual(Options().marker_size_px(kind), 294)
            self.assertEqual(qr_module_count_for(kind), QR_MODULE_COUNT)

    def test_sync_marker_size(self) -> None:
        self.assertEqual(qr_module_count_for(MarkerKind.SYNC), SYNC_QR_MODULE_COUNT)
        self.assertEqual(Options().marker_size_px(MarkerKind.SYNC), 198)
        self.assertEqual(Options(3).marker_size_px(MarkerKind.SYNC), 99)
        self.assertEqual(Options(1, 0).marker_size_px(MarkerKind.SYNC), 25)

    def test_module_size_recommendations_match_the_documentation(self) -> None:
        self.assertEqual(Options.minimum(1080, 1080).module_size_px, 2)
        self.assertEqual(Options.recommended(1080, 1080).module_size_px, 3)
        self.assertEqual(Options.recommended(1080, 1080, mjpeg=True).module_size_px, 4)
        self.assertEqual(Options.minimum(1440, 1080).module_size_px, 3)
        self.assertEqual(Options.recommended(1440, 1080).module_size_px, 4)
        self.assertEqual(Options.minimum(1080, 540).module_size_px, 4)
        self.assertEqual(Options.recommended(1080, 540).module_size_px, 6)
        self.assertEqual(Options.recommended(2160, 1080).module_size_px, 6)
        self.assertEqual(Options.recommended(1080, 540, mjpeg=True).module_size_px, 8)
        self.assertEqual(Options.minimum(1080, 360).module_size_px, 6)
        self.assertEqual(Options.recommended(1080, 360).module_size_px, 9)
        self.assertEqual(Options.minimum(2160, 540).module_size_px, 8)
        self.assertEqual(Options.recommended(2160, 540).module_size_px, 12)
        self.assertEqual(Options.recommended(540, 1080).module_size_px, 3)
        self.assertEqual(Options.recommended(0, 1080).module_size_px, 3)

    def test_recommended_origins(self) -> None:
        options = Options()
        # The main marker top-left, the sync marker bottom-left
        self.assertEqual(options.recommended_origin(MarkerKind.FRAME, 1080), Point(32, 32))
        self.assertEqual(options.recommended_origin(MarkerKind.SEQUENCE_START, 1080), Point(32, 32))
        self.assertEqual(options.recommended_origin(MarkerKind.SEQUENCE_END, 1080), Point(32, 32))
        self.assertEqual(options.recommended_origin(MarkerKind.SYNC, 1080), Point(32, 1080 - 32 - 198))
        # Aligned to a 3:1 downscale ratio
        self.assertEqual(options.recommended_origin(MarkerKind.FRAME, 1080, 3), Point(33, 33))
        self.assertEqual(options.recommended_origin(MarkerKind.SYNC, 1080, 3), Point(33, 849))
        self.assertEqual(options.recommended_origin(MarkerKind.FRAME, 1080, 4), Point(32, 32))
        # A frame lower than the marker: C# and C++ truncate toward zero, Python's // would floor
        self.assertEqual(options.recommended_origin(MarkerKind.SYNC, 100), Point(32, -130))
        self.assertEqual(options.recommended_origin(MarkerKind.SYNC, 100, 3), Point(33, -129))

    def test_every_main_marker_kind_is_version_6(self) -> None:
        for kind in (MarkerKind.FRAME, MarkerKind.SEQUENCE_START, MarkerKind.SEQUENCE_END):
            with self.subTest(kind):
                self.assertEqual(generate_modules(Payload(kind, 3, 1, MarkerFlags.NONE, 2, target_frame_ticks=5, intended_display_ticks=4)).size, 41)
        start = Payload(MarkerKind.SEQUENCE_START, 3, 1, MarkerFlags.NONE, 2)
        self.assertEqual(generate_modules(start, StartMetadata(-1, SequenceId(bytes([0xFF]) * 16))).size, QR_MODULE_COUNT)
        with self.assertRaises(ValueError):
            _ = generate_modules(start, StartMetadata(1 << 63, SequenceId()))

    def test_sync_markers_are_version_2(self) -> None:
        self.assertEqual(
            generate_modules(Payload(MarkerKind.SYNC, 3, 1, MarkerFlags.NONE, 2, target_frame_ticks=5, intended_display_ticks=4)).size, SYNC_QR_MODULE_COUNT
        )
        self.assertEqual(generate_modules(Payload(MarkerKind.SYNC, 0, 0xFFFF_FFFF_FFFF_FFFF, MarkerFlags.NONE, 0)).size, 25)
        # The background quad follows the symbol size, in every output
        payload, options, origin = Payload(MarkerKind.SYNC, 0, 7, MarkerFlags.NONE, 0), Options(3, 4), Point(10, 20)
        quads = generate_quads(payload, options, origin)
        self.assertEqual(quads[0], MarkerQuad(Rectangle(10, 20, 99, 99), False))
        self.assertTrue(all(quad.rect.right <= 10 + 99 - 12 and quad.rect.bottom <= 20 + 99 - 12 for quad in quads[1:]))
        self.assertEqual(generate_triangles(payload, options, origin), to_triangles(quads))
        self.assertEqual(generate_indexed(payload, options, origin, 5), to_indexed(quads, 5))
        # Only the run id and the frame index are encoded: the same symbol whatever the other fields hold
        self.assertEqual(
            generate_quads(Payload(MarkerKind.SYNC, 0, 7, MarkerFlags.NONE, 123, target_frame_ticks=6, intended_display_ticks=5), options, origin), quads
        )
        self.assertNotEqual(generate_quads(Payload(MarkerKind.SYNC, 4, 7, MarkerFlags.NONE, 0), options, origin), quads)

    def test_every_main_marker_kind_has_the_same_size(self) -> None:
        options, origin = Options(), Point(32, 32)
        expected = MarkerQuad(Rectangle(32, 32, 294, 294), False)
        start = Payload(MarkerKind.SEQUENCE_START, 3, 1, MarkerFlags.NONE, 2)
        self.assertEqual(generate_quads(Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NONE, 2), options, origin)[0], expected)
        self.assertEqual(generate_quads(Payload(MarkerKind.SEQUENCE_END, 3, 1, MarkerFlags.NONE, 2), options, origin)[0], expected)
        self.assertEqual(generate_start_quads(start, StartMetadata(1, SequenceId.from_text("x" * 16)), options, origin)[0], expected)

    def test_quads_are_pixel_aligned_background_first(self) -> None:
        quads = generate_quads(Payload(MarkerKind.FRAME, 7, 5, MarkerFlags.NONE, 6), Options(3, 4), Point(10, 20))
        self.assertTrue(2 <= len(quads) <= MAX_QUAD_COUNT)
        self.assertEqual(quads[0], MarkerQuad(Rectangle(10, 20, 147, 147), False))
        for quad in quads[1:]:
            self.assertTrue(quad.dark)
            self.assertEqual(quad.rect.height, 3)
            self.assertEqual((quad.rect.left - 22) % 3, 0, "left edges on module boundaries")
            self.assertEqual((quad.rect.top - 32) % 3, 0, "top edges on module boundaries")

    def test_triangles_and_indexed_follow_the_quads_in_the_documented_order(self) -> None:
        payload, options, origin = Payload(MarkerKind.FRAME, 3, 42, MarkerFlags.NONE, 1_234_567), Options(2, 4), Point(7, 9)
        quads = generate_quads(payload, options, origin)
        self.assertEqual(generate_triangles(payload, options, origin), to_triangles(quads))
        self.assertEqual(generate_indexed(payload, options, origin, 50), to_indexed(quads, 50))

    def test_vertex_order_matches_the_cpp_library(self) -> None:
        # The background quad comes first: its vertices in the documented order
        options = Options(2, 4)
        size = options.marker_size_px()
        triangles = generate_triangles(Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NONE, 2), options, Point(10, 20))
        light = [
            Vertex(10, 20, 255),
            Vertex(10 + size, 20, 255),
            Vertex(10, 20 + size, 255),
            Vertex(10, 20 + size, 255),
            Vertex(10 + size, 20, 255),
            Vertex(10 + size, 20 + size, 255),
        ]
        self.assertEqual(triangles[:6], light)
        vertices, indices = generate_indexed(Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NONE, 2), options, Point(10, 20), 100)
        self.assertEqual(vertices[:4], [Vertex(10, 20, 255), Vertex(10 + size, 20, 255), Vertex(10 + size, 20 + size, 255), Vertex(10, 20 + size, 255)])
        self.assertEqual(indices[:12], [100, 101, 103, 103, 101, 102, 104, 105, 107, 107, 105, 106])

    def test_options_are_always_valid(self) -> None:
        self.assertEqual(Options(), Options(DEFAULT_MODULE_SIZE_PX, RECOMMENDED_QUIET_ZONE_MODULES))
        self.assertEqual(DEFAULT_MODULE_SIZE_PX, 6)
        self.assertEqual(Options(3, 4).quiet_zone_px, 12)
        # A value outside its range is clamped, as in the C# library (and the C++ library without asserts)
        self.assertEqual(Options(0, 4), Options(MIN_MODULE_SIZE_PX, 4))
        self.assertEqual(Options(MAX_MODULE_SIZE_PX + 1, 4).module_size_px, MAX_MODULE_SIZE_PX)
        self.assertEqual(Options(6, -1).quiet_zone_modules, 0)
        self.assertEqual(Options(6, MAX_QUIET_ZONE_MODULES + 1).quiet_zone_modules, MAX_QUIET_ZONE_MODULES)
        self.assertEqual(Options.recommended(1_000_000, 10).module_size_px, MAX_MODULE_SIZE_PX)
        self.assertEqual(Options.minimum(1080, 540), Options(4, RECOMMENDED_QUIET_ZONE_MODULES))

    def test_markers_fit_the_quad_count(self) -> None:
        for frame in range(0, 500, 7):
            payload = Payload(
                MarkerKind(frame % 4), 9, frame * 7919, MarkerFlags.NONE, frame * 166_667, target_frame_ticks=166_667, intended_display_ticks=frame * 166_700
            )
            self.assertLessEqual(len(generate_quads(payload, Options(), Point(0, 0))), MAX_QUAD_COUNT)


class ModuleMatrixTests(unittest.TestCase):
    def test_bits_are_packed_row_major_most_significant_bit_first(self) -> None:
        for kind in (MarkerKind.FRAME, MarkerKind.SYNC):
            matrix = generate_modules(Payload(kind, 9, 12345, MarkerFlags.NONE, 678))
            self.assertEqual(len(matrix.bits), packed_module_byte_count(matrix.size))
            for y in range(matrix.size):
                for x in range(matrix.size):
                    index = (y * matrix.size) + x
                    self.assertEqual(matrix.is_dark(x, y), (matrix.bits[index // 8] >> (7 - (index % 8))) & 1 == 1)
        self.assertEqual((packed_module_byte_count(41), packed_module_byte_count(25)), (211, 79))

    def test_takes_the_marker_sizes_and_ignores_the_padding(self) -> None:
        matrix = generate_modules(Payload(MarkerKind.SYNC, 3, 1, MarkerFlags.NONE, 2))
        padded = bytearray(matrix.bits)
        padded[-1] |= 0x7F  # 625 modules: the last byte uses 1 bit
        self.assertEqual(ModuleMatrix(25, bytes(padded) + b"extra"), matrix)
        for size, bits in ((24, padded), (45, padded), (25, padded[:78]), (0, padded), (-25, padded)):
            with self.subTest(size), self.assertRaises(ValueError):
                _ = ModuleMatrix(size, bytes(bits))
        # The two sizes are the constants' (a matrix of each is made from enough zero bytes)
        self.assertEqual([ModuleMatrix(size, bytes(211)).size for size in (QR_MODULE_COUNT, SYNC_QR_MODULE_COUNT)], [41, 25])
        # The other QR sizes are not markers: nothing draws their grid
        for size in (21, 29, 33, 37):
            with self.subTest(size), self.assertRaises(ValueError):
                _ = ModuleMatrix(size, bytes(211))
        main = generate_modules(Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NONE, 2))
        self.assertEqual((ModuleMatrix(41, main.bits), ModuleMatrix(41, main.bits).size, len(main.bits)), (main, 41, 211))
        with self.assertRaises(ValueError):
            _ = ModuleMatrix(41, main.bits[:210])


class BitmapTests(unittest.TestCase):
    def test_equals_the_rasterized_quads_in_every_pixel_format(self) -> None:
        width, height = 173, 131
        cases = [
            (Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NONE, 2), Options(3, 4), Point(5, 7)),
            (Payload(MarkerKind.SEQUENCE_END, 1, 99, MarkerFlags.NONE, -5), Options(1, 0), Point(0, 0)),
            (Payload(MarkerKind.SYNC, 0, 7, MarkerFlags.NONE, 0), Options(2, 4), Point(3, 1)),
            (
                Payload(
                    MarkerKind.FRAME,
                    2,
                    0xFFFF_FFFF_FFFF_FFFF,
                    MarkerFlags.NONE,
                    1,
                    target_frame_ticks=4,
                    intended_display_ticks=3,
                    cpu_start_ticks=5,
                    cpu_busy_ticks=6,
                ),
                Options(2, 1),
                Point(-9, -4),
            ),
        ]
        for payload, options, origin in cases:
            matrix = generate_modules(payload)
            expected = software_raster.quads(modules_to_quads(matrix, options, origin), width, height)
            for pixel_format in PixelFormat:
                with self.subTest(payload=payload, pixel_format=pixel_format):
                    size = pixel_format.bytes_per_pixel
                    stride = (width * size) + 5  # padded rows
                    pixels = bytearray([128]) * (stride * height)
                    modules_to_bitmap(matrix, options, origin, pixels, width, height, pixel_format, stride)
                    for y in range(height):
                        row = pixels[y * stride : (y + 1) * stride]
                        wanted = bytearray()
                        for luma in expected[y * width : (y + 1) * width]:
                            wanted += bytes([luma] * min(size, 3)) + (bytes([255 if luma != 128 else 128]) if size == 4 else b"")
                        self.assertEqual(bytes(row[: width * size]), bytes(wanted), f"row {y}")
                        self.assertEqual(bytes(row[width * size :]), bytes([128] * 5), "the row padding is never written")

    def test_a_module_resolution_image_scaled_up_equals_the_full_size_one(self) -> None:
        matrix = generate_modules(Payload(MarkerKind.FRAME, 59, 31, MarkerFlags.NONE, 41))
        small, large, module = Options(1, 4), Options(3, 4), 3
        small_size, large_size = small.marker_size_px(), large.marker_size_px()
        modules = bytearray(small_size * small_size)
        pixels = bytearray(large_size * large_size)
        modules_to_bitmap(matrix, small, Point(0, 0), modules, small_size, small_size)
        modules_to_bitmap(matrix, large, Point(0, 0), pixels, large_size, large_size)
        for y in range(large_size):
            for x in range(large_size):
                self.assertEqual(pixels[(y * large_size) + x], modules[((y // module) * small_size) + (x // module)])

    def test_rejects_invalid_arguments_without_writing(self) -> None:
        matrix = generate_modules(Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NONE, 2))
        pixels = bytearray([128]) * (64 * 64 * 4)
        with self.assertRaises(ValueError):
            modules_to_bitmap(matrix, Options(1, 4), Point(0, 0), pixels, 64, 64, PixelFormat.R8G8B8, (64 * 3) - 1)
        with self.assertRaises(ValueError):
            modules_to_bitmap(matrix, Options(1, 4), Point(0, 0), memoryview(pixels)[:-1], 64, 64, PixelFormat.R8G8B8A8)
        for width, height in ((-1, 64), (64, -1)):
            with self.subTest(width=width, height=height), self.assertRaises(ValueError):
                modules_to_bitmap(matrix, Options(1, 4), Point(0, 0), pixels, width, height)
        with self.assertRaises(ValueError):
            modules_to_bitmap(matrix, Options(1, 4), Point(0, 0), pixels, 64, 64, PixelFormat.R8, 0)
        self.assertTrue(all(value == 128 for value in pixels))
        modules_to_bitmap(matrix, Options(1, 4), Point(500, 500), pixels, 64, 64)
        self.assertTrue(all(value == 128 for value in pixels), "outside the buffer: nothing to draw")


class GridTests(unittest.TestCase):
    def test_vertex_counts_fit_16_bit_indices(self) -> None:
        self.assertEqual((grid_vertex_count(MarkerKind.FRAME), grid_vertex_count(MarkerKind.SYNC)), (1768, 680))

    def test_resolved_indices_equal_the_indexed_triangles(self) -> None:
        base = 100
        for payload in (
            Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NONE, 2),
            Payload(MarkerKind.SYNC, 0, 7, MarkerFlags.NONE, 0),
            Payload(MarkerKind.SEQUENCE_START, 7, 5, MarkerFlags.NONE, 6),
        ):
            for options in (Options(1, 0), Options(3, 4)):
                with self.subTest(payload=payload, options=options):
                    matrix = generate_modules(payload)
                    grid = grid_vertices(payload.kind, options, Point(17, 23))
                    grid_indices = modules_to_grid_indices(matrix, base)
                    vertices, indices = modules_to_indexed(matrix, options, Point(17, 23), base)
                    self.assertEqual([grid[i - base] for i in grid_indices], [vertices[i - base] for i in indices])


if __name__ == "__main__":
    _ = unittest.main()
