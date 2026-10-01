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

| Area                                                                               | State                                    | Checked by                                                         |
| ---------------------------------------------------------------------------------- | ---------------------------------------- | ------------------------------------------------------------------ |
| Rig calibration (`camera-rig calibrate`, GUI wizard)                               | Implemented                              | Synthetic camera unit tests, `selftest --camera`, real ffmpeg test |
| Homography refinement (Gauss-Newton on the module pattern)                         | Implemented                              | Unit tests: ~1 px detector error down to 0.1–0.2 px                |
| Rig verification before every capture                                              | Implemented                              | Unit tests (moved camera fails, start markers accepted)            |
| Saved cameras (`camera-rigs/` library, `--name`, `list`, `delete`)                 | Implemented                              | Unit tests, DocImages                                              |
| Import of recorded clips (`import --camera`, `--recorded-fps`)                     | Implemented                              | End to end through a real ffmpeg (slow motion FFV1 clip)           |
| Rectification in ffmpeg (`crop,perspective,scale,vstack`)                          | Implemented                              | End to end through a real ffmpeg                                   |
| Rectification in C# (`CameraRectifier`, sources without ffmpeg)                    | Implemented                              | `selftest --camera`, benchmarks (no allocations)                   |
| Camera decoding (module grid sampler, camera captures only)                        | Implemented                              | Unit tests; capture cards keep the pure barcode path               |
| Camera analysis (scanout delay, camera tears, second zone only)                    | Implemented                              | Unit tests against the synthetic ground truth                      |
| Zones decoded live, only the capture data stored (`--keep-frames` keeps the zones) | Implemented                              | `selftest --camera --fps 1000` (no drops), `FfmpegCameraTests`     |
| Refresh rate calculated per run (`RefreshEstimator`), late frames                  | Implemented                              | Unit tests, `selftest --camera` (60 Hz found as 16.67 ms)          |
| GUI camera wizard (shows the camera frames), camera card                           | Implemented                              | DocImages (headless); no unit tests for the wizard view model      |
| GUI: hidden unless Settings → Experimental features is on                          | Implemented                              | DocImages (headless: off for the capture page, on for the camera)  |
| Live UVC cameras (`capture -d <camera> --camera`)                                  | Implemented, **never run with a camera** | Same code path as import; never tried with hardware                |
| Real cameras and displays                                                          | **Not validated**                        | Nothing yet                                                        |

Results on the synthetic camera (`selftest --camera --fps 1000 --refresh 60`):

- Every presented frame is found.
- Display deltas are within two camera periods of the truth, with a mean error of about 0.62 ms (timed by the sync marker).
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
- **The main marker is version 6 (41×41 modules), the sync marker version 2 (25×25).** The camera times the frames by the sync
  marker: timing by the taller main marker scattered "first seen" by 2 to 3 camera frames (the scanout needs longer to cross it,
  and when a half switched row still decodes depends on the payload), which doubled the display time step error. With the sync marker
  the synthetic camera is back to a mean error of 0.62 ms at 1000 fps.
- **Steep angles:** ZXing's finder search estimates the symbol size from the finder distances and gives up when that estimate is
  off by about a module, which happens across 41 modules at an angle. Calibration (which searches) works at about 20 degrees off
  the screen normal and fails at about 30 (`CameraCalibratorTests`, the 30 degree case is explicit). Captures are not affected: they
  decode through the rig's transform. Next step: a detector that uses the known symbol size.
- A display that scans bottom to top reaches the sync marker first: tears can not be told apart then.
- **Exposure and focus** can not be set through ffmpeg; the user sets them in the camera or its tools.
- "First seen" is when the sync marker shows the new frame completely. That is a roughly constant time after vsync (the scanout
  reaching and crossing it plus the panel response). Frame-to-frame times are unaffected, but absolute times are late.
- The GUI's Analyze page does not show the camera statistics yet. They are only in `summary.json` (`runs[].camera`) and the CSV
  files.
