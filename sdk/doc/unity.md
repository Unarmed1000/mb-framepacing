# Unity

The Unity package **MB Frame Pacing** (`com.manabattery.framepacing`) draws the marker into every frame of a Unity game. It contains:

- **the SDK's general C# modules** `MB.FramePacing` (the core: `Point`, `Rectangle`, the time types) and `MB.FramePacing.Marker`: the
  same code as [`sdk/csharp/core`](../csharp/core) and [`sdk/csharp/marker`](../csharp/marker), with no Unity dependency;
- **Unity helpers** `MB.FramePacing.Marker.Unity`: an overlay component that does everything, and building blocks for your own render
  pipeline code.

Unity 2021.3 or newer (C# 9). The package contents are checked in a real Unity editor, see [What is verified](#what-is-verified).

## Install

**Package Manager → + → Add package from git URL**, then:

```text
https://github.com/Unarmed1000/mb-framepacing.git#upm/v0.1.0
```

`upm/v<version>` tags are created by the SDK release workflow. To use an unreleased version, assemble the package yourself and
install it with **Add package from disk** (select its `package.json`):

```sh
python sdk/unity/build_upm.py --output <folder>
```

## Quick start

> **Photosensitivity warning.** The marker is a high-contrast pattern that changes every frame, and flickering patterns can
> trigger seizures in people with photosensitive epilepsy. Use the overlay in test builds only; see
> [Photosensitivity](integrating.md#photosensitivity).

1. Add a GameObject with **Add Component → MB → Frame Marker Overlay**.
2. Set **Stored Height** to the height the capture tool stores (for example 540 for `--scale 960x540`). The module size follows from
   it: 3 stored pixels per module, 4 with **MJPEG**.
3. Turn HDR output off while capturing.
4. Bracket the part to measure:

   ```csharp
   using MB.FramePacing.Marker.Unity;

   var overlay = FindAnyObjectByType<FrameMarkerOverlay>();
   StartCoroutine(overlay.RunFor(10.0)); // start marker, 10 s of frame markers, end marker
   // or overlay.BeginRun(); ... overlay.EndRun();
   ```

   Every run gets a new UUID as its **sequence id**, which the reports show; `overlay.SequenceId` tells you which one. To pick it
   yourself, pass any 16 bytes unique to the run: `SequenceId.FromGuid(...)`, or a text tag of at most 16 ASCII characters
   (`SequenceId.TryFromText("camera pan", out var id)`).

5. Record the capture card with OBS and import the recording with `mb-framepacing import recording.mkv --wait-for-start
--stop-at-end --analyze` (see [Using mb-framepacing](../../measure/doc/usage.md)).

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
overlay.StaticAfterProvider = () => nothingPending; // optional: nothing animates while this frame is on screen
overlay.StaticBeforeProvider = () => wokeFromIdle; // optional: nothing animated while the previous frame was
overlay.PreferredFrameTimeProvider = () => pacer.PreferredFrameTime; // a NanosecondTimeDuration: the rate the game wants; default: as the target
overlay.TargetFrameTimeProvider = () => pacer.TargetFrameTime; // a NanosecondTimeDuration: the interval it aims for now; default: Unity's settings (below)
overlay.IntendedDisplayTimeProvider = () => pacer.IntendedDisplayTime; // a NanosecondTickCount on the pacer's steady clock
overlay.CpuStartTimeProvider = () => pacer.CpuStartTime; // a NanosecondTickCount on the same clock
overlay.CpuBusyProvider = () => pacer.CpuBusy; // optional, a NanosecondTimeDuration
```

The **target frame time** without a provider is what Unity's settings aim for: on Android and iOS `Application.targetFrameRate`
(30 fps while it is unset; they ignore `QualitySettings.vSyncCount`); elsewhere the refresh rate divided by
`QualitySettings.vSyncCount` while vsync is on (`Application.targetFrameRate` is then ignored), else `Application.targetFrameRate`,
else on the web the refresh rate, else unknown (0). XR platforms ignore both settings: give the XR display's rate as a provider. The
**preferred frame time** is the rate the game wants to run at. Without a provider it is the same default, which Unity does not lower
on its own; a pacer that runs the game slower than it wants gives its preferred rate here. `Payload.OnDemandFrameTime` says the game
presents only when something changes. What each field means, and what to write for typical frame pacers, is in
[Filling the marker fields](marker-fields.md).

With only an `IntendedDisplayTimeProvider`, the CPU start time is left unknown (0) rather than mixing two clocks; CPU busy still
comes from Unity.

## Settings

| Setting                    | Meaning                                                                                                                                                                                                                                                                                                                  |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Stored Height              | Height of the frames the capture tool stores; picks the module size. 0 = the output height                                                                                                                                                                                                                               |
| MJPEG                      | The capture card delivers MJPEG: 4 instead of 3 stored pixels per module                                                                                                                                                                                                                                                 |
| Module Size Px             | Fixed module size in output pixels (overrides Stored Height)                                                                                                                                                                                                                                                             |
| Sync Marker                | Also draw the small sync marker at the bottom left: it detects tearing, and camera capture needs it for its timing                                                                                                                                                                                                       |
| Draw When Idle             | Draw frame markers (run id 0) while no run is active; `DrawWhenIdle` in code, e.g. off while in menus                                                                                                                                                                                                                    |
| Material                   | Optional unlit vertex color material without blending, depth test or culling; default Hidden/Internal-Colored                                                                                                                                                                                                            |
| Render Mode                | How it is drawn, the same pixels every way, fastest first: Shader Packed Bits (one quad whose shader reads the 211 packed module bytes; the default), Shader (one quad, a texel per module; both need shader model 3.5), Bitmap (a module texture scaled up) or Geometry (quads, works everywhere); `RenderMode` in code |
| Packed Shader, Quad Shader | The two shader modes' shaders, set when the component is added; referencing them keeps them in player builds                                                                                                                                                                                                             |
| Sequence Marker Seconds    | How long the start and end markers stay on screen (default 0.1 s: three frames of a 30 fps capture; one captured frame is enough)                                                                                                                                                                                        |

## How it draws, and the rules

`FrameMarkerOverlay` waits for the end of the frame (`WaitForEndOfFrame`), after cameras, post processing, upscaling and UI, and draws
the marker with GL immediate mode straight into the output in pixel coordinates: by default one quad with the dedicated shader, the
fastest way (see [the ways to draw it](../unity/README.md#ways-to-draw-it-most-efficient-first)). The rules from [Integrating the marker](integrating.md)
apply:

- **Last in the frame:** nothing may be drawn over or blended with the marker.
- **HDR output off:** tone mapping would change its black and white. The overlay warns once when HDR output is active (Unity 2023.1+).
- **Pixel exact:** vertices lie on pixel corners and map 1:1 to output pixels, no half-pixel offset.
- **Player builds:** the shader render modes use `Hidden/MB/FrameMarkerQuadPacked` (the default) and `Hidden/MB/FrameMarkerQuad`,
  which the component references, so builds keep them. Without it (or without shader model 3.5) the overlay warns once and draws geometry, whose default
  material uses the built-in shader `Hidden/Internal-Colored`. If the marker is missing in a build, add the shaders under **Project
  Settings → Graphics → Always Included Shaders**, or assign a material.

Nothing is allocated per frame: the generator, the buffers, the textures and the materials are created once.

## Drawing it from your own pipeline code

To draw the marker yourself (a URP renderer feature, an HDRP custom pass, a `CommandBuffer`), encode it with a `MarkerGenerator` and
give the matrix to `FrameMarkerMesh`. It keeps a `Mesh` with the current marker in Unity screen pixels: a static grid of vertices, set
again only when the size, options, origin or output height change, and per frame only the indices of the dark modules:

```csharp
using MB.FramePacing;
using MB.FramePacing.Marker;
using MB.FramePacing.Marker.Unity;

var generator = new MarkerGenerator();                   // once
var modules = new byte[ModuleMatrix.MaxPackedModuleByteCount]; // once: the encoded marker lives here
var markerMesh = new FrameMarkerMesh();                  // once
var material = FrameMarkerGL.CreateMaterial();            // once, or your own unlit vertex color material

// every frame, as the last thing drawn into the output
var options = Options.Recommended(Screen.height, 540);
var origin = options.RecommendedOrigin(MarkerKind.Frame, Screen.height);
var payload = new Payload(MarkerKind.Frame, runId, (ulong)Time.frameCount, MarkerFlags.NoFlags, NanosecondTimeSpan.FromSeconds(Time.timeAsDouble));
if (generator.TryGenerateModules(payload, modules, out var matrix))
  markerMesh.Update(matrix, options, origin, Screen.height);
commands.SetViewProjectionMatrices(Matrix4x4.identity, PixelSpace.Projection(Screen.width, Screen.height));
commands.DrawMesh(markerMesh.Mesh, Matrix4x4.identity, material);
```

`FrameMarkerGL.DrawQuads` draws marker quads with GL immediate mode into the current render target, the way the overlay does.

`FrameMarkerQuad` draws the marker as one opaque quad with a dedicated shader: per frame only its texture changes, the 211 packed module
bytes (`new FrameMarkerQuad(packedBits: true)`, `Hidden/MB/FrameMarkerQuadPacked`) or a 41×41 texel per module (`Hidden/MB/FrameMarkerQuad`).
`quad.Update(matrix, options, origin, Screen.height)`, then `quad.DrawNow(Screen.width)` (GL immediate mode) or
draw `quad.Mesh` with `quad.Material` from a command buffer with `PixelSpace.Projection`. It needs shader model 3.5 (`Material` is null
without it).

`FrameMarkerTexture` keeps a `Texture2D` with the marker at module resolution (one texel per module, quiet zone included) for UI or
anything that shows an image (`RawImage`, `Graphics.DrawTexture`, a material): `texture.Update(matrix)`. Draw it scaled up by a whole
number with point filtering, on whole pixels, so every module covers exactly the pixels the geometry would.

## Using the general library directly

The marker module works without the helpers (and outside Unity). Create one `MarkerGenerator` and reuse it: it encodes the marker into
bytes you own (the `ModuleMatrix`), and `FrameMarker` draws it straight into your arrays:

```csharp
var generator = new MarkerGenerator();
var modules = new byte[ModuleMatrix.MaxPackedModuleByteCount];
var vertices = new Vertex[FrameMarker.MaxTriangleVertexCount];

if (generator.TryGenerateModules(payload, metadata, modules, out var matrix))  // the metadata only goes into start markers
{
  int count = FrameMarker.ModulesToTriangles(matrix, options, origin, vertices);      // 6 vertices per quad, pixel coordinates, top-left origin
}
```

`ModulesToIndexed` (vertices and indices), `ModulesToQuads` (rectangles) and `ModulesToBitmap` (pixels) are the alternatives; one
matrix can feed several.

## What is verified

- **Every push (CI):** the package is assembled and validated with `python sdk/unity/build_upm.py --output <folder> --check`:
  every asset has a `.meta` file with a unique, stable GUID, the version matches `sdk/VERSION`, every file the package takes from
  the repository equals its source (`sdk/csharp/core/source`, `sdk/csharp/marker/source`, the Unity helpers, `FrameMarker.hlsl`), and
  each default reference names a file and a field of the package. The marker module itself is tested on .NET, where it matches the
  C++ marker module module for module and pixel for pixel.
- **Before a release (local, needs a Unity license):** `python sdk/unity/check_in_unity.py` runs a real Unity editor in batch mode
  (no window) on a throw-away project. It checks that:
  - the package compiles;
  - the marker module reproduces all 512 C++ module matrices on Unity's scripting runtime;
  - every drawing method renders pixel exact into a render texture, including the y flip: `FrameMarkerGL` (the Geometry mode),
    `FrameMarkerMesh` (the static grid with per-frame indices, through a command buffer), `FrameMarkerTexture` (the Bitmap mode) and
    `FrameMarkerQuad` (the Shader and Shader Packed Bits modes, each with GL and through a command buffer), for frame, start, end and
    sync markers at several module sizes and origins;
  - `FrameMarkerTexture` holds the module bitmap, and keeps a quiet zone outside its range within it;
  - `FrameMarkerOverlay` keeps drawing after a frame's draw threw (a provider of the game's that throws).

  `--graphics d3d11|d3d12|glcore|gles|vulkan|metal` forces a graphics API.

  Passed with Unity 6000.3.24f1 and 6000.6.2f1 on Windows (Direct3D 11), and 6000.6.3f1 on Windows with Direct3D 11, OpenGL Core and
  Vulkan.

- **Not verified yet:**
  - the overlay's end-of-frame drawing in a running game and in player builds, with URP and HDRP;
  - other graphics APIs (Direct3D 12, OpenGL ES, Metal);
  - Unity versions before 6 (the package declares 2021.3).
