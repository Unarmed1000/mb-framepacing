# MB Frame Pacing for Python

The [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) SDK in Python: one package, `mb_framepacing`, with two
subpackages. Standard library only, Python 3.12 or later.

- **`mb_framepacing.marker`** draws the frame marker into every frame of an application: a small QR code that carries the frame index
  and the animation time. A capture of the display output, analysed with the mb-framepacing tools, then shows the **animation
  error**: how far what the application animated is from what was actually shown on screen. It draws exactly the same pixels as the
  C++ and C# libraries: the tests check it against the golden images the C++ library writes (`test-data/markers`). The format is
  specified in [doc/marker-format.md](../doc/marker-format.md); what to write in each field is in
  [Filling the marker fields](../doc/marker-fields.md).
- **`mb_framepacing.data`** reads the data of the tools: the capture data (`captures.mbcd`,
  [format](../doc/capture-data-format.md)) and the analysis output (`summary.json`, `captures.csv`, `run-<id>-frames.csv`,
  [format](../doc/analysis-output-format.md)). It reads what the C# library reads: the tests check it against the same golden data
  ([`test-data/data`](../test-data/data)).

## The marker

```python
from mb_framepacing.marker import (
    MarkerFlags,
    MarkerKind,
    Options,
    Payload,
    PixelFormat,
    generate_modules,
    modules_to_bitmap,
    seconds_to_ticks,
)

options = Options(module_size_px=3)
origin = options.recommended_origin(MarkerKind.FRAME, height)

# Every frame, last (after post effects and UI), without blending:
matrix = generate_modules(Payload(MarkerKind.FRAME, 1, frame_index, MarkerFlags.NO_FLAGS, seconds_to_ticks(animation_seconds)))  # encode once
modules_to_bitmap(matrix, options, origin, rgb_frame, width, height, PixelFormat.R8G8B8)  # draw it
```

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Flags:** `MarkerFlags.NO_FLAGS`, or `MarkerFlags.STATIC_AFTER` on a frame when nothing animates while it is on screen, or
  `MarkerFlags.STATIC_BEFORE` on the next frame when that is only known then (the analysis does not judge the step out of the static
  frame).
- **Animation time:** the moment the frame shows, as the application animated it, in 100 ns ticks (`seconds_to_ticks`).
- **Frame pacing (optional):** when the application paces its frames, `Payload(..., preferred_frame_ticks=..., target_frame_ticks=...,
intended_display_ticks=...)` carries the interval the application wants to run at (it differs from the target only while the pacer
  runs slower than wanted), the interval the pacer aims for (`166_667` for 60 fps) and when it intends the frame to be shown (100 ns
  ticks on its steady clock, any epoch). All default to `0` (unknown); `ON_DEMAND_FRAME_TICKS` = frames only when something changes.
