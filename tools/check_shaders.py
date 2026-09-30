#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check the marker's reference shaders (sdk/shaders) with glslang (required; CI installs it):
  - gl/      as OpenGL 3.3 core and as OpenGL ES 3.0 (the header their comment names);
  - gles2/   as GLSL ES 1.00 (OpenGL ES 2.0, WebGL 1);
  - vulkan/  to SPIR-V for Vulkan 1.0;
  - hlsl/    every entry point, and with DXC also as DXIL (shader model 6.0) and, when that DXC supports it, as SPIR-V.

With --render it also draws them with OpenGL (moderngl and a 4.1 context: uv run --with moderngl tools/check_shaders.py --render) and compares every pixel with the Python
marker library's bitmap, for every marker kind at several module sizes, quiet zones and origins: gl/ and gles2/ directly, vulkan/ and
hlsl/ compiled to SPIR-V (glslang, DXC) and translated to OpenGL GLSL by SPIRV-Cross (skipped without them).

The tools are found on PATH, or in the Vulkan SDK (VULKAN_SDK, which its installer sets; it has all three).

  python tools/check_shaders.py
  python tools/check_shaders.py --render
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
SHADERS = REPOSITORY_ROOT / "sdk" / "shaders"
HLSL = SHADERS / "hlsl" / "FrameMarkerShaders.hlsl"
STAGES = ("frame_marker.vert", "frame_marker_packed.frag", "frame_marker_modules.frag")
# Entry point, glslang stage, DXC profile
HLSL_ENTRIES = (("FrameMarkerVS", "vert", "vs_6_0"), ("FrameMarkerPackedPS", "frag", "ps_6_0"), ("FrameMarkerModulesPS", "frag", "ps_6_0"))
ES3_HEADER = "#version 300 es\nprecision highp float;\nprecision highp int;\n"


class Arguments(argparse.Namespace):
    render: bool = False


def find_tool(name: str) -> str | None:
    """The Vulkan SDK's copy first (its DXC can write SPIR-V), then PATH."""
    if sdk := os.environ.get("VULKAN_SDK"):
        for folder in ("Bin", "bin"):
            for candidate in (Path(sdk) / folder / name, Path(sdk) / folder / f"{name}.exe"):
                if candidate.is_file():
                    return str(candidate)
    return shutil.which(name)


def run(command: list[str], what: str) -> bool:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"FAIL {what}\n{result.stdout}{result.stderr}".rstrip())
        return False
    print(f"  ok  {what}")
    return True


def es3_variant(source: Path, work: Path) -> Path:
    """A gl/ shader with its '#version 330 core' line replaced by the OpenGL ES 3.0 header its comment names."""
    lines = source.read_text(encoding="utf-8").splitlines(keepends=True)
    assert lines[0].startswith("#version 330 core"), source
    target = work / "es3" / source.name
    target.parent.mkdir(exist_ok=True)
    _ = target.write_text(ES3_HEADER + "".join(lines[1:]), encoding="utf-8")
    return target


def dxc_writes_spirv(dxc: str, work: Path) -> bool:
    result = subprocess.run([dxc, "-spirv", "-T", "vs_6_0", "-E", "FrameMarkerVS", "-Fo", str(work / "probe.spv"), str(HLSL)], capture_output=True, text=True)
    return result.returncode == 0


