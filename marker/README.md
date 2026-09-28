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
- **Renderer independent.** They give you pixel aligned geometry (quads, triangles or indexed triangles) to draw with whatever you
  already use: Direct3D, Vulkan, Metal, OpenGL, a 2D API or a pixel buffer.

How to put the marker into an application (size, place, start and end markers, the rules that keep it readable) is in
[Integrating the marker](../doc/integrating.md); Unity has its own [guide](../doc/unity.md).

## Version

All four libraries share one version, [`VERSION`](VERSION), released with `marker-v<version>` tags (see
[Releasing](../doc/releasing.md)). While it is 0.x, a new minor version may change the API.

## License

BSD 3-Clause ([LICENSE](LICENSE)). The QR encoder in every library is based on the QR Code generator library by Project Nayuki (MIT):
C++ and Python keep it in their own `third_party/` folder, and the C# port carries the notice in `QrEncoder.cs`. The license texts
are listed in [`licenses/`](../licenses/README.md).
