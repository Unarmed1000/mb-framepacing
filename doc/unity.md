# Unity

The Unity package **MB Frame Marker** (`com.manabattery.framemarker`) draws the marker into every frame of a Unity game. It contains:

- **the general C# library** `MB.FrameMarker`: the same code as [`marker/csharp`](../marker/csharp), with no Unity dependency;
- **Unity helpers** `MB.FrameMarker.Unity`: an overlay component that does everything, and building blocks for your own render
  pipeline code.

Unity 2021.3 or newer (C# 9). The package contents are checked in a real Unity editor, see [What is verified](#what-is-verified).

## Install

**Package Manager → + → Add package from git URL**, then:

```text
https://github.com/Unarmed1000/mb-framepacing.git#upm/v0.1.0
```

`upm/v<version>` tags are created by the marker release workflow. To use an unreleased version, assemble the package yourself and
install it with **Add package from disk** (select its `package.json`):

```sh
python marker/unity/build_upm.py --output ../mb-framemarker-upm
```

## Quick start

1. Add a GameObject with **Add Component → MB → Frame Marker Overlay**.
2. Set **Stored Height** to the height the capture tool stores (for example 540 for `--scale 960x540`). The module size follows from
   it: 3 stored pixels per module, 4 with **MJPEG**.
3. Turn HDR output off while capturing.
4. Bracket the part to measure:

   ```csharp
   using MB.FrameMarker.Unity;

   var overlay = FindAnyObjectByType<FrameMarkerOverlay>();
   StartCoroutine(overlay.RunFor(10.0)); // start marker, 10 s of frame markers, end marker
   // or overlay.BeginRun(); ... overlay.EndRun();
   ```

   Every run gets a new UUID as its **sequence id**, which the reports show; `overlay.SequenceId` tells you which one. To pick it
   yourself, pass any 16 bytes unique to the run: `SequenceId.FromGuid(...)`, or a text tag of at most 16 ASCII characters
   (`SequenceId.TryFromText("camera pan", out var id)`).

5. Record with `mb-framepacing capture --wait-for-start --stop-at-end --analyze` (see [Using mb-framepacing](usage.md)).

The Package Manager also offers the **Benchmark** sample: it turns the camera for a few seconds and measures exactly that.

## The two timers

The marker carries two values from the game, and Unity provides both:

| Marker value   | Unity source (default) | What it is                                                                                                                                                                             |
| -------------- | ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Frame index    | `Time.frameCount`      | Counts every frame of the player loop                                                                                                                                                  |
| Animation time | `Time.timeAsDouble`    | The game time of the current frame: what `Update` code (via `Time.deltaTime`), Animators, particle systems, Timeline in Game Time mode and interpolated physics evaluate the frame for |

The animation time is the time the frame's content was animated for, not a measurement of when it reaches the screen; the capture
provides that side, and the difference is the animation error.

When your game animates from another clock, give the overlay that clock:

```csharp
overlay.AnimationTimeProvider = () => myClock.Seconds;         // for example a simulation clock
overlay.AnimationTimeProvider = () => Time.unscaledTimeAsDouble; // animations that ignore Time.timeScale
```

A simulation that only advances in fixed steps and renders without interpolation shows `Time.fixedTimeAsDouble` rather than
`Time.timeAsDouble`.

The **CPU start time** and **CPU busy** come from Unity's clock: the start is `Time.unscaledTimeAsDouble` (the time at the
beginning of the frame), and CPU busy runs from there until the overlay draws the marker at the end of the frame, just before Present.
A game with its own frame pacer gives the pacer's clock for the CPU start time and the intended display time, since both share one
steady clock, and may give its own CPU busy:

```csharp
overlay.IntendedDisplayTicksProvider = () => pacer.IntendedDisplayTicks;
overlay.CpuStartTicksProvider = () => pacer.CpuStartTicks;
overlay.CpuBusyTicksProvider = () => pacer.CpuBusyTicks; // optional
```

With only an `IntendedDisplayTicksProvider`, the CPU start time is left unknown (0) rather than mixing two clocks; CPU busy still
comes from Unity.

## Settings

