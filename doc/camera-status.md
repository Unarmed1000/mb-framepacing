# Camera capture: status and next steps

> [!WARNING]
> Camera capture is **VERY EXPERIMENTAL**. How to use it: [camera.md](camera.md). This page records what exists, how each part
> was checked, what is known to be missing, and what to do next. Keep it current with every camera change.

Status on 2026-09-26: merged into `master` as very experimental (PR #2). CI passes on Windows, Ubuntu and macOS. Validation
with real hardware is still pending (next steps 1–3).

## Summary

The whole camera pipeline is implemented and tested against a **simulated** camera and display, and end to end through a real
ffmpeg on synthetic clips. It has **never run with a real camera or display**, so none of its numbers are validated. It must stay
labelled "very experimental" until the validation below has been done.

## What exists and how it was checked

| Area                                                               | State                                    | Checked by                                                         |
| ------------------------------------------------------------------ | ---------------------------------------- | ------------------------------------------------------------------ |
| Rig calibration (`camera-rig calibrate`, GUI wizard)               | Implemented                              | Synthetic camera unit tests, `selftest --camera`, real ffmpeg test |
| Homography refinement (Gauss-Newton on the module pattern)         | Implemented                              | Unit tests: ~1 px detector error down to 0.1–0.2 px                |
| Rig verification before every capture                              | Implemented                              | Unit tests (moved camera fails, start markers accepted)            |
| Saved cameras (`camera-rigs/` library, `--name`, `list`, `delete`) | Implemented                              | Unit tests, DocImages                                              |
| Import of recorded clips (`import --camera`, `--recorded-fps`)     | Implemented                              | End to end through a real ffmpeg (slow motion FFV1 clip)           |
| Rectification in ffmpeg (`crop,perspective,scale,vstack`)          | Implemented                              | End to end through a real ffmpeg                                   |
| Rectification in C# (`CameraRectifier`, sources without ffmpeg)    | Implemented                              | `selftest --camera`, benchmarks (no allocations)                   |
| Camera decoding (module grid sampler, camera captures only)        | Implemented                              | Unit tests; capture cards keep the pure barcode path               |
| Camera analysis (scanout delay, camera tears, second zone only)    | Implemented                              | Unit tests against the synthetic ground truth                      |
| GUI camera wizard (shows the camera frames), camera card           | Implemented                              | DocImages (headless); no unit tests for the wizard view model      |
| Live UVC cameras (`capture -d <camera> --camera`)                  | Implemented, **never run with a camera** | Same code path as import; never tried with hardware                |
| Real cameras and displays                                          | **Not validated**                        | Nothing yet                                                        |

Results on the synthetic camera (`selftest --camera --fps 1000 --refresh 60`):

- Every presented frame is found.
- Display deltas are within two camera periods of the truth, with a mean error of about 0.66 ms.
- The scanout delay matches the simulation (10.0 ms).
- With vsync off (`--tear-every`), every tear between the zones is found. A tear at the very start or end of a run is not
  counted.
- Precision by camera rate: the table in [camera.md](camera.md#what-you-need), regenerated with
  `python tools/camera_rate_table.py --update-doc`. On a 60 Hz display the mean error is about half a camera period and the
  maximum about one period; every frame was found even at 100 fps (1.7× refresh), because the simulated panel is idealised.

Performance numbers are in [camera.md](camera.md#performance). Per camera frame the work is about 35 µs of decoding plus 0.2 ms
of C# rectification, well above 1000 fps on one core.

## Known issues and limits

- **Not validated** with real cameras, displays or reference hardware.
- Only tears **between** the two zones are detectable, and only when the zones are at least 4 camera frames apart in the scanout.
- A start marker in the **BottomLeft** slot is larger than the frame marker and runs off the bottom of the screen. Verification
  therefore reads a whole second of frames. A display that scans bottom to top makes the BottomLeft zone the timing zone, and its
  start markers are then cut off.
- **Exposure and focus** can not be set through ffmpeg; the user sets them in the camera or its tools.
- "First seen" is when the timing zone shows the new frame completely. That is a roughly constant time after vsync (the scanout
  crossing the marker plus the panel response). Frame-to-frame times are unaffected, but absolute times are late.
- The GUI's Analyze page does not show the camera statistics yet. They are only in `summary.json` (`runs[].camera`) and the CSV
  files.
- The synthetic camera models a simple exponential panel response and a global shutter. Rolling shutter cameras (most phones)
  and overdrive are not modelled.

## Next steps

In priority order:

1. **First real footage.** Film the C++ `marker-render` output or the Unity sample, with both markers and vsync on, at a fixed
   refresh rate. A phone's 240 fps slow motion is enough to start. Then run `camera-rig calibrate clip.mp4 --recorded-fps 240
--name phone` and `import --camera phone --analyze`. Record which checks warn, and why.
2. **Validate against a capture card** on the same display (vsync on, fixed refresh). The frame-to-frame display times of the
   camera and the card must agree within the camera's resolution. Document the result here.
3. **Live UVC camera:** run `capture -d <camera> --camera <name>` with a 240–330 fps MJPEG webcam class camera on Windows, and
   on Linux (v4l2) if available. Check the source's frame drops and device timestamps.
4. **Rolling shutter:** most phones and consumer cameras read the sensor line by line. Model it in the synthetic camera, and
   check whether the scanout delay and tear detection need to correct for it.
5. **Analyze page:** show the scanout delay, camera tears and second-zone-only frames.
6. **GenICam GenTL capture source** for USB3/GigE machine vision cameras (500–1000+ fps live with hardware timestamps). It is
   vendor neutral, since it loads any vendor's `.cti` producer, and slots in as another `ICaptureSource` using `CameraRectifier`.
7. **OpenCV** (OpenCvSharp4, Apache-2.0; not Emgu CV), only if real footage shows the need:
   - lens calibration with a full-screen ChArUco board, if per-zone transforms are not accurate enough;
   - exposure and focus control on Windows and Linux.
8. **Drop "very experimental":** merged early with the labels on (PR #2). Once steps 1–3 give credible numbers, review the
   results and decide whether the labels can be softened.

## Where things are

| What                                          | Where                                                                                                        |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Calibration, verification, rig file, library  | `measure/libs/MB.FramePacing.Capture/source/Camera/`                                                         |
| ffmpeg rectification filter, `--recorded-fps` | `Capture/source/Ffmpeg/FfmpegCommandBuilder.cs` (`BuildCameraFilter`), `FfmpegCaptureSource.cs`              |
| Homography, refinement, grid sampler          | `measure/libs/MB.FramePacing.Marker/source/` (`Homography*.cs`, `ModuleGridSampler.cs`, `MarkerGeometry.cs`) |
| Camera analysis                               | `Analysis/source/CaptureDecoder.cs` (`CameraLayout`), `TimelineAnalyzer.cs` (`AnalyzeCamera`)                |
| Synthetic camera (ground truth)               | `Capture/source/Synthetic/SyntheticCamera*.cs`                                                               |
| CLI                                           | `measure/app/FramePacing/source/Commands/CameraRigCommand.cs`, `SelfTestCommand.cs` (`--camera`)             |
| GUI                                           | `measure/app/FramePacing.Gui/source/ViewModels/Camera*.cs`, `Views/CameraWizardWindow.axaml`                 |
| Tests                                         | `*Camera*Tests.cs`, `HomographyTests.cs`, `ModuleGridSamplerTests.cs`; `FfmpegCameraTests` needs ffmpeg      |
| Benchmarks                                    | `measure/tools/Benchmarks` (`dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj`)    |

Quick checks after a change:

```sh
mb-quality -r --all .
dotnet run --project measure/app/FramePacing/FramePacing.csproj -- selftest --camera --fps 1000 --refresh 60 --tear-every 9
dotnet run --project measure/tools/DocImages -c Release -- <scratch dir>   # the wizard and camera card, headless
```
