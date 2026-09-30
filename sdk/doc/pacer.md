# The frame pacer

The pacer module plans every frame on the display's grid of refreshes and adapts the **swap interval** (how many refreshes each frame
stays on screen) to how long the frames take. It hands the application what to apply and what to write into the frame marker: the
intended display time, the target and preferred frame time and the CPU start time. `AnimationClock` gives each frame the animation
time it will be shown at.

It is **values in, values out**: every frame the application passes what its platform knows and gets back a plan. The pacer calls no
graphics or platform API, has no callbacks and never reads a clock. Made once (it allocates its window then), it never allocates again.

| Language | Module                                                                                                    |
| -------- | --------------------------------------------------------------------------------------------------------- |
| C++20    | `mb_framepacing::pacer`, `<mb/framepacing/pacer/…>`, namespace `MB::FramePacing::Pacer`                   |
| C#       | [`MB.FramePacing.Pacer`](../csharp/pacer/README.md) (.NET Standard 2.1), namespace `MB.FramePacing.Pacer` |

Both have the same types and the same integer arithmetic: they plan every frame alike to the tick. The examples below are C++; the C#
names are the same (`FrameInput`'s optional values are constructor parameters, the getters properties).

## The frame loop

```cpp
#include <mb/framepacing/pacer/AnimationClock.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

// Once: the display's refresh period, from its display mode (here DXGI's 59.94 Hz as the output mode states it)
const PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60'000, 1'001));
PC::FramePacer pacer(settings);
PC::AnimationClock clock(settings.Refresh());

// Every frame
PC::FrameInput input;
input.NowTicks = NowTicks();                      // required: your steady clock in 100 ns ticks
input.VsyncTicks = LatestVsyncTicks();            // optional, 0 = unknown: only what your platform reports
const PC::FrameSchedule schedule = pacer.BeginFrame(input);
const PC::AnimationTime animation = clock.Advance(schedule);
UpdateAndDraw(animation.AnimationTicks);
const uint32_t cpuBusy = pacer.EndFrame({NowTicks()});   // as you draw the marker, last, just before Present
DrawMarker(schedule, animation, cpuBusy);
Present(schedule);                                 // apply the schedule, below
```

### Applying the schedule

Use the first way your platform has:

| Way                     | What to do with the `FrameSchedule`                                                                                                                                                                                         |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Scheduled present**   | Present for `IntendedDisplayTicks`, converted to the platform's clock: `VK_GOOGLE_display_timing`'s `desiredPresentTime`, `VK_EXT_present_timing`, `EGL_ANDROID_presentation_time`, Metal's `present(at:)`                  |
| **Sleep, then present** | Sleep until `EarliestPresentTicks` (the refresh before the intended one), then present with a swap interval of 1: with FIFO vsync a frame presented then is shown at the intended refresh                                   |
| **Swap interval**       | Present with `SwapInterval`: DXGI's `SyncInterval`, `eglSwapInterval`, Unity's `QualitySettings.vSyncCount`. The interval counts from the refresh the previous frame was shown at, so a late frame shifts the ones after it |

### The platform's values

Everything in `FrameInput` but `NowTicks` is optional: pass what your platform reports and leave the rest `0` (unknown). Times are
ticks on the same steady clock as `NowTicks`, so a clock must not give a real time of exactly `0`; steady clocks count from boot.

| `FrameInput`               | What it is                                                  | From                                                                                                                                         | Without it                                                                                           |
| -------------------------- | ----------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `NowTicks` (required)      | The frame's CPU start time                                  | Your steady clock                                                                                                                            |                                                                                                      |
| `VsyncTicks`               | The latest vsync the platform knows                         | DWM's `qpcVBlank`, Choreographer's frame time, `CADisplayLink`'s `timestamp`                                                                 | The grid starts at the first frame's `NowTicks` and follows the inferred display times               |
| `PreviousDisplayTicks`     | When the previous frame was really shown                    | `VK_GOOGLE_display_timing`, `VK_EXT_present_timing`, DXGI frame statistics, EGL frame timestamps, Wayland `presentation-time`                | Inferred: the first refresh the previous frame could make after its Present, never before its target |
| `PredictedDisplayTicks`    | The display time the platform predicts for this frame       | Choreographer's expected presentation time, OpenXR's `predictedDisplayTime`, `CADisplayLink`'s `targetTimestamp`                             | The pacer's own plan                                                                                 |
| `RefreshPeriodNanoseconds` | The refresh period the platform reports now, in nanoseconds | Choreographer's refresh rate callback, `VK_GOOGLE_display_timing`'s `refreshDuration`, `VK_EXT_present_timing`, `CADisplayLink`'s `duration` | The period of `PacerSettings`, or the last `SetRefreshPeriod`                                        |

A reported period that rounds to another nanosecond than the pacer's is a display mode change, as `SetRefreshPeriod` is: the pacer
starts again on a new grid, with an empty window, at the preferred swap interval. The period it already has changes nothing.

Platform clocks convert with the core: `FP::TickCount64::FromNanoseconds` (`CLOCK_MONOTONIC`, Vulkan and EGL present times,
Choreographer), `FP::TickCount64::FromCounter(counter, frequency)` (`QueryPerformanceCounter` with `QueryPerformanceFrequency`), or
a `std::chrono` clock through `core/time/ChronoConversion.hpp` (`FP::ToTickCount64`).

### How a frame is planned

- The pacer keeps a grid of refreshes, exact to 2⁻³² of a tick (`RefreshPeriod`), so it never drifts from the display's rate.
- A frame aims at the refresh one swap interval after the previous frame's display, and never earlier than the first refresh after
  now, or than the platform's predicted display time.
- The previous frame's display is the reported one (`PreviousDisplayTicks`) or the inferred one. It is **late** when it is shown on
  a later refresh than it aimed at: half a refresh or more after its intended display time, as the analysis counts late frames.
- `FrameEnd::WorkTicks` is what the rule counts as the frame's work: `0` takes the CPU busy time, or pass the CPU's and the GPU's time
  if you measure it. A frame without `EndFrame` counts as presented when the next frame begins.
- A frame more than 30 s from the grid (after a pause) starts a new grid, and the window's frames from before it no longer count.
  `Reset()` does the same on purpose.

## The swap interval rule

The rule is Swappy's, the adaptive swap interval of Android's [frame pacing library](https://developer.android.com/games/sdk/frame-pacing)
(`SwappyCommon.cpp`), as [mb-framepacing-explained](https://github.com/Unarmed1000/mb-framepacing-explained) describes and simulates it
(`tools/frame_pacing_video/adaptive_rate.py`), with that repository's proposed fix as the default. Integer arithmetic only, so the C++
and C# pacers decide alike to the tick.

- **The window** holds the frames of the last `WindowTicks` since the last change of swap interval, and one frame beyond it. It is
  **full** when its oldest frame is more than `WindowTicks` older than its newest. Every change of swap interval empties it.
- **A frame's need**: the frames' average work plus `FrameMarginTicks`, in whole refreshes rounded up (a remainder of at most 500 ns does
  not need another refresh), at least 1.
- **Slow down**, only while the current swap interval is at most `SlowestFrameTicks + FrameMarginTicks` long, to the larger of one
  refresh more and the frames' need:
  - `SlowDownRule::FullWindow` (Swappy's rule): when the window is full and more than `SlowDownLatePercent` of its frames were late
    (the share rounded to a whole percent, a half to the even one).
  - `SlowDownRule::LateCount` (the fix, the default): the same, and also as soon as the late frames since the last change pass
    `SlowDownLatePercent` of the frames a full window holds at the current swap interval. Swappy's rule waits for a full window after
    every change, so a load that comes back right after a speed-up is late for a whole window first; the fix slows down after the
    late frames of one.
- **Speed up** (both rules) when the window is full, none of its frames was late, the swap interval is above the preferred one and the
  frames' average work plus twice the margin fits one refresh less: to the larger of the preferred swap interval and the frames' need.
- `AutoSwapInterval` off keeps the preferred swap interval: the pacer still plans every frame and counts late frames.

`SwapIntervalRule` is public: an application with a frame loop of its own can feed it frames (display time, work, late) and read the
swap interval it decides.

## Settings

`PacerSettings` is always valid: its constructor takes the refresh period, and every setter asserts that its value is within its range
(without asserts it clamps a value outside into the range; C# clamps). The rule's defaults are Swappy's; they are settings, not properties of frame
pacing in general.

| Setting                 | Default   | Range                     | What it is                                                                                                       |
| ----------------------- | --------- | ------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `Refresh`               | required  | 1 tick to 1 s             | The display's refresh period (`RefreshPeriod::FromRate`, `FromNanoseconds`, `FromTicks`)                         |
| `PreferredSwapInterval` | 1         | 1 to 100                  | The swap interval the application wants; the pacer never goes faster. Its refreshes are the preferred frame time |
| `AutoSwapInterval`      | on        |                           | Adapt the swap interval with the rule                                                                            |
| `SlowDown`              | LateCount | `LateCount`, `FullWindow` | When the rule slows down                                                                                         |
| `WindowTicks`           | 2 s       | 1 tick to 60 s            | How long a stretch of frames the rule looks at                                                                   |
| `SlowDownLatePercent`   | 10        | 0 to 100                  | The share of late frames the rule slows down beyond                                                              |
| `FrameMarginTicks`      | 1 ms      | 0 to 1 s                  | Added to the frames' average work before it is compared with swap intervals                                      |
| `SlowestFrameTicks`     | 50 ms     | 0 to 10 s                 | The rule slows down no further once the swap interval is longer than this plus the margin                        |
| `PresentLatencyTicks`   | 0         | 0 to 1 s                  | Without display feedback: how long after Present a frame can be shown at the earliest                            |
| `WindowCapacity`        | 0         | 0, or 2 to 1 048 576      | Frames the window holds; 0: enough for `WindowTicks` at the preferred swap interval, twice over                  |

`RefreshPeriod` is always valid too: from 1 tick (10 MHz) to 1 s (1 Hz), with no default. The application gives the pacer its
display's period. In C#, `default(RefreshPeriod)` is the one value that is not a period (`IsDefault`): the pacer's types throw an
`ArgumentException` for it, at setup.

## Filling the marker

| Marker field          | From                                                                                 |
| --------------------- | ------------------------------------------------------------------------------------ |
| Frame index           | Your own frame counter (the pacer's `FrameIndex` counts its frames from 0)           |
| Animation time        | `AnimationClock`'s `AnimationTicks`                                                  |
| Preferred frame time  | `schedule.PreferredFrameTicks`                                                       |
| Target frame time     | `schedule.TargetFrameTicks`                                                          |
| Intended display time | `schedule.IntendedDisplayTicks`                                                      |
| CPU start time        | `schedule.CpuStartTicks`                                                             |
| CPU busy              | `EndFrame`'s return value: call it as you draw the marker, last, just before Present |

[Filling the marker fields](marker-fields.md) says what the analysis does with each.

## AnimationClock

A frame is shown a whole number of refreshes after the previous one and should animate for the moment it is shown. `AnimationClock` is
the vsync timer of mb-framepacing-explained (`doc/frame-pacing-strategies.md`): it advances the animation time in whole refreshes.

- **With the pacer:** `Advance(schedule)` steps by the refreshes between this frame's intended display time and the previous frame's.
  After a late frame the next step includes the refresh it lost, so the animation catches up exactly.
- **Without a pacer**, in a loop paced by vsync: `AdvanceMeasured(wakeUpTicks, swapInterval)` rounds the time since the previous
  wake-up to whole refreshes (at least the previous swap interval) and corrects it for a change of swap interval. Rounding removes the
  wake-ups' jitter while it stays under half a refresh.
- The steps add up in 2⁻³² ticks, so the animation time never drifts from the refreshes. `Pause()` and `Resume()` hold it and go on
  without a jump; `maxStepRefreshes` limits a step (for a process that was suspended); `SetRefreshPeriod` follows a mode change.

## Tests and golden data

`sdk/test-data/pacer` holds the pacer's golden results, which the C++ and the C# tests must both reproduce to the byte. `python tools/update_pacer_test_data.py` regenerates them with `pacer-sim --golden`:

- `60-busy`: the busy stretch of mb-framepacing-explained's `60-busy-swappy` clip. Swappy's rule reproduces that simulation's swap
  intervals and display refreshes frame by frame.
- `60-busy-full-rate`: the `60-busy-full-rate` clip at a fixed swap interval: every frame is shown on the clip's refresh.
- `100-stages` and `60-relapse`: seeded staged loads. The fix is never late more often than Swappy's rule and never slows down later;
  on `100-stages` and `60-relapse` it is late less often.
