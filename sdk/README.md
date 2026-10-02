# mb-framepacing SDK

The part of [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) that goes into your own code, in C++, C#, Python and Unity.
It has modules:

- **marker**: draws a small QR code into every frame your application renders. It carries the frame index and the animation time.
  A capture of the display output, analysed with the mb-framepacing tools, then shows the **animation error**: how far what the
  application animated is from what was actually shown on screen.
- **data**: reads what the mb-framepacing tools capture and analyse.
- **pacer** (C++, off until it is reworked): plans every frame on the display's refreshes and adapts the swap interval to how long frames take, and hands the
  application the values the marker carries.
- **core**: what the modules share: `Point` and `Rectangle` in every language, the time types (`TickCount64`, `TickCount32`, `TimeSpan32`) in C++ and C#, and in C++ the
  library version.

Everything here is under the BSD 3-Clause License. The measuring tools themselves (capture, analysis, GUI) are in
[`measure/`](../measure) under another license.

## Where to start

| You want to                                  | Start with                                                                                                                                                    |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Put the marker into a C++, C# or Python app  | [Integrating the marker](doc/integrating.md), then the library's README below                                                                                 |
| Put the marker into a Unity game             | [Unity](doc/unity.md)                                                                                                                                         |
| Pace your frames and fill the marker from it | [The frame pacer](doc/pacer.md) (off until it is reworked)                                                                                                    |
| Know what to write in each marker field      | [Filling the marker fields](doc/marker-fields.md)                                                                                                             |
| Read the tools' results in your own code     | [The data module](#the-data-module), [the analysis output format](doc/analysis-output-format.md)                                                              |
| Implement the marker or a reader yourself    | [The marker format](doc/marker-format.md), [the capture data format](doc/capture-data-format.md), [the analysis output format](doc/analysis-output-format.md) |
| Know what the marker costs per frame         | [Encoding performance](doc/encoding-performance.md), and the benchmarks in the [C++ README](cpp/README.md)                                                    |

## Pick a library

| Your application                 | Library                                                                                                                         | How to get it                                                                          |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| C++ (any engine or graphics API) | [C++20](cpp/README.md): `mb_framepacing::core`, `::marker`, `::data` (`::pacer` off until it is reworked)                       | Release archive via CMake `FetchContent`, git, `add_subdirectory`, an install or Conan |
| Unity 2021.3+                    | [Unity package](unity/README.md) (`com.manabattery.framepacing`)                                                                | Package Manager, git URL (`#upm/v<version>`)                                           |
| Other C# / .NET                  | [`MB.FramePacing.Marker`](csharp/marker/README.md) (.NET Standard 2.1, with the core [`MB.FramePacing`](csharp/core/README.md)) | The source at an `sdk-v*` tag, as a project reference or a copy                        |
|                                  | [`MB.FramePacing.Data`](csharp/data/README.md) (.NET 10, reads and writes)                                                      |                                                                                        |
| Python 3.12+                     | [`mb_framepacing`](python/README.md): `mb_framepacing.marker`, `mb_framepacing.data`                                            | One package, standard library only                                                     |

The Unity package contains the C# core and marker modules plus Unity helpers (an overlay component that does everything for you).
What the C++ modules add to an executable, per compiler, is in [the C++ README](cpp/README.md#what-it-adds-to-your-executable).
The C# modules are these assemblies (Release builds; `python tools/measure_sdk_size.py --csharp --update-doc` rewrites the table, and
CI checks it):

<!-- sdk-csharp-size-table: generated by tools/measure_sdk_size.py -->

| Assembly                | Target         |     Size |
| ----------------------- | -------------- | -------: |
| `MB.FramePacing`        | netstandard2.1 | 13.5 KiB |
| `MB.FramePacing.Marker` | netstandard2.1 | 27.0 KiB |
| `MB.FramePacing.Data`   | net10.0        | 89.5 KiB |

<!-- /sdk-csharp-size-table -->

## What is here

| Path                            | Contents                                                                                                                                                             |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`cpp/`](cpp/README.md)         | The C++20 library: one CMake project, a folder per module (`core/`, `marker/`, `data/`, and `pacer/`, off until it is reworked), and its Conan recipe (`cpp/conan/`) |
| [`csharp/`](csharp)             | The C# modules: [`core/`](csharp/core/README.md), [`marker/`](csharp/marker/README.md) and [`data/`](csharp/data/README.md)                                          |
| [`python/`](python/README.md)   | The Python package `mb_framepacing`, with the `marker` and `data` subpackages                                                                                        |
| [`unity/`](unity/README.md)     | The Unity package's sources: helpers, samples, and the scripts that assemble and check it                                                                            |
| [`shaders/`](shaders/README.md) | The reference shaders that draw the marker as one quad: HLSL, GLSL for OpenGL 3.3 / ES 3.0, OpenGL ES 2.0 and Vulkan                                                 |
| [`doc/`](doc)                   | The formats, the integration guides and the [vocabulary](doc/vocabulary.md)                                                                                          |
| [`test-data/`](test-data)       | The golden data every language's tests check against: marker images (`markers/`), an analysed test clip (`data/`) and the pacer's scenarios (`pacer/`)               |

## The marker module

- **The same pixels.** Every library draws exactly the same marker: the tests check each one against the golden images the C++
  library writes ([`test-data/markers`](test-data/markers)).
