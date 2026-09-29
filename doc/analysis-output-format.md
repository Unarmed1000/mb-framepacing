# Analysis output format

The analysis writes its results into the capture's `analysis/` folder: `summary.json`, `captures.csv`, and `run-<id>-frames.csv` for
every run. This document specifies those files, so other tools can read them. The data libraries in [`data/`](../data/README.md) read
them in C#, Python and C++; the C# library also writes them, and the tools write through it.

```text
analysis/
├── summary.json          the capture, the analysis settings, and every run's counts, statistics, pacing and histograms
├── captures.csv          one line per captured frame: its times, what its main marker said and the marker's bytes
└── run-<id>-frames.csv   one line per application frame that was shown (per run)
```

A run's files are named after its run id: `run-<id>-frames.csv`, and `run-<id>-<n>-frames.csv` for the n-th run (from 2 on) with the
same id. `summary.json` names each run's frames file (`runs[].framesFile`); read that name instead of building it.

## Format version

`summary.json` has a top-level `formatVersion` (currently **1**), which covers the CSV files it names. A file without it is format 1.
Readers refuse a newer format ("update the tools or the library"). Within a format version, fields and columns may be added:

- Readers ignore JSON fields and CSV columns they do not know.
- CSV columns are found by their name in the header line, never by position.
- A field or column an older file lacks reads as "not known" (null).

## Conventions

- **Text:** UTF-8 without a byte order mark. Lines end with the writing platform's line ending (`\n`, or `\r\n` on Windows); readers
  accept both.
- **Times:** milliseconds (`...Ms`). In the CSV files they have at most four decimals (`16.6667`), which is exactly a whole number of
  100 ns ticks (TimeSpan ticks), so reading one back gives the tick it was written from: ticks = round(ms × 10000). Numbers use `.` as
  the decimal separator, whatever the machine's language.
- **Empty cell:** the value is not known or does not apply (null).
- **Clocks:** display times (`firstSeenMs`, `captureMs`, ...) are on the capture's clock (the capture device's, or the host's when the
  device gives none: `summary.json` `timeSource`), from the start of the capture. Values from the markers (`animationMs`,
  `intendedDisplayMs`, `cpuStartMs`) are on the application's clocks, as it wrote them.
- **The terms** are those of [vocabulary.md](vocabulary.md): display time, display time step, animation time step, animation error.

## `summary.json`

One JSON object, written indented, with camelCase names. Null values are left out. Enumerations are written as their names.

| Field                     | Type    | Meaning                                                                                              |
| ------------------------- | ------- | ---------------------------------------------------------------------------------------------------- |
| `formatVersion`           | integer | The format of the analysis output (see above)                                                        |
| `toolVersion`             | string  | The version of the tools that wrote it                                                               |
| `experimental`            | string  | A notice when the analysis is experimental (a camera capture); absent otherwise                      |
| `scanout`                 | string  | `SingleScanout` (a capture card) or `Camera` (very experimental)                                     |
| `analysedUtc`             | string  | When the analysis ran (ISO 8601, UTC)                                                                |
| `captureDirectory`        | string  | The capture folder it analysed                                                                       |
| `capture`                 | object  | The capture's `capture.json`, as it was when analysed                                                |
| `frameSize`               | string  | The stored frame size, `WIDTHxHEIGHT`                                                                |
| `timeSource`              | string  | The clock of the capture times: `Device` or `Host`                                                   |
| `capturePeriodMs`         | number  | The capture period                                                                                   |
| `measurementResolutionMs` | number  | How precisely a display time is known (the capture period)                                           |
| `errorThresholdMs`        | number  | The \|animation error\| above which a frame counts as off                                            |
| `markers`                 | array   | Where the markers were: `{ "bounds": "x,y,width,height", "moduleSizePx": number }`, in stored pixels |
| `warnings`                | array   | Warnings about the whole capture (strings)                                                           |
| `runs`                    | array   | One object per run (below)                                                                           |

### `runs[]`

| Field                            | Type    | Meaning                                                                               |
| -------------------------------- | ------- | ------------------------------------------------------------------------------------- |
| `runId`                          | integer | The run id the markers carry                                                          |
| `name`                           | string  | The name the user gave the capture; absent without one                                |
| `sequenceId`                     | string  | The start marker's sequence id (as text, or as a UUID); absent without a start marker |
| `startTimeUtc`                   | string  | The start marker's wall clock time (ISO 8601, UTC); absent without one                |
| `hasStartMarker`, `hasEndMarker` | boolean | Whether the run was bracketed by start and end markers                                |
| `framesFile`                     | string  | The run's frames CSV, next to `summary.json`                                          |
| `counts`                         | object  | What the run's captures held (below)                                                  |
| `statistics`                     | object  | The run's statistics (below)                                                          |
| `pacing`                         | object  | The refresh, the target, late frames and the verdict (below)                          |
| `histograms`                     | object  | `animationErrorMs` and `displayDeltaMs` (below)                                       |
| `camera`                         | object  | Very experimental camera captures only (below)                                        |
| `warnings`                       | array   | Warnings about the run (strings)                                                      |

