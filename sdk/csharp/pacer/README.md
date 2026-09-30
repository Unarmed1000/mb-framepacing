# MB Frame Pacing Pacer for C#

The frame pacer of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) SDK for C#. It plans every frame on the display's
refreshes, adapts the swap interval to how long frames take (Swappy's rule, with mb-framepacing-explained's fix as the default) and hands you
the values the frame marker carries. `AnimationClock` gives every frame its animation time in whole refreshes.

`MB.FramePacing.Pacer`: .NET Standard 2.1, C# 9, no dependencies besides the SDK's core ([`MB.FramePacing`](../core/README.md)). It is the
C++ pacer's API and arithmetic: both reproduce the same golden results byte for byte ([`test-data/pacer`](../../test-data/pacer)). It is
values in, values out: it calls no platform API and never reads a clock. Made once (the pacer allocates its window then), it never allocates
again. [The frame pacer](../../doc/pacer.md) describes the frame loop for every way of presenting, the platform values, the rule and every
setting.

## Add it

It is not published as a NuGet package. Use the source at an `sdk-v*` tag, as a project reference to `MB.FramePacing.Pacer.csproj` (it
references `../core`) or as a copy of `source/` and `../core/source/`.

## Quick start

```csharp
using MB.FramePacing.Pacer;

// Once: the display's refresh period, from its display mode (required)
var settings = new PacerSettings(RefreshPeriod.FromRate(60_000, 1_001));   // DXGI's 59.94 Hz as the output mode states it
var pacer = new FramePacer(settings);
var clock = new AnimationClock(settings.Refresh);

// Every frame: what your platform knows (the time now is required; the rest 0 = unknown)
FrameSchedule schedule = pacer.BeginFrame(new FrameInput(NowTicks(), vsyncTicks: LatestVsyncTicks()));
AnimationTime animation = clock.Advance(schedule);
UpdateAndDraw(animation.AnimationTicks);
uint cpuBusy = pacer.EndFrame(new FrameEnd(NowTicks()));   // as you draw the marker, last, just before Present
Present(schedule.SwapInterval);                            // or present for schedule.IntendedDisplayTicks, or sleep until EarliestPresentTicks
```

Ticks are 100 ns (`TimeSpan` ticks) on your steady clock: `Stopwatch.GetTimestamp()` converted with its frequency, or the platform's clock.

| Type                                                    | What it is                                                                                                    |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `FramePacer`                                            | Plans every frame (`BeginFrame`, `EndFrame`, `SetRefreshPeriod`, `Reset`, `Window`)                           |
| `PacerSettings`, `SlowDownRule`                         | The refresh period (required) and the rule's settings; every value is kept in its range                       |
| `RefreshPeriod`                                         | The refresh period exact to 2⁻³² tick (`FromRate`, `FromNanoseconds`, `FromTicks`); `default` is not a period |
| `FrameInput`, `FrameSchedule`, `FrameEnd`               | What goes in and comes out every frame                                                                        |
| `SwapIntervalRule`, `SwapIntervalChange`, `WindowState` | The adaptive swap interval rule on its own, for a frame loop of your own                                      |
| `AnimationClock`, `AnimationTime`                       | The animation time in whole refreshes: with the pacer (`Advance`) or measured (`AdvanceMeasured`)             |

`default(RefreshPeriod)` is the one value that is not a period: `PacerSettings`, `FramePacer.SetRefreshPeriod` and `AnimationClock` throw an
`ArgumentException` for it, at setup. Every setting out of its range is clamped into it.

## Tests

`UnitTest/` (NUnit) holds the C++ pacer's test cases, the timing diagrams of mb-framepacing-explained, a zero-allocation test, and a C#
port of the tests' simulation of a frame loop (test code, not part of the module) whose results must be the golden data's bytes.
