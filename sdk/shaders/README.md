# MB Frame Marker shaders

Ready-made shaders that draw the frame marker as **one opaque quad**: the fragment shader finds the module under each pixel and outputs
pure black or white. They draw exactly the pixels of the libraries' `ModulesToBitmap` (`tools/check_shaders.py --render` compares every
pixel, for every marker kind, at several module sizes, quiet zones and origins). This is the fastest way to draw the marker: per frame
only 211 bytes change.

| Folder    | API                                         | Language                                      |
| --------- | ------------------------------------------- | --------------------------------------------- |
| `hlsl/`   | Direct3D 11 and 12 (and Vulkan through DXC) | HLSL, shader model 4.0 (fxc) or 6.0 (DXC)     |
| `gl/`     | OpenGL 3.3+, OpenGL ES 3.0+, WebGL 2        | GLSL 3.30 (ES 3.00: swap the header, below)   |
| `gles2/`  | OpenGL ES 2.0, WebGL 1                      | GLSL ES 1.00, float arithmetic only           |
| `vulkan/` | Vulkan 1.0+                                 | GLSL 4.50 for SPIR-V (glslangValidator/glslc) |

Every folder has two fragment shaders; pick one:

| Variant                          | Per frame                                     | Where the modules come from                                                                                         |
| -------------------------------- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| **Packed** (`*packed*`, fastest) | 211 bytes: `ModuleMatrix::Bits()` as they are | 14 `uint4` of constants (`hlsl/`, `gl/`, `vulkan/`), or a 211 × 1 texture (`gles2/`: no integers, no large arrays)  |
| **Modules** (`*modules*`)        | 1,681 bytes: one byte per module              | A 41 × 41 single channel texture: `ModulesToBitmap(matrix, Options(1, 0), {0, 0}, texels, 41, 41, PixelFormat::R8)` |

> **Photosensitivity warning.** The marker is a high-contrast pattern that changes every frame, and flickering patterns can
> trigger seizures in people with photosensitive epilepsy. Draw it in test builds only; see
> [Photosensitivity](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/integrating.md#photosensitivity).

## Drawing it

Once:

1. Compile the vertex shader and the fragment shader you picked.
2. Make the pipeline state: **no blending, no depth test or depth writes, no culling**, triangle strip. Draw into the swap chain's
   image at its full resolution (no MSAA resolve or scaling after it).
3. Make the constants, and for the modules variant (or `gles2/` packed) the texture: single channel 8 bit (`R8_UNORM`, `GL_R8`,
   `GL_LUMINANCE` on OpenGL ES 2.0), nearest filtering, clamp to edge, no mip maps. OpenGL: `glPixelStorei(GL_UNPACK_ALIGNMENT, 1)`
   before uploading, since 41 and 211 are not multiples of 4.

Every frame, **last**, after post effects, upscaling and UI:

1. Encode the marker: `GenerateModules(payload, matrix)` (C#: `TryGenerateModules`).
2. Set the constants: the output size, the origin (`RecommendedOrigin`), `ModuleSizePx`, `QuietZoneModules` and the symbol size
   (`matrix.Size()`: 41, or 25 for the sync marker), and copy `matrix.Bits()` into the packed bits (the rest stays zero); or upload the
   module texture.
3. Draw **4 vertices** as a triangle strip. `hlsl/`, `gl/` and `vulkan/` make the quad from the vertex index and need no vertex or
   index buffer (OpenGL core profile: bind an empty vertex array object). `gles2/` has no vertex index: bind a buffer of the 4 corners
   `(0, 0) (1, 0) (0, 1) (1, 1)` to the attribute `corner`.
4. The sync marker is a second draw with its own constants (its origin, size 25) and its own bits; with one constant buffer, update it
   between the two draws or use two.

The texture coordinate the vertex shader passes on is the marker-local pixel coordinate (top-left 0, 0, +y down), so every fragment
gets `(px + 0.5, py + 0.5)` and `floor(uv / ModuleSizePx)` is exact on every platform; the y flips between APIs are in the vertex
shaders (OpenGL and Direct3D: +y up in clip space; Vulkan: +y down).

### The constants

`hlsl/` and `vulkan/` share one constant block (std140, 256 bytes), so an engine with both can fill it the same way:

| Offset | Field                                                |
| ------ | ---------------------------------------------------- |
| 0      | `float2` output size, in pixels                      |
| 8      | `float2` origin, the marker's top-left pixel         |
| 16     | `float` module size, in pixels                       |
| 20     | `float` quiet zone, in modules                       |
| 24     | `float` symbol size: 41, or 25 for the sync marker   |
| 28     | `float` unused                                       |
| 32     | `uint4[14]` packed bits: `Bits()` copied as they are |

`gl/` and `gles2/` use plain uniforms with the same names; `gl/`'s packed bits are `uniform uvec4 bits[14]`, set with
`glUniform4uiv(location, 14, words)`.

### The packed bits

Module `i = row × size + column` is bit `7 − (i mod 8)` of byte `i / 8`: row-major, most significant bit first, continuous across rows,
a set bit dark. That is `Bits()` (211 bytes for the 41 × 41 main marker, 79 for the 25 × 25 sync marker), copied as it is: the 32 bit
words hold 4 bytes each, the first in their lowest 8 bits (little endian, as on every platform the tools run on).

## Per API

- **Direct3D 11 and 12** (`hlsl/FrameMarkerShaders.hlsl`): entry points `FrameMarkerVS`, `FrameMarkerPackedPS` and
  `FrameMarkerModulesPS`; `fxc /T vs_4_0 /E FrameMarkerVS` and `/T ps_4_0` (or DXC with `vs_6_0` / `ps_6_0`). Constants in `b0`, the
  module texture in `t0`. `hlsl/FrameMarker.hlsl` holds the module lookup alone, to include in your own shaders.
- **Vulkan**: `vulkan/*.vert` and `*.frag` with `glslangValidator -V` or `glslc`: the constants at set 0, binding 0 (a uniform
  buffer; 256 bytes is more than the push constant space Vulkan guarantees), the module image at set 0, binding 1 (a combined image
  sampler). Or the HLSL through `dxc -spirv`, the vertex shader with `-fvk-invert-y`.
- **OpenGL 3.3+** (`gl/`): link `frame_marker.vert` with one fragment shader. **OpenGL ES 3.0 and WebGL 2** (which takes GLSL ES
  only): replace each file's first line with `#version 300 es` followed by `precision highp float; precision highp int;`.
- **OpenGL ES 2.0 and WebGL 1** (`gles2/`): GLSL ES 1.00 has no integer operations, so the packed variant gets each bit with float
  arithmetic and reads the 211 bytes from a 211 × 1 `GL_LUMINANCE` texture. Both variants **need `highp` in the fragment shader**:
  with 32 bit floats every value is exact, with the `mediump` OpenGL ES 2.0 guarantees (some GPUs, such as Mali-400, have no more) a
  pixel near a module edge could land in the wrong module. Without `highp` they do not compile (`#error`): draw the marker as
  geometry instead (`GridVertices` and `ModulesToGridIndices` with 16 bit indices, or `ModulesToTriangles`), which is exact on every
  GPU.
- **Metal**: translate the HLSL or the Vulkan SPIR-V with SPIRV-Cross (`--msl`).
- **Unity**: the Unity package's overlay draws with these lookups already (`Hidden/MB/FrameMarkerQuadPacked`, the default).

## License

BSD 3-Clause, as the marker libraries ([LICENSE](../LICENSE)).