**`counts`** (integers): `captures`, `decoded`, `undecodable`, `torn`, `notRecorded` (captures the recorder dropped),
`sourceDropEvents` (drops the source reported), `presentedFrames`, `skippedFrameIndices` (frame indices never seen),
`droppedFrames` (frames the target dropped: skipped frame indices over a capture without gaps that never came back out of order),
`outOfOrderCaptures` (captures that showed an older frame again), `segments` (parts of the run between gaps in the capture).

**Statistics of a quantity** (every `...Ms` object below): `count`, `min`, `mean`, `stdDev` (the sample standard deviation), `p50`,
`p95`, `p99`, `p999`, `max`. Percentiles interpolate linearly between the closest ranks. `count` 0 means no value, and every other
field is then 0.

**`statistics`:**

| Field                                       | Meaning                                                                                                                |
| ------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `animationDeltaMs`, `animationErrorMs`      | The animation time step and animation error of the frames with an animation error                                      |
| `displayDeltaMs`                            | The display time steps that count toward the frame rate: all but a static frame's time on screen (flag `StaticBefore`) |
| `absoluteAnimationErrorMs`                  | The same frames' \|animation error\|                                                                                   |
| `driftMs`, `onScreenMs`                     | Every presented frame's drift and time on screen                                                                       |
| `framesWithAnimationError`                  | Frames whose \|animation error\| is above `errorThresholdMs`                                                           |
| `errorPerFrameMs`, `percentError`           | The mean \|animation error\|; the summed \|animation error\| as a percentage of those frames' display time steps       |
| `averageFps`                                | Those frames over the time their display time steps cover                                                              |
| `onePercentLowFps`, `pointOnePercentLowFps` | The frame rate at the 99th / 99.9th percentile display time step (nearest rank); absent below 100 / 1000 frames        |
| `excludedStaticFrames`                      | The display time steps the frame rate numbers leave out: each is a static frame's time on screen                       |
| `uncertainSteps`                            | The display time steps a capture gap made uncertain (flag `UncertainStep`): not judged, left out of the frame rates    |
| `cpuBusyMs`, `frameTimeMs`, `cpuWaitMs`     | From the markers: CPU busy, the frametime and CPU wait (count 0 when the markers carry none)                           |

**`pacing`:**

| Field                                                             | Meaning                                                                                                            |
| ----------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `refreshPeriodMs`, `refreshHz`                                    | The display's refresh (a capture card's capture period; calculated from a camera's frames)                         |
| `refreshCalculated`                                               | True when the refresh was calculated from a camera capture's frames                                                |
| `targetFrameMs`                                                   | The frame time the run is measured against, in whole refreshes                                                     |
| `source`                                                          | Where the targets come from: `Schedule`, `TargetFrameTime`, `PreferredFrameTime`, `GivenTarget` or `NativeRefresh` |
| `lateFrames`, `lateShare`                                         | Frames shown late, and their share of the presented frames with a display time step (0..1)                         |
| `worstLateShare`                                                  | The highest share of late frames in any 2 s window (0..1)                                                          |
| `errorFramesWithUnevenDisplay`, `errorFramesWithEvenDisplay`      | Frames with an animation error on an uneven or an even display                                                     |
| `verdict`                                                         | `None`, `BadPacing`, `DeltaTimeJitter` or `Both`                                                                   |
| `expectedRefreshHz`, `refreshDeviation`, `matchesExpectedRefresh` | The refresh rate the user expects, and how the measured one compares; absent without one                           |
| `pacingErrorMs`, `predictionErrorMs`                              | With a schedule (statistics): display / animation time step minus the intended step                                |

**`histograms`:** `animationErrorMs` and `displayDeltaMs`, each `{ "binWidthMs", "total", "bins": [{ "centerMs", "count" }] }`. Bin
_k_ covers [(k − 0.5) × width, (k + 0.5) × width); the bins are listed by their centre, from the lowest to the highest used.

**`camera`** (very experimental): `scanoutDelay` (statistics: first seen in the second zone minus first seen in the timing zone),
`framesSeenInBothZones`, `tornFrames`, `secondZoneOnlyFrames`.

## `run-<id>-frames.csv`

