# MB Frame Marker for Python

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of an application: a small
QR code that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing
tools, then shows the **animation error**: how far what the application animated is from what was actually shown on screen.

It draws exactly the same pixels as the C++ and C# libraries: the tests check it against the golden images the C++ library writes
(`test-data/markers`). The format is specified in [doc/marker-format.md](../../doc/marker-format.md); what to write in each field is
in [Filling the marker fields](../../doc/marker-fields.md). Standard library only, Python 3.11 or later.

## Quick start

```python
from mb_framemarker import MarkerKind, Options, Payload, PixelFormat, generate_modules, modules_to_bitmap, recommended_origin, seconds_to_ticks

options = Options(module_size_px=3)
origin = recommended_origin(MarkerKind.FRAME, width, height, options)

# Every frame, last (after post effects and UI), without blending:
matrix = generate_modules(Payload(frame_index, seconds_to_ticks(animation_seconds), run_id=1))  # encode once
modules_to_bitmap(matrix, options, origin, rgb24_frame, width, height, PixelFormat.RGB24)  # draw it
```

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Animation time:** the moment the frame shows, as the application animated it, in 100 ns ticks (`seconds_to_ticks`).
- **Frame pacing (optional):** when the application paces its frames, `Payload(..., intended_display_ticks=..., target_frame_ticks=...)`
  carries when the pacer intends the frame to be shown (100 ns ticks on its steady clock, any epoch) and the interval it aims for
  (`166_667` for 60 fps); `preferred_frame_ticks=...` the interval the application wants to run at (it differs from the target only
  while the pacer runs slower than wanted). All default to `0` (unknown); `ON_DEMAND_FRAME_TICKS` = frames only when something
  changes.
- **Flags (optional):** `flags=MarkerFlags.STATIC` on frames where nothing animates (the analysis does not judge their animation
  error).
