# The frame pacer (experimental)

> **Experimental.** The pacer is checked against its own simulation only: no capture of it on a real swap chain has been analysed
> yet. It is off by default (`MB_FRAMEPACING_BUILD_PACER`, Conan's `with_pacer`), its API may change in any release, and it is not
> what the marker and the tools need: they measure any pacer. See [Status](#status).

The pacer module paces a frame loop with nothing but a steady clock and a `Present` that waits for vsync: a baseline that works on any
platform, with the oldest graphics APIs too. It gives every frame the **swap interval** to hold it for, the **animation time** to render
it for, and what to write into the frame marker. It holds a **target frame rate**, and it adapts the swap interval to how the frames
do. Where the platform reports when frames were shown, it can measure them by that instead of by their starts
([Present feedback](#present-feedback-optional), optional).

It is **values in, values out**: the application passes the time a frame starts and gets back a plan. The pacer calls no graphics or
platform API, has no callbacks and never reads a clock. Made once (it allocates its frame window then), it never allocates again while
it paces; only other settings that need a larger frame window do.

| Language | Module                                                                                  |
| -------- | --------------------------------------------------------------------------------------- |
| C++20    | `mb_framepacing::pacer`, `<mb/framepacing/pacer/…>`, namespace `MB::FramePacing::Pacer` |

## Status

| Checked                                                                                                                                                                  | Not checked                                                     |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------- |
| Its simulation of a frame loop, against golden results (`sdk/test-data/pacer`)                                                                                           | Any real swap chain, on any platform or graphics API            |
| The swap intervals and refreshes of mb-framepacing-explained's simulation, frame by frame                                                                                | A real display's clock against a real CPU clock                 |
| That repository's timing diagrams (the vsync timer, half rate, switching rates)                                                                                          | A compositor, a frame queue longer than one, a real GPU's limit |
| A simulated display 0.1 % off its nominal rate with 2 ms of jitter on every frame start, for an hour                                                                     | Variable refresh, vsync off                                     |
| The refresh rates monitors have, 50 to 540 Hz: frames on time, a display off its rate, target frame rates, a load that comes and goes, a loop the GPU limits (simulated) |                                                                 |
| Every line and branch of the module by its tests; no allocation per frame                                                                                                |                                                                 |
| Present feedback: a simulated display that queues presents, with frame starts that wobble and feedback that is late, missing, refused or stops                           | Present feedback in a running application, on any platform      |
| Present feedback: one present log of a real swap chain (Vulkan FIFO, 240 Hz, `VK_EXT_present_timing`), replayed                                                          | Any platform's feedback but that one driver's                   |
| Work over a refresh in a loop no vsync holds: simulated, and one frame log of a real swap chain (Vulkan FIFO, 120 Hz, CPU work of 133 % of a refresh), replayed          | That rule in a running application                              |

It is here to be tried and measured (the marker and the tools exist for exactly that), not to be relied on.

**A first integration.** This project's author's own, **unofficial** [gtec-demo-framework](https://github.com/Unarmed1000/gtec-demo-framework) has the pacer in its three
FramePacing samples, for [Vulkan](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/Vulkan/FramePacing), [OpenGL ES 3](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES3/FramePacing) and [OpenGL ES 2](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES2/FramePacing)
([its description](https://github.com/Unarmed1000/gtec-demo-framework/blob/master/Doc/FramePacing.md)): a frame loop with a target frame rate, the adaptive rule, a CPU and a GPU load to try them with, and
the marker filled from the schedule. There the pacer has paced Vulkan swap chains on Windows, where it held its targets by its own
count of late frames; the OpenGL ES samples have only run on an emulator whose swap is not locked to vsync. No capture of it has
been analysed with the tools, so the "not checked" column above stands. What that integration found is in this guide
([Applying the schedule](#applying-the-schedule), `EndFrame`'s work, and [Present feedback](#present-feedback-optional)). Its
present logs (the driver's times, not captures) show the pacer holding a swap interval of one by the frame starts alone at 23.98,
24, 25, 29.97, 60, 100, 120 and 240 Hz on an idle machine, with the frames starting within 0.2 ms of a refresh. On the same machine
busy with other work the frames started up to 3 ms off the refreshes, which the pacer read as late frames at 240 Hz. With work of
130 % of a refresh the pacer of that time never slowed down, at 120 and 240 Hz: the frames started 1.37 refreshes apart, which
rounds to one ([How a frame is paced](#how-a-frame-is-paced) has what changed).

## What it needs

- **A steady clock**, read by the application and passed in as a `TickCount64` (the core's time types: `TickCount64::FromNanoseconds`,
  `TickCount64::FromCounter(counter, frequency)`, or a `std::chrono` clock through `core/time/ChronoConversion.hpp`).
- **A loop paced by vsync**: vsync on, a fixed refresh rate, and a `Present` (or the wait for a free buffer) that waits for the
  display, so every frame starts when the previous one is shown, or on a refresh at least: the first integration's swap chain
  queues its presents (a frame is shown four refreshes after its present) and still starts every frame within 0.2 ms of a
  refresh. A machine busy with other work starts them less evenly: fine while they stay within half a refresh, and what
  [present feedback](#present-feedback-optional) is for where they do not.
- **The display's refresh period**: from the display mode, or a hard-coded value to start with (not every window system reports
  it). Give it with its fraction: `RefreshPeriod::FromRate(24002, 100)` for 240.02 Hz, or `FromNanoseconds`; whole ticks
  (`FromTimeSpan`) lose it. Not from a swap chain's present timing without a check: the first integration's driver reported a
  refresh duration of 8.33 ms at every display rate from 23.98 to 120 Hz.

Nothing else: no vsync timestamps, no scheduled presents. Present feedback is used where the application gives it and never
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
const PC::FrameSchedule schedule = pacer.BeginFrame(Now());     // your steady clock, as an FP::TickCount64
UpdateAndDraw(schedule.AnimationTime);                          // render the frame for this time
const FP::TimeSpan32 cpuBusy = pacer.EndFrame(Now());           // as you draw the marker, last, just before Present
DrawMarker(schedule, cpuBusy);
Present(schedule.SwapInterval);                                 // hold the frame for that many refreshes
```

`BeginFrame` returns a `FrameSchedule`:

| `FrameSchedule`       | What it is                                                                                                                   |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `FrameId`             | The pacer's count of the frames it began, from 1: what [present feedback](#present-feedback-optional) names the frame by     |
| `SwapInterval`        | Refreshes from the previous frame's display to this one's: hold the frame for it                                             |
| `AnimationTime`       | The frame's predicted display time on the display's clock: render the frame for it (the marker's)                            |
| `AnimationStep`       | The step from the previous frame's animation time: the frame's delta time                                                    |
| `IntendedDisplayTime` | When the pacer aims for the frame to be shown, on your steady clock (the marker's)                                           |
| `NextFrameStartTime`  | The frame's start plus its swap interval, on your steady clock: what a loop that sleeps holds to (below)                     |
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

Use the first way your platform has:

| Way                     | What to do                                                                                                                                                                                                                                       |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Swap interval**       | Present with `SwapInterval`: DXGI's `SyncInterval`, `eglSwapInterval`, `wglSwapIntervalEXT`, `glXSwapIntervalEXT`, Unity's `QualitySettings.vSyncCount`                                                                                          |
| **Present it again**    | Where vsync holds a frame for one refresh only (core Vulkan's FIFO, core Wayland): present the finished frame `SwapInterval` times. In Vulkan an image that was presented can not be presented again: draw or copy the frame into the next image |
| **Sleep, then present** | Sleep until one refresh before `NextFrameStartTime`, then present. A guess: the thread wakes a little late, differently every time                                                                                                               |

**A platform's swap interval has a largest value.** DXGI's `SyncInterval` takes at most 4, and `eglSwapInterval` fails above the
config's `EGL_MAX_SWAP_INTERVAL`. A target of 30 fps on a 240 Hz display is a swap interval of 8: set the largest the platform takes
and wait for the rest, as below.

**When you sleep, hold the next frame's start too.** A present that was delayed by a sleep is not paced by the display any more: where
the swap chain has an image to spare, the present returns at once, the next frame starts early, and the loop runs faster than the
swap interval says (a 30 fps target ran at 33 fps). The pacer can not see that: it counts a frame start that comes early as the
previous frame's swap interval, never less. So begin the next frame no earlier than the previous frame's `NextFrameStartTime`. The
frame starts then follow your steady clock, not the display's, so now and then a frame is held a refresh more or less: this way
stays the guess the table calls it. (At 30 fps on a 240 Hz display, the first integration's log has 6 % of the frames shown a
refresh early or late.)

Use `NextFrameStartTime` for this, not `IntendedDisplayTime`: without present feedback the two are equal, with it the intended
display time is the refresh the frame is shown on, the swap chain's queue included, and a loop that waited for that would run late.

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

## Present feedback (optional)

Some platforms report when a frame was shown, a few frames after it was presented. Where the application passes that on, the pacer
measures the frames by their display times and not by their starts. It is off by default, and without it the pacer is exactly the
baseline above.

**What it is for.** The frame starts measure the display while they stay within half a refresh of its refreshes, and on an idle
machine they do: the first integration's Vulkan FIFO swap chain started its frames within 0.2 ms of a refresh (5 % to 95 %) at
every fixed refresh rate from 23.98 to 240 Hz. On the same machine busy with other work (other programs were being built and
tested), at 240 Hz, the display still showed 1953 of 1955 frames one refresh (4.17 ms) apart, while the frame starts were 1.3 to
7.0 ms apart. That is more than half a refresh off, so by their starts 214 of 1999 frames read as late: the animation stepped a
refresh too far each time, and the rule slowed down and sped up again every frame window. By their display times 2 frames were
late, the two the display held longer. That log is in the tests (`sdk/test-data/pacer/240-vulkan-present-log.csv`).

So present feedback is for a high refresh rate on a machine that is not idle. At 60 Hz half a refresh is 8.3 ms; how far a busy
machine moves the frame starts there has not been measured.

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
and its present: it must pass the present time**. `PresentFeedback::NotShown(frameId)` is a frame the platform says was never shown;
it counts as a late frame. A frame the platform reports nothing for gets no feedback. A result without a display time is one or
the other by platform: on the first integration's driver (`VK_EXT_present_timing`) such a result, reported as complete, was a frame
replaced before the display took it (across each the display moved on two or three refreshes), so the sample gives it as not
shown.

What the pacer does with it:

- **Late frames come from the display.** The whole refreshes between two display times, against the swap intervals of the frames
  from one to the other: when the display fell behind, the frame is late. A frame held a refresh longer after one that was shown a
  refresh sooner is not: no refresh was lost over the two (a loop paced by sleeping lands a present on the wrong side of a refresh
  now and then, and slowing down does not cure that). A frame the platform reports as never shown is late. So is a frame whose
  work was over its frame time, as [without feedback](#how-a-frame-is-paced). A frame with no feedback at all counts as on time,
  and the next display time measures across it.
- **A late frame is caught up when its feedback comes**, a few frames after it (4 in that log), not on the next frame.
- **The intended display time is a refresh of the display**: the newest display time plus the swap intervals of the frames since,
  this one included. It has the swap chain's queue in it and none of the frame starts' wobble, so the analysis judges the frames
  against where they really were due. It is unknown (0, as the marker has it) until the first display time comes, and again after a
  restart.
- **A frame shown sooner than its swap interval** (two frames the display took in one refresh; a loop paced by sleeping that
  lands a frame a refresh early): the animation is a refresh ahead of the display until a frame is held a refresh longer, which
  then costs no animation step. The intended display time is not moved by it: it is counted from the display time alone.
- **The frame starts** still say when the loop paused (a gap longer than the frame window), and `NextFrameStartTime` is counted
  from them.

**Feedback is refused** when it can not be a refresh of the display, and `pacer.FeedbackState()` counts what became of it (`Used`,
`Refused`, `NotShown`, `Missing`):

- a display time before the frame's present;
- a display time that is not a whole number of refreshes, within an eighth of one, after the display time used before it. When the
  next one is a whole number of refreshes after a refused one, the count starts again there (a new swap chain, a mode change);
- feedback for a frame the pacer does not keep (more than 64 frames old, or from before a pause, `Reset`, `SetRefreshPeriod` or
  `SetSettings`), or not newer than the feedback before it.

Nearly everything refused means a wrong refresh period or **a display with a variable refresh rate** (G-SYNC, FreeSync), which
refreshes when a frame arrives and has no grid of refreshes to count in. The pacer needs a fixed refresh rate with or without
feedback; switch feedback off there. The first integration's logs with G-SYNC on had display times on no grid, some before their
present.

**While it is on and no feedback comes, no frame counts as late.** A frame leaves as on time once it is 64 frames old, so the rule
can still speed up, but it never slows down. An application whose platform stops reporting (a new swap chain without support) sees
`Missing` grow and switches back with `SetSettings`, which works on a live pacer: it starts again, as for any other setting.

**What it does not do.** It does not steady the swap chain's queue or the latency, and it does not make a sleep land on the right
refresh: both need the platform to take a time for the present ([Not used yet](#not-used-yet)).

Where a display time comes from. Only the first has been looked at, and only as one driver's present log:

| Platform                           | The display time                                              | The frame's name there          |
| ---------------------------------- | ------------------------------------------------------------- | ------------------------------- |
| `VK_EXT_present_timing`            | The first pixel out (or first pixel visible) stage's time     | The present id you set          |
| `VK_GOOGLE_display_timing`         | `actualPresentTime`                                           | The `presentID` you set         |
| Wayland presentation-time          | `presented`'s time; `discarded` is a frame that was not shown | One feedback object per commit  |
| `EGL_ANDROID_get_frame_timestamps` | `EGL_DISPLAY_PRESENT_TIME_ANDROID`                            | `eglGetNextFrameIdANDROID`'s id |
| Metal                              | `MTLDrawable.presentedTime` (0: not shown)                    | The drawable                    |

The times must be on the clock `BeginFrame` gets: convert them where the platform has another clock (Vulkan's calibrated
timestamps).

## Settings

`PacerSettings` is always valid: its constructor takes the refresh period, and every setter asserts that its value is within its range
(without asserts it clamps a value outside into the range). The rule's defaults are those of the simulation it reproduces; they are
settings, not properties of frame pacing in general.

| Setting                 | Default                              | Range                     | What it is                                                                                     |
| ----------------------- | ------------------------------------ | ------------------------- | ---------------------------------------------------------------------------------------------- |
| `Refresh`               | required                             | 100 µs to 1 s             | The display's refresh period (`RefreshPeriod::FromRate`, `FromNanoseconds`, `FromTimeSpan`)    |
| `PreferredFrameTime`    | none                                 | 0 (none) to 10 s          | The target frame rate as a frame time (`SetPreferredFrameRate` takes a rate)                   |
| `PreferredSwapInterval` | 1                                    | 1 to 100                  | The swap interval the application wants; the pacer never goes faster                           |
| `AutoSwapInterval`      | on                                   |                           | Adapt the swap interval with the rule                                                          |
| `SlowDown`              | LateCount                            | `LateCount`, `FullWindow` | When the rule slows down                                                                       |
| `FrameWindowLength`     | 2 s                                  | 1 tick to 60 s            | How long a stretch of frames the rule looks at; a longer gap between frames is a pause         |
| `SlowDownLatePercent`   | 10                                   | 0 to 100                  | The share of late frames the rule slows down beyond                                            |
| `FrameMargin`           | 1 ms, at most an eighth of a refresh | 0 to 1 s                  | Added to the frames' average work before it is compared with swap intervals                    |
| `SlowestFrameTime`      | 50 ms                                | 0 to 10 s                 | The rule slows down no further once the swap interval is longer than this plus the margin      |
| `UsePresentFeedback`    | off                                  |                           | Measure the frames by the display times given ([Present feedback](#present-feedback-optional)) |

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
- `Measure(frameStartTime)` and then `Step(swapInterval)` do the same in two steps, for a loop that decides the swap interval from the
  measurement: `FrameMeasurement` says how many refreshes after the frame before it the previous frame was shown, whether that was
  late, and when on the display's clock.
- It is given its longest gap (the pacer gives it the frame window's length): a longer one is a pause.
- `MeasureLate(frameStartTime, lateRefreshes)` takes the measurement from elsewhere: the display moved on by the previous frame's
  swap interval and the refreshes given, and the frame start only says whether this is a pause.

`FramesInFlight` is that elsewhere for [present feedback](#present-feedback-optional): it keeps the frames that were begun (`Begin`,
`End`), measures each from its display time (`Add`), and hands out how many refreshes they were late by (`TakeLateRefreshes`), each
frame as it is decided (`TakeMeasured`, for the rule) and the newest frame's `IntendedDisplayTime`.

## Not used yet

The baseline takes nothing a platform may not have, and of what newer platforms offer the pacer uses one thing when it is given:
[present feedback](#present-feedback-optional). What it does not use yet; each is a possible upgrade on the
[roadmap](https://github.com/Unarmed1000/mb-framepacing/blob/master/doc/roadmap.md), as is a C# port:

| Not used yet                                | Where it exists                                                                                                  | What it would improve                                                        |
| ------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| Vsync times the platform reports            | DWM's `qpcVBlank`, Choreographer's frame time, `CADisplayLink`'s `timestamp`                                     | The intended display time without the frame starts' jitter                   |
| Predicted display times                     | Choreographer's expected presentation time, OpenXR's `predictedDisplayTime`, `CADisplayLink`'s `targetTimestamp` | The animation time the platform itself aims for                              |
| Scheduled presents and per-frame targets    | `VK_EXT_present_timing`, `EGL_ANDROID_presentation_time`, Metal's `present(at:)`, Windows' `SetTargetTime`       | Back at full rate a frame sooner after one slow frame; no sleep that guesses |
| The refresh period measured from the frames | Anywhere                                                                                                         | A change of rate followed without being told; 59.94 Hz taken for 60          |
| Slewing against drift                       | Audio and display times in one system clock                                                                      | Animation that stays in step with audio or a server over hours               |
| Variable refresh and vsync off              | G-SYNC, FreeSync, tearing presents                                                                               | Pacing where there is no grid of refreshes to round to                       |

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
presented and was shown, and the frame in which the application read that, all in ticks). The [present feedback](#present-feedback-optional) tests pace it by the frame
starts and by the display times, and pin both results.

`120-vulkan-work-130-log.csv` is a frame log of the same sample on a 120 Hz display with a fixed refresh rate and an idle machine,
with CPU work of 11.1 ms a frame (133 % of a refresh): 1500 frames, when each started, when its work ended, the work, and when it
was shown. The frame starts are 1.37 refreshes apart and the display showed 550 of 1495 frames for two refreshes. The tests pin
that every frame is late, that the animation's refreshes are the time that passed, and the frame the rule slows down at.
