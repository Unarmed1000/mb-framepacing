# MB Frame Pacing Data for C++

`mb_framepacingdata` reads the data of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) tools: the capture data
(`captures.mbcd`, [format](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/capture-data-format.md)) and the analysis
output (`summary.json`, `captures.csv`, `run-<id>-frames.csv`,
[format](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/analysis-output-format.md)). C++20, CMake 4.0+. It reads what the
C# library reads: the tests check it against the same golden data (`test-data/data`).

It is its own library next to the marker library, which it uses to decode the markers' payloads (`mb::framemarker`). summary.json is
parsed with [nlohmann/json](https://github.com/nlohmann/json), inside the library only: it is not part of the API.

## Add it

Inside the repository, `add_subdirectory(sdk/data/cpp)` adds the marker library from `sdk/marker/cpp` too. Elsewhere, provide the marker library
first (a target `mb::framemarker` from `add_subdirectory`/`FetchContent`, or an installed `mb_framemarker`), then add this folder:

```cmake
add_subdirectory(third_party/mb-framepacing/sdk/data/cpp)
target_link_libraries(my_tool PRIVATE mb::framepacingdata)
```

With Conan 2, `mb-framepacingdata/0.1.0` from the recipes in
[`sdk/conan`](https://github.com/Unarmed1000/mb-framepacing/tree/master/sdk/conan) brings the marker library and nlohmann/json along
(`conan remote add mb-framepacing <checkout>/sdk/conan --type local-recipes-index`, the profile with `compiler.cppstd=20`), and
`find_package(mb_framepacingdata CONFIG REQUIRED)` gives `mb::framepacingdata`.

nlohmann/json is found with `find_package(nlohmann_json 3.12)` when installed, and downloaded (a pinned release) otherwise. When the
library is not the top-level project its tests and warnings-as-errors are off (`MB_FRAMEPACINGDATA_BUILD_TESTS`,
`MB_FRAMEPACINGDATA_WARNINGS_AS_ERRORS`), so GoogleTest is never downloaded.

## Quick start

```cpp
#include <mb/framepacingdata/FramePacingData.hpp>
namespace FD = MB::FramePacingData;

const auto analysis = FD::FindAnalysis(captureFolder);   // the capture folder's analysis folder (or the folder itself)
const FD::AnalysisSummary summary = FD::ReadSummary(*analysis / FD::SummaryFileName);
for (const FD::SummaryRun& run : summary.Runs)
{
  for (const FD::FrameRow& frame : FD::ReadFrames(*analysis / run.FramesFile))
  {
    // Times are 100 ns ticks; an empty cell is an empty std::optional
    if (frame.AnimationErrorTicks)
    {
      std::printf("%llu: %.4f ms\n", static_cast<unsigned long long>(frame.FrameIndex), *frame.AnimationErrorTicks / 10'000.0);
    }
  }
}

FD::CaptureDataReader reader(captureFolder / FD::CaptureDataFileName);
for (const FD::CaptureDataRecord& record : reader.ReadAll())
{
  MB::FrameMarker::Payload payload;
  if (record.TryDecodeMain(payload))
  {
    // payload.FrameIndex, payload.AnimationTicks, ...
  }
}
```

Reading allocates and throws: `FD::DataFormatError` for a file it cannot read (another kind of file, damaged content, or a newer format
version, whose message says to update), `std::runtime_error` for a file it cannot open.

## API

Everything is declared by `<mb/framepacingdata/FramePacingData.hpp>` in `MB::FramePacingData`, one header per type.

| Function or type                                                            | What it does                                                                 |
| --------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `CaptureDataReader`, `CaptureDataHeader`, `CaptureDataRecord`               | `captures.mbcd`: the header and the records (`TryDecodeMain`, ...)           |
| `CaptureDataStatus`, `DataRect`, `MarkerLocation`, `UnknownTicks`           | A record's status, where the markers are, a missing device time              |
| `ReadSummary`, `ParseSummary`, `AnalysisSummary` and the `Summary…` structs | `summary.json` (capture.json inside it as JSON text)                         |
| `ReadFrames`, `FrameRow`                                                    | A run's frames CSV, by column name                                           |
| `ReadCaptures`, `CaptureCsvRow`                                             | `captures.csv`, by column name                                               |
| `FindAnalysis`, `FramesFileName`, `ParseTicks`, the file name constants     | The analysis folder, the file names, the CSV time format                     |
| `GetLibraryVersion`, `LibraryVersion`; `Version.hpp` (include it yourself)  | The linked library's version; at compile time, for `#if` and `static_assert` |

## Build and test

```sh
cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # or linux, linux-clang, macos
```

## License

BSD 3-Clause (`../LICENSE`). nlohmann/json is MIT; the tests use GoogleTest (BSD 3-Clause). Their license texts are in the repository's
`licenses/`.
