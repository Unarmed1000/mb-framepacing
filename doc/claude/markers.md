# Markers: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **Markers (format version 1, `sdk/doc/marker-format.md`):** a 57 byte header shared by frame, start and end, its fields grouped by
  category: format (magic, version, kind), which run and frame (run id, frame index), what the frame shows (flags, animation time),
  pacing (preferred frame time, target frame time, refresh period, intended display time), the CPU's work (CPU start time and CPU busy, named as
  PresentMon's CPUStartTime and MsCPUBusy). Start/end carry the values of the frame that shows them, and the tearing check compares
  their run id and frame index with the sync marker's. Every main marker (frame, start, end) is QR version 6 (41×41), so it never changes size and has room for future fields.
  - **CRC** (agreed with the user): every payload ends with the CRC-32 of zlib and Ethernet over all the bytes before it, little
    endian: a frame or end marker is 61 bytes, a start marker 85, a sync marker 20, and a field added later goes before the CRC. A
    payload whose CRC does not match is not decoded (`TryDecodePayload` false: `MarkerDecodeStatus.InvalidPayload`, the capture
    `Undecodable`): the QR code's Reed-Solomon code repairs, the CRC refuses what it repaired into other bytes. In C++ it is the
    core's `Crc32Util` (`core/Crc32Util.hpp`: a 16-entry table, for the executable size; its 256-entry form with the same result is for
    callers that want speed), in C# a private `Crc32.cs` with the 16-entry table, in Python `binascii.crc32`. The tests' expected
    CRC bytes come from Python's `binascii`, never from our own code.
  - **Times are nanoseconds** (the user, 2026-10-07 and 08; the bytes did not move, the unit did): the animation time an `i64`
    (`NanosecondTimeSpan`), the intended display and CPU start time points on the pacer's clock (`NanosecondTickCount`), the two frame
    times, the refresh period and CPU busy four unsigned bytes each. **A length of time is the duration type**, never negative by construction
    (`NanosecondTimeDuration`, 64-bit: what the code computes with and what a decoded payload returns), never a signed span and
    never a 32-bit time type (the four bytes are the marker's business).
    `Payload` caps a duration that is too long for its four bytes where it is made, never an error (it runs in a frame loop): CPU
    busy at `Payload::MaxCpuBusy` and a refresh period at `Payload::MaxRefreshPeriod` (`0xFFFFFFFF`, 4.294967295 s), a frame time at `Payload::MaxFrameTime` (`0xFFFFFFFE`), since
    `0xFFFFFFFF` there is `Payload::OnDemandFrameTime`, a duration of exactly 4294967295 ns. So every payload is valid on the wire
    and decodes to exactly what was encoded. Python keeps plain `int`s named `*_ns` (`ON_DEMAND_FRAME_NS`, `MAX_FRAME_NS`,
    `MAX_REFRESH_PERIOD_NS`, `MAX_CPU_BUSY_NS`, `seconds_to_ns`). The start marker's start time stays C# `DateTime` UTC ticks: a calendar time does not fit
    64 bits of nanoseconds. The format version stayed 1 (changed in place): a marker from before reads without an error and a
    hundred times too small, and every recording and capture from before is invalid (the user's decision: no migration).
  - **Pacing terms:** the intended display time is the pacer's aim; the animation time is the predicted display time the game
    animated for (`sdk/doc/vocabulary.md`). The target frame time is what the pacer aims for now, the **preferred frame time** what the
    application wants (it differs only while the pacer runs slower); `0xFFFFFFFF` in both = on demand. The **refresh period**
    (the user, 2026-10-23: optional, `0` = unknown, offset 33 in the pacing group, named as the pacer's `RefreshPeriod`) is the
    application's word about the display it works with: the tools compare it with the capture's period (a capture card: a warning
    beyond `RefreshTolerance` listing each rate and its frames; a camera: the hint after `--display-hz`, before the calibration),
    never measure with it, and never say which of the two is wrong. A frames CSV has it as `markerRefreshPeriodNs`, after
    `preferredNs`. Never call the preferred frame
    time "desired": that is Vulkan's word for a present time point.
  - **Flags:** static is a frame's time on screen (nothing animates until the next frame; the frame itself may have moved). Bit 0
    `StaticAfter` says it on the frame (known upfront: no pending work), bit 1 `StaticBefore` on the next frame (known in hindsight,
    only for frame index − 1). `TimelineAnalyzer.BuildFrames` turns both into `PresentedFrameFlags.StaticAfter` on the frame and
    `StaticBefore` on the next: that static step gets no animation or prediction error (the drift sums only judged errors), the step
    into the static frame is judged. Independent flags, not an enum; bits 4 to 7 are reserved (write 0, decoders keep them).
    - **Bit 2, `FrameRecordLogged`** (the user, 2026-10-13: "just so we know it exists"): on a **start marker**, the
      application's word that it logs the run's frame record to a trace. **The application sets it; the SDK never does** (the trace
      module writes no trace, so it can not know) and it is off unless set. Read from the start marker only
      (`MarkerPayload.IsFrameRecordLogged`, `RunAnalysis.FrameRecordLogged`); on another marker it says nothing. The tools **note**
      it and judge nothing by it (`FrameRecordNote`: a line in `analyze`'s output and under the GUI's run, and a line more in the
      warning of a trace that was given and not joined). A note only, judged by nothing; `summary.json` keeps it (`runs[].frameRecordLogged`, required, since 2026-10-29) so that a card from the files has the note too. A trace is joined with or
      without it. Not offered by the Unity helper: the trace module is not in the Unity package.
    - **Bit 3, `Paced`** (the user, 2026-10-29: "a pacer or a frame limiter held this frame's start"; vsync alone is not that):
      per frame, set by the application (Unity's `PacedProvider`, the experimental pacer's `FrameSchedule::Paced`, true in the
      two loops that place frames, false in `FreeRunLoop`). The tools keep it as `PresentedFrameFlags.Paced` (the frames CSV's flag
      `Paced`) and judge nothing by it. **The report's pacer band** (`RunHeadline.Pacing` → `PacingSummary`, `ReportItem.Pacing`,
      a full-width band under the panels, labelled PACER so it is not read as "pacing was fine"; the GUI's "Pacer or limiter:"
      caption line) answers from the flag only: a frame without it was not held, so a section where none carries it reads "no"
      (the user, 2026-10-29: "I dont understand why a run without a pacer bit isnt just NO"; no start-marker bit to declare the
      flag is used). `--playback-hide <ids>` (import, analyze, render) leaves items out of the playback page's card; the notes
      b8096cdb added have ids of their own (`cause`, `counts`, `run-warnings`, `frame-record-note`, `ReportItem.NoteIds`), so
      explained's cards can be as before. **Never guess it from the other fields** (the user, 2026-10-29: "stop
      guessing now we have a bit"; a vsync loop that knows its refresh writes a schedule, a frame limiter a target). Then a
      checkbox per pacing field (✓ every frame, the share as the mark when some frames carry it, – none: a half-filled circle
      reads as a dot at the card's size) and what the frames are measured against (`RunHeadline.TargetSource`, also `analyze`'s
      words). Every clip of mb-framepacing-explained writes intended display times, the naive ones too (on purpose: they fill
      every field).
    - **Bit 4, `PresentFailedBefore`** (the user, 2026-10-29: one bit, "only previous frame failed, we cant support multiple or
      dropped frames"): on the frame after one whose present call failed (it did not queue the frame: `VK_ERROR_OUT_OF_DATE_KHR`,
      a DXGI error, `eglSwapBuffers` false; not `VK_SUBOPTIMAL_KHR` or `DXGI_STATUS_OCCLUDED`). It speaks for frame index - 1 only.
      No Unity provider (Unity gives scripts no present result). The tools keep it as `PresentedFrameFlags.PresentFailedBefore`;
      `DroppedFrames.Before` stays every frame never shown, `PresentFailedBefore` is that one frame
      and `ByTargetBefore` the rest: summary.json `counts.droppedFrames` is by the target, `counts.presentFailedFrames` (required)
      the failed ones, and the vsync-off check counts by the target only. The report: `HoldKind.PresentFailed`,
      `RunEventKind.PresentFailed`, the strip's last refreshes before the next frame, all tan (`#c69c6d`, `*-failed`,
      `key-tan`), the frames dropped tile's "· N present failed", the counts line, the hover and the playback page's pill.
  - **Present timing** (the user, 2026-10-29: how long from submission to screen, "if we could detect if a frame was late due to
    being submitted too late"): `PresentTimings` in Charts (`PresentTiming`, `LateReason`, `RunChartData.PresentTimings`) from the
    frames, no output format: the present is CPU start + CPU busy. Planned latency (intended display − present, one clock, the
    pacer's plan), the extra delay after the present over the run's quickest frame (the offset cancels out), a late frame's
    present step against its intended period (`IntendedPeriods.Of`, half a refresh) for its reason (submitted late through its work
    or its start, or held after an on-time present), and work over its intended period. Notes under `present-latency` and
    `late-reasons` (`RunHeadline.PresentLines`), the GUI's caption, the hover. **Never an absolute present-to-screen time.**
  - **Assumed static** (`TimelineOptions.AssumeStatic`, on by default; `analyze --no-static-guess`, the GUI's "Guess static",
    `GuiSettings.AssumeStatic`): `TimelineAnalyzer.AssumeStatic` flags a frame `StaticAfter | StaticAssumed` when a frame the target
    dropped took the rest's flag: the next presented frame carries `StaticBefore` for a frame never shown, or (the flag lost) the run
    uses static flags, the next frame is on demand or later than the frames in between were due, and the animation clock stood still
    (animation step at most one frame time per rendered frame and two frame times short of the display step; the frame time is the next
    frame's target or preferred frame time, else the animation step into the held frame). Never across a capture gap or for a camera.
    The report's description and the hover say "assumed"; `ClipManifest.IsAssumedStatic` mirrors the rule for the clip tests.
  - **Field order:** every payload type (C++ and C# `Payload`, Python `Payload`, the tools' `MarkerPayload`) lists its fields in the
    order of the wire format: kind, run id, frame index, flags, animation time (required by every constructor; C++ also has a default
    constructor and stays trivially copyable and standard layout: private fields read through getters, `WithKind` for another kind; the
    constructor asserts the kind and `EncodePayload` refuses an unknown one, the flags are kept as given), then the optional
    preferred frame time, target frame time, refresh period, intended display time, CPU start time and CPU busy. The golden CSVs' columns
    (`sdk/test-data/markers`) and the docs' field lists follow it; keep new fields in their wire position.
  - **Frame rates describe the frames that animate:** a static frame's time on screen (the next frame's display time step, flag
    `StaticBefore`) is left out of average fps, the lows and the display time step statistics and histogram
    (`RunStatistics.CountsTowardFrameRate`, `RunChartData.FrameRateSteps`), and counted (`excludedStaticFrames`); the tile, the report's
    description and the statistics tables say "excluding N static frames" (`RunHeadline.ExcludedStatic`).
  - **Late share:** amber = on screen at least half a refresh longer than the preferred frame time (the marker's, else
    `--target-fps`, else one refresh) without being late; never for static steps or on-demand frames (`LateShareData`, `PresentedFrame.PreferredFrameTime`).
    A window that reaches back before the run's first frame is still a whole window (the user, 2026-10-28): nothing before the run
    is late, and it holds as many frames as the run's first 2 s (`LateShare.FirstWindowCount`), so a late frame among the first few
    is not a large share (`LateShare.Rolling`, `LateShareData.Create`; `Worst` still takes whole windows only).
  - The start marker (85 bytes) carries a 16 byte opaque sequence id (`SequenceId`: a UUID or a text tag of at
    most 16 ASCII characters, shown as text or UUID hex), not a name. Format version 1 is the baseline for all data (markers,
    captures.mbcd, analysis output): change it in place, no version bump, until there are users. The sync marker (kind 3, 20 bytes: the header's start, run id and frame index, and the CRC; matched to its main marker by both) is QR version 2 (25×25), drawn
    bottom-left: it checks tearing (capture cards, optional) and times the frames for a camera (required). `Options.RecommendedOrigin(kind, …)`
    places both; there are no other slots.
- **Reference shaders (`sdk/shaders/`, BSD):** one quad whose fragment shader finds each pixel's module, from the packed bits
  (`Bits()` as constants; the fastest way to draw the marker) or a texel per module. One folder per API: `hlsl/` (`FrameMarker.hlsl` is
  the lookup, also included by the Unity shaders; `build_upm.py` copies it into the package), `gl/`, `gles2/` (GLSL ES 1.00: no
  integers, so float arithmetic, and `#error` without `highp`: not 100 % exact with `mediump`), `vulkan/`. `python
tools/check_shaders.py` compiles them all (glslang, and DXC when found; CI runs it); `--render` (`uv run --with moderngl tools/check_shaders.py --render`, needs a
  GPU; `VULKAN_SDK` for DXC and SPIRV-Cross) draws every one and compares every pixel with `modules_to_bitmap`. Run it after touching
  a shader, and `check_in_unity.py` (also `--graphics glcore|gles|vulkan`) after touching the Unity ones.
- **A matrix is a marker's:** `ModuleMatrix::TryFromBits` (C#, and Python's `ModuleMatrix(size, bits)`) takes the two marker sizes only (41 and
  25), the ones the drawing functions and the grid know. **Seconds truncate** toward zero in every language: to the nanosecond for the
  marker (`NanosecondTimeSpan::FromSeconds`, Python's `seconds_to_ns`: 1/60 s is 16 666 666 ns), to the tick for the tick types
  (`TimeSpan::FromSeconds`, `TimeSpanUtil.FromSeconds`: 166 666 ticks).
- **Encode once, draw from the modules:** every marker library encodes a marker once (`GenerateModules` / C# `TryGenerateModules`: the
  `ModuleMatrix`, 1 bit per module, packed exactly as `modules.csv`) and draws it with `ModulesToQuads`, `ModulesToTriangles`,
  `ModulesToIndexed`, `ModulesToBitmap` or the static grid (`GridVertices` once, `ModulesToGridIndices` per frame). A drawn rectangle is a
  `MarkerQuad` (a core `Rectangle` `Rect` and `Dark`). `Options` (module size and quiet zone) is always valid: C++ asserts a value outside its range and clamps it
  without asserts, C# and Python clamp (C# stores it relative to the defaults, so `default(Options)` is `Options.Default`); the sizing
  and placement are its members (`Recommended`, `Minimum`, `MarkerSizePx`, `QuietZonePx`, `RecommendedOrigin`). Pixel formats are named
  by their channels: `R8`, `R8G8B8`, `R8G8B8A8`. The C# marker mirrors the C++ one: the wire format is the internal `WireFormat`, the symbol sizes are `ModuleMatrix`'s
  (`MainSize`, `SyncSize`, `SizeFor`, `PackedModuleByteCount`), the sizing limits `Options`', `Payload.MaxEncodedByteCount` and
  `Payload.OnDemandFrameTime` the payload's, `PixelFormatUtil.BytesPerPixel` the pixel formats'; `Payload`'s fields are typed and named
  as C++'s getters, and its constructor throws `ArgumentOutOfRangeException` for a kind that is not a `MarkerKind`. The C# static
  class is `FrameMarker` (not `Marker`: a class named like its namespace `MB.FramePacing.Marker` breaks lookups in `MB.FramePacing.*` code). C#'s `ModuleMatrix` is a `ref struct` view over the caller's bytes (no allocation), C++'s a
  value with an inline `std::array`. There are no payload-taking draw functions.
- **Golden set:** if you change the marker payload or geometry, regenerate it with
  `sdk/cpp/build/<preset>/marker/marker-render --golden sdk/test-data/markers` (Windows: `sdk\cpp\build\windows\marker\Release\marker-render.exe`),
  then run the C# tests and the Python tests (`python -m unittest discover -s sdk/python -t sdk/python`).
  - The digest's seed (`marker-render`'s `WriteModuleDigest`) is one with which both symbol versions use all eight masks in its
    rows. Check that again when the payload's bytes change (the mask is in a symbol's format bits: row 8, columns 2 to 4, XOR 5)
    and take the next seed that does.
  - **A change to the payload's bytes also needs new test clips** (`measure/test-data/videos`: their markers are in the pixels, and their manifests hold `animationNs`, `cpuStartNs` and `cpuBusyNs`,
    each value rounded once from the exact time).
    mb-framepacing-explained makes them from a local, unpublished commit of this repository (it fetches a named branch into its
    submodule, exports into its own folder and never writes here). Copy the 33 folders unchanged, then `update_test_data.py`,
    DocImages and `camera_rate_table.py --update-doc`. The format change, the golden data and the clips go in one commit: `master`
    never holds clips its own tools refuse. The sister repository and the applications that embed the SDK move their pins after.
