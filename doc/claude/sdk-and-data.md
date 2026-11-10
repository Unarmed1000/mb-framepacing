# SDK modules and data: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **`sdk/`** (BSD 3-Clause) holds everything applications embed or use to read the results: one SDK with one version, laid out by
  language (`sdk/cpp/`, `sdk/csharp/`, `sdk/python/`, `sdk/unity/`, `sdk/shaders/`), its docs and its golden data. Its modules:
  - **marker**: what goes **into** the application, a QR marker drawn into every frame (C++ `MB::FramePacing::Marker`, C#
    `MB.FramePacing.Marker`, Python `mb_framepacing.marker`, the Unity package);
  - **data**: reads the tools' capture data and analysis output (C++ `MB::FramePacing::Data`, C# `MB.FramePacing.Data`, Python
    `mb_framepacing.data`);
  - **pacer** (C++ `MB::FramePacing::Pacer` only; **experimental**, off by default): paces a frame loop from what the application
    says its platform can do (its capabilities), with a steady clock and the display's refresh period at the least: it gives every
    frame its waits, its start time, its swap interval, its animation time, how to present it and the marker's pacing values, holds
    a target frame rate, and adapts the swap interval (the full-window rule of mb-framepacing-explained's simulation, and its fix
    as the default); it counts what the display did from the display times the platform reports (statistics only);
  - **trace** (C++ `MB::FramePacing::Trace` and C# `MB.FramePacing.Trace`; on by default in C++): the **frame
    record**, the slice an application logs to its own Perfetto trace for every frame so that the tools can merge a trace of a
    run with a capture of it (`sdk/doc/trace-frame-record.md` is the reference). It defines every string of the record and works
    out what to log from a marker's payload; it writes no trace and calls no tracing API;
  - **core**: the types every module shares, `Point` and `Rectangle` (always valid: a negative size is 0; its edges must fit int32, which is asserted and never clamped) in every
    language (C++
    `MB::FramePacing` with the library version and the time types in `core/time/`: `NanosecondTimeSpan`, `NanosecondTickCount` and `NanosecondTimeDuration` (a signed interval, a point on a clock and a length of time that is never negative, in nanoseconds: **what the marker, the data modules and the tools hold every time in**; exact from ticks, and to ticks truncated for an interval and the tick it is in for a point; `FromSeconds(double)` truncates to the nanosecond and `NanosecondTickCount::FromCounter` rounds down to it; in C# and Python too, where they are the only time types Python has), the tick types `TimeSpan` (C#'s `System.TimeSpan`, out of range throws), `TickCount64`, `TickCount32` (wraps every 429.5 s, compares across the wrap), `TimeSpan32` and `TimeDuration` (ticks of 100 ns, for applications and .NET's own APIs: nothing in the marker, the data modules or the tools holds a measured time in them), and the optional `core/time/ChronoConversion.hpp` (the tick and the nanosecond types); `ByteSpanUtil` (`WriteLE`/`ReadLE<T>`: little-endian values, the
    byte count from the type) for every module's file and wire formats; the core and the marker module have 100 % test coverage
    (regions, functions, lines, branches), measured with llvm-cov without asserts (`NDEBUG`); the C# core has the same time types
    (`sdk/csharp/core/source/Time/`, member for member, .NET exceptions; among the tick types `System.TimeSpan` is the signed interval, and `TimeSpanUtil.FromSeconds`
    converts seconds to the tick on every runtime, since Unity's `TimeSpan.FromSeconds` rounds to a millisecond), and the C# core and
    marker module have 100 % line and branch coverage too: `python tools/check_csharp_coverage.py` (Microsoft code coverage through `dotnet test --collect`; CI's `dotnet-lint`), C# assembly `MB.FramePacing`, Python
    `mb_framepacing`). The SDK never reads a clock: applications pass their own clock's times (the C++ tests' `SteadyClock` is a test
    helper). The core's types hide same-named types that a `using` brings into `MB.FramePacing.*` code: the GUI writes `Avalonia.Point`.
- **Data modules (BSD 3-Clause):** `MB.FramePacing.Data` reads and writes `captures.mbcd` (`sdk/doc/capture-data-format.md`) and
  the analysis output (`sdk/doc/analysis-output-format.md`: `summary.json` with `formatVersion`, which covers the CSVs, and the CSVs). The
  tools write and read every file through it; their own types map to it (`CaptureDataMapping` in Capture, `AnalysisDataMapping` in
  Analysis). Keep the output byte for byte: the golden data (`sdk/test-data/data`, `digest.json`) is written back exactly, and every
  language's reader must read the digest's values. After a format change: `python tools/update_test_data.py` (needs ffmpeg).
  - **A `captures.mbcd` record is 256 bytes** with two equal 112-byte slots for the markers' bytes as read: either holds any payload
    a main marker's QR code can carry (106), so a field added to the markers does not change the records (the user's choice over
    the smallest slots that fit). Readers refuse another record size.
  - **Typed times** (C++ and C#, the same names; the tools are typed throughout too, with these names: `PresentedFrame`, `CaptureRow`,
    `ChartRun`): every time is a whole number of nanoseconds. Points in time are `NanosecondTickCount` (named `…Time`:
    `FirstSeenTime`, `HostTime`, `DeviceTime`, empty when unknown). **A length of time is `NanosecondTimeDuration`**, which is never
    negative (the user's rule), named for what it is: in a frame's row `FrameTime`, `TargetFrameTime`, `PreferredFrameTime` and the
    marker's own `MarkerTargetFrameTime`, `MarkerPreferredFrameTime`, `MarkerRefreshPeriod` and `CpuBusy`; in the summary `CapturePeriod`,
    `MeasurementResolution` and `ErrorThreshold`; in a run's pacing `RefreshPeriod` and `TargetFrameTime`. The signed
    `NanosecondTimeSpan` is only for what can go either way by meaning: the animation time and step (`AnimationTime`,
    `AnimationDelta`), the three errors (`AnimationError`, `PacingError`, `PredictionError`), `Lateness`, `Drift`, `CpuWait`,
    a camera's per-frame `ScanoutDelay` (the user's decision: a measured difference of two sightings, not a length, as the sync
    marker can be seen before the taller main marker decodes; the camera's delays, tear test and usual delay in `TimelineAnalyzer`
    are signed and never clamped), and **the display time step and the time on screen** (`DisplayDelta`, `OnScreen`, columns
    `displayDeltaNs` and `onScreenNs`; the user's decision): each is one frame's first sighting minus another's, and a camera can
    time a frame after the next one's sighting, so the difference can be negative. Turned into zero, the display time steps would no
    longer add up to the time that passed and the drift would carry a false offset: they are never clamped, a negative one is
    written and read as it is, and what is computed from them (the animation error, the static guess's hold, the statistics, the
    charts) is signed too. **Readers refuse a negative duration:** a negative number in a duration's column or field
    (`frameTimeNs`, `targetNs`, `preferredNs` in a frames CSV; `capturePeriodNs`, `measurementResolutionNs`, `errorThresholdNs`,
    `pacing.refreshPeriodNs`, `pacing.targetFrameNs` in `summary.json`) is outside its range and refused as any such number is, in
    every language; the files keep their `i64`. **A length that can not be worked out is unknown, never 0:** a frametime is
    the next frame's CPU start minus this one's, and where an application's CPU start times go backwards it is empty (the user,
    2026-10-14, on landing this: a 0 would hide a broken clock behind a number that reads as a measurement). `System.TimeSpan` remains only for .NET
    waits, timeouts and progress, converted with the types' own helpers (`ToTimeSpan()`, `FromTimeSpan(...)`: never `* 100` or
    `/ 100` at a call site). Python's marker and data modules keep plain `int`s named `*_ns`, and the data module checks the same
    range.
  - **The files hold times as whole nanoseconds** (`…Ns` columns and fields), in the integer type the value has: a marker's value is
    in the CSV exactly as the marker carried it (`animationNs` an `i64`, `markerTargetNs` a `u32`, 4294967295 = on demand), and
    `summary.json`'s settings too (`capturePeriodNs`, `errorThresholdNs`, `pacing.refreshPeriodNs`, `targetFrameNs`). The tools'
    `MarkerPayload` holds the marker's nanoseconds as they are: nothing between a marker and a file is converted or rounded, and
    nothing goes through a floating point number: never add a milliseconds column or field for a time.
    Only `summary.json`'s statistics and histograms are milliseconds (`…Ms`): a mean or an interpolated percentile is no whole
    nanosecond. A whole number is digits with a `-` in front when negative, in every language's reader (no `+`, spaces, fraction or
    exponent). The rule covers the files the tools read too: an image sequence's timestamp file (`import --timestamps`) is
    `fileName,timeNs`. **The format versions stayed 1** (changed in place; the user's decision: no migration): a marker, a recording,
    a capture folder or an analysis output from before the nanoseconds is invalid and is made again
    (`sdk/doc/analysis-output-format.md` and `capture-data-format.md` say what is refused and what reads a hundred times too small).
  - **Readers are strict, and alike:** what every file has is required (`sdk/doc/analysis-output-format.md` marks it), a value must be
    of its field's type and in its range, and content that is not is one error type per module (C++ `DataFormatError`, C#
    `InvalidDataException`, Python `DataFormatError`), with the file and line for a CSV. A file that cannot be opened is the
    platform's error, not a format error.
  - **The C# CSV code costs a row per line, not a string per cell** (a run of an hour at 240 Hz has 864,000 lines in each file):
    `CsvLineWriter` builds a line in its own buffer and `CsvLineReader` hands out each line as a span of its buffer; `CsvRow` is a view
    of the line's cells; the texts a file repeats (status, kind, a set of flags) are made once (`CsvTextCache`, and the name caches in
    `AnalysisDataMapping`). The output is byte for byte what `ToString` and `string.Join` wrote. `OutputFileBenchmarks`
    (`--long-running`): an hour's frames CSV writes in 87 ms with 1 KB allocated and reads in 295 ms with 340 MB (the rows). Regenerating
    the golden data (`update_test_data.py`) always changes its wall-clock values (start and analysis times, duration, host times);
    anything else that changes is a real difference.
  - **Capture times** (`MB.FramePacing.Capture`): `CaptureClock.Now` and a frame's host time are `NanosecondTickCount` (the time since the capture
    started, from the stopwatch counter: `NanosecondTickCount.FromCounter`); ffmpeg's timestamps become nanoseconds in integer
    arithmetic, rounded to the nearest nanosecond. A frame's device time is a `DeviceTimestamp`: a time, `Unknown` (the device gave none) or `Pending` (it arrives after the
    pixels: ffmpeg's showinfo lines, resolved by the recorder through `IDeviceTimestampSource`). Pending exists only between a source and
    the recorder; files hold a time or unknown, and everything after the recorder has a `NanosecondTickCount?`.
  - **Analysis times** (`PresentedFrame`, `CaptureRow`): the marker's own values keep the marker's 0 = unknown (`IntendedDisplayTime`,
    `CpuStartTime`, `MarkerTargetFrameTime`, `MarkerPreferredFrameTime`, `MarkerRefreshPeriod`, `CpuBusy`); what the analysis works out is nullable
    (`DisplayDelta`, `TargetFrameTime`, `Lateness`). `IntendedDisplayTime` and `CpuStartTime` are on the pacer's clock, `FirstSeenTime`
    on the capture's: `PacingAnalyzer.CaptureMinusPacer` is where the analysis subtracts them. Milliseconds are
    `NanosecondTimeSpan.TotalMilliseconds` (a duration's through its `Value`), one division of the nanosecond count (for a value of whole ticks, the same double as ticks / 10000.0).
    The charts read the typed values; what they keep as nanosecond counts is integer arithmetic (whole refreshes, strip cells) and the prepared
    sequences (`FrameSequence`, `WaveletMatrix`), which rank plain integers. The GUI's `Stopwatch` timestamps stay raw.
