# Filling the marker fields

[The marker format](marker-format.md) defines every field of the payload; [Integrating the marker](integrating.md) (and
[Unity](unity.md)) shows how to build it into an application. This page is the guide in between: for every field, where its value
comes from, when it changes, what to write when you do not know it, what the analysis does with it and what goes wrong in the report
when it is wrong. Worked examples for typical frame pacers and a checklist of common mistakes follow.

Only the frame index and the animation time are required. Every other field makes the report say more; each one you leave out
(`0`) is replaced by something the analysis can measure or be told, as described below.

## The clocks

Every time is in **ticks of 100 ns** (C# `TimeSpan` ticks, 10'000'000 per second). The fields come from three clocks; keep each field
on its clock for the whole run:

| Clock                                     | Fields                                | What it is                                                                                                              |
| ----------------------------------------- | ------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| The **animation clock** (the "game time") | Animation time                        | The time the application's animation is evaluated for: whatever its motion follows                                      |
| The **pacer's steady clock**              | Intended display time, CPU start time | A monotonic clock (`std::chrono::steady_clock`, `QueryPerformanceCounter`, `Stopwatch`, `time.monotonic_ns`), any epoch |
| **UTC**                                   | Start time (start marker only)        | The wall clock, as C# `DateTime` UTC ticks (100 ns since 0001-01-01)                                                    |

The three frame times (target, preferred, CPU busy) are intervals, not points in time, so they have no clock of their own.

`0` means **unknown** in every time field but the animation time. A steady clock whose epoch could make a real value
exactly `0` (a counter that starts at the process start, say) must be offset, or that frame reads as unknown.

Steady clock ticks in each language:

```cpp
// C++: std::chrono has no 100 ns duration; define one
using Ticks = std::chrono::duration<int64_t, std::ratio<1, FM::TicksPerSecond>>;
const int64_t nowTicks = std::chrono::duration_cast<Ticks>(std::chrono::steady_clock::now().time_since_epoch()).count();
```

```csharp
// C#: Stopwatch's frequency differs between platforms; convert through double so a long uptime cannot overflow
long nowTicks = (long)(Stopwatch.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
```

```python
# Python
now_ticks = time.monotonic_ns() // 100
```

An animation clock in seconds converts with `Marker.SecondsToTicks` (C#), `seconds_to_ticks` (Python) or
`std::llround(seconds * FM::TicksPerSecond)` (C++).

## The fields

### Run id

- **Means:** which test run the marker belongs to. The start marker, every frame marker and the end marker of a run carry the same id.
- **Value:** a counter or a random `u32`, picked when the run starts. `0` is a valid id; it has no special meaning in the format.
- **Changes:** once per run. Give every run its own id, and give a new id after the application restarts.
- **What the analysis does:** it measures the frames between a run's last captured start marker and its first captured end marker
  with the same id. A frame marker with another id ends the run. Without start markers, the frames of each id form one run (with a
  warning), so frames drawn between runs with another id form a run of their own.
- **Goes wrong when:** the sync marker carries another run id than its main marker (below): every capture then reads as torn.

### Frame index

- **Means:** the application's own counter of rendered frames. It has nothing to do with the capture's frame counter.
- **Value:** `+1` for every frame the application renders, including the frames that show the start and end markers and the frames
  that never reach the display. A frame keeps its index for every refresh it stays on screen. `u64`: it never wraps.
- **What the analysis does:**
  - A frame index skipped over a capture without gaps, and never shown later, is a **dropped frame**. Without the pacer's schedule,
    the frame after dropped frames is due one target frame time per dropped frame later, so it is dropped, not late.
  - An index that goes back by at most 1000 is an older frame shown again after a newer one: **out of order**. The newer frame
    stays the one presented.
  - An index that goes back by more than 1000 starts a new segment, with a warning (an application that restarted). Steps are never
    measured across segments.
  - The frametime needs the next index: it is measured only where frame index + 1 was captured too.
- **Goes wrong when:**
  - The index counts refreshes or game loop ticks instead of rendered frames: the report shows dropped frames that were never
    rendered, or misses real ones.
  - The application restarts with the same run id within 1000 frames of where it was: the new frames read as out of order. Use a
    new run id.

### Flags: Static

- **Means:** bit 0, **static**: nothing animates in this frame. It would look the same whatever time it was shown at (an idle
  screen, a paused menu with nothing moving).
- **Value:** set it on every frame where nothing animates, and only there. Bits 1 to 7 are reserved: write `0`.
- **What the analysis does:**
  - The animation error of the steps to and from a static frame is not judged (and has no prediction error), so an animation clock
    that pauses while idle does not look like a huge error. The drift adds up only the judged errors.
  - A static frame's time on screen (the next frame's display time step) is left out of the average fps, the 1 % and 0.1 % lows and
    the display time step statistics and histogram; the report says how many (**excluding N static frames**).
  - The report draws static stretches in violet, and leaves them out of the display time step and frametime scales.
  - A static frame is never in the late share's amber ("held longer than the preferred frame time").
- **Goes wrong when:**
  - The flag is set on a frame where something still moves (a spinner, a blinking cursor, a video): its errors are hidden and its
    time is missing from the frame rates.
  - It is missing while the animation clock pauses: every step of the pause shows an animation error of a whole display step. When
    no frames are presented while idle, the first frame after it shows an error as long as the pause.

### Animation time

- **Means:** the time the frame's animation was evaluated for: what the frame shows, from the clock the application's animation
  uses. It is not a measurement of when the frame reaches the screen; the capture measures that.
- **Value:** the same value the frame's animation used, converted to ticks. Negative values are allowed.
- **Changes:** every frame, by the application's animation time step (its delta time).
- **What the analysis does:** the **animation error** of a step is the animation time step minus the display time step: how far the
  motion on screen moved off real time. It is the report's main number; the drift sums it over the run.
- **Goes wrong when:**
  - It comes from a separate wall clock read while drawing the marker instead of the animation's own clock: the report then measures
    the render loop's timing, not what the viewer saw.
  - A time scale other than 1 (slow motion) or a pause changes the animation clock: every step shows an error of the difference.
    Measure at time scale 1, write the clock the visible motion follows, and flag paused frames as static.
  - A fixed-step simulation renders without interpolation: write the simulation time the frame shows, not the loop's time.

### Preferred frame time

- **Means:** the interval the application **wants** to run at: what it would aim for if nothing held it back. It differs from the
  target frame time only while the pacer runs slower than the application wants.
- **Value:** `u32` ticks: `166'667` for 60 fps, `333'333` for 30 fps, `10'000'000` for 1 fps. `0xFFFFFFFF` (**on demand**,
  `OnDemandFrameTicks`) for an application that presents only when something changes.
- **Changes:** when the application's own wish changes (a menu that wants 30 fps, an idle state that wants 1 fps); not when a pacer
  lowers the rate on its own.
- **Unknown (`0`):** the analysis assumes the application wants the target frame rate given to the tools (`--target-fps`), else the
  display's native refresh rate.
- **What the analysis does:**
  - The value is rounded up to whole refreshes, with 5 % slack (59.9 fps on 60 Hz is one refresh, 60 fps on 144 Hz three).
  - The **late share's amber**: a frame on screen at least half a refresh longer than the preferred frame time, without being late.
    The pacer intended it, but the application runs slower than it wants. On demand and static frames are never amber.
  - Without a target frame time and a schedule, frames are measured against it.
  - The report's display box says "preferred N fps" (or "on demand").
- **Goes wrong when:** a game that deliberately runs at 30 fps writes the target but not the preferred frame time: the analysis
  assumes it wants one frame per refresh, and every frame is amber.

### Target frame time

- **Means:** the interval the pacer aims for **now**, between the previous frame and this one: `1 / target frame rate`.
- **Value:** `u32` ticks, as the preferred frame time. `0xFFFFFFFF` (on demand) is allowed here too.
- **Changes:** on the frame where the pacer changes its rate (Swappy dropping from 60 to 30 fps writes `333'333` from the first
  frame it paces at 30).
- **Unknown (`0`):** the preferred frame time stands in, then `--target-fps`, then one refresh.
- **What the analysis does:** without the pacer's schedule, every frame is measured against the frame before it: it is **late**
  when its display time step is at least half a refresh longer than its target (rounded up to whole refreshes, as above, and one
  target per dropped frame longer after dropped frames). An on-demand frame is never late without a schedule.
- **Goes wrong when:** it holds the display's refresh period while a frame limiter holds a lower rate: every limited frame is late.

### Intended display time

- **Means:** when the pacer intends this frame to become visible: the vsync it targets, the present time it requests from a present
  timing API (`desiredPresentTime` in `VK_GOOGLE_display_timing`, the target present time of `VK_EXT_present_timing`,
  `EGL_ANDROID_presentation_time`, Swappy), or the predicted display time the platform gives it and it adopts (OpenXR
  `predictedDisplayTime`, Android Choreographer's expected presentation time, `CADisplayLink.targetTimestamp`).
- **Value:** `i64` ticks on the pacer's steady clock, the same clock for the whole run and the same one as the CPU start time. It is
  the plan, known before the frame is presented, not a measurement taken afterwards.
- **Changes:** every frame, usually by the target frame time.
- **Unknown (`0`):** the frame has no schedule. The analysis uses the schedule only when at least half the run's frames carry one.
- **What the analysis does:**
  - It lines the pacer's clock up with the capture's clock from the run's **on-time frames**: the earliest group of at least a quarter
    of the frames that appear within half a refresh of each other, relative to their intended times.
  - A frame shown half a refresh or more after its intended time, relative to that, is **late**. That also finds the frames that
    stay late after a hitch, which a step by step comparison misses.
  - It splits every animation error exactly: **pacing error** (the display time step minus the intended step: shown off the plan)
    and **prediction error** (the animation time step minus the intended step: animated for another moment than planned).
  - The frame timeline card places the CPU start times on the capture's clock with the same alignment.
- **Goes wrong when:**
  - It comes from another clock than the CPU start time: the frame timeline card places the CPU work wrongly.
  - Most of the run is consistently late (a full frame queue for the whole run): a delay the whole run shares cannot be told from
    the difference between the two clocks, so only changes of it show.

### CPU start time

- **Means:** when the CPU started working on this frame (input, simulation, building the render commands); PresentMon's
  `CPUStartTime`. It can be anywhere inside a refresh, and with several frames in flight the next frame can start before this one
  is presented.
- **Value:** `i64` ticks on the same steady clock as the intended display time.
- **What the analysis does:** the **frametime** is the step from this frame's CPU start time to the next frame's (PresentMon's
  `MsBetweenAppStart`), known where frame index + 1 was captured too and both values are known. The report draws it in the
  frametime panel; the frame timeline card draws each frame's CPU work as a box.
- **Goes wrong when:** it is read on another clock than the intended display time, or at the end of the frame instead of its start.

### CPU busy

- **Means:** how long the CPU worked on this frame before presenting it: from the CPU start time until Present is called
  (PresentMon's `MsCPUBusy`). The marker is drawn last, just before Present, so measure it as you draw the marker. It does not
  include the GPU's work or time blocked inside Present.
- **Value:** `u32` ticks. It can span several refreshes; clamp it at `0xFFFFFFFF` (about 429 s).
- **What the analysis does:** the frametime panel draws it under the frametime, the frame timeline card as the length of the CPU
  box, and the **CPU wait** is the frametime minus CPU busy (PresentMon's `MsCPUWait`).

### Start time and sequence id (start marker only)

- **Start time:** when the run started, as C# `DateTime` UTC ticks (`ToDateTimeTicks(std::chrono::system_clock::now())` in C++,
  `DateTime.UtcNow.Ticks` in C#). Read it once when the run starts, not per frame. The reports show it (`startTimeUtc` in
  `summary.json`); no measurement uses it.
- **Sequence id:** 16 bytes unique to the run: a new UUID, or a text tag of at most 16 printable ASCII characters. The reports show it
  as text, or as a UUID. With a name given to the tools (`--name`, the GUI's **Name**), the report's title shows the name and the
  sequence id below it.

### The sync marker

- **Means:** the small second marker (kind `3`) carries only the run id and the frame index, and belongs to the main marker with the
  same two values.
- **Value:** the main marker's run id and frame index, every frame: `payload.WithKind(MarkerKind.Sync)` (C#),
  `payload.with_kind(MarkerKind.SYNC)` (Python), or a `Payload` with the same `FrameIndex` and `RunId` and `Kind = MarkerKind::Sync`
  (C++).
- **What the analysis does:** on a capture card, a capture whose two markers disagree is **torn**: a capture gap, so the steps
  around it are not judged. A camera times every frame by it.
- **Goes wrong when:** it carries a run id of `0` (the default) while the main marker has another: every capture reads as torn, and
  the run has no frames to measure.

## Worked examples

Ticks are 100 ns. `0` = unknown; "on demand" = `0xFFFFFFFF`.

**A fixed 60 fps loop with vsync on a 60 Hz display, without a present timing API.** Preferred and target frame time `166'667` on
every frame, intended display time `0`. The analysis measures every frame against the one before it: a step of two refreshes or
more is late, unless the frame index shows that frames between were dropped.

**A 30 fps lock on a 60 Hz display.** Preferred and target frame time `333'333`. Steps of two refreshes are on target; a step of three
is late. Nothing is amber: the game runs at the rate it wants. Without the preferred frame time every frame would be amber.

**Swappy lowering from 60 to 30 fps for a busy stretch.** The preferred frame time stays `166'667`; the target frame time is
`166'667`, then `333'333` from the first frame paced at 30; the intended display times are the present times Swappy requests:

| Frame | Target    | Preferred | Intended display time | Report                                   |
| ----- | --------- | --------- | --------------------- | ---------------------------------------- |
| 100   | `166'667` | `166'667` | T                     | on time                                  |
| 101   | `333'333` | `166'667` | T + `333'333`         | on time, amber: below its preferred rate |
| 102   | `333'333` | `166'667` | T + `666'667`         | shown a refresh after its plan: late     |

The late share shows the lowered stretch in amber and the frames shown after their plan in red.

**A frame rate cap just under the refresh rate (117 fps on 120 Hz).** Preferred and target frame time `85'470`. A capture card needs a
fixed refresh rate, so the display refreshes every `83'333`: the target rounds up to one refresh (5 % slack), and the refreshes
where the cap makes a frame wait a second refresh are measured as late.

**An idle device at 1 fps.** While idle, preferred and target frame time `10'000'000`, and the static flag on the idle frames when
nothing moves. Steps of one second are on target and not amber; the idle frames' time is left out of the frame rates and drawn in
violet. Back to 60 fps, both fields return to `166'667` on the first frame paced at 60.

**An on-demand renderer (an editor, a UI that redraws on input).** Preferred and target frame time on demand on every frame, no
intended display time. No wait for the next frame is late and nothing is amber; the animation errors of the frames it does present
are judged as usual.

**A static menu in a game that keeps rendering at 60 fps.** Preferred and target frame time `166'667`, and the static flag on every
frame of the menu while nothing in it moves. The animation clock may pause meanwhile; the steps into and out of the menu are not
judged, and the menu's frames are left out of the frame rates ("excluding N static frames").

**Several frames in flight.** The CPU starts frame 11 before frame 10 is presented; CPU busy runs from each frame's own start:

| Frame | CPU start time | CPU busy  | Frametime (from the markers)    |
| ----- | -------------- | --------- | ------------------------------- |
| 10    | C              | `200'000` | `166'667`                       |
| 11    | C + `166'667`  | `190'000` | `166'667`                       |
| 12    | C + `333'333`  | `210'000` | known once frame 13 is captured |

The frame timeline card draws the overlapping CPU boxes in further lanes.

## Common mistakes

- The animation time comes from a wall clock instead of the clock the animation uses, or runs at a time scale other than 1.
- The animation clock pauses without the static flag, or the static flag is set on frames that still move.
- The intended display time and the CPU start time come from two different clocks.
- A real time of exactly `0` (a steady clock started at 0): that frame reads as unknown.
- A deliberate lower rate written as the target frame time without the preferred frame time: every frame is amber.
- The sync marker without the run id: every capture reads as torn, and nothing is measured.
- The frame index counts something other than rendered frames, or restarts without a new run id.
- The payload built by position in the wrong order: the constructors take frame index, animation time, run id, kind, intended display
  time, target frame time, CPU start time, CPU busy, preferred frame time and flags, which is not the order on the wire. Name the
  fields (C# named arguments, Python keywords, C++ designated initializers).
- CPU busy not clamped to `u32`.