One line per presented frame (an application frame that was seen at least once), in display order.

| Column              | Meaning                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `segment`           | The part of the run between two gaps in the capture (0 first)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `frameIndex`        | The marker's frame index                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `animationMs`       | The marker's animation time                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `firstCaptureIndex` | The first capture that showed the frame                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `firstSeenMs`       | The frame's display time: when it was first seen                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `onScreenMs`        | How long it stayed on screen                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `captures`          | How many captures showed it                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `skippedBefore`     | Frame indices before this one that were never seen                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `displayDeltaMs`    | The display time step: from the previous frame's display time to this one's                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `animationDeltaMs`  | The animation time step                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `animationErrorMs`  | The animation time step minus the display time step; empty for a step from or to a static frame (nothing animates)                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `driftMs`           | Since the segment's first frame: the sum of the animation errors (the animation time that passed minus the display time that passed, without the steps from or to static frames)                                                                                                                                                                                                                                                                                                                                                                 |
| `flags`             | `\|`-separated: `SkippedBefore`, `UncertainStart`, `Torn`, `Late`, `Static` (the marker says nothing animates), `StaticBefore` (the frame before it is static, so its display time step is that frame's time on screen and the frame rate leaves it out), `UncertainStep` (a capture card missed a moment this step depends on: this or the previous frame was first seen after captures not decoded, not recorded or dropped by the source; the step gets no animation error or late verdict and the frame rates leave it out); empty when none |
| `intendedDisplayMs` | From the marker: when the pacer intended the frame to be shown (its own clock)                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `markerTargetMs`    | From the marker: the pacer's target frame time; `429496.7295` (the marker's `0xFFFFFFFF` ticks) = on demand                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `targetMs`          | The frame time this frame is measured against, in whole refreshes; without the pacer's schedule, a frame after frames the target dropped counts one target per dropped frame too; empty on demand                                                                                                                                                                                                                                                                                                                                                |
| `markerPreferredMs` | From the marker: the frame time the application wants to run at; `429496.7295` = on demand                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `preferredMs`       | The preferred frame time the late share is measured against, in whole refreshes: the marker's, else the target frame rate given to the tools, else one refresh; empty on demand                                                                                                                                                                                                                                                                                                                                                                  |
| `pacingErrorMs`     | With a schedule: the display time step minus the intended step                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `predictionErrorMs` | With a schedule: the animation time step minus the intended step                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `latenessMs`        | With a schedule: how long after its intended time the frame appeared, relative to the run's on-time frames                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `lastSeenMs`        | When the last capture that showed it was taken                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `cpuStartMs`        | From the marker: the CPU start time (PresentMon's CPUStartTime, on the pacer's clock)                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `cpuBusyMs`         | From the marker: CPU busy (PresentMon's MsCPUBusy)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `frameTimeMs`       | The frametime: this frame's CPU start to the next frame's, when both are known (PresentMon's MsBetweenAppStart)                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `cpuWaitMs`         | The frametime minus CPU busy (PresentMon's MsCPUWait)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `olderFrames`       | The captures that showed an older frame out of order while this frame was the newest (before the next presented frame): `frameIndex@captureMs` entries separated by `\|`, in capture order; empty when none                                                                                                                                                                                                                                                                                                                                      |

Camera captures (very experimental) add `mainMarkerFirstSeenMs` (when the main marker first showed the frame) and `scanoutDelayMs`
(`firstSeenMs` minus it).

## `captures.csv`

One line per capture index, including the ones the recorder dropped.

| Column             | Meaning                                                                                           |
| ------------------ | ------------------------------------------------------------------------------------------------- |
| `captureIndex`     | The capture source's frame counter (unrelated to the marker's frame index)                        |
| `captureMs`        | When the capture was taken (the analysis's clock); empty for a dropped capture                    |
| `status`           | `Decoded`, `Undecodable`, `Torn` or `NotRecorded` (dropped by the recorder)                       |
| `kind`             | The main marker's kind: `Frame`, `SequenceStart`, `SequenceEnd` or `Sync`; empty without a marker |
| `runId`            | The main marker's run id                                                                          |
| `frameIndex`       | The main marker's frame index                                                                     |
| `animationMs`      | The main marker's animation time                                                                  |
| `sourceDropBefore` | `1` when the source reported dropping frames before this one, else `0`                            |
| `hostMs`           | The host clock's time of the capture                                                              |
| `deviceMs`         | The capture device's time of the capture; empty when it gave none                                 |
| `payloadHex`       | The main marker's encoded bytes as read ([marker format](marker-format.md)), in hexadecimal       |

Camera captures (very experimental) add `secondZoneFrameIndex`: the frame index the second zone showed.