- **One format.** The payload and geometry are specified in [marker-format.md](doc/marker-format.md), the reference all languages
  follow byte for byte.
- **No allocations per frame** (C++ and C#): you give the buffers, the libraries fill them. Zero-allocation tests prove it.
- **Renderer independent.** They encode the marker once per frame and draw it in the form your renderer takes: Direct3D, Vulkan,
  Metal, OpenGL, a 2D API or a pixel buffer.

### Ways to draw it, most efficient first

| #   | Option                                                                              | Per frame (41×41 main marker, about 440 dark runs)           | Needs                                                                         |
| --- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| 1   | **Shader, packed bits**: one quad; the shader reads each module's bit from `Bits()` | 211 bytes (the packed bits as constants), 4 vertices         | A shader of ours: HLSL, GLSL for OpenGL 3.3 / ES 3.0, OpenGL ES 2.0 or Vulkan |
| 2   | **Shader, a texel per module**: one quad and a 41×41 texture                        | 1,681 bytes (the texture), 4 vertices                        | The same, and a single channel texture                                        |
| 3   | **Static grid**: `GridVertices` once, then `ModulesToGridIndices`                   | About 2,600 indices (10 KB as 32 bit, 5 KB as 16 bit)        | Index buffers and vertex colours; the 1,768 vertices stay                     |
| 4   | **Module texture scaled up**: `ModulesToBitmap` at 1 px per module                  | 1,681 pixels                                                 | A texture drawn scaled by a whole number with point filtering, pixel exact    |
| 5   | **Triangles**: `ModulesToIndexed` or `ModulesToTriangles`                           | About 1,750 vertices and 2,600 indices, or 2,600 vertices    | Only vertex colours: the simplest to add to a renderer                        |
| 6   | **Rectangles**: `ModulesToQuads`                                                    | About 440 filled rectangles (`MarkerQuad`)                   | A 2D fill-rectangle API                                                       |
| 7   | **Full-size bitmap**: `ModulesToBitmap`                                             | The marker's pixels (294×294 at 6 px per module: 86 KB grey) | A CPU pixel buffer: software rendering, video frames, images                  |

Every option draws exactly the same pixels, from one encode per frame (the 211 byte module matrix). The shaders for 1 and 2 and how
to draw them are in [`shaders/`](shaders/README.md). On OpenGL ES 2.0 they need `highp` floats in the fragment shader; without it,
draw 3 or 5. The names above are C++ and C#; Python's are the same in snake case. Unity's overlay draws 1 (Render Mode **Shader
Packed Bits**, the default), 2 (**Shader**), 4 (**Bitmap**) or 6 (**Geometry**, which works everywhere and is the fallback without
shader model 3.5), and `FrameMarkerMesh` draws 3 from your own command buffers.

How to put the marker into an application (size, place, start and end markers, the rules that keep it readable) is in
[Integrating the marker](doc/integrating.md); Unity has its own [guide](doc/unity.md). What to write in each field, for typical frame
pacers, is in [Filling the marker fields](doc/marker-fields.md).

## The data module

It reads what the tools write, in your own code:

- **The capture data** (`captures.mbcd`): every captured frame's timestamps and the markers' bytes as read
  ([format](doc/capture-data-format.md)).
- **The analysis output** (`analysis/summary.json`, `captures.csv`, `run-<id>-frames.csv`): every run's counts, statistics, pacing and
  histograms, and every presented frame's display time, display time step, animation error and pacing
  ([format](doc/analysis-output-format.md)).

| Language | Module                                                   | Reads | Writes |
| -------- | -------------------------------------------------------- | ----- | ------ |
| C#       | [`MB.FramePacing.Data`](csharp/data/README.md) (.NET 10) | yes   | yes    |
| Python   | [`mb_framepacing.data`](python/README.md) (3.12+)        | yes   | no     |
| C++      | [`mb_framepacing::data`](cpp/README.md) (C++20)          | yes   | no     |

The C# module is the reference: the tools write every file through it. A marker payload inside the capture data is decoded with the
marker module of the same language.

- **Format versions.** `captures.mbcd` has a format version in its header, and `summary.json` a `formatVersion` that covers the CSV
  files it names. A reader refuses a newer version than it knows. Within a version, fields and columns may be added; readers look CSV
  columns up by name and ignore the ones they do not know.
- **Golden data.** [`test-data/data`](test-data/data) holds a test clip imported and analysed by the tools, and `digest.json`: the
  counts and sums every language's reader must read from it. `python tools/update_test_data.py` regenerates it after a format change.

## Version

The SDK has one version for every module and language, [`VERSION`](VERSION), released with `sdk-v<version>` tags (see
[Releasing](../doc/releasing.md)). The version follows semantic versioning. While it is 0.x, a new minor version may change the API.

## License

BSD 3-Clause, the same for everything under `sdk/`. [`LICENSE`](LICENSE) holds the text, and the release archives and packages ship
it. The QR encoder in every marker library is based on the QR Code generator library by Project Nayuki (MIT): the C++ and C# encoders
are ports that carry its notice (`QrEncoder.cpp`, `QrEncoder.cs`), Python keeps its port in a `third_party/` folder, and the library
itself is vendored next to the C++ module as the reference its tests and benchmarks compare with (`cpp/marker/reference/third_party`;
see [Encoding performance](doc/encoding-performance.md)). The C++ data module uses nlohmann/json (MIT)
inside the library. The license texts are listed in [`licenses/`](../licenses/README.md).
