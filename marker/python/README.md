# MB Frame Marker for Python

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of an application: a small
QR code that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing
tools, then shows the **animation error**: how far what the application animated is from what was actually shown on screen.

It draws exactly the same pixels as the C++ and C# libraries: the tests check it against the golden images the C++ library writes
(`test-data/markers`). The format is specified in [doc/marker-format.md](../../doc/marker-format.md). Standard library only, Python
3.11 or later.

## Quick start

```python
from mb_framemarker import MarkerSlot, Options, Payload, fill_quads, generate_quads, recommended_origin, seconds_to_ticks

options = Options(module_size_px=3)
origin = recommended_origin(MarkerSlot.TOP_LEFT, width, height, options)

# Every frame, last (after post effects and UI), without blending:
quads = generate_quads(Payload(frame_index, seconds_to_ticks(animation_seconds), run_id=1), options, origin)
fill_quads(rgb24_frame, width, height, quads, channels=3)
```

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Animation time:** the moment the frame shows, as the application animated it, in 100 ns ticks (`seconds_to_ticks`).
- **Start and end:** bracket the part to measure with `generate_start_quads(payload, StartMetadata(utc_ticks, name), options,
origin)` and a payload of kind `MarkerKind.SEQUENCE_END`, each shown for a few frames; keep `max_marker_size_px(options)` free
  around the origin while the start marker shows.

## API

The same API as the C# library (`MB.FrameMarker`), in Python's naming:

| Python                                                                           | What it does                                                                     |
| -------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| `Payload`, `StartMetadata`, `MarkerKind`                                         | What a marker carries                                                            |
| `Options`, `MarkerSlot`, `Point`                                                 | Size and place                                                                   |
| `generate_quads`, `generate_start_quads`                                         | The marker as quads: the light background, then one dark quad per run of modules |
| `generate_triangles`, `generate_indexed` (and `generate_start_…`)                | The marker as a triangle list or indexed triangles, for a GPU                    |
| `fill_quads`                                                                     | Draws quads into a pixel buffer (grey or rgb24, any stride)                      |
| `marker_size_px`, `max_marker_size_px`, `recommended_origin`                     | Sizing and placement                                                             |
| `minimum_module_size_px`, `recommend_module_size_px`                             | Module size for a capture's scaling                                              |
| `encode_payload`, `try_decode_payload`, `seconds_to_ticks`, `to_date_time_ticks` | The wire format and its time units                                               |
| `generate_modules`                                                               | The QR module matrix                                                             |

`fill_quads` is the one addition: the C# and C++ libraries leave drawing to the GPU, a Python caller usually has a pixel buffer.

## Tests

```powershell
python -m unittest discover -s marker/python -t marker/python
```

The golden images are found by walking up to `test-data/markers`, or from the `MB_FRAMEMARKER_TEST_DATA` environment variable;
without them those tests are skipped (a copy of the library in another repository).

## License

BSD 3-Clause ([LICENSE](mb_framemarker/LICENSE)). Third-party code is in `mb_framemarker/third_party`, each file under its own
license with its text next to it: the QR encoder (`qrcodegen.py`) is based on the QR Code generator library by Project Nayuki, MIT.
