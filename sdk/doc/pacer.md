# The frame pacer (experimental)

> **Experimental.** The pacer has only run against its own simulation, never against a real swap chain. It is off by default
> (`MB_FRAMEPACING_BUILD_PACER`, Conan's `with_pacer`), its API may change in any release, and it is not what the marker and the tools
> need: they measure any pacer. See [Status](#status).

The pacer module paces a frame loop with nothing but a steady clock and a `Present` that waits for vsync: a baseline that works on any
platform, with the oldest graphics APIs too. It gives every frame the **swap interval** to hold it for, the **animation time** to render
it for, and what to write into the frame marker. It holds a **target frame rate**, and it adapts the swap interval to how the frames
do.

It is **values in, values out**: the application passes the time a frame starts and gets back a plan. The pacer calls no graphics or
platform API, has no callbacks and never reads a clock. Made once (it allocates its frame window then), it never allocates again.

| Language | Module                                                                                  |
| -------- | --------------------------------------------------------------------------------------- |
| C++20    | `mb_framepacing::pacer`, `<mb/framepacing/pacer/…>`, namespace `MB::FramePacing::Pacer` |

## Status

| Checked                                                                                              | Not checked                                              |
| ---------------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| Its simulation of a frame loop, against golden results (`sdk/test-data/pacer`)                       | Any real swap chain, on any platform or graphics API     |
| The swap intervals and refreshes of mb-framepacing-explained's simulation, frame by frame            | A real display's clock against a real CPU clock          |
| That repository's timing diagrams (the vsync timer, half rate, switching rates)                      | A compositor, a frame queue longer than one, a GPU limit |
| A simulated display 0.1 % off its nominal rate with 2 ms of jitter on every frame start, for an hour | Variable refresh, vsync off                              |
| Every line and branch of the module by its tests; no allocation per frame                            |                                                          |

It is here to be tried and measured (the marker and the tools exist for exactly that), not to be relied on.

## What it needs

- **A steady clock**, read by the application and passed in as a `TickCount64` (the core's time types: `TickCount64::FromNanoseconds`,
  `TickCount64::FromCounter(counter, frequency)`, or a `std::chrono` clock through `core/time/ChronoConversion.hpp`).
- **A loop paced by vsync**: vsync on, a fixed refresh rate, and a `Present` (or the wait for a free buffer) that waits for the
  display, so every frame starts when the previous one is shown.
- **The display's refresh period**: from the display mode, or a hard-coded value to start with.

Nothing else: no vsync timestamps, no presentation feedback, no scheduled presents. [Not used yet](#not-used-yet) lists what a newer
platform could add.

## The two clocks

A frame is timed by two clocks. The **CPU's clock** is the application's steady clock: the pacer is given the time a frame starts on
it. The **display's clock** counts refreshes: on a display with a fixed refresh rate, every frame is shown a whole number of refreshes
after the previous one.

The pacer measures on the first and counts on the second. The time from one frame start to the next, rounded to whole refreshes, is
how many refreshes the display moved on. Rounding removes the frame starts' jitter while it stays under half a refresh (8.3 ms at
60 Hz, 2.1 ms at 240 Hz), and every step is measured on its own, so a display that runs a little off its nominal rate never adds up to
a jump. The refreshes counted add up exactly (`RefreshTime`): an hour of 60 Hz frames is an hour to the tick.

## The frame loop

```cpp
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

// Once: the display's refresh period, from its display mode (here DXGI's 59.94 Hz as the output mode states it)
PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60'000, 1'001));
settings.SetPreferredFrameRate(30);                             // optional: a target frame rate
PC::FramePacer pacer(settings);

// Every frame
const PC::FrameSchedule schedule = pacer.BeginFrame(Now());     // your steady clock, as an FP::TickCount64
UpdateAndDraw(schedule.AnimationTime);                          // render the frame for this time
const FP::TimeSpan32 cpuBusy = pacer.EndFrame(Now());           // as you draw the marker, last, just before Present
DrawMarker(schedule, cpuBusy);
Present(schedule.SwapInterval);                                 // hold the frame for that many refreshes
```

`BeginFrame` returns a `FrameSchedule`:

| `FrameSchedule`       | What it is                                                                                                              |
| --------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `SwapInterval`        | Refreshes from the previous frame's display to this one's: hold the frame for it                                        |
| `AnimationTime`       | The frame's predicted display time on the display's clock: render the frame for it (the marker's)                       |
| `AnimationStep`       | The step from the previous frame's animation time: the frame's delta time                                               |
| `IntendedDisplayTime` | When the pacer aims for the frame to be shown, on your steady clock (the marker's)                                      |
| `TargetFrameTime`     | `SwapInterval` refreshes, as a time (the marker's)                                                                      |
| `PreferredFrameTime`  | The swap interval the application prefers, as a time (the marker's)                                                     |
| `Change`              | What the rule decided from the previous frame (`None`, `Slower`, `Faster`): this frame is the first at the new interval |

`EndFrame(presentTime, work)` takes the time the frame is presented and returns its CPU busy time for the marker. `work` is how long the
frame needed as the rule should count it (the CPU's and the GPU's time, if you measure it); leave it out and the CPU busy time counts. A
frame without `EndFrame` counts as presented when the next one begins.

### Applying the schedule

Use the first way your platform has:

| Way                     | What to do                                                                                                                                              |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Swap interval**       | Present with `SwapInterval`: DXGI's `SyncInterval`, `eglSwapInterval`, `wglSwapIntervalEXT`, `glXSwapIntervalEXT`, Unity's `QualitySettings.vSyncCount` |
| **Present it again**    | Where vsync holds a frame for one refresh only (core Vulkan's FIFO, core Wayland): present the finished frame `SwapInterval` times                      |
| **Sleep, then present** | Sleep until one refresh before `IntendedDisplayTime`, then present. A guess: the thread wakes a little late, differently every time                     |

### How a frame is paced

- **The previous frame is measured** when the next one starts: the time between the two frame starts, rounded to whole refreshes and
  at least the previous frame's swap interval, is how many refreshes after the frame before it it was shown. It was **late** when that
  is more than its swap interval.
- **The rule decides** this frame's swap interval from the frames it has seen ([below](#the-swap-interval-rule)).
- **The frame animates for its predicted display time**: the previous frame's display plus its own swap interval. A late frame shows
  a moment already past, and the frame after it catches up exactly: the lost refresh is in its step.
- **A pause.** A frame that starts longer after the previous one than the frame window is long (or than two frames, when that is
  longer), or before it, starts again: the frame from before the pause is not counted, the frame window is empty, the swap interval
  stays, and the animation goes on one swap interval instead of jumping. `Reset()` does the same on purpose, and goes back to the
  preferred swap interval.
- **A display mode change.** `SetRefreshPeriod` starts again on the new period, with an empty frame window, at the swap interval the
  application prefers there. The animation time goes on. The period the pacer already has changes nothing.

## A target frame rate

`PacerSettings::SetPreferredFrameRate(30)` (or `30'000, 1'001`, or `SetPreferredFrameTime` with a frame time) is the frame rate the
application wants. The pacer turns it into the swap interval that gives it on the display it runs on, and again when the refresh period
changes:

| Wanted | Display  | Swap interval | Frame rate                                      |
| ------ | -------- | ------------- | ----------------------------------------------- |
| 30 fps | 60 Hz    | 2             | 30 fps                                          |
| 30 fps | 120 Hz   | 4             | 30 fps                                          |
| 60 fps | 59.94 Hz | 1             | 59.94 fps (the display's)                       |
| 60 fps | 144 Hz   | 3             | 48 fps: never faster than asked (2 would be 72) |
| 90 fps | 60 Hz    | 1             | 60 fps (the display's)                          |

The rule is the frame time in whole refreshes, rounded up, with a twentieth of a refresh of slack, at least one. It is the rounding the
tools judge a target frame rate by, so the pacer aims where it is measured.

`SetPreferredSwapInterval` says it in refreshes instead ("half rate on any display" is 2). With both set, the slower of the two counts.
The result is the pacer's **fastest** rate: the rule only ever goes slower, and with `AutoSwapInterval` off it is a fixed, evenly paced
frame rate.

## The swap interval rule

The rule is the adaptive swap interval rule as [mb-framepacing-explained](https://github.com/Unarmed1000/mb-framepacing-explained) describes
and simulates it (`tools/frame_pacing_video/adaptive_rate.py`), with that repository's proposed fix as the default. Integer arithmetic on
whole ticks only, so every port decides alike.

- **The frame window** holds the frames of the last `FrameWindowLength` since the last change of swap interval, and one frame beyond it.
  It is **full** when its oldest frame is more than `FrameWindowLength` older than its newest (or when it holds all the frames it has
  room for). Every change of swap interval empties it.
- **A frame's need**: the frames' average work plus `FrameMargin`, in whole refreshes rounded up, at least 1.
- **Slow down**, only while the current swap interval is at most `SlowestFrameTime + FrameMargin` long, to the larger of one refresh
  more and the frames' need:
  - `SlowDownRule::FullWindow` (the full-window rule): when the frame window is full and more than `SlowDownLatePercent` of its frames
    were late (the share rounded to a whole percent, a half to the even one).
  - `SlowDownRule::LateCount` (the fix, the default): the same, and also as soon as the late frames since the last change pass
    `SlowDownLatePercent` of the frames a full frame window holds at the current swap interval. The full-window rule waits for a full
    window after every change, so a load that comes back right after a speed-up is late for a whole window first; the fix slows down
    after the late frames of one.
- **Speed up** (both rules) when the frame window is full, none of its frames was late, the swap interval is above the preferred one
  and the frames' average work plus twice the margin fits one refresh less: to the larger of the preferred swap interval and the
  frames' need.
- `AutoSwapInterval` off keeps the preferred swap interval: the pacer still paces every frame and counts late frames.

`SwapIntervalRule` is public: an application with a frame loop of its own can feed it frames (display time, work, late) and read the
swap interval it decides.

## Settings

`PacerSettings` is always valid: its constructor takes the refresh period, and every setter asserts that its value is within its range
(without asserts it clamps a value outside into the range). The rule's defaults are those of the simulation it reproduces; they are
settings, not properties of frame pacing in general.

| Setting                 | Default   | Range                     | What it is                                                                                  |
| ----------------------- | --------- | ------------------------- | ------------------------------------------------------------------------------------------- |
| `Refresh`               | required  | 100 µs to 1 s             | The display's refresh period (`RefreshPeriod::FromRate`, `FromNanoseconds`, `FromTimeSpan`) |
| `PreferredFrameTime`    | none      | 0 (none) to 10 s          | The target frame rate as a frame time (`SetPreferredFrameRate` takes a rate)                |
| `PreferredSwapInterval` | 1         | 1 to 100                  | The swap interval the application wants; the pacer never goes faster                        |
| `AutoSwapInterval`      | on        |                           | Adapt the swap interval with the rule                                                       |
| `SlowDown`              | LateCount | `LateCount`, `FullWindow` | When the rule slows down                                                                    |
| `FrameWindowLength`     | 2 s       | 1 tick to 60 s            | How long a stretch of frames the rule looks at; a longer gap between frames is a pause      |
| `SlowDownLatePercent`   | 10        | 0 to 100                  | The share of late frames the rule slows down beyond                                         |
| `FrameMargin`           | 1 ms      | 0 to 1 s                  | Added to the frames' average work before it is compared with swap intervals                 |
| `SlowestFrameTime`      | 50 ms     | 0 to 10 s                 | The rule slows down no further once the swap interval is longer than this plus the margin   |

`RefreshPeriod` is always valid too: from 100 µs (10 kHz) to 1 s (1 Hz), with no default. The application gives the pacer its display's
period.

## Filling the marker

| Marker field          | From                                                                                 |
| --------------------- | ------------------------------------------------------------------------------------ |
| Frame index           | Your own frame counter                                                               |
| Animation time        | `schedule.AnimationTime`                                                             |
| Preferred frame time  | `schedule.PreferredFrameTime`                                                        |
| Target frame time     | `schedule.TargetFrameTime`                                                           |
| Intended display time | `schedule.IntendedDisplayTime`                                                       |
| CPU start time        | The time you gave `BeginFrame`                                                       |
| CPU busy              | `EndFrame`'s return value: call it as you draw the marker, last, just before Present |

[Filling the marker fields](marker-fields.md) says what the analysis does with each.

The intended display time is the frame's start plus its swap interval. A frame starts a little after the previous one is shown, by the
time the thread takes to wake up, so it carries that jitter: without a vsync time from the platform the pacer knows no better.

## The pacer's animation clock alone

`PacerAnimationClock` is the part that measures and counts, for an application that decides its swap interval itself. It is not a
general animation timer: it has no speed and no pause, only the refreshes the display has shown.

- `Advance(frameStartTime, swapInterval)` gives the frame's `AnimationTime` (`Time`, `Step`, `StepRefreshes`).
- `Measure(frameStartTime)` and then `Step(swapInterval)` do the same in two steps, for a loop that decides the swap interval from the
  measurement: `FrameMeasurement` says how many refreshes after the frame before it the previous frame was shown, whether that was
  late, and when on the display's clock.
- It is given its longest gap (the pacer gives it the frame window's length): a longer one is a pause.

## Not used yet

The baseline takes nothing a platform may not have. What newer platforms offer, and what the pacer does not use yet
([roadmap](../../doc/roadmap.md)):

| Not used yet                                | Where it exists                                                                                                                | What it would improve                                                        |
| ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------- |
| Vsync times the platform reports            | DWM's `qpcVBlank`, Choreographer's frame time, `CADisplayLink`'s `timestamp`                                                   | The intended display time without the frame starts' jitter                   |
| Predicted display times                     | Choreographer's expected presentation time, OpenXR's `predictedDisplayTime`, `CADisplayLink`'s `targetTimestamp`               | The animation time the platform itself aims for                              |
| Presentation feedback                       | `VK_EXT_present_timing`, `VK_GOOGLE_display_timing`, DXGI frame statistics, Metal's `presentedTime`, Wayland presentation-time | Frames that were late although they were presented in time                   |
| Scheduled presents and per-frame targets    | `VK_EXT_present_timing`, `EGL_ANDROID_presentation_time`, Metal's `present(at:)`, Windows' `SetTargetTime`                     | Back at full rate a frame sooner after one slow frame; no sleep that guesses |
| The refresh period measured from the frames | Anywhere                                                                                                                       | A change of rate followed without being told; 59.94 Hz taken for 60          |
| Slewing against drift                       | Audio and display times in one system clock                                                                                    | Animation that stays in step with audio or a server over hours               |
| Variable refresh and vsync off              | G-SYNC, FreeSync, tearing presents                                                                                             | Pacing where there is no grid of refreshes to round to                       |

## Tests and golden data

`sdk/test-data/pacer` holds the pacer's golden results, which the tests must reproduce to the byte. `python tools/update_pacer_test_data.py`
regenerates them with `pacer-sim --golden` (built with the tests when the pacer is built: `-DMB_FRAMEPACING_BUILD_PACER=ON`):

- `60-busy`: the busy stretch of mb-framepacing-explained's `60-busy-adaptive` clip. The full-window rule reproduces that simulation's
  swap intervals and display refreshes frame by frame.
- `60-busy-full-rate`: the `60-busy-full-rate` clip at a fixed swap interval: every frame is shown on the clip's refresh.
- `100-stages` and `60-relapse`: seeded staged loads. The fix is never late more often than the full-window rule and never slows down
  later; on `100-stages` and `60-relapse` it is late less often.

The simulation's display runs exactly at the nominal rate. `PacerAnimationClock`'s tests add what it leaves out: a display off its rate,
jitter on every frame start, the clock's wrap.