- **The refresh rate is calculated from each run** (a capture card captures at the refresh rate and needs none). The first-seen
  intervals form clusters at whole refreshes; the refresh is the largest period that makes every well-populated cluster a whole
  multiple, so a game alternating 2 and 3 refreshes still gives the refresh. A steady game below the refresh rate (only
  2-refresh intervals) can not be told from a slower display; the rig's calibrated refresh settles that, and the analysis warns
  when the two disagree by more than 1 %. The period found that way is then measured with a least squares line through every
  reliable first-seen time against its refresh number (`RefreshEstimator.RefinePeriodTicks`; the average of the intervals only uses
  the two ends of each unbroken stretch). On simulated sightings its error is 10 to 20 times smaller, from 100 to 2000 fps on a
  60 Hz display; `selftest --camera` finds 59.99 to 60.00 Hz at every rate of the table (59.90 to 60.11 Hz before). The line is
  not taken when its own numbering does not hold up against it (a camera below about twice the refresh rate) or when it leaves
  the estimate by more than 1 %. The user can give the **expected display rate** (`--display-hz`, GUI "Display refresh
  rate"): it settles the ambiguity first, and the analysis compares it with the calculated rate (`runs[].pacing.refreshDeviation`,
  a warning above 1 %). The calibration uses the same estimator, so a calibration clip must show one frame per
  refresh for most of it (vsync on, full rate).
- **Animation error noise:** a camera's animation error is the difference of two first-seen times, each good to about one camera
  period. The error threshold is the same for every source (1 ms, `--error-threshold-ms`), so a slower camera's noise counts as
  error frames; the analysis warns when the camera period is longer than the threshold.
- **Vsync off:** a frame the main marker never saw (presented below it and replaced before the next scanout reached it) has no data
  of its own and counts as skipped; the frames around it can be marked late although their animation error is about 0.
- The synthetic camera models a simple exponential panel response and a global shutter. Rolling shutter cameras (most phones)
  and overdrive are not modelled.

## Next steps

In priority order:

1. **First real footage.** Film the C++ `marker-render` output or the Unity sample, with both markers and vsync on, at a fixed
   refresh rate. A phone's 240 fps slow motion is enough to start. Then run `camera-rig calibrate clip.mp4 --recorded-fps 240
--name phone` and `import --camera phone --analyze`. Record which checks warn, and why.
2. **Validate against a capture card** on the same display (vsync on, fixed refresh). The display time steps of the
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

| What                                          | Where                                                                                                                |
| --------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Calibration, verification, rig file, library  | `measure/libs/MB.FramePacing.Capture/source/Camera/`                                                                 |
| ffmpeg rectification filter, `--recorded-fps` | `Capture/source/Ffmpeg/FfmpegCommandBuilder.cs` (`BuildCameraFilter`), `FfmpegCaptureSource.cs`                      |
| Homography, refinement, grid sampler          | `measure/libs/MB.FramePacing.MarkerDecoding/source/` (`Homography*.cs`, `ModuleGridSampler.cs`, `MarkerGeometry.cs`) |
| Camera analysis                               | `Analysis/source/CaptureDecoder.cs` (`CameraLayout`), `TimelineAnalyzer.cs` (`AnalyzeCamera`)                        |
| Synthetic camera (ground truth)               | `Capture/source/Synthetic/SyntheticCamera*.cs`                                                                       |
| CLI                                           | `measure/app/FramePacing/source/Commands/CameraRigCommand.cs`, `SelfTestCommand.cs` (`--camera`)                     |
| GUI                                           | `measure/app/FramePacing.Gui/source/ViewModels/Camera*.cs`, `Views/CameraWizardWindow.axaml`                         |
| Tests                                         | `*Camera*Tests.cs`, `HomographyTests.cs`, `ModuleGridSamplerTests.cs`; `FfmpegCameraTests` needs ffmpeg              |
| Benchmarks                                    | `measure/tools/Benchmarks` (`dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj`)            |

Quick checks after a change:

```sh
mb-quality -r --all .
dotnet run --project measure/app/FramePacing/FramePacing.csproj -- selftest --camera --fps 1000 --refresh 60 --tear-every 9
dotnet run --project measure/tools/DocImages -c Release -- <scratch dir>   # the wizard and camera card, headless
```
