# The frame pacer (experimental)

> **Experimental.** The pacer is checked against its own simulation and, on one machine, against the display times a graphics
> driver reports: no capture of it has been analysed with the tools yet. It is off by default (`MB_FRAMEPACING_BUILD_PACER`,
> Conan's `with_pacer`), its API may change in any release, and it is not what the marker and the tools need: they measure any
> pacer. See [Status](#status).

The pacer module paces a frame loop. It gives every frame the time to start at, the **swap interval** to hold it for, the
**animation time** to render it for, when and how to present it, and what to write into the frame marker. It holds a **target
frame rate**, and it adapts the swap interval to how the frames do.

**The pacer holds every pacing rule and every time calculation. The application supplies information and carries out what it is
given**: a wait until a time, a wait the platform has, a present with a value. An application says what its platform can do as
a set of **capabilities**, and the pacer paces with what is there: with nothing but a steady clock and the display's refresh
period, and better with a wait until a present was shown, the times of the display's vertical blanks, or a time on the present
itself. How good the pacing can be for a set is its **tier**.

It is **values in, values out**: the pacer calls no graphics or platform API, has no callbacks, never waits and never reads a
clock. Made once (it allocates its frame windows then), it never allocates again while it paces; only other settings that need
a larger frame window do.

| Language | Module                                                                                  |
| -------- | --------------------------------------------------------------------------------------- |
| C++20    | `mb_framepacing::pacer`, `<mb/framepacing/pacer/…>`, namespace `MB::FramePacing::Pacer` |

[The frame pacer: redesign proposal](pacer-design.md) has how this pacer came to be: what was wrong with the one it replaced,
every measurement so far with its conditions, the decisions and what is still missing. This guide says how to use it.

## Status

| Checked                                                                                                                                                                                             | Not checked                                                                                                              |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| A simulated frame loop on a display model that queues presents, against golden results (`sdk/test-data/pacer`): the four ways of pacing below, both aims, sixteen cases                             | Any real swap chain recorded by a capture card and analysed with the tools                                               |
| The swap intervals and refreshes of mb-framepacing-explained's simulation, frame by frame                                                                                                           | Any platform but Windows, any graphics API but Vulkan, any GPU vendor but one                                            |
| The refresh rates monitors have, 50 to 540 Hz: frames on time, two and three refreshes per frame, a display a thousandth off its rate (simulated)                                                   | A real display's clock against a real CPU clock for longer than minutes                                                  |
| Every line and branch of the module by its tests; no allocation per frame; two compilers, and a build with the address and undefined behaviour sanitizers                                           | The compilers of Linux and macOS                                                                                         |
| A change of what is active while the frames go on: every change between the four ways, both aims, at one, two and four refreshes per frame (simulated), and 140 changes on one system               | The same recorded by a capture card                                                                                      |
| The four ways of pacing without a time on the present, on one system (Windows, a Vulkan FIFO swap chain in a window, 240 Hz, one driver), by the driver's display times: first runs, a few per case | A compositor, a frame queue and a GPU's limit on any other machine                                                       |
| A minimum duration on the present, on that system: 144 runs at one, two and four refreshes per frame                                                                                                | A time on the present before which a frame is not shown (tiers 2.1 to 2.4): on no system, as the one at hand has none    |
| Display reports: the pacer's counts against the first integration's own, equal in 16 runs of 16 on that system                                                                                      | The wait for the GPU's work under a real GPU load; a swap interval on the present; a display that skips an overdue frame |
|                                                                                                                                                                                                     | Vsync off; a variable refresh rate ([below](#a-variable-refresh-rate))                                                   |

It is here to be tried and measured (the marker and the tools exist for exactly that), not to be relied on.

**A first integration.** This project's author's own, **unofficial**
[gtec-demo-framework](https://github.com/Unarmed1000/gtec-demo-framework) has the pacer in its three FramePacing samples, for
[Vulkan](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/Vulkan/FramePacing),
[OpenGL ES 3](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES3/FramePacing) and
[OpenGL ES 2](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES2/FramePacing)
([its description](https://github.com/Unarmed1000/gtec-demo-framework/blob/master/Doc/FramePacing.md)). Every measurement in
the proposal is of that integration's Vulkan sample on one Windows machine, by the display times its driver reports. Those are
no capture of the display, and most cases have a few runs: the proposal gives each number with its conditions, and this guide
gives none of them as advice.

## What it needs

Every application has the **baseline**, which is no capability:

- **A steady clock**, read by the application and passed in as a `NanosecondTickCount` (the core's time types: nanoseconds as
  the platform gives them with `NanosecondTickCount::FromNanoseconds`, a clock that counts in ticks of 100 ns through
  `NanosecondTickCount::FromTickCount64`). Every time the pacer takes and gives is in nanoseconds: `NanosecondTickCount` for a
  point on the clock, `NanosecondTimeSpan` for a span, `NanosecondTimeDuration` for a length of time, as the marker's payload
  takes them.
- **The refresh period of the display the window is on**: `RefreshPeriod::FromRate(24002, 100)` for 240.02 Hz keeps the
  fraction of a nanosecond, and `FromNanosecondTimeSpan` takes a period in whole nanoseconds. Not the period a swap chain
  reports without a check: on the first integration's machine that was the period of the fastest display of the desktop.
- **A wait until a time** on that clock, in two places: before a frame takes anything, and before its present.
- **A present** with vsync on, on a display with a fixed refresh rate, that shows every frame in the order it was presented for
  at least a refresh.

**Nothing about a display is used unless it is certain to be about the display the window is on.** That is the application's
to meet for the refresh period and for every capability that says something of a display. A window that moves to another
display changes the period (`SetRefreshPeriod`) and takes such a capability away until the application has it for the new
display (`SetCapabilities`).

## Capabilities and tiers

What a platform can do beyond the baseline, in terms of no graphics API (`PacerCapability`, combined into a `PacerCapabilities`):

| Capability                                                                       | The application can …                                                                                                                      | Examples                                                                             |
| -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------ |
| `WaitForPresent`                                                                 | wait until a present it names was shown, with a timeout                                                                                    | `VK_KHR_present_wait2`, DXGI's waitable swap chain                                   |
| `VBlankTimes`                                                                    | say when a vertical blank of its display was                                                                                               | DXGI's output, Wayland's presentation feedback, Choreographer                        |
| `PresentAtTime`                                                                  | give a present a time before which the frame is not shown                                                                                  | `VK_EXT_present_timing` (absolute), `EGL_ANDROID_presentation_time`                  |
| `PresentSkipsOverdue`                                                            | say that of two presents whose times have both passed the later is shown and the earlier never                                             | Vulkan's `FIFO_LATEST_READY` with a target time, Android's buffers with a time       |
| `PresentAfterDuration`                                                           | give a present a time the frame before it stays on screen at least                                                                         | `VK_EXT_present_timing` (relative), Metal's minimum duration                         |
| `PresentSwapInterval`                                                            | present with a swap interval of 1 to a maximum it gives                                                                                    | `eglSwapInterval`, DXGI's sync interval                                              |
| `WaitForGpuWork`                                                                 | wait until the GPU finished a frame it names, with a timeout                                                                               | a fence                                                                              |
| `GpuWorkTimes`, `GpuWorkDurations`                                               | say when the GPU began and ended its work on a frame, or how long it took, frames later                                                    | calibrated timestamp queries, timer queries                                          |
| `DisplayTimes`                                                                   | say when a present was shown, or that it never was, frames later                                                                           | `VK_EXT_present_timing`, DXGI's frame statistics, `EGL_ANDROID_get_frame_timestamps` |
| `PresentWaits` / `PresentReturnsAtOnce`, `AcquireWaits` / `AcquireReturnsAtOnce` | say whether its present, and its call for the next image, wait for the display: one of each pair, or neither                               | DXGI's default present waits; one asked to return at once does not                   |
| `WaitForImage`, `FrameCallback`                                                  | wait until an image of its swap chain is free; say when the window system called for a new frame. Information only: no pacer paces by them | a Vulkan acquire's fence, Wayland's frame callback                                   |

The application gives the pacer two sets: what it **has** (when the pacer is made, and again when that changes), and what of
it is **active** now (`SetActiveCapabilities`). The active set is how an application controls the pacer: a capability that is
not active is not used and not expected. Leaving one out is how a mechanism is switched off, and how two ways of pacing are
compared on one system.

**A tier is the capabilities a set needs to reach it**, and it says how a frame is paced. There are three **major tiers**, by
who places a frame on its refresh, each with four **sub tiers**. A tier is written as the two, "3.1"; 1.1 is the best and 3.4
is the baseline that every set reaches.

| Major tier | Who places the frame                                           | Needs                                   | Status                                                                            |
| ---------- | -------------------------------------------------------------- | --------------------------------------- | --------------------------------------------------------------------------------- |
| 1          | The display's side, and it skips a frame that is overdue       | `PresentAtTime` + `PresentSkipsOverdue` | **Rated only: no pacer is built.** A set that reaches it is paced as major tier 2 |
| 2          | The display's side, every frame in the order it was presented  | `PresentAtTime`                         | Built against the simulation's display only: no system has been measured with it  |
| 3          | The frame loop: it has to make the present at the right moment | nothing                                 | Built; first runs and measurements on one system                                  |

| Sub tier | In major tiers 1 and 2           | In major tier 3                  |
| -------- | -------------------------------- | -------------------------------- |
| .1       | `WaitForPresent` + `VBlankTimes` | `VBlankTimes` + `WaitForPresent` |
| .2       | `WaitForPresent`                 | `VBlankTimes`                    |
| .3       | `VBlankTimes`                    | `WaitForPresent`                 |
| .4       | nothing more                     | nothing (the baseline)           |

- **A wait for a present** holds the loop until the display took an earlier frame, so the frames that wait to be shown can not
  grow. Without one the loop is held on a timer, and a frame that comes to wait stays.
- **Vertical blank times** tell the pacer where the display's refreshes are. Without them it counts refresh periods on the
  clock: a grid that is a constant part of a refresh away from the display, and slides against it as slowly as the two clocks
  differ.
- **Every order in the list is a proposal until the tiers have been measured against each other.**
- Beside the tier, a rating says whether display times are reported (the mark "+": "3.1+") and whether the display's side can
  hold a frame for more than one refresh (a time on the present, a minimum duration, or a swap interval of two or more).
- **A minimum duration and a swap interval on the present make no tier**: they place nothing by themselves, as they count from
  wherever the frame before was shown. They are given on every present where they are active, next to what the loop does.
- **A set with `PresentSkipsOverdue` gets a pacer that takes every present as shown.** Where its display did skip a frame, the
  pacer's counts are off by that frame until major tier 1 has a pacer.

```cpp
#include <mb/framepacing/pacer/capability/PacerTierText.hpp>
#include <mb/framepacing/pacer/capability/PacerTierUtil.hpp>

const PC::PacerRating rating = PC::PacerTierUtil::Rate(has);        // of any set, without a pacer
ShowText(PC::PacerTierText::NumberOf(rating.Tier));                 // "3.1"
ShowText(PC::PacerTierText::ShortDescriptionOf(rating.Tier));       // one line of 44 characters at most
// rating.RaisesTier: the capabilities any one of which would raise the tier
```

The pacer gives three of them: `Rating()` of what the application has, `ActiveRating()` of what is active, and `WorkingTier()`,
the tier that is pacing this frame. That one is lower than the active tier while something a capability promised is missing: no
vertical blank reading has come yet, or the waits for a present stopped because none is shown.

## The frame loop

```cpp
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

// Once: the display's refresh period, and what the platform can do (PC::PacerCapabilities() for the baseline)
PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60'000, 1'001));
settings.SetPreferredFrameRate(30);                                  // optional: a target frame rate
const PC::PacerCapabilities has(PC::PacerCapability::WaitForPresent | PC::PacerCapability::VBlankTimes);
PC::TierPacer pacer(settings, has);

// Every frame. Now() is your steady clock, as an FP::NanosecondTickCount
pacer.AddVBlank({LastVBlankTime(), {}, Now()});                      // with VBlankTimes: whenever you have a reading

PC::FrameStartPlan start = pacer.PlanFrame(Now());                   // before the frame takes anything
if (start.WaitsForPresent())
{
  const FP::NanosecondTickCount begin = Now();
  const bool shown = WaitUntilShown(start.WaitForPresentFrameId, start.WaitForPresentTimeout);   // yours
  pacer.AddPresentWait({start.WaitForPresentFrameId, begin, Now(), shown});
  start = pacer.PlanFrame(Now());                                    // planned again after a wait
}
if (start.WaitsForGpuWork())
{
  const FP::NanosecondTickCount begin = Now();
  const bool done = WaitUntilGpuDone(start.WaitForGpuWorkFrameId, start.WaitForGpuWorkTimeout);  // yours
  pacer.AddGpuWait({start.WaitForGpuWorkFrameId, begin, Now(), done});
  start = pacer.PlanFrame(Now());
}
if (start.WaitsForStartTime())
{
  WaitUntil(start.StartTime);                                        // yours
}

const PC::FrameSchedule schedule = pacer.BeginFrame(Now());          // the frame starts
UpdateAndDraw(schedule.AnimationTime);                               // render the frame for this time
const PC::PresentPlan present = pacer.EndFrame(Now());               // the CPU's work is done
if (present.WaitsForPresentTime())
{
  WaitUntil(present.PresentTime);
}
const FP::NanosecondTickCount call = Now();
const bool taken = Present(present);                                 // yours, with the plan's values your present takes
pacer.AddPresent({present.FrameId, call, Now(), taken});             // before the next frame is planned
```

- **The application computes nothing.** It waits for what a plan asks and until the times it is given, and reports what
  happened. Which present to wait for, where in the frame, how long at most and what a wait that ran out means are the pacer's.
- **A plan changes nothing**, so a frame may be planned again: after every wait it is, as the wait may have taken long.
- **A wait a plan does not ask for is not made**, and a capability that is not active is not asked for. The wait for the GPU's
  work is asked for where it is active and the wait for a present is not: it is then the application's one wait for a frame
  slot, and the application makes no such wait of its own next to it.
- **A wait may take `WaitForPresentTimeout` or `WaitForGpuWorkTimeout` at the most**: a few of the frame's own swap intervals,
  and 50 ms at the least (`MinWaitTimeout`). A wait that ran out is reported as not shown, or not done. The pacer counts it and
  does not hold the frame to it, and when waits for a present run out in a row it stops asking for a while: a window that is
  not shown.
- **`EndFrame` comes when the CPU's work is done**, before the wait for the present time and before the present: a wait is not
  work.
- **`Present` gets what the plan has for it**: `SwapInterval` where the present takes one, `NotBeforeTime` where it takes a
  time before which the frame is not shown, `MinimumDuration` where it takes a time the frame before stays. A value the
  present does not take is not set.
- **A present the system did not take** (a swap chain out of date) is reported with `Accepted` false. After a swap chain was
  made anew, `ForgetPresents()`: the presents made so far are never shown.
- **`FrameId`** is the pacer's count of the frames it began, from 1. It is the key of every report; an application whose
  presents have ids of their own maps the one to the other.

**What else the application reports**, where it has it. None of it is needed, and each is used where it is given:

| Call               | What                                                                                                                                                                | When                                             |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| `AddGpuWork`       | The GPU's work on an earlier frame: `GpuWorkReport::Times(frameId, begin, end)`, `EndAndDuration`, or `OfDuration` where only its length is known                   | Frames later, when the platform has it           |
| `AddSystemWait`    | A wait the application made by itself before a frame starts, with its begin and end: for a free image (`SystemWaitKind::Acquire`) or for a frame slot (`FrameSlot`) | Before `BeginFrame`                              |
| `AddDisplayReport` | When an earlier frame was shown, or that it never was: `{frameId, displayTime, shown}`, oldest first                                                                | Frames later, with `DisplayTimes` active         |
| `AddVBlank`        | A vertical blank of the window's display: its time, and the time it was read at. A reading that is no vertical blank time of that display is not given              | Whenever there is one, with `VBlankTimes` active |

**The GPU's work has to be reported for a loop the GPU limits.** A frame's work is judged from the CPU's time and the GPU's
as two stretches: one after the other with one frame in flight, the longer of the two with two (`MaxFramesInFlight`, which is
the application's word for how far its CPU may be ahead of its GPU). Without a GPU work report the pacer knows the CPU's time
only, and a loop whose GPU work does not fit a refresh is not slowed down by its work.

## The two aims

Every tier's pacer has two aims, and an application chooses (`PacerSettings::SetAim`):

- **`PacerAim::Smoothness`**, the default: the display is kept supplied. At one refresh per frame a frame is made ahead as a
  reserve, so a frame that runs a little long is covered and where in a refresh a present lands matters less. A frame reaches
  the screen that many refreshes later; a constant delay does not show in the motion.
- **`PacerAim::LowLatency`**: the frames that wait are kept as few as the tier can. A frame's start is held so that it is ready
  just in time, no frame is made ahead, and a frame that runs long is a repeated frame where the reserve would have covered it.

| Tier (and the same with a time on the present) | Smoothness                                                                                                        | Low latency                                                                                                        |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| 3.1, vertical blank times and a wait           | The wait, then the frame starts at once and its present is held to its place in the refresh; one frame in reserve | The wait, then the frame's start is held so that it is ready at its place and no sooner; presented when it is done |
| 3.2, vertical blank times                      | The frame starts at once and its present is held to its place; one frame in reserve                               | The frame's start is held, presented when done. One pause after start-up                                           |
| 3.3, a wait                                    | One frame made ahead of the display, and the wait keeps the frames that wait to what may wait                     | No frame made ahead; the wait keeps the frames that wait to what may wait                                          |
| 3.4, the baseline                              | One frame made ahead; a frame late within the reserve gives up no step                                            | One pause after start-up (a guess); a frame half a period late or more takes the step the loop is at               |

**With low latency the CPU's and the GPU's work come one after the other**: one frame is in flight. A loop whose two fit a
refresh side by side and not added runs at half its frame rate with this aim (in the simulation, 10 ms of each on a 60 Hz
display: 60 frames a second with smoothness, 30 with low latency). That is what the aim costs, and the pacer does not pick
the aim by itself.

With `WaitForPresent`, `WaitingPresents` is how many presents may be waiting while a frame is made: two unless set, so one
frame may wait. With smoothness the reserve is that number less one.

## Where the frames are placed

- **On a grid on the clock** (no `VBlankTimes`): a frame start is due at a whole number of refresh periods from the first one,
  and stays due there whatever the frames before it did. A frame of the pacer's own that ran long costs whole steps of the grid,
  after which the loop is where it was against the display. A refresh the display lost although the frame was ready in time is
  not seen: at one refresh per frame that frame stays waiting unless a wait for a present is active.
- **On the display's vertical blanks** (`VBlankTimes`): every frame is for one vertical blank, and its start, its present and
  its animation time come from that blank. The pacer aims a frame to be ready at a place in the refresh before its blank
  (`ReadyPlacePercent`, the middle unless set). With a wait for a present it learns where a frame was really shown, and moves
  that place earlier when frames are shown later than worked out; it tries it later again after a quiet stretch.
- **By the display's side** (`PresentAtTime`): the present is given the frame's intended display time less half a refresh
  period, so neither a grid that is off the display's refreshes nor a period that is a little off puts the frame on another
  refresh. The loop then holds no present.

**The animation time does not catch up.** After a refresh that was lost, a frame's animation step stays its swap interval and
the animation time is that refresh behind the clock from then on (`RefreshesBehindClock`). Catching up would show the loss a
second time. A loss that repeats in every frame is followed: the step is then what the display does. A gap between two frame
starts longer than the frame window (or than two frames, where those take longer), or a clock that went back, is a pause: the
frames start again and the animation time goes on.

**A start the system held is no time that passed.** Where the application reports its own waits and a wait of the display's
side held the loop (a full swap chain, a frame slot that came free late while the GPU did no work), the frame after it is not
late and the animation time does not step over the hold.

## Changing what is active

`SetActiveCapabilities` takes effect when the frame that is open has ended. The frames and their ids, the animation time, the
swap interval with the rule's frame window, the GPU's work and the presents that can be waited for go on. Where the frames are
placed starts anew with the first frame after the change, which begins when the frame before it said the next one would.

`SetRefreshPeriod` with another period and `SetSettings` with other settings start the frames again; both keep the animation
time, and the same period or settings change nothing. `Settings()` always has the refresh period the pacer is on. `Reset()`
starts again as made.

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

**A frame of more than one refresh is held by the best mechanism that is active**: the display's side where the present takes
a time, a minimum duration or a swap interval, and the loop's own held present or held start for the rest.

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

In the simulation, with CPU work of 30 ms a frame on a 60 Hz display the rule goes to two refreshes per frame within half a
second, and to one again 1.6 s after the load ended. **Work close to a whole refresh is decided by the run**: whether enough
frames are late is not settled by anything in the rule.

`SwapIntervalRule` is public: an application with a frame loop of its own can feed it frames (display time, work, late) and read the
swap interval it decides.

## What the pacer tells

For an overlay or a log; none of it has to be read to pace.

| Accessor                                                                | What                                                                                                                                          |
| ----------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `Rating()`, `ActiveRating()`, `WorkingTier()`                           | The tiers, as [above](#capabilities-and-tiers)                                                                                                |
| `SwapInterval()`, `FrameWindow()`                                       | The swap interval now, and what the rule's frame window holds: its frames, the late ones, and `StartsAhead`                                   |
| `RefreshesBehindClock()`                                                | The refreshes the animation time is behind the clock: what was lost and not caught up with                                                    |
| `PresentWaitTimeouts()`, `PresentWaitsStopped()`                        | The waits for a present that ended without the present shown, and whether the waits are stopped now                                           |
| `GpuWaitTimeouts()`, `GpuTime()`                                        | The waits for the GPU's work that ran out, and the GPU time a frame is judged with                                                            |
| `HasVBlankReading()`, `VBlankJumps()`                                   | Whether a vertical blank reading was taken, and the readings that were off where the ones before them put the vertical blanks                 |
| `ReadyPlaceNow()`, `ReadyPlaceTries()`, `ShownLaterByWaits()`           | Where in a refresh a frame is to be ready now, how often that was tried a step later again, and the vertical blanks frames were shown late by |
| `DisplayHeldRefreshes()`, `SystemHeldFrames()`, `FrameSlotHeldFrames()` | What the display's side held the loop for                                                                                                     |
| `StartupPauses()`, `LastPresentBlocked()`                               | The pauses after start-up that were made, and how long the last present held the loop                                                         |
| `DisplayErrors()`                                                       | What the display reports said: [below](#display-reports)                                                                                      |

`FrameWindow().StartsAhead` is how far ahead of the times the pacer gave the frames of the frame window began, added up, late
frames left out. Close to zero is a loop in step. A refresh or more ahead is an application that does not wait for the times
it is given; below zero it falls behind (wake-up delays add up).

### Display reports

With `DisplayTimes` active the application gives each frame's display time back by its frame id, a few frames later
(`AddDisplayReport`), or says that the frame was never shown. **The pacer paces by none of it**: it counts, by the measuring
tools' rules, what the platform says the display did (`DisplayErrorState`), since the pacer was made and for about the last
second:

- a frame is **judged** when it and the frame before it were both reported as shown. Its animation error is its animation time
  step less the time between the two display times;
- an error of more than 1 ms either way is an **error frame**; half a refresh or more is a frame **at another refresh** than it
  was made for, and **late** when it is the later one;
- the time **from a frame's start to its display**, of every frame reported as shown: their number, the times added up and the
  longest. It grows by a refresh for every frame more that waits to be shown.

These are the platform's word, not a measurement of the display: the tools read the display.

## A variable refresh rate

The pacer needs a fixed refresh rate. A display with a variable one (G-SYNC, FreeSync, HDMI VRR) refreshes when a frame
arrives, so there are no whole refreshes to count in, and its vertical blanks follow the frames. **The pacer does not pace
such a display**, and it is not told of one: an application that finds its display's refresh rate to be variable does not use
the pacer for it.

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

**Or measure it**, which needs no such query. Both signs showed within a second in the capture sessions of 2026-10-04
([the first](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-hold.md),
[the second](https://github.com/Unarmed1000/mb-framepacing/blob/master/pacer-captures/2026-10-04-windows-session2.md)):

- the display times the platform reports are not whole refreshes apart;
- a wait on the vertical blank comes back at uneven times, a few refreshes apart, instead of every refresh. The second session
  measured exactly that (the median time between the last 64 vertical blanks) and it agreed with the driver's own state in
  every run: one refresh apart with a fixed rate, two at 120 fps with variable refresh enabled.

Two limits, from the same runs. A frame loop at the display's own rate can not be told from a fixed refresh rate this way. And
the driver's setting is not the answer either: set to "full screen only", the driver enabled variable refresh for a window in
three runs of ten and not in the other seven.

## Settings

`PacerSettings` is always valid: its constructor takes the refresh period, and every setter asserts that its value is within its range
(without asserts it clamps a value outside into the range). The rule's defaults are those of the simulation it reproduces; they are
settings, not properties of frame pacing in general.

| Setting                    | Default                              | Range                      | What it is                                                                                                |
| -------------------------- | ------------------------------------ | -------------------------- | --------------------------------------------------------------------------------------------------------- |
| `Refresh`                  | required                             | 100 µs to 1 s              | The display's refresh period (`RefreshPeriod::FromRate`, `FromNanosecondTimeSpan`)                        |
| `Aim`                      | `Smoothness`                         | `Smoothness`, `LowLatency` | What the pacer optimizes for ([The two aims](#the-two-aims))                                              |
| `PreferredFrameTime`       | none                                 | 0 (none) to 10 s           | The target frame rate as a frame time (`SetPreferredFrameRate` takes a rate)                              |
| `PreferredSwapInterval`    | 1                                    | 1 to 100                   | The swap interval the application wants; the pacer never goes faster                                      |
| `AutoSwapInterval`         | on                                   |                            | Adapt the swap interval with the rule                                                                     |
| `SlowDown`                 | LateCount                            | `LateCount`, `FullWindow`  | When the rule slows down                                                                                  |
| `FrameWindowLength`        | 2 s                                  | 1 ns to 60 s               | How long a stretch of frames the rule looks at; a longer gap between frames is a pause                    |
| `SlowDownLatePercent`      | 10                                   | 0 to 100                   | The share of late frames the rule slows down beyond                                                       |
| `FrameMargin`              | 1 ms, at most an eighth of a refresh | 0 to 1 s                   | Added to the frames' average work before it is compared with swap intervals                               |
| `SlowestFrameTime`         | 50 ms                                | 0 to 10 s                  | The rule slows down no further once the swap interval is longer than this plus the margin                 |
| `WaitingPresents`          | picked by the pacer: 2               | 0 (picked), 1 to 8         | With a wait for a present: the presents that may wait while a frame is made, the frame itself counted     |
| `PresentWaitSwapIntervals` | 4                                    | 1 to 64                    | The longest a wait may take, in swap intervals of the frame that waits                                    |
| `MinWaitTimeout`           | 50 ms                                | 0 to 10 s                  | And the least a wait may take before it runs out; 0: the swap intervals alone                             |
| `MaxFramesInFlight`        | 1                                    | 1 to 8                     | The frames the application lets be in flight: 2 or more is the CPU working on a frame while the GPU works |
| `ReadyPlacePercent`        | 50                                   | 0 to 100                   | With vertical blank times: where in a refresh a frame is to be ready, in percent after its vertical blank |
| `StartupPauseRefreshes`    | 4                                    | 0 (none) to 64             | Without a wait for a present: the one pause after start-up, in refreshes. A guess, named as one           |
| `StartupPauseDelay`        | 0.5 s                                | 0 to 10 s                  | How long after the first frame that pause is made                                                         |
| `SwapChainImages`          | 0 (not known)                        | 0 to 64                    | How many images the swap chain has, where the application knows                                           |
| `SystemHoldsLoop`          | off                                  |                            | The system holds the loop while its queue is full, and the application reports those waits                |

`RefreshPeriod` is always valid too: from 100 µs (10 kHz) to 1 s (1 Hz), with no default. The application gives the pacer its display's
period.

**`FrameMargin` follows the display by default.** The rule speeds up only when the frames' average work plus twice the margin fits
one refresh less. A margin of 1 ms is a small share of a 60 Hz refresh and half of a 500 Hz one: twice that is a whole refresh at
500 Hz and leaves 0.08 ms for the work at 480 Hz, so a pacer that had slowed down to every second refresh would stay there, and
with work of a tenth of a refresh it already would above 450 Hz. So the
default is 1 ms and at most an eighth of the refresh period (less than 1 ms above 125 Hz: 0.87 ms at 144 Hz, 0.52 ms at 240 Hz,
0.25 ms at 500 Hz), and it follows a change of the refresh period. A margin you set with `SetFrameMargin` is that margin on every
display (`FrameMarginAt(refresh)` says what it is on one).

**The start-up pause is a guess.** A pacer that can neither wait for a present nor see what the display shows does not learn
whether a frame waits. The first presents of a new swap chain can take longer to reach the display than the later ones, and the
frames that pile up behind them then wait for as long as the loop runs at one refresh per frame. The pause lets the display
take them; its default is what emptied the queue on the one system measured.

## Filling the marker

| Marker field          | From                                                                                            |
| --------------------- | ----------------------------------------------------------------------------------------------- |
| Frame index           | Your own frame counter                                                                          |
| Animation time        | `schedule.AnimationTime`                                                                        |
| Preferred frame time  | `schedule.PreferredFrameTime`                                                                   |
| Target frame time     | `schedule.TargetFrameTime`                                                                      |
| Intended display time | `schedule.IntendedDisplayTime`                                                                  |
| CPU start time        | The time you gave `BeginFrame`                                                                  |
| CPU busy              | `EndFrame`'s `PresentPlan::CpuBusy`, or `CpuBusyAt(Now())` for a marker drawn before `EndFrame` |

[Filling the marker fields](marker-fields.md) says what the analysis does with each. The pacer and the marker both count in
nanoseconds, so a schedule's values go into a `Payload` as they are; what a marker's four bytes do not hold is capped by the
payload.

The intended display time is the pacer's aim for the frame: the vertical blank it is for where the pacer knows them, and a
step of its grid on the clock where it does not. On the grid it is a constant part of a refresh away from the real refreshes,
and whole refreshes away where frames wait that the pacer does not know of.

## Not used yet

What the pacer does not use; each is a possible upgrade on the
[roadmap](https://github.com/Unarmed1000/mb-framepacing/blob/master/doc/roadmap.md), as is a C# port:

| Not used yet                                 | Where it exists                                                                                                  | What it would improve                                                                     |
| -------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| A display's side that skips an overdue frame | Vulkan's `FIFO_LATEST_READY` with a target time, the composition swapchain of Windows 11, Android                | After a late frame the frame on screen is the one made for that refresh: rated, not paced |
| Pacing by display times                      | The platforms with `DisplayTimes`                                                                                | Frames that wait seen and taken away where there is no wait for a present                 |
| Predicted display times                      | Choreographer's expected presentation time, OpenXR's `predictedDisplayTime`, `CADisplayLink`'s `targetTimestamp` | The animation time the platform itself aims for                                           |
| The refresh period measured from the frames  | Anywhere                                                                                                         | A change of rate followed without being told; 59.94 Hz taken for 60                       |
| An aim the pacer picks                       | Anywhere                                                                                                         | Low latency where it costs no frame rate, without the application choosing                |
| Slewing against drift                        | Audio and display times in one system clock                                                                      | Animation that stays in step with audio or a server over hours                            |
| Variable refresh and vsync off               | G-SYNC, FreeSync, tearing presents                                                                               | Pacing by a frame time that is no multiple of the refresh                                 |

## Tests and golden data

`sdk/test-data/pacer` holds the pacer's golden results, which the tests must reproduce to the byte. `python tools/update_pacer_test_data.py`
regenerates them with `pacer-sim --golden` (built with the tests when the pacer is built: `-DMB_FRAMEPACING_BUILD_PACER=ON`):

- `60-busy`: the busy stretch of mb-framepacing-explained's `60-busy-adaptive` clip, in that repository's frame model (a frame
  starts when the frame before it is shown). The full-window rule reproduces that simulation's swap intervals and display
  refreshes frame by frame.
- `60-busy-full-rate`: the `60-busy-full-rate` clip at a fixed swap interval: every frame is shown on the clip's refresh.
- `100-stages` and `60-relapse`: seeded staged loads. The fix is never late more often than the full-window rule and never slows down
  later; on `100-stages` and `60-relapse` it is late less often.
- `tier-loops.csv` and `tier-loop-<run>.csv`: the pacer in a simulated frame loop on a display model that queues what a present
  gives it. 128 runs: the four ways of pacing of major tier 3, both aims and sixteen cases (light work, GPU work of 90 and
  130 % of a refresh, a frame that runs long, a slow display, two frames in flight, vertical blanks at which no frame is taken,
  a time on the present, a minimum duration, a swap interval on the present, the wait for the GPU's work, display reports,
  changes of the active set, 60 Hz). A line per run with the length and the CRC-32 of its frames as text, and the eight runs
  with GPU work nobody reports written whole.

The simulation and `pacer-sim` are test code, built with the tests only and never part of the library. The display model's
settings are knobs to try a pacer against, not a description of any system.
