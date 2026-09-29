# MB Frame Marker

The marker libraries of [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing): the part that goes **into your
application**. It draws a small QR code into every frame that carries the frame index and the animation time. A capture of the display
output, analysed with the mb-framepacing tools, then shows the **animation error**: how far what the application animated is from
what was actually shown on screen.

## Pick a library

| Your application                 | Library                                    | How to get it                                                                   |
| -------------------------------- | ------------------------------------------ | ------------------------------------------------------------------------------- |
| C++ (any engine or graphics API) | [C++20](cpp/README.md)                     | Release archive via CMake `FetchContent`, git, `add_subdirectory` or an install |
| Unity 2021.3+                    | [Unity package](unity/README.md)           | Package Manager, git URL (`#upm/v<version>`)                                    |
| Other C# / .NET                  | [C# (.NET Standard 2.1)](csharp/README.md) | The source at a `marker-v*` tag, as a project reference or a copy               |
| Python 3.11+                     | [Python](python/README.md)                 | The `mb_framemarker` package (standard library only)                            |

The Unity package contains the C# library plus Unity helpers (an overlay component that does everything for you).

## What they share

- **The same pixels.** Every library draws exactly the same marker: the tests check each one against the golden images the C++
  library writes ([`test-data/markers`](../test-data/markers)).
- **One format.** The payload and geometry are specified in [marker-format.md](../doc/marker-format.md), the reference all four
  follow byte for byte.
- **No allocations per frame** (C++ and C#): you give the buffers, the libraries fill them. Zero-allocation tests prove it.
- **Renderer independent.** They encode the marker once per frame and draw it in the form your renderer takes: Direct3D, Vulkan,
  Metal, OpenGL, a 2D API or a pixel buffer.

## Ways to draw it, most efficient first

| #   | Option                                                                              | Per frame (41×41 main marker, about 440 dark runs)           | Needs                                                                         |
| --- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| 1   | **Shader, packed bits**: one quad; the shader reads each module's bit from `Bits()` | 211 bytes (the packed bits as constants), 4 vertices         | A shader of ours: HLSL, GLSL for OpenGL 3.3 / ES 3.0, OpenGL ES 2.0 or Vulkan |
| 2   | **Shader, a texel per module**: one quad and a 41×41 texture                        | 1,681 bytes (the texture), 4 vertices                        | The same, and a single channel texture                                        |
| 3   | **Static grid**: `GridVertices` once, then `ModulesToGridIndices`                   | About 2,600 indices (10 KB as 32 bit, 5 KB as 16 bit)        | Index buffers and vertex colours; the 1,768 vertices stay                     |
| 4   | **Module texture scaled up**: `ModulesToBitmap` at 1 px per module                  | 1,681 pixels                                                 | A texture drawn scaled by a whole number with point filtering, pixel exact    |
| 5   | **Triangles**: `ModulesToIndexed` or `ModulesToTriangles`                           | About 1,750 vertices and 2,600 indices, or 2,600 vertices    | Only vertex colours: the simplest to add to a renderer                        |
| 6   | **Rectangles**: `ModulesToQuads`                                                    | About 440 filled rectangles                                  | A 2D fill-rectangle API                                                       |
| 7   | **Full-size bitmap**: `ModulesToBitmap`                                             | The marker's pixels (294×294 at 6 px per module: 86 KB grey) | A CPU pixel buffer: software rendering, video frames, images                  |

Every option draws exactly the same pixels, from one encode per frame (the 211 byte module matrix). The shaders for 1 and 2
(HLSL for Direct3D, GLSL for OpenGL 3.3 and OpenGL ES 3.0, OpenGL ES 2.0 and Vulkan) and how to draw them are in
[`sdk/marker/shaders`](shaders/README.md). On OpenGL ES 2.0 they need `highp` floats in the fragment shader; without it, draw 3 or 5. The names above are C++ and C#; Python's are the same in snake case.
Unity's overlay draws 1 (Render Mode **Shader Packed Bits**, the default), 2 (**Shader**), 4 (**Bitmap**) or 6 (**Geometry**, which
works everywhere and is the fallback without shader model 3.5), and `FrameMarkerMesh` draws 3 from your own command buffers.

How to put the marker into an application (size, place, start and end markers, the rules that keep it readable) is in
[Integrating the marker](../doc/integrating.md); Unity has its own [guide](../doc/unity.md). What to write in each field, for
typical frame pacers, is in [Filling the marker fields](../doc/marker-fields.md).

## Version

All four libraries share one version, [`VERSION`](VERSION), released with `marker-v<version>` tags (see
[Releasing](../../doc/releasing.md)). While it is 0.x, a new minor version may change the API.

## License

BSD 3-Clause ([LICENSE](LICENSE)). The QR encoder in every library is based on the QR Code generator library by Project Nayuki (MIT):
C++ and Python keep it in their own `third_party/` folder, and the C# port carries the notice in `QrEncoder.cs`. The license texts
are listed in [`licenses/`](../../licenses/README.md).
