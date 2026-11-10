# Camera capture: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **Camera capture (VERY EXPERIMENTAL, `measure/doc/camera.md`):**
  - The GUI hides it (the camera card, the synthetic camera source) unless **Settings → Experimental features** is on, and the CLI
    unless `--experimental` is given (see live capture in `capture.md`)
    (`GuiSettings.ExperimentalFeatures`, `CaptureViewModel.ExperimentalFeatures`); switched off, a camera chosen earlier is not used.
  - Every place users meet it says "very experimental": CLI help, the GUI card, `CameraRig.ExperimentalNotice` in rig files and
    analysis warnings, docs. Keep it that way until it is validated with real hardware, and keep `measure/doc/camera-status.md`
    (status, known issues, next steps) current with every camera change.
  - Saved cameras: `CameraRigLibrary` (`camera-rigs/` next to the config file; automation (DocImages) and `--output-root` GUI runs use their output root, never
    the user's library). GUI: `CameraWizardViewModel` + `CameraWizardWindow` (the wizard), `CameraRigViewModel` (the capture page card).
  - `Capture/source/Camera/`: `CameraCalibrator` (calibrate/verify), `CameraRig`/`CameraZone` (the rig file), `CameraRectifier` and
    `RectifyingCaptureSource` (C# path). `FfmpegCommandBuilder.BuildCameraFilter` is the ffmpeg path; both produce the same layout:
    zones of `CameraZone.StoredSizePx` stacked in scanout order.
  - The analysis switches to `ScanoutModel.Camera` when `capture.json` has a `camera` section.
  - Zones: `CameraZone.MainZone` (the main marker, identifies each frame) and `CameraZone.SyncZone` (the sync marker, times it:
    `TimelineAnalyzer.TimeBySyncMarker`). A camera decode only counts when every sampled module matches the decoded payload's symbol
    (`MarkerDecoder.MaxModuleMismatchFraction`).
  - The synthetic camera (`Capture/source/Synthetic/SyntheticCamera.cs`) is the ground truth. `selftest --experimental --camera --fps 1000
--refresh 60 [--tear-every 9]` runs it end to end.
  - Benchmarks: `dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj -- --filter "*"`. Name the csproj: the
    folder's `.slnx` does not build the libraries optimized. The long-running ones (`[BenchmarkCategory(BenchmarkCategories.LongRunning)]`:
    `CardBenchmarks`, `PlaybackBenchmarks`, `OutputFileBenchmarks`, `AnalysisBenchmarks`, many minutes) are left out unless the run adds `--long-running`; mark a new one that takes
    minutes the same way.
  - The precision-by-camera-rate table in `measure/doc/camera.md` is generated: `python tools/camera_rate_table.py --update-doc` (runs
    `selftest --experimental --camera` per rate; selftest prints its error against the simulation for it). Rerun it after changes to the camera
    pipeline.