- **CPU start time and CPU busy (optional):** `Payload(..., cpu_start_ticks=..., cpu_busy_ticks=...)` carries when the CPU started
  working on the frame (on the same steady clock as the intended display time, PresentMon's `CPUStartTime`) and how long it worked on
  it before presenting it (from the CPU start time until Present is called, measured as the marker is drawn, PresentMon's
  `MsCPUBusy`; it may span several refreshes and does not include the GPU's work), in 100 ns ticks. CPU busy is `u32`; both default
  to `0` (unknown).
- **Start and end:** bracket the part to measure with a payload of kind `MarkerKind.SEQUENCE_START`, encoded with its metadata
  (`generate_modules(payload, StartMetadata(utc_ticks, sequence_id))`), and a payload of kind `MarkerKind.SEQUENCE_END`, each shown for a few frames. The sequence id is 16 opaque bytes unique to
  the run: `SequenceId.from_uuid(uuid.uuid4())` or a text tag of up to 16 printable ASCII characters, `SequenceId.from_text("run-42")`.
  `str(sequence_id)` shows it as the text, or as the UUID's 8-4-4-4-12 form.
- **Size:** every main marker (frame, start and end) is QR version 6, 41×41 modules, so it never changes size:
  `marker_size_px(options)` is `49 × module_size_px` with the default quiet zone (294 px for the default 6 px modules).
- **Sync marker (optional; required for camera capture):** a small second marker that carries only the run id and frame index, drawn every frame
  next to the main marker. It checks tearing on a capture card and times the frames for a camera. It is QR version 2, 25×25
  modules: `marker_size_px(options, MarkerKind.SYNC)` is `33 × module_size_px` (198 px for 6 px modules).

```python
sync_origin = recommended_origin(MarkerKind.SYNC, width, height, options)  # bottom-left
sync = generate_modules(Payload(frame_index, 0, run_id=1, kind=MarkerKind.SYNC))  # the main marker's run id and frame index
modules_to_bitmap(sync, options, sync_origin, rgb24_frame, width, height, PixelFormat.RGB24)
```

`recommended_origin(kind, ...)` places the main marker top-left and the sync marker bottom-left, both inset 32 px (rounded up to the
`align_px` downscale ratio). A sync payload is 16 bytes (magic, format version, kind, run id, frame index); its other fields are not encoded
and decode as `0`.

## Ways to draw it, most efficient first

| #   | Option                                                                              | Per frame (41×41 main marker, about 440 dark runs)           | Needs                                                                         |
| --- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| 1   | **Shader, packed bits**: one quad; the shader reads each module's bit from `Bits()` | 211 bytes (the packed bits as constants), 4 vertices         | A shader of ours: HLSL, GLSL for OpenGL 3.3 / ES 3.0, OpenGL ES 2.0 or Vulkan |
| 2   | **Shader, a texel per module**: one quad and a 41×41 texture                        | 1,681 bytes (the texture), 4 vertices                        | The same, and a single channel texture                                        |
| 3   | **Static grid**: `grid_vertices` once, then `modules_to_grid_indices`               | About 2,600 indices (10 KB as 32 bit, 5 KB as 16 bit)        | Index buffers and vertex colours; the 1,768 vertices stay                     |
| 4   | **Module texture scaled up**: `modules_to_bitmap` at 1 px per module                | 1,681 pixels                                                 | A texture drawn scaled by a whole number with point filtering, pixel exact    |
| 5   | **Triangles**: `modules_to_indexed` or `modules_to_triangles`                       | About 1,750 vertices and 2,600 indices, or 2,600 vertices    | Only vertex colours: the simplest to add to a renderer                        |
| 6   | **Rectangles**: `modules_to_quads`                                                  | About 440 filled rectangles                                  | A 2D fill-rectangle API                                                       |
| 7   | **Full-size bitmap**: `modules_to_bitmap`                                           | The marker's pixels (294×294 at 6 px per module: 86 KB grey) | A CPU pixel buffer: software rendering, video frames, images                  |

Every option draws exactly the same pixels, from one encode per frame (the 211 byte module matrix). The shaders for 1 and 2
(HLSL for Direct3D, GLSL for OpenGL 3.3 and OpenGL ES 3.0, OpenGL ES 2.0 and Vulkan) and how to draw them are in
[`marker/shaders`](../shaders/README.md). On OpenGL ES 2.0 they need `highp` floats in the fragment shader; without it, draw 3 or 5. A Python caller usually holds a pixel buffer (a video
frame, an image), so the quick start uses 7; for a GPU, 1 and 2 cost the least per frame.

## API

The same API as the C# library (`MB.FrameMarker`), in Python's naming:

| Python                                                                           | What it does                                                                  |
| -------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| `Payload`, `StartMetadata`, `SequenceId`, `MarkerKind`                           | What a marker carries                                                         |
| `Options`, `Point`                                                               | Size and place                                                                |
| `generate_modules`, `ModuleMatrix` (`size`, `is_dark`, `bits`)                   | Encode the marker: its QR symbol, 1 bit per module (211 bytes)                |
| `grid_vertices`, `grid_vertex_count`, `modules_to_grid_indices`                  | A static grid uploaded once, and per frame only the indices                   |
| `modules_to_bitmap`, `PixelFormat`                                               | Draw it into a pixel buffer (grey, RGB or RGBA, any stride)                   |
| `modules_to_indexed`, `modules_to_triangles`                                     | Draw it as indexed triangles or a triangle list, for a GPU                    |
| `modules_to_quads`                                                               | Draw it as quads: the light background, then one dark quad per run of modules |
| `marker_size_px`, `qr_module_count_for`, `recommended_origin`                    | Sizing and placement                                                          |
| `minimum_module_size_px`, `recommend_module_size_px`                             | Module size for a capture's scaling                                           |
| `encode_payload`, `try_decode_payload`, `seconds_to_ticks`, `to_date_time_ticks` | The wire format and its time units                                            |

A Python caller usually has a pixel buffer: `modules_to_bitmap` draws into it (a `bytearray`, PIL's `Image.tobytes`, a numpy array's
memory). `PixelFormat` gives the byte layout: `RGB24` is `[R, G, B]`, `RGBA32` `[R, G, B, A]` with A 255; the marker is black and white,
so BGR and BGRA buffers take the same bytes.

## Tests

```powershell
python -m unittest discover -s marker/python -t marker/python
```

The golden images are found by walking up to `test-data/markers`, or from the `MB_FRAMEMARKER_TEST_DATA` environment variable;
without them those tests are skipped (a copy of the library on its own).

## License

BSD 3-Clause ([LICENSE](mb_framemarker/LICENSE)). Third-party code is in `mb_framemarker/third_party`, each file under its own
license with its text next to it: the QR encoder (`qrcodegen.py`) is based on the QR Code generator library by Project Nayuki, MIT.