| Setting                 | Meaning                                                                                                                           |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Stored Height           | Height of the frames the capture tool stores; picks the module size. 0 = the output height                                        |
| MJPEG                   | The capture card delivers MJPEG: 4 instead of 3 stored pixels per module                                                          |
| Module Size Px          | Fixed module size in output pixels (overrides Stored Height)                                                                      |
| Sync Marker             | Also draw the small sync marker at the bottom left: it detects tearing, and camera capture needs it for its timing                |
| Draw When Idle          | Draw frame markers (run id 0) while no run is active; `DrawWhenIdle` in code, e.g. off while in menus                             |
| Material                | Optional unlit vertex color material without blending, depth test or culling; default Hidden/Internal-Colored                     |
| Sequence Marker Seconds | How long the start and end markers stay on screen (default 0.1 s: three frames of a 30 fps capture; one captured frame is enough) |

## How it draws, and the rules

`FrameMarkerOverlay` waits for the end of the frame (`WaitForEndOfFrame`), after cameras, post processing, upscaling and UI, and draws
the marker with GL immediate mode straight into the output in pixel coordinates. The rules from [Integrating the marker](integrating.md)
apply:

- **Last in the frame:** nothing may be drawn over or blended with the marker.
- **HDR output off:** tone mapping would change its black and white. The overlay warns once when HDR output is active (Unity 2023.1+).
- **Pixel exact:** vertices lie on pixel corners and map 1:1 to output pixels, no half-pixel offset.
- **Player builds:** the default material uses the built-in shader `Hidden/Internal-Colored`. If the marker is missing in a build,
  add the shader under **Project Settings → Graphics → Always Included Shaders**, or assign a material.

Nothing is allocated per frame: the generator, the quad buffer and the material are created once.

## Drawing it from your own pipeline code

To draw the marker yourself (a URP renderer feature, an HDRP custom pass, a `CommandBuffer`), use `FrameMarkerMesh`. It keeps a
`Mesh` with the current marker in Unity screen pixels:

```csharp
using MB.FrameMarker;
using MB.FrameMarker.Unity;

var markerMesh = new FrameMarkerMesh();                 // once
var material = FrameMarkerGL.CreateMaterial();           // once, or your own unlit vertex color material

// every frame, as the last thing drawn into the output
var options = new Options(Marker.RecommendModuleSizePx(Screen.height, 540));
var origin = Marker.RecommendedOrigin(MarkerKind.Frame, Screen.width, Screen.height, options);
var payload = new Payload((ulong)Time.frameCount, Marker.SecondsToTicks(Time.timeAsDouble), runId);
markerMesh.Update(payload, default, options, origin, Screen.height);
commands.SetViewProjectionMatrices(Matrix4x4.identity, PixelSpace.Projection(Screen.width, Screen.height));
commands.DrawMesh(markerMesh.Mesh, Matrix4x4.identity, material);
```

`FrameMarkerGL.DrawQuads` draws marker quads with GL immediate mode into the current render target, the way the overlay does.

## Using the general library directly

The core library works without the helpers (and outside Unity). Create one `MarkerGenerator` and reuse it; it writes the marker
straight into your arrays:

```csharp
var generator = new MarkerGenerator();
var vertices = new Vertex[Marker.MaxTriangleVertexCount];

int count = generator.GenerateTriangles(payload, options, origin, vertices); // 6 vertices per quad, pixel coordinates, top-left origin
```

`GenerateIndexed` (vertices and indices) and `GenerateQuads` (rectangles) are the alternatives; start markers use the `GenerateStart…`
variants with a `StartMetadata` (sequence id and time).

## What is verified

- **Every push (CI):** the package is assembled and validated with `python marker/unity/build_upm.py --output <folder> --check`:
  every asset has a `.meta` file with a unique, stable GUID, the version matches `marker/VERSION` and the core sources equal
  `marker/csharp/source`. The core library itself is tested on .NET, where it matches the C++ library module for module and pixel for
  pixel.
- **Before a release (local, needs a Unity license):** `python marker/unity/check_in_unity.py` runs a real Unity editor in batch mode
  (no window) on a throw-away project. It checks that:
  - the package compiles, without warnings;
  - the core library reproduces all 512 C++ module matrices on Unity's scripting runtime;
  - `FrameMarkerGL` (the overlay's drawing) and `FrameMarkerMesh` (command buffer drawing) render pixel exact into a render texture,
    including the y flip.

  Passed with Unity 6000.3.24f1 and 6000.6.2f1 on Windows (Direct3D 11).

- **Not verified yet:**
  - the overlay's end-of-frame drawing in a running game and in player builds, with URP and HDRP;
  - other graphics APIs (Vulkan, Metal, OpenGL);
  - Unity versions before 6 (the package declares 2021.3).
