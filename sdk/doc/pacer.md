# The frame pacer (experimental)

> **Experimental.** The pacer is checked against its own simulation and, on one machine, against the display times a graphics
> driver reports: no capture of it on a real swap chain has been analysed with the tools yet. It is off by default (`MB_FRAMEPACING_BUILD_PACER`, Conan's `with_pacer`), its API may change in any release, and it is not
> what the marker and the tools need: they measure any pacer. See [Status](#status).

The pacer module paces a frame loop with nothing but a steady clock and a `Present` that waits for vsync: a baseline that works on any
platform, with the oldest graphics APIs too. It gives every frame the **swap interval** to hold it for, the **animation time** to render
it for, and what to write into the frame marker. It holds a **target frame rate**, and it adapts the swap interval to how the frames
do. Where the platform reports when frames were shown, it counts what the display did and aims the marker's intended display time
at a real refresh ([Present feedback](#present-feedback-optional), optional); it paces the same with it.

It is **values in, values out**: the application passes the time a frame starts and gets back a plan. The pacer calls no graphics or
platform API, has no callbacks and never reads a clock. Made once (it allocates its frame window then), it never allocates again while
it paces; only other settings that need a larger frame window do.

| Language | Module                                                                                  |
| -------- | --------------------------------------------------------------------------------------- |
| C++20    | `mb_framepacing::pacer`, `<mb/framepacing/pacer/…>`, namespace `MB::FramePacing::Pacer` |

## Status

| Checked                                                                                                                                                                                            | Not checked                                                                                                                                         |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Its simulation of a frame loop, against golden results (`sdk/test-data/pacer`)                                                                                                                     | Any real swap chain recorded by a capture card and analysed with the tools                                                                          |
| The swap intervals and refreshes of mb-framepacing-explained's simulation, frame by frame                                                                                                          | Any platform but Windows, any graphics API but Vulkan, any GPU vendor but one                                                                       |
| That repository's timing diagrams (the vsync timer, half rate, switching rates)                                                                                                                    | A real display's clock against a real CPU clock for longer than 20 seconds                                                                          |
| A simulated display 0.1 % off its nominal rate with 2 ms of jitter on every frame start, for an hour                                                                                               | Vsync off; variable refresh as something to pace (it is not: what G-SYNC does to the pacer is under [Present feedback](#present-feedback-optional)) |
| The refresh rates monitors have, 50 to 540 Hz: frames on time, a display off its rate, target frame rates, a load that comes and goes, a loop the GPU limits (simulated)                           |                                                                                                                                                     |
| Every line and branch of the module by its tests; no allocation per frame                                                                                                                          |                                                                                                                                                     |
| Present feedback: a simulated display that queues presents, with frame starts that wobble and feedback that is late, missing, refused or stops                                                     |                                                                                                                                                     |
| Present feedback: one present log of a real swap chain (Vulkan FIFO, 240 Hz, `VK_EXT_present_timing`), replayed, and its statistics in the running sample at 120 and 240 Hz                        | Any platform's feedback but that one driver's                                                                                                       |
| Work over a refresh in a loop no vsync holds: simulated, one frame log of a real swap chain replayed (Vulkan FIFO, 120 Hz, CPU work of 133 % of a refresh), and the running sample at 50 to 240 Hz | Work close to a whole refresh, which the rule does not settle ([The swap interval rule](#the-swap-interval-rule))                                   |
| Frames held for a fixed frame rate on one real swap chain (Vulkan FIFO, Windows, 50 to 240 Hz, a timer sleep and a wait on the vertical blank), by the driver's display times                      | The same recorded by a capture card and analysed with the tools                                                                                     |
| That swap chain in a window under the desktop compositor, with one and two frames in flight and GPU work of 90 % and 130 % of a refresh                                                            | A compositor, a frame queue and a GPU's limit on any other machine                                                                                  |

It is here to be tried and measured (the marker and the tools exist for exactly that), not to be relied on.

**A first integration.** This project's author's own, **unofficial** [gtec-demo-framework](https://github.com/Unarmed1000/gtec-demo-framework) has the pacer in its three
FramePacing samples, for [Vulkan](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/Vulkan/FramePacing), [OpenGL ES 3](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES3/FramePacing) and [OpenGL ES 2](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES2/FramePacing)
([its description](https://github.com/Unarmed1000/gtec-demo-framework/blob/master/Doc/FramePacing.md)): a frame loop with a target frame rate, the adaptive rule, a CPU and a GPU load to try them with, and
the marker filled from the schedule. There the pacer has paced Vulkan swap chains on Windows, where it held its targets by its own
count of late frames; the OpenGL ES samples have only run on an emulator whose swap is not locked to vsync. No capture of it has
been analysed with the tools, so the "not checked" column above stands. What that integration found is in this guide
([Applying the schedule](#applying-the-schedule), `EndFrame`'s work, [The swap interval rule](#the-swap-interval-rule) and
[Present feedback](#present-feedback-optional)). Its
present logs (the driver's times, not captures) show the pacer holding a swap interval of one by the frame starts alone at 23.98,
24, 25, 29.97, 60, 100, 120 and 240 Hz on an idle machine, with the frames starting within 0.2 ms of a refresh. On the same machine
busy with other work the frames started up to 3 ms off the refreshes, which the pacer read as late frames at 240 Hz. With work of
130 % of a refresh the pacer of that time never slowed down, at 120 and 240 Hz: the frames started 1.37 refreshes apart, which
rounds to one ([How a frame is paced](#how-a-frame-is-paced) has what changed).

**Its loop did not hold the frame start at a swap interval of one**, up to and including the two sessions below: that sample
waited for the pacer's `NextFrameStartTime` only where it held a frame for two refreshes or more, and trusted the present at one.
So what its logs of that time show of frames at a swap interval of one (frames never shown with work close to a refresh) is that
loop's, not the pacer's: [The frame loop](#the-frame-loop) says what an application must do. Since then the sample waits for the
pacer's times at every swap interval (by default it renders a frame at once and holds its present), and logs
`FrameWindowState::StartsAhead`. No capture of that loop is stored yet.

**Two capture sessions kept with their results.** 601 runs of that sample (Vulkan FIFO on Windows, displays at 50, 60, 120 and
240 Hz, an idle machine and one under CPU load) have the row per run, the tables and the charts worked out from their frame logs
in the repository: [Windows hold captures, 2026-10-04](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-hold.md) and
[its second session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-session2.md), which ran parts of the first again and corrected them. This guide quotes them as "the first session"
and "the second session". They are the display times a driver reports on one machine, not a capture of the display.

## What it needs

- **A steady clock**, read by the application and passed in as a `NanosecondTickCount` (the core's time types: nanoseconds as
  the platform gives them with `NanosecondTickCount::FromNanoseconds`, a clock that counts in ticks of 100 ns through
  `NanosecondTickCount::FromTickCount64`). Every time the pacer takes and gives is in nanoseconds: `NanosecondTickCount` for a
  point on the clock, `NanosecondTimeSpan` for a span, `NanosecondTimeDuration` for a length of time (a frame time, the CPU
  busy time), as the marker's payload takes them.
- **A loop paced by vsync**: vsync on, a fixed refresh rate, and frames that start a swap interval apart, on a refresh or close
  to one. A `Present` (or a wait for a free buffer) that waits for the display gives that by itself. Not every one does: on the
  first integration's Vulkan FIFO swap chain neither the present nor the acquire ever waited. So the application holds the next
  frame's start to the time the pacer gives it, always ([The frame loop](#the-frame-loop)). A machine busy with other work starts
  the frames less evenly: fine while they stay within half a refresh. Beyond it a
  frame on time reads as late; the statistics of [present feedback](#present-feedback-optional) show when that happens.
- **The display's refresh period**: from the display mode, or a hard-coded value to start with (not every window system reports
  it). Give it as exactly as the platform has it: `RefreshPeriod::FromRate(24002, 100)` for 240.02 Hz keeps the fraction of a
  nanosecond, and `FromNanosecondTimeSpan` takes a period in whole nanoseconds. Not from a swap chain's present timing without a check: on the first integration's machine the
  swap chain's refresh duration was that of the fastest display of the desktop (8.33 ms for a window on a 60 Hz or a 50 Hz
  display next to a 120 Hz one), while the window system gave the rate of the display the window was on. A present scheduled by
  that refresh was held twice as long.

Nothing else: no vsync timestamps, no scheduled presents. Present feedback is counted where the application gives it and never
needed. [Not used yet](#not-used-yet) lists what a newer platform could add.

## The two clocks

A frame is timed by two clocks. The **CPU's clock** is the application's steady clock: the pacer is given the time a frame starts on
it. The **display's clock** counts refreshes: on a display with a fixed refresh rate, every frame is shown a whole number of refreshes
after the previous one.

The pacer measures on the first and counts on the second. The time from one frame start to the next, rounded to whole refreshes, is
how many refreshes the display moved on. Rounding removes the frame starts' jitter while it stays under half a refresh (8.3 ms at
60 Hz, 2.1 ms at 240 Hz; beyond it a frame on time reads as late), and every step is measured on its own, so a display that runs
a little off its nominal rate never adds up to a jump. The refreshes counted add up exactly (`RefreshTime`): an hour of 60 Hz
frames is an hour to the tick. The one exception is a frame whose work took longer than its swap interval
([below](#how-a-frame-is-paced)).

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
const PC::FrameSchedule schedule = pacer.BeginFrame(Now());     // your steady clock, as an FP::NanosecondTickCount
UpdateAndDraw(schedule.AnimationTime);                          // render the frame for this time
const FP::NanosecondTimeDuration cpuBusy = pacer.EndFrame(Now()); // as you draw the marker, last, just before Present
DrawMarker(schedule, cpuBusy);
Present(schedule.SwapInterval);                                 // hold the frame for that many refreshes
WaitUntil(schedule.NextFrameStartTime);                         // yours: the next frame begins no earlier, at any swap interval
```

**Never begin a frame before the previous frame's `NextFrameStartTime`.** It is part of using the pacer, at every swap interval,
one included. Where the present waits for the display that time has passed when it returns, and the wait costs nothing. Where it
does not, this wait is the only thing that keeps the loop from running ahead of the display. The pacer can not do it for you: it
never waits and never reads a clock. And it can not make up for a wait that was left out: it counts a frame start that comes early
as the previous frame's swap interval, never less. [Applying the schedule](#applying-the-schedule) has what a loop without it did.

**The pacer shows a loop that leaves the wait out**: `pacer.FrameWindow().StartsAhead` is how far ahead of those times the frames
of the frame window (the last 2 s) began, added up, with the late frames left out. Put it in your overlay or log, and read it
while the frame window is full (`Full`): a window that is still filling holds the start of the run and little else. It is a check
of the loop; the rule does not use it.

| `StartsAhead`                   | What it says about the loop                                                                              |
| ------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Close to zero                   | It is in step with the display: the jitter of the frame starts cancels in the sum                        |
| A refresh or more               | It runs ahead of the display: nothing holds it, or the refresh period given is longer than the display's |
| Below zero by a refresh or more | It falls behind its times: a wait that wakes late every frame adds up                                    |

A sum, and not a count of the frames that began early, because a loop in step with the display begins about half its frames a few
microseconds early. Worked out for the 507 runs of the first integration's two capture sessions that have display times and a
fixed refresh rate (`startsAheadTicks` in their `runs.csv`): 446 were no more than a tenth of a refresh a second ahead and no more
than one behind, with no present never shown at the median; 13 were more than a refresh a second ahead, and those were the runs of
the loop that left the wait out with work close to a refresh (15 to 50 presents of 1000 never shown) and four runs of a loop at
twice the display's rate.

`BeginFrame` returns a `FrameSchedule`:

| `FrameSchedule`       | What it is                                                                                                                   |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `FrameId`             | The pacer's count of the frames it began, from 1: what [present feedback](#present-feedback-optional) names the frame by     |
| `SwapInterval`        | Refreshes from the previous frame's display to this one's: hold the frame for it                                             |
| `AnimationTime`       | The frame's predicted display time on the display's clock: render the frame for it (the marker's)                            |
| `AnimationStep`       | The step from the previous frame's animation time: the frame's delta time                                                    |
| `IntendedDisplayTime` | When the pacer aims for the frame to be shown, on your steady clock (the marker's)                                           |
| `NextFrameStartTime`  | The frame's start plus its swap interval, on your steady clock: the next frame begins no earlier than this (above)           |
| `TargetFrameTime`     | `SwapInterval` refreshes, as a time (the marker's)                                                                           |
| `PreferredFrameTime`  | The swap interval the application prefers, as a time (the marker's)                                                          |
| `Change`              | What the rule decided from the previous frame (`Unchanged`, `Slower`, `Faster`): this frame is the first at the new interval |

`EndFrame(presentTime, work)` takes the time the frame's work is done and returns its CPU busy time for the marker. Call it as you
draw the marker: before the present, and before any wait for the frame's time (the sleep below). A wait inside it would count as
work, and the rule would slow down. A frame without `EndFrame` counts as presented when the next one begins, and is never
judged by its work.

`work` is how long the frame needed as the rule should count it; leave it out and the CPU busy time counts. **An application that
the GPU limits must put the GPU's time into it** (a timer query; the time of the last frame that was measured will do). The work
is what tells the pacer that a frame did not fit its swap interval: where the swap chain takes the present at once the frame starts
do not show it ([How a frame is paced](#how-a-frame-is-paced)). And where late frames slow the pacer down without it, the work the
rule sees fits a refresh, so after a frame window without a late frame it speeds up again, is late again, and goes on like that.

### Applying the schedule

Use the platform's swap interval where it has one. Core Vulkan's FIFO and core Wayland hold a frame for one refresh only: there the
application holds the frame itself, in one of the other ways.

| Way                                           | What to do                                                                                                                                                                                           |
| --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Swap interval**                             | Present with `SwapInterval`: DXGI's `SyncInterval`, `eglSwapInterval`, `wglSwapIntervalEXT`, `glXSwapIntervalEXT`, Unity's `QualitySettings.vSyncCount`                                              |
| **Sleep, then present**                       | Sleep until one refresh before `NextFrameStartTime`, then present. Nothing ties the sleep to the display: measure it on your platform (below)                                                        |
| **Wait for the vertical blank, then present** | Where the platform has a wait for the display's vertical blank (DXGI's `WaitForVBlank`): wait for the one before the refresh aimed at, then present a share of a refresh before that refresh (below) |
| **Present it again**                          | Present the finished frame `SwapInterval` times. In Vulkan an image that was presented can not be presented again: draw or copy the frame into the next image. Not tried in any integration          |

**A platform's swap interval has a largest value.** DXGI's `SyncInterval` takes at most 4, and `eglSwapInterval` fails above the
config's `EGL_MAX_SWAP_INTERVAL`. A target of 30 fps on a 240 Hz display is a swap interval of 8: set the largest the platform takes
and wait for the rest, as below.

**A present delayed by a sleep is not paced by the display any more.** Where the swap chain has an image to spare the present
returns at once, and a loop that goes straight on runs faster than the swap interval says (a 30 fps target ran at 33 fps). The
hold of the next frame's start prevents that, as at every swap interval. The frame starts then follow your steady clock, not the
display's, and nothing keeps the present away from the moment the display takes a frame.

Hold to `NextFrameStartTime`, not to `IntendedDisplayTime`: without present feedback the two are equal, with it the intended
display time is the refresh the frame is shown on, the swap chain's queue included, and a loop that waited for that would run late.

**What a loop that does not hold the frame start did.** The first integration's sample left the hold out at a swap interval of
one, on a FIFO swap chain whose present and acquire returned at once in every frame. With light work that loop still ran at the
display's rate, a present reaching the display three to four refreshes later. With work close to a refresh it ran at the GPU's speed, a
little faster than the display, and frames were dropped. With the hold ([the second session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-session2.md), 240 Hz, a swap interval of
one):

| Work              | The frame start              | Presents never shown | Frames shown longer | Frame start to frame start |
| ----------------- | ---------------------------- | -------------------- | ------------------- | -------------------------- |
| 22 % of a refresh | Not held                     | 0                    | 3 of 2331           | 4.01 to 4.33 ms            |
| 22 % of a refresh | Held to `NextFrameStartTime` | 3                    | 0 of 2326           | 4.17 to 4.17 ms            |
| 95 % of a refresh | Not held                     | 92                   | 44 of 2147          | 3.83 to 8.41 ms            |
| 97 % of a refresh | Held to `NextFrameStartTime` | 5                    | 57 of 2321          | 4.17 to 4.68 ms            |

One run each, from a sample whose swap chain code is still being checked: a first look, not a result. The rows without the hold are what an application gets when it ignores
`NextFrameStartTime`, not what the pacer gives. Waiting for a fence on the acquire did as much for the heavy case as the hold (5
never shown, 6 shown longer); two frames in flight or a third swap chain image did not help.

How long a present takes to reach the display in those runs is not understood (about three refreshes in most, 3 ms in one), and
that sample's swap chain code is being checked for a fault. Nothing is concluded from it here until there is more data.

**What the sleep did when it was measured.** Mostly it held, and now and then it did not. With an idle machine and one display,
the sleep had these frames not shown for exactly their swap interval ([the second session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-session2.md)), next to a wait on the vertical
blank (DXGI's `WaitForVBlank` there):

| Display | Frame rates     | Sleep, idle | Sleep, loaded | Vertical blank wait, idle | Vertical blank wait, loaded |
| ------- | --------------- | ----------- | ------------- | ------------------------- | --------------------------- |
| 240 Hz  | 120, 60, 30 fps | 4 of 1993   | 19 of 1993    | 5 of 1991                 | 10 of 1991                  |
| 60 Hz   | 30, 20 fps      | 12 of 712   | 0 of 712      | 2 of 712                  | 3 of 712                    |
| 50 Hz   | 25 fps          | 0 of 331    | 0 of 331      | 0 of 331                  | 11 of 331                   |

But a run can land on the wrong side of a refresh for a stretch: 123 of 2327 frames a refresh early or late at 120 fps on 240 Hz
in one idle run of that session, and from 1 % to 35 % in earlier logs, while [the first session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-hold.md) had none at all on an idle
machine. Nothing ties the sleep to the display, the pacer can not see it, and none of the sessions explains when it happens. A
platform that takes a time for the present does better: the scheduled present of `VK_EXT_present_timing` had 3 of 2329 frames
off in the round where the sleep had 123 ([Not used yet](#not-used-yet)).

**A wait on the vertical blank** in place of the sleep held a frame no better (the table), and its frame starts spread by 0.1 ms
to each side where the sleep's are flat. Where you use one, the place of the present before the refresh you aim at matters, and
no place was safe in every run: at 240 Hz one stretch was bad (85 % of a refresh before it on one power plan, 75 to 85 % on the
other, 5 to 45 % with a second display on) and the rest clean on an idle machine; at 60 and 50 Hz most places had a frame or two
off, and 95 % the most. On a display with a variable refresh rate the wait holds no frame, as the vertical blank follows the
frames; the sleep keeps its frame starts there.

### How a frame is paced

- **The previous frame is measured** when the next one starts: the time between the two frame starts, rounded to whole refreshes and
  at least the previous frame's swap interval, is how many refreshes after the frame before it it was shown. It was **late** when that
  is more than its swap interval, or when its work (what `EndFrame` was given) took longer than its swap interval's time.
- **A frame that worked over its time is not held by vsync.** A swap chain with a buffer to spare (triple buffering; the first
  integration's Vulkan swap chain with two images does it too) takes the present at once, so a frame that worked 1.3 refreshes is
  followed by one that starts 1.3 refreshes later, not 2. Rounded on its own, every such frame would count one refresh: never late,
  the rule never slowing down, and the animation at 73 % of real time. So such a frame is late by its work, and the time between
  the frame starts counts as real time: what rounding it to whole refreshes leaves is carried to the next such frame, and their
  refreshes add up to the time that passed. On a swap chain that does wait, the next frame starts two whole refreshes later and
  nothing is left to carry. A frame whose work fits is rounded on its own, as above: the pacer has no grid of its own. The
  application gives nothing new for this; the work is `EndFrame`'s.
- **The rule decides** this frame's swap interval from the frames it has seen ([below](#the-swap-interval-rule)).
- **The frame animates for its predicted display time**: the previous frame's display plus its own swap interval. A late frame shows
  a moment already past, and the frame after it catches up exactly: the lost refresh is in its step.
- **A pause.** A frame that starts longer after the previous one than the frame window is long (or than two frames, when that is
  longer), or before it, starts again: the frame from before the pause is not counted, the frame window is empty, the swap interval
  stays, and the animation goes on one swap interval instead of jumping. `Reset()` does the same on purpose, and goes back to the
  preferred swap interval. An application that pauses its animation and keeps presenting frames needs neither: the pacer goes on
  pacing (and the marker's static flag says that nothing moves).
- **A display mode change.** `SetRefreshPeriod` starts again on the new period, with an empty frame window, at the swap interval the
  application prefers there. The animation time goes on. The period the pacer already has changes nothing.
- **Other settings.** `SetSettings` changes them on a live pacer (another target frame rate, the rule switched off, another
  margin): take `pacer.Settings()`, change it, and give it back. The pacer starts again with them, with an empty frame window, at
  the swap interval the application prefers; the animation time goes on, and a frame that is open is ended as usual. The same
  settings change nothing, so it can be called every frame with the application's current ones. It allocates only when the frame
  window needs more room than it has (a longer window, a faster display or frame rate). `pacer.Settings()` always has the refresh
  period the pacer is on.

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

### The frame rates a display can show

Not every frame rate can be hit: a display with a fixed refresh rate shows a frame for a whole number of refreshes, so its frame
rates are its own divided by 1, 2, 3 and so on. An application that lets its user choose one lists those
(`pacer/FrameRateStepUtil.hpp`, functions of the refresh period alone, no pacer needed):

```cpp
namespace Steps = MB::FramePacing::Pacer::FrameRateStepUtil;

for (uint32_t swapInterval = 1; swapInterval <= Steps::StepCount(refresh); ++swapInterval)
{
  const PC::FrameRateStep step = Steps::StepAt(refresh, swapInterval);
  AddMenuItem(step.RateMillihertz, step.SwapInterval);          // 59'940 is 59.94 frames a second
}
settings.SetPreferredSwapInterval(chosen.SwapInterval);         // or SetPreferredFrameTime(chosen.FrameTime)
```

| Display  | Its steps, in frames a second                             |
| -------- | --------------------------------------------------------- |
| 60 Hz    | 60, 30, 20                                                |
| 59.94 Hz | 59.94, 29.97, 19.98                                       |
| 120 Hz   | 120, 60, 40, 30, 24, 20                                   |
| 144 Hz   | 144, 72, 48, 36, 28.8, 24, 20.57                          |
| 240 Hz   | 240, 120, 80, 60, 48, 40, 34.29, 30, 26.67, 24, 21.82, 20 |

- A `FrameRateStep` is a swap interval, its frame time to the nanosecond, and its rate in millihertz (the nearest; a number
  to show). The list ends at 20 frames a second (`SlowestFrameTime`, 50 ms, judged with the same twentieth of a refresh of
  slack: 19.98 is the last step of a 59.94 Hz display). A display slower than 20 Hz has its own rate and nothing else.
- `StepFor(refresh, frameTime)` and `StepForRate(refresh, 50)` give the step a frame rate becomes: the pacer's own rounding,
  so 50 frames a second at 60 Hz is the step of 30.
- `IsStep` and `IsStepRate` say whether a rate is one of the display's steps: 50 is none at 60 Hz, 24 is none at 60 Hz and
  one at 120, 144 and 240 Hz, and a rate slower than 20 frames a second is none. The pacer still paces a slower rate when
  it is asked for one: the steps are what to offer, not a limit on the settings.
- Fixed refresh rates only, as the pacer: on a display with a variable refresh rate any rate in its range can be shown.

## The swap interval rule

The rule is the adaptive swap interval rule as [mb-framepacing-explained](https://github.com/Unarmed1000/mb-framepacing-explained) describes
and simulates it (`tools/frame_pacing_video/adaptive_rate.py`), with that repository's proposed fix as the default. Integer arithmetic on
whole nanoseconds only, so every port decides alike.

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

**Work close to a whole refresh is decided by the run.** The rule slows down on late frames, and with work a little under a refresh
whether enough frames are late is not settled by anything in it. With GPU work of 94 to 96 % of a refresh at 240 Hz the pacer
counted 28 to 38 late frames of the 48 it slows down beyond and stayed at one refresh in four runs of four
([the first session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-hold.md)). With work of 88 to 89 % it went to two refreshes in sixteen runs of sixteen, after 0.6 to 3.2 s
([the second session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-session2.md)). All of those runs are of a loop that did not hold its frame start at a swap interval of one
([Applying the schedule](#applying-the-schedule)), so the frames never shown in them are that loop's. The one run of the kind
with the hold went to two refreshes after 150 frames, its twin without it after 144; more than that has not been captured. At
120, 60 and 50 Hz the same share of work fitted. With work of 130 % the rule goes to two refreshes after 0.2 s at every one of
those rates, and stays.

`SwapIntervalRule` is public: an application with a frame loop of its own can feed it frames (display time, work, late) and read the
swap interval it decides.

## Present feedback (optional)

Some platforms report when a frame was shown, a few frames after it was presented. Where the application passes that on, the pacer
counts what the display did and aims the intended display time at a refresh of the display. **It is statistics only: the pacer
paces exactly as without it**, by the frame starts and the frames' work. It is off by default.

**What it is for.**

- **Seeing what the display did**, next to what the pacer read. `pacer.FeedbackState()` counts the refreshes the display fell
  behind and the frames it never showed; `pacer.FrameWindow()` has the pacer's own count of late frames. They differ where the
  frame starts stop measuring the display. The first integration's Vulkan FIFO swap chain started its frames within 0.2 ms of a
  refresh (5 % to 95 %) at every fixed refresh rate from 23.98 to 240 Hz on an idle machine. On the same machine busy with other
  work (other programs were being built and tested), at 240 Hz, the display still showed 1953 of 1955 frames one refresh (4.17 ms)
  apart, while the frame starts were 1.3 to 7.0 ms apart. That is more than half a refresh off, so by their starts 214 of 1999
  frames read as late; the display times say two refreshes were lost. That log is in the tests
  (`sdk/test-data/pacer/240-vulkan-present-log.csv`).
- **An intended display time that is a refresh of the display**, for the marker: the analysis then judges the frames against
  where they really were due.

**Why it does not pace.** A first version measured the frames by their display times and not by their starts. In the first
integration's sample that changed nothing with work of 20 % and of 130 % of a refresh (120 and 240 Hz, an idle machine and one
under CPU load), it reacted two to four frames later (the display times come that long after their frames), and with work of 90 %
at 240 Hz, right at the rule's threshold, it counted a frame that was never shown and the late frame after it as two where the
frame starts count one. So the rule has one input on every platform, and pacing by display times is a possible upgrade
([Not used yet](#not-used-yet)).

```cpp
PC::PacerSettings settings(refreshPeriod);                          // from the display mode, as without feedback
settings.SetUsePresentFeedback(true);
PC::FramePacer pacer(settings);

// Every frame, before BeginFrame: what the platform has measured since the last frame, oldest first
for (const PresentTiming& timing : ReadPresentTimings())           // yours: the platform's results
{
  pacer.AddPresentFeedback(PC::PresentFeedback::Shown(timing.FrameId, timing.DisplayTime, timing.PresentTime));
}
const PC::FrameSchedule schedule = pacer.BeginFrame(Now());
RememberFrameId(schedule.FrameId);                                  // yours: the platform's id for this present -> schedule.FrameId
```

`PresentFeedback::Shown(frameId, displayTime, presentTime)` is a frame that was shown: the start of its first refresh, and the time
it was presented (the present call, or the time the platform took it over), both on the clock `BeginFrame` gets. The present time
can be left out; the time `EndFrame` was given counts then, which is too early for **an application that waits between `EndFrame`
and its present: it must pass the present time**. `PresentFeedback::NotShown(frameId)` is a frame the platform says was never shown.
A frame the platform reports nothing for gets no feedback. A result without a display time is one or
the other by platform: on the first integration's driver (`VK_EXT_present_timing`) such a result, reported as complete, was a frame
replaced before the display took it (across each the display moved on two or three refreshes), so the sample gives it as not
shown.

**The statistics** (`pacer.FeedbackState()`, counted since the pacer was made):

| `PresentFeedbackState` | What it counts                                                                                     |
| ---------------------- | -------------------------------------------------------------------------------------------------- |
| `Used`                 | Display times that were counted from                                                               |
| `Refused`              | Feedback that can not be a refresh of the display (below)                                          |
| `NotShown`             | Frames the platform reported as never shown                                                        |
| `Missing`              | Frames no feedback came for: feedback for a newer frame came first, or the frame got 64 frames old |
| `LateRefreshes`        | Refreshes the display fell behind the frames' swap intervals: what late frames cost on the display |

`LateRefreshes` is the whole refreshes between two display times beyond the swap intervals of the frames from one to the other.
A frame held a refresh longer right after one that was shown a refresh sooner lost none, and is not counted: a loop paced by
sleeping lands a present on the wrong side of a refresh now and then. A frame with no feedback is counted across by the next
display time.

**The intended display time is a refresh of the display**: the newest display time plus the swap intervals of the frames since,
this one included. It has the swap chain's queue in it and none of the frame starts' wobble. It is unknown (0, as the marker has
it) until the first display time comes, again after a restart, and when no display time has come for 64 frames. The display times
come a few frames late, so the frames begun between a late frame and its display time are aimed as if it had not been late.

**Everything else is the baseline's**: the swap interval, the animation time and its step, `NextFrameStartTime`, the rule's frame
window and when a frame is late.

**Feedback is refused** when it can not be a refresh of the display:

- a display time before the frame's present;
- a display time that is not a whole number of refreshes, within an eighth of one, after the display time used before it. When the
  next one is a whole number of refreshes after a refused one, the count starts again there (a new swap chain, a mode change);
- feedback for a frame the pacer does not keep (more than 64 frames old, or from before a pause, `Reset`, `SetRefreshPeriod` or
  `SetSettings`), or not newer than the feedback before it.

Nearly everything refused means a wrong refresh period or **a display with a variable refresh rate** (G-SYNC, FreeSync), which
refreshes when a frame arrives and has no grid of refreshes to count in. The pacer needs a fixed refresh rate with or without
feedback; switch feedback off there. The first integration's logs with G-SYNC on had display times on no grid, some before their
present. In [the first session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-hold.md), of the 597 display times a run gave the pacer it could count from 5 to 9; the other 588 to 592 were
refused (of 1195 in the longer runs, 1186 to 1190). The swap chain went on reporting the fixed refresh of the mode all the while.
So nearly every display time refused is a sign of a variable refresh rate that needs no query of the platform
([A variable refresh rate](#a-variable-refresh-rate) has the queries).

**What it does not do.** It does not pace, it does not steady the swap chain's queue or the latency, and it does not make a sleep
land on the right refresh: the last two need the platform to take a time for the present ([Not used yet](#not-used-yet)).

Where a display time comes from. Only the first has been looked at, and only on one driver:

| Platform                           | The display time                                              | The frame's name there          |
| ---------------------------------- | ------------------------------------------------------------- | ------------------------------- |
| `VK_EXT_present_timing`            | The first pixel out (or first pixel visible) stage's time     | The present id you set          |
| `VK_GOOGLE_display_timing`         | `actualPresentTime`                                           | The `presentID` you set         |
| Wayland presentation-time          | `presented`'s time; `discarded` is a frame that was not shown | One feedback object per commit  |
| `EGL_ANDROID_get_frame_timestamps` | `EGL_DISPLAY_PRESENT_TIME_ANDROID`                            | `eglGetNextFrameIdANDROID`'s id |
| Metal                              | `MTLDrawable.presentedTime` (0: not shown)                    | The drawable                    |

The times must be on the clock `BeginFrame` gets: convert them where the platform has another clock (Vulkan's calibrated
timestamps).

## A variable refresh rate

The pacer needs a fixed refresh rate. A display with a variable one (G-SYNC, FreeSync, HDMI VRR) refreshes when a frame arrives, so
there are no whole refreshes to count in. What the pacer does there, as [the first session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-hold.md) saw it with G-SYNC on:

- **A fixed frame rate held by a timer sleep still holds**: the frame starts are as even as with a fixed refresh rate, and at 80,
  60 and 30 fps on a 240 Hz display every frame was shown a swap interval after the one before.
- **A wait on the vertical blank holds no frame**: the vertical blank follows the frames.
- **Present feedback is refused**, nearly all of it ([above](#present-feedback-optional)).
- **The pacer does not use what the display offers**: with work over a refresh it goes to every second refresh, where such a
  display could show each frame for as long as it took. Pacing by a frame time that is no multiple of the refresh is a possible
  upgrade ([Not used yet](#not-used-yet)).

**How an application can tell.** There is no way that works everywhere. Only the first of these has been seen here; the others
are as their documentation has them:

| Where                           | What to ask                                                                                                                                                                                           | What it says                                                                                                                                                                                                          |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Vulkan, `VK_EXT_present_timing` | `VkSwapchainTimingPropertiesEXT`: `refreshInterval` equal to `refreshDuration` is a fixed refresh rate, `UINT64_MAX` a variable one (`refreshDuration` is then the shortest refresh), zero is unknown | The swap chain's own answer. The first integration's driver gave the two as equal in every read, also while the driver itself had variable refresh enabled for the display, so check it against the measurement below |
| Windows, NVIDIA                 | NVAPI's `NvAPI_Disp_GetVRRInfo` for a display                                                                                                                                                         | Whether it is enabled, and whether the display is in variable refresh mode now                                                                                                                                        |
| Windows, AMD                    | ADLX's `IADLXDisplayFreeSync`: `IsSupported`, `IsEnabled`                                                                                                                                             | Whether FreeSync is supported and enabled on a display                                                                                                                                                                |
| Windows, DXGI                   | `CheckFeatureSupport` with `DXGI_FEATURE_PRESENT_ALLOW_TEARING`                                                                                                                                       | That the system can do it, not that it is on                                                                                                                                                                          |
| Linux, KMS                      | The connector's `vrr_capable` and the CRTC's `VRR_ENABLED` properties                                                                                                                                 | Exact, for the program that owns the display: a compositor, or an application that runs without one                                                                                                                   |
| Wayland                         | The `refresh` of presentation-time's `presented` event                                                                                                                                                | Zero when the output has no constant refresh rate (from version 2 of the protocol it may be a rate the compositor picked)                                                                                             |

**Or measure it**, which needs no such query. Both signs showed within a second in those sessions:

- the display times the platform reports are not whole refreshes apart (what present feedback's `Refused` counts);
- a wait on the vertical blank comes back at uneven times, a few refreshes apart, instead of every refresh. [The second
  session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-session2.md) measured exactly that (the median time between the last 64 vertical blanks) and it agreed with the driver's own
  state in every run: one refresh apart with a fixed rate, two at 120 fps with variable refresh enabled.

Two limits, from the same runs. A frame loop at the display's own rate can not be told from a fixed refresh rate this way. And
the driver's setting is not the answer either: set to "full screen only", the driver enabled variable refresh for a window in
three runs of ten and not in the other seven.

## Settings

`PacerSettings` is always valid: its constructor takes the refresh period, and every setter asserts that its value is within its range
(without asserts it clamps a value outside into the range). The rule's defaults are those of the simulation it reproduces; they are
settings, not properties of frame pacing in general.

| Setting                 | Default                              | Range                     | What it is                                                                                    |
| ----------------------- | ------------------------------------ | ------------------------- | --------------------------------------------------------------------------------------------- |
| `Refresh`               | required                             | 100 µs to 1 s             | The display's refresh period (`RefreshPeriod::FromRate`, `FromNanosecondTimeSpan`)            |
| `PreferredFrameTime`    | none                                 | 0 (none) to 10 s          | The target frame rate as a frame time (`SetPreferredFrameRate` takes a rate)                  |
| `PreferredSwapInterval` | 1                                    | 1 to 100                  | The swap interval the application wants; the pacer never goes faster                          |
| `AutoSwapInterval`      | on                                   |                           | Adapt the swap interval with the rule                                                         |
| `SlowDown`              | LateCount                            | `LateCount`, `FullWindow` | When the rule slows down                                                                      |
| `FrameWindowLength`     | 2 s                                  | 1 tick to 60 s            | How long a stretch of frames the rule looks at; a longer gap between frames is a pause        |
| `SlowDownLatePercent`   | 10                                   | 0 to 100                  | The share of late frames the rule slows down beyond                                           |
| `FrameMargin`           | 1 ms, at most an eighth of a refresh | 0 to 1 s                  | Added to the frames' average work before it is compared with swap intervals                   |
| `SlowestFrameTime`      | 50 ms                                | 0 to 10 s                 | The rule slows down no further once the swap interval is longer than this plus the margin     |
| `UsePresentFeedback`    | off                                  |                           | Take the display times given, for statistics ([Present feedback](#present-feedback-optional)) |

`RefreshPeriod` is always valid too: from 100 µs (10 kHz) to 1 s (1 Hz), with no default. The application gives the pacer its display's
period.

**`FrameMargin` follows the display by default.** The rule speeds up only when the frames' average work plus twice the margin fits
one refresh less. A margin of 1 ms is a small share of a 60 Hz refresh and half of a 500 Hz one: twice that is a whole refresh at
500 Hz and leaves 0.08 ms for the work at 480 Hz, so a pacer that had slowed down to every second refresh would stay there, and
with work of a tenth of a refresh it already would above 450 Hz. So the
default is 1 ms and at most an eighth of the refresh period (less than 1 ms above 125 Hz: 0.87 ms at 144 Hz, 0.52 ms at 240 Hz,
0.25 ms at 500 Hz), and it follows a change of the refresh period. A margin you set with `SetFrameMargin` is that margin on every
display (`FrameMarginAt(refresh)` says what it is on one).

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
time the thread takes to wake up, so it carries that jitter: without a time from the platform the pacer knows no better. With
[present feedback](#present-feedback-optional) it is counted from a display time the platform measured, and is unknown (0) while
there is none: the marker's 0, which the analysis reads as "no schedule for this frame".

## The pacer's refresh clock alone

`PacerRefreshClock` is the part that measures the frame starts and counts the display's refreshes, for an application that decides
its swap interval itself. It is not the application's animation clock: it has no speed and no pause, only the refreshes the display
has shown.

- `Advance(frameStartTime, swapInterval)` gives the frame's `AnimationTime` (`Time`, `Step`, `StepRefreshes`).
- `Measure(frameStartTime, work)` and then `Step(swapInterval)` do the same in two steps, for a loop that decides the swap interval
  from the measurement: `FrameMeasurement` says how many refreshes after the frame before it the previous frame was shown, whether
  that was late (by those refreshes, or by its work where that is given), and when on the display's clock.
- It is given its longest gap (the pacer gives it the frame window's length): a longer one is a pause.

`FramesInFlight` is the part behind [present feedback](#present-feedback-optional), for the same application: it keeps the frames
that were begun (`Begin`, `End`), takes each one's display time (`Add`), and has the statistics (`State`) and the newest frame's
`IntendedDisplayTime`.

## Not used yet

The baseline takes nothing a platform may not have, and of what newer platforms offer the pacer takes one thing when it is given:
[present feedback](#present-feedback-optional), which it counts and does not pace by. What it does not use yet; each is a possible
upgrade on the [roadmap](https://github.com/Unarmed1000/mb-framepacing/blob/master/doc/roadmap.md), as is a C# port:

| Not used yet                                    | Where it exists                                                                                                  | What it would improve                                                                                        |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Vsync times the platform reports                | DXGI's vertical blank wait, Choreographer's frame time, `CADisplayLink`'s `timestamp`                            | The intended display time without the frame starts' jitter                                                   |
| Predicted display times                         | Choreographer's expected presentation time, OpenXR's `predictedDisplayTime`, `CADisplayLink`'s `targetTimestamp` | The animation time the platform itself aims for                                                              |
| Scheduled presents and per-frame targets        | `VK_EXT_present_timing`, `EGL_ANDROID_presentation_time`, Metal's `present(at:)`, Windows' `SetTargetTime`       | Back at full rate a frame sooner after one slow frame; the present placed by the platform                    |
| Pacing by the display times of present feedback | The platforms of [present feedback](#present-feedback-optional)                                                  | Late frames as the display had them where the frame starts are uneven: a busy machine at a high refresh rate |
| The refresh period measured from the frames     | Anywhere                                                                                                         | A change of rate followed without being told; 59.94 Hz taken for 60                                          |
| Slewing against drift                           | Audio and display times in one system clock                                                                      | Animation that stays in step with audio or a server over hours                                               |
| Variable refresh and vsync off                  | G-SYNC, FreeSync, tearing presents                                                                               | Pacing by a frame time that is no multiple of the refresh                                                    |

Measured so far, on one machine ([the first session](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-hold.md), [the second](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-session2.md)):

- **A wait on the vertical blank** the platform reports held a frame for its swap interval no better than the timer sleep did.
  The desktop compositor's time (DWM's) was that of the fastest display: wrong for a window on a slower one.
- **The scheduled present** of `VK_EXT_present_timing` held a frame right where every display of the desktop had the rate of the
  window's display, and was the steadier of the two at 240 Hz. With a faster second display it held every frame twice as long,
  because the swap chain reports that display's refresh.

## Tests and golden data

`sdk/test-data/pacer` holds the pacer's golden results, which the tests must reproduce to the byte. `python tools/update_pacer_test_data.py`
regenerates them with `pacer-sim --golden` (built with the tests when the pacer is built: `-DMB_FRAMEPACING_BUILD_PACER=ON`):

- `60-busy`: the busy stretch of mb-framepacing-explained's `60-busy-adaptive` clip. The full-window rule reproduces that simulation's
  swap intervals and display refreshes frame by frame.
- `60-busy-full-rate`: the `60-busy-full-rate` clip at a fixed swap interval: every frame is shown on the clip's refresh.
- `100-stages` and `60-relapse`: seeded staged loads. The fix is never late more often than the full-window rule and never slows down
  later; on `100-stages` and `60-relapse` it is late less often.

The simulation's display runs exactly at the nominal rate. `PacerRefreshClock`'s tests add what it leaves out: a display off its rate,
jitter on every frame start, the clock's wrap.

`240-vulkan-present-log.csv` is not written by `pacer-sim`: it is a present log of the first integration's Vulkan sample, not paced,
on a 240 Hz display with a fixed refresh rate and a machine busy with other work (1999 frames: when each frame started, was
presented and was shown, and the frame in which the application read that, all in ticks of 100 ns as it was recorded; the tests make nanoseconds of them). The [present feedback](#present-feedback-optional) tests pace it without and
with the display times and pin that the pacing is the same (214 frames late by their starts) and that the statistics count the two
refreshes the display lost.

`120-vulkan-work-130-log.csv` is a frame log of the same sample on a 120 Hz display with a fixed refresh rate and an idle machine,
with CPU work of 11.1 ms a frame (133 % of a refresh): 1500 frames, when each started, when its work ended, the work, and when it
was shown. The frame starts are 1.37 refreshes apart and the display showed 550 of 1495 frames for two refreshes. The tests pin
that every frame is late, that the animation's refreshes are the time that passed, and the frame the rule slows down at.
