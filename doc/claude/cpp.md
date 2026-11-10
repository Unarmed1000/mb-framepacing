# C++ library: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **C++ formatting and linting**
  - C++ follows `sdk/cpp/.clang-format` and `sdk/cpp/.clang-tidy` (namespaces are CamelCase: `MB::FramePacing::Marker`). The
    `.clang-tidy` checks and options are gtec-demo-framework's (plus `hicpp-exception-baseclass`). Suppress a finding only with
    `NOLINTNEXTLINE(<check>)` and a comment line saying why (the MSVC standard library makes `bugprone-exception-escape` report
    allocation failures that are caught).
  - `python tools/check_cpp.py` runs both on our sources only (never `third_party/` or fetched dependencies) of every module
    (`--module core|marker|data|pacer` for one), with the versions CI pins in `uv.lock` (`uv run tools/check_cpp.py`). clang-tidy needs a
    configured build: `sdk/cpp/build/<preset>`, default `windows` (the VS generator writes no compile database, so the script passes the
    include paths); `--preset` takes another one. With a compile database (`linux-sanitize`, CI) clang-tidy checks the sources that build
    compiles: a module that build leaves out is skipped, and the script says so. To apply formatting: `clang-format -i` on the files the script lists.
  - **The C++ library is one project of modules** (Boost/Poco style): a folder per module (`sdk/cpp/<module>/{include,source,tests}`),
    each a static library `mb_framepacing_<module>` (alias and export `mb_framepacing::<module>`), headers `<mb/framepacing/<module>/<Type>.hpp>`,
    grouped in subfolders where a module has many (`core/time/`, `marker/geometry/`, `marker/payload/`, `data/analysis/`, `data/capture/`,
    `pacer/capability/`, `pacer/display/`, `pacer/frame/`, `pacer/hold/`, `pacer/placement/`, `pacer/rule/`, `pacer/tier/`, `pacer/timeline/`)
    (no umbrella headers: callers include each type's header; functions live in a header of their own, e.g. `marker/FrameMarker.hpp`, as
    C#'s static classes), sources mirroring them (`source/mb/framepacing/<module>/<Name>.cpp`, one per header; private helpers in
    `source/.../detail/`, in a namespace named for what they are, such as `Marker::WireFormat` and `Data::CaptureDataFormat`, and named in
    full at every use: no `using namespace`), namespaces
    `MB::FramePacing` (core) and `MB::FramePacing::<Module>`. One export set and package (`find_package(mb_framepacing CONFIG
COMPONENTS ...)`). `MB_FRAMEPACING_BUILD_MARKER` / `_DATA` / `_PACER` / `_TRACE` leave modules out (no data module: nlohmann/json is never fetched;
    the pacer is experimental and off unless asked for).
    `MB_FRAMEPACING_INSTALL` (on only when top-level) is around every install rule and the CMake package: an application that builds the
    library inside its own project installs its own files only (`tests/consumer/check_consumers.py` checks it).
    Each module's tests are their own executable. What they share is in `sdk/cpp/testing` (`mb_framepacing_test_support`, an OBJECT
    library built with the tests only, never installed): `Testing::AllocationCounter` and the counting global `operator new`/`delete`
    the allocation tests link.
  - **C++17 is an option** (the user, 2026-11-09: for applications pinned to `-std=c++17` on current compilers, not old toolchains):
    `MB_FRAMEPACING_CXX_STANDARD` 20 (default) or 17. 17 builds the core, marker, trace and pacer modules (`cxx_std_17`, through
    `MB_FRAMEPACING_CXX_FEATURE`) and their tests, tools, benchmarks, stress and fuzz targets; the data module and its tests stay
    C++20 in every build (the user's choice), and so do the size probes. Conan follows `compiler.cppstd` (below 20: 17, and
    `with_data=False` is required).
    - **The generated `<mb/framepacing/core/Config.hpp>` holds `MB_FRAMEPACING_CXX_STANDARD`, and every `#if` on the standard reads it,
      never `__cplusplus`** (the user's choice of design): every file of one build, the application's too, sees the same types, so
      the ABI never depends on the consumer's standard. The header stops a file compiled below the library's standard with an
      `#error` naming the option.
    - **`MB::FramePacing::Span`** (`core/Span.hpp`) is every buffer of the API: `std::span` in a C++20 build (the C++20 API
      unchanged), the SDK's own class in a C++17 one, holding only what the SDK uses of `std::span` under the same names. Write
      `Span`, never `std::span`, in the four modules, their tests and tools; the data module's own headers keep `std::span`.
    - **One code path wherever C++17 code is also valid C++20:** hand-written `operator==` and `operator!=` comparing every field
      (no defaulted comparisons: add a new field to its `operator==`), `std::enable_if_t` in place of concepts,
      `IntegerCompareUtil::Less`/`Greater` in place of `std::cmp_*`, loops in place of constexpr algorithms.
      `#if MB_FRAMEPACING_CXX_STANDARD >= 20` only where C++20 keeps its own: `std::span`, the time types' ordering (a defaulted
      `<=>` in C++20, which applications' own defaulted `<=>` needs; hand-written `<`, `<=`, `>`, `>=` in C++17 only, never both:
      C++20 would take the hand-written ones and leave `<=>` to `a <=> b` alone), `std::bit_cast` (`ByteSpanUtil::BitCast`,
      `__builtin_bit_cast` in C++17) and the marker's `<bit>` counts (`detail/BitCount.hpp`, plain code in C++17).
    - **Build and test both:** the `windows-cxx17` and `linux-sanitize-cxx17` presets (`CMAKE_CXX_STANDARD=17` too, so a compiler
      whose default is newer still checks C++17); CI runs the C++17 tree on Windows, Ubuntu and macOS, the sanitizers and clang-tidy
      on it, the consumer check's `subdirectory-cxx17` and `package-cxx17`, and a Conan run with `compiler.cppstd=17`. Coverage is
      measured in both (the C++17 `Span` and `BitCount` halves only exist there).
  - **Executable size:** `sdk/cpp/tests/size` holds size probes (a baseline program, then one using the core, the marker, the marker
    and data modules), linked so unused code is dropped (the modules are built with `-ffunction-sections -fdata-sections`).
    `tools/measure_sdk_size.py --toolchain <id>` builds them in Release and MinSizeRel (in `sdk/cpp/build/size`) and reports what each
    one loads from its file minus what the baseline loads (the sum of its sections, `tools/executable_sections.py`: file sizes grow a
    page at a time, 16 KiB on macOS); `--update-doc` rewrites the table in `sdk/cpp/README.md` (between `sdk-size-table` markers),
    `--check` fails beyond 10 % (at least 4 KiB). CI's `cpp-size` job checks MSVC, GCC, Clang and AppleClang and uploads each
    measurement (`--update-doc size-*.json` takes them). Refresh the table in the change that alters a module's size. `--csharp`
    measures the C# modules' Release assemblies for the table in `sdk/README.md` (`--check` in CI's `dotnet-lint`, `--update-doc` to rewrite).
  - **QR encoder (`sdk/doc/encoding-performance.md`):** the marker module encodes with its own encoder
    (`marker/source/mb/framepacing/marker/detail/QrEncoder.cpp`: versions 2 and 6, level M; the symbol as a 64-bit word per row and
    column, compile-time tables, masks by XOR, the mask penalty scored with word operations), about 40 times faster than qrcodegen
    with exactly its symbols: the mask rule (lowest penalty, the lowest numbered on a tie) is part of the format. qrcodegen stays
    unchanged in `marker/reference/third_party/qrcodegen` (`QrcodegenReference` adds access to its penalty score), built only for the
    tests and benchmarks: `QrEncoderTests` compares every symbol, every mask's score and drawn symbols with it, and the benchmark
    `QrcodegenEncode` runs next to `GenerateModules`. Refresh the document's numbers and the size table in the change that alters the
    encoder.
    After any change to the encoder also run the stress test (`mb_framepacing_marker_qr_stress`, `marker/tests/stress`: no arguments
    for about two million payloads on every core plus every 25-module line; ctest runs a short one) and, with Clang, the libFuzzer
    target (`marker/tests/fuzz`, `MB_FRAMEPACING_BUILD_FUZZERS`, on in `linux-sanitize`; CI's `cpp-analysis` fuzzes for a minute).
    Both compare with qrcodegen and stop at the first difference.
    The C# marker has the same encoder (`sdk/csharp/marker/source/QrEncoder.cs` with `QrVersionTables`, `QrMaskPatterns`,
    `QrReedSolomon`, `BitUtil`: .NET Standard 2.1 has no `BitOperations`). Its reference is the module-by-module encoder the library
    had before, `sdk/csharp/marker/Reference/ReferenceQrEncoder.cs`: `Reference` folders, like `UnitTest` and `Benchmarks`, are kept
    out of the library's compile by the root `Directory.Build.props`, and the unit tests and benchmarks link the file. `QrEncoderTests`
    compares the two (and pins the reference to the golden modules), `QrEncoderStressTests` is the stress test (`MB_QR_STRESS_SCALE`
    multiplies its amount; 200 for a long run), and the benchmark `ReferenceEncode` runs next to `GenerateModules`.
  - **Benchmarks:** `sdk/cpp/marker/benchmarks` (Google Benchmark 1.9.5 through FetchContent, `MB_FRAMEPACING_BUILD_BENCHMARKS`, off
    by default, on in the `windows` and `linux-sanitize` presets): encoding, every drawing output, a whole frame. CI builds them
    (`cpp-analysis`) but never runs them: timings on shared runners are noise. Run a Release build locally. The C# marker has the
    same groups with BenchmarkDotNet (`sdk/csharp/marker/Benchmarks`, built with the solution; `Benchmarks` folders, like `UnitTest`,
    are kept out of their library's compile by the root `Directory.Build.props`).
  - Clang's `-Wconversion` includes `-Wsign-conversion` (GCC's and MSVC's do not), so macOS CI can fail where Windows and Linux
    pass: shift and combine small unsigned types after casting them to `uint32_t`.
  - The `linux-sanitize` preset (Clang, AddressSanitizer + UndefinedBehaviorSanitizer, compile database) is what CI runs the tests
    and clang-tidy with.
  - `cmake/Version.hpp.in` is guarded with `// clang-format off`, because formatting breaks its `@VAR@` placeholders.