def compile_all(glslang: str, work: Path) -> bool:
    ok = True
    for name in STAGES:
        ok &= run([glslang, str(SHADERS / "gl" / name)], f"gl/{name} (OpenGL 3.3 core)")
        ok &= run([glslang, str(es3_variant(SHADERS / "gl" / name, work))], f"gl/{name} (OpenGL ES 3.0)")
        ok &= run([glslang, str(SHADERS / "gles2" / name)], f"gles2/{name} (GLSL ES 1.00)")
        spv = work / f"vulkan.{name}.spv"
        ok &= run([glslang, "-V", "--target-env", "vulkan1.0", "-o", str(spv), str(SHADERS / "vulkan" / name)], f"vulkan/{name} (SPIR-V)")
    for entry, stage, _ in HLSL_ENTRIES:
        spv = work / f"{entry}.glslang.spv"
        ok &= run([glslang, "-V", "-D", "-S", stage, "-e", entry, "-o", str(spv), str(HLSL)], f"hlsl {entry} (glslang)")
    if (dxc := find_tool("dxc")) is None:
        print("  --  DXC not found: DXIL and its SPIR-V skipped")
        return ok
    spirv = dxc_writes_spirv(dxc, work)
    for entry, _, profile in HLSL_ENTRIES:
        ok &= run([dxc, "-T", profile, "-E", entry, "-Fo", str(work / f"{entry}.dxil"), str(HLSL)], f"hlsl {entry} (DXC, DXIL)")
        if spirv:
            invert = ["-fvk-invert-y"] if profile.startswith("vs") else []
            spv = work / f"{entry}.spv"
            ok &= run([dxc, "-spirv", *invert, "-T", profile, "-E", entry, "-Fo", str(spv), str(HLSL)], f"hlsl {entry} (DXC, SPIR-V)")
    if not spirv:
        print("  --  this DXC cannot write SPIR-V: its SPIR-V check skipped")
    return ok


def spirv_to_glsl(cross: str, spv: Path, vertex: bool, flip: bool) -> str:
    """SPIR-V as OpenGL 3.3 GLSL, the varying named 'uv' on both sides so the stages link; flip turns Vulkan's clip space (+y down) into
    OpenGL's."""
    command = [cross, str(spv), "--version", "330", "--no-es", "--rename-interface-variable", "out" if vertex else "in", "0", "uv"]
    if flip and vertex:
        command.append("--flip-vert-y")
    return subprocess.run(command, check=True, capture_output=True, text=True).stdout


def through_spirv(glslang: str, work: Path) -> dict[str, dict[str, str]]:
    """vulkan/ and hlsl/ as OpenGL GLSL (vertex, packed, modules), through SPIR-V and SPIRV-Cross; empty without the tools."""
    cross = find_tool("spirv-cross")
    if cross is None:
        print("  --  SPIRV-Cross not found: vulkan/ and hlsl/ are not drawn")
        return {}
    programs: dict[str, dict[str, str]] = {"vulkan": {}}
    for name in STAGES:
        spv = work / f"vulkan.render.{name}.spv"
        _ = subprocess.run([glslang, "-V", "-o", str(spv), str(SHADERS / "vulkan" / name)], check=True, capture_output=True)
        programs["vulkan"][name] = spirv_to_glsl(cross, spv, name.endswith(".vert"), flip=True)
    dxc = find_tool("dxc")
    if dxc is None or not dxc_writes_spirv(dxc, work):
        print("  --  DXC with SPIR-V not found: hlsl/ is not drawn")
        return programs
    programs["hlsl"] = {}
    for (entry, stage, profile), name in zip(HLSL_ENTRIES, STAGES, strict=True):
        spv = work / f"{entry}.render.spv"
        # Without the Vulkan y flip: OpenGL's clip space is Direct3D's
        _ = subprocess.run([dxc, "-spirv", "-T", profile, "-E", entry, "-Fo", str(spv), str(HLSL)], check=True, capture_output=True)
        programs["hlsl"][name] = spirv_to_glsl(cross, spv, stage == "vert", flip=False)
    return programs


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--render", action="store_true", help="Also draw the shaders with OpenGL and compare every pixel (needs moderngl).")
    args = parser.parse_args(namespace=Arguments())
    glslang = find_tool("glslangValidator")
    if glslang is None:
        print("glslangValidator not found: install glslang (Linux: glslang-tools) or the Vulkan SDK")
        return 1
    with tempfile.TemporaryDirectory() as folder:
        work = Path(folder)
        ok = compile_all(glslang, work)
        if args.render:
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            from shader_render import render_all  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport] (moderngl only for --render)

            ok &= render_all(SHADERS, through_spirv(glslang, work))
    print("Shaders: OK" if ok else "Shaders: FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
