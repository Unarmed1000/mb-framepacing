# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""check_shaders.py --render: draw the marker's reference shaders with OpenGL (moderngl, a 4.1 context, which also takes GLSL ES 1.00) and
compare every pixel with mb_framemarker's modules_to_bitmap, for every marker kind at several module sizes, quiet zones and origins. The
only script that needs a package beyond the standard library, and only for this optional check."""

import random
import struct
import sys
from collections.abc import Iterator
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "marker" / "python"))
from mb_framemarker import (  # noqa: E402
    MarkerKind,
    ModuleMatrix,
    Options,
    Payload,
    PixelFormat,
    Point,
    SequenceId,
    StartMetadata,
    generate_modules,
    marker_size_px,
    modules_to_bitmap,
)

WIDTH = 640
HEIGHT = 400
GREY = 128
CASES = 24
STAGES = ("frame_marker.vert", "frame_marker_packed.frag", "frame_marker_modules.frag")
MODULES_UNIT = 0
BITS_UNIT = 1


def cases() -> Iterator[tuple[int, MarkerKind, ModuleMatrix, Options, Point]]:
    """Frame, start, end and sync markers with random payloads, module sizes 1 to 6, quiet zones 4, 2 and 0, random origins."""
    rng = random.Random(4)
    kinds = (MarkerKind.FRAME, MarkerKind.SEQUENCE_START, MarkerKind.SEQUENCE_END, MarkerKind.SYNC)
    for case in range(CASES):
        kind = kinds[case % len(kinds)]
        payload = Payload(
            rng.randrange(1 << 40),
            rng.randrange(1 << 50),
            run_id=rng.randrange(1, 1000),
            kind=kind,
            intended_display_ticks=rng.randrange(1 << 50),
            target_frame_ticks=166_667,
            cpu_start_ticks=rng.randrange(1 << 50),
            cpu_busy_ticks=rng.randrange(1 << 20),
        )
        if kind == MarkerKind.SEQUENCE_START:
            matrix = generate_modules(payload, StartMetadata(rng.randrange(1 << 60), SequenceId.from_text("shader-check")))
        else:
            matrix = generate_modules(payload)
        options = Options(module_size_px=1 + (case % 6), quiet_zone_modules=(4, 2, 0)[case % 3])
        size = marker_size_px(options, kind)
        yield case, kind, matrix, options, Point(rng.randrange(0, WIDTH - size), rng.randrange(0, HEIGHT - size))


def render_all(shaders: Path, through_spirv: dict[str, dict[str, str]]) -> bool:
    try:
        import moderngl  # pyright: ignore[reportMissingImports]
    except ImportError:
        print("FAIL --render needs moderngl: pip install moderngl")
        return False
    context = moderngl.create_standalone_context(require=410)
    framebuffer = context.simple_framebuffer((WIDTH, HEIGHT), components=4)
    framebuffer.use()

    sources: dict[str, dict[str, str]] = {
        folder: {name: (shaders / folder / name).read_text(encoding="utf-8") for name in STAGES} for folder in ("gl", "gles2")
    }
    sources.update(through_spirv)
    programs = {}
    for folder, stages in sources.items():
        for variant in ("packed", "modules"):
            programs[f"{folder} {variant}"] = (
                folder,
                context.program(vertex_shader=stages["frame_marker.vert"], fragment_shader=stages[f"frame_marker_{variant}.frag"]),
            )
    # gles2/ has no gl_VertexID: its quad's corners come from a vertex buffer
    corners = context.buffer(struct.pack("<8f", 0, 0, 1, 0, 0, 1, 1, 1))
    modules = context.texture((41, 41), 1, dtype="f1")
    bits = context.texture((211, 1), 1, dtype="f1")
    for texture in (modules, bits):
        texture.filter = (moderngl.NEAREST, moderngl.NEAREST)
        texture.repeat_x = texture.repeat_y = False
    # The constant block of hlsl/ and vulkan/: 8 floats, then 14 uint4 of packed bits
    constants = context.buffer(reserve=32 + 224)

    failed = 0
    for case, kind, matrix, options, origin in cases():
        expected = bytearray([GREY]) * (WIDTH * HEIGHT)
        modules_to_bitmap(matrix, options, origin, expected, WIDTH, HEIGHT, PixelFormat.GRAY8)
        texels = bytearray(41 * 41)
        modules_to_bitmap(matrix, Options(module_size_px=1, quiet_zone_modules=0), Point(0, 0), texels, 41, 41, PixelFormat.GRAY8)
        modules.write(bytes(texels))
        packed = bytes(matrix.bits) + bytes(224 - len(matrix.bits))
        bits.write(packed[:211])
        values = (WIDTH, HEIGHT, origin.x, origin.y, options.module_size_px, options.quiet_zone_modules, matrix.size, 0)
        constants.write(struct.pack("<8f", *values) + packed)
        for name, (folder, program) in programs.items():
            if folder in ("gl", "gles2"):
                uniforms = {
                    "outputSize": (WIDTH, HEIGHT),
                    "origin": (origin.x, origin.y),
                    "moduleSizePx": options.module_size_px,
                    "quietZoneModules": options.quiet_zone_modules,
                    "size": matrix.size,
                    "modules": MODULES_UNIT,
                }
                for key, value in uniforms.items():
                    if key in program:
                        program[key].value = value
                if "bits" in program:
                    if folder == "gl":
                        program["bits"].write(packed)
                    else:
                        program["bits"].value = BITS_UNIT
            else:
                for key in program:
                    if "FrameMarkerConstants" in key:
                        program[key].binding = 0
                    elif "modules" in key.lower():
                        program[key].value = MODULES_UNIT
                constants.bind_to_uniform_block(0)
            modules.use(MODULES_UNIT)
            bits.use(BITS_UNIT)
            context.clear(GREY / 255, GREY / 255, GREY / 255, 1.0)
            content = [(corners, "2f", "corner")] if folder == "gles2" else []
            context.vertex_array(program, content).render(moderngl.TRIANGLE_STRIP, vertices=4)
            # OpenGL reads the bottom row first
            rows = framebuffer.read(components=1)
            pixels = b"".join(rows[y * WIDTH : (y + 1) * WIDTH] for y in reversed(range(HEIGHT)))
            wrong = sum(1 for a, b in zip(pixels, expected, strict=True) if a != b)
            if wrong:
                failed += 1
                print(f"FAIL {name} case {case} ({kind.name}, {options.module_size_px} px, quiet zone {options.quiet_zone_modules}): {wrong} pixels")
    drawn = len(programs) * CASES
    print(f"  {'ok' if failed == 0 else '--'}  {', '.join(programs)}: drawn {drawn} times ({CASES} markers each), {failed} different")
    return failed == 0