- **CPU start time and CPU busy (optional):** `Payload(..., cpu_start_ticks=..., cpu_busy_ticks=...)` carries when the CPU started
  working on the frame (on the same steady clock as the intended display time, PresentMon's `CPUStartTime`) and how long it worked on
  it before presenting it (from the CPU start time until Present is called, measured as the marker is drawn, PresentMon's
  `MsCPUBusy`; it may span several refreshes and does not include the GPU's work), in 100 ns ticks. CPU busy is `u32`; both default
  to `0` (unknown).
- **Start and end:** bracket the part to measure with a payload of kind `MarkerKind.SEQUENCE_START`, encoded with its metadata
  (`generate_modules(payload, StartMetadata(utc_ticks, sequence_id))`), and a payload of kind `MarkerKind.SEQUENCE_END`, each shown
  for a few frames. The sequence id is 16 opaque bytes unique to the run: `SequenceId.from_uuid(uuid.uuid4())` or a text tag of up to
  16 printable ASCII characters, `SequenceId.from_text("run-42")`. `str(sequence_id)` shows it as the text, or as the UUID's
  8-4-4-4-12 form.
- **Size:** every main marker (frame, start and end) is QR version 6, 41×41 modules, so it never changes size:
  `options.marker_size_px()` is `49 × module_size_px` with the default quiet zone (294 px for the default 6 px modules).
- **Sync marker (optional; required for camera capture):** a small second marker that carries only the run id and frame index, drawn
  every frame next to the main marker. It checks tearing on a capture card and times the frames for a camera. It is QR version 2,
  25×25 modules: `options.marker_size_px(MarkerKind.SYNC)` is `33 × module_size_px` (198 px for 6 px modules).

```python
sync_origin = options.recommended_origin(MarkerKind.SYNC, height)  # bottom-left
sync = generate_modules(Payload(MarkerKind.SYNC, 1, frame_index, MarkerFlags.NO_FLAGS, 0))  # the main marker's run id and frame index
modules_to_bitmap(sync, options, sync_origin, rgb_frame, width, height, PixelFormat.R8G8B8)
```

`options.recommended_origin(kind, ...)` places the main marker top-left and the sync marker bottom-left, both inset 32 px (rounded up to the
`align_px` downscale ratio). A sync payload is 16 bytes (magic, format version, kind, run id, frame index); its other fields are not
encoded and decode as `0`.

### Ways to draw it, most efficient first

| #   | Option                                                                            | Per frame (41×41 main marker, about 440 dark runs)           | Needs                                                                         |
| --- | --------------------------------------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| 1   | **Shader, packed bits**: one quad; the shader reads each module's bit from `bits` | 211 bytes (the packed bits as constants), 4 vertices         | A shader of ours: HLSL, GLSL for OpenGL 3.3 / ES 3.0, OpenGL ES 2.0 or Vulkan |
| 2   | **Shader, a texel per module**: one quad and a 41×41 texture                      | 1,681 bytes (the texture), 4 vertices                        | The same, and a single channel texture                                        |
| 3   | **Static grid**: `grid_vertices` once, then `modules_to_grid_indices`             | About 2,600 indices (10 KB as 32 bit, 5 KB as 16 bit)        | Index buffers and vertex colours; the 1,768 vertices stay                     |
| 4   | **Module texture scaled up**: `modules_to_bitmap` at 1 px per module              | 1,681 pixels                                                 | A texture drawn scaled by a whole number with point filtering, pixel exact    |
| 5   | **Triangles**: `modules_to_indexed` or `modules_to_triangles`                     | About 1,750 vertices and 2,600 indices, or 2,600 vertices    | Only vertex colours: the simplest to add to a renderer                        |
| 6   | **Rectangles**: `modules_to_quads`                                                | About 440 filled rectangles (`MarkerQuad`)                   | A 2D fill-rectangle API                                                       |
| 7   | **Full-size bitmap**: `modules_to_bitmap`                                         | The marker's pixels (294×294 at 6 px per module: 86 KB grey) | A CPU pixel buffer: software rendering, video frames, images                  |

Every option draws exactly the same pixels, from one encode per frame (the 211 byte module matrix). The shaders for 1 and 2 (HLSL for
Direct3D, GLSL for OpenGL 3.3 and OpenGL ES 3.0, OpenGL ES 2.0 and Vulkan) and how to draw them are in
[`sdk/shaders`](../shaders/README.md). On OpenGL ES 2.0 they need `highp` floats in the fragment shader; without it, draw 3 or 5. A
Python caller usually holds a pixel buffer (a video frame, an image), so the quick start uses 7; for a GPU, 1 and 2 cost the least per
frame.

### API

The same API as the C# marker module (`MB.FramePacing.Marker`), in Python's naming:

| Python                                                                                                                          | What it does                                                                            |
| ------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `Payload`, `StartMetadata`, `SequenceId`, `MarkerKind`                                                                          | What a marker carries                                                                   |
| `Options` (`recommended`, `minimum`, `marker_size_px`, `quiet_zone_px`, `recommended_origin`), `Point`                          | Size and place: always valid (a value outside its range is clamped)                     |
| `generate_modules`, `ModuleMatrix` (`size`, `is_dark`, `bits`)                                                                  | Encode the marker: its QR symbol, 1 bit per module (211 bytes)                          |
| `grid_vertices`, `grid_vertex_count`, `modules_to_grid_indices`                                                                 | A static grid uploaded once, and per frame only the indices                             |
| `modules_to_bitmap`, `PixelFormat`                                                                                              | Draw it into a pixel buffer (R8, R8G8B8 or R8G8B8A8, any stride)                        |
| `modules_to_indexed`, `modules_to_triangles`                                                                                    | Draw it as indexed triangles or a triangle list, for a GPU                              |
| `modules_to_quads`, `MarkerQuad` (`rect`, a `Rectangle`: `x`, `y`, `width`, `height`, `left`, `right`, `top`, `bottom`; `dark`) | Draw it as rectangles: the light background, then one dark rectangle per run of modules |
| `qr_module_count_for`                                                                                                           | Modules per side of a kind's symbol                                                     |
| `encode_payload`, `try_decode_payload`, `seconds_to_ticks`, `to_date_time_ticks`                                                | The wire format and its time units                                                      |

A Python caller usually has a pixel buffer: `modules_to_bitmap` draws into it (a `bytearray`, PIL's `Image.tobytes`, a numpy array's
memory). `PixelFormat` gives the byte layout: `R8G8B8` is `[R, G, B]`, `R8G8B8A8` `[R, G, B, A]` with A 255; the marker is black
and white, so B8G8R8 and B8G8R8A8 buffers take the same bytes.

## The data

```python
from mb_framepacing.data import CaptureDataReader, find_analysis, read_frames, read_summary

analysis = find_analysis(capture_folder)  # the capture folder's analysis folder (or the folder itself)
summary = read_summary(analysis / "summary.json")
for run in summary.runs:
    print(run.run_id, run.statistics.average_fps, run.pacing.late_frames if run.pacing else None)
    for frame in read_frames(analysis / run.frames_file):
        # Times are 100 ns ticks; None where the file has an empty cell
        if frame.animation_error_ticks is not None:
            print(frame.frame_index, frame.animation_error_ticks / 10_000, "ms")

with CaptureDataReader(capture_folder / "captures.mbcd") as reader:
    for record in reader.records():
        decoded = record.try_decode_main()  # decoded with mb_framepacing.marker
        if decoded is not None:
            payload, start = decoded
            print(record.capture_index, payload.frame_index)
```

### API

| Python                                                                          | What it is                                                                |
| ------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| `CaptureDataReader`, `CaptureDataHeader`, `CaptureDataRecord`                   | `captures.mbcd`: the header, the records (`records()`, `read_all()`, ...) |
| `CaptureDataStatus`, `Rectangle`, `MarkerLocation`, `UNKNOWN_TICKS`             | A record's status, where the markers are, a missing device time           |
| `read_summary`, `parse_summary`, `AnalysisSummary` and the `Summary…` classes   | `summary.json`                                                            |
| `read_frames`, `FrameRow`                                                       | A run's frames CSV                                                        |
| `read_captures`, `CaptureCsvRow`                                                | `captures.csv`                                                            |
| `find_analysis`, `frames_file_name`, `run_file_prefix`, `TICKS_PER_MILLISECOND` | The analysis folder, the file names, the ticks in a millisecond           |
| `DataFormatError`                                                               | Raised for another kind of file or a newer format version ("update ...")  |

## Tests

```powershell
python -m unittest discover -s sdk/python -t sdk/python
```

The golden marker images are found by walking up to `test-data/markers`, or from the `MB_FRAMEPACING_TEST_DATA` environment variable;
the golden data by walking up to `test-data/data`, or from `MB_FRAMEPACING_DATA_TEST_DATA`. Without them those tests are skipped (a copy
of the library on its own). `mb_framepacing.__version__` is the SDK's version (`sdk/VERSION`, as PEP 440 spells it).

## License

BSD 3-Clause ([LICENSE](mb_framepacing/LICENSE)). Third-party code is in `mb_framepacing/marker/third_party`, each file under its own
license with its text next to it: the QR encoder (`qrcodegen.py`) is based on the QR Code generator library by Project Nayuki, MIT.
