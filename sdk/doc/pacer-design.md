# The frame pacer: redesign proposal (experimental)

> **A proposal, not a description.** Nothing in this document is built. The pacer the library has today is described in
> [pacer.md](pacer.md). This document is here to be agreed on before any pacer code changes; once it is agreed and built, it
> becomes that guide. The pacer stays experimental and off by default.

## The rule it has to meet

When the pacer is on, **the pacer holds every pacing rule and every time calculation**. The application does two things only:

- it **supplies information**: what it can do, what it can measure, and the times it measured;
- it **carries out** what the pacer returns: a wait until a time, a present with a value the pacer gave. This is the "how", and
  it stays in the application, because only the application has the graphics API.

The pacer does not know whether it paces Vulkan, OpenGL ES or Direct3D. It does know the kinds of mechanism an application can
have, in generic terms, and picks among them. Logic about pacing that an application has to write itself is a fault of the
pacer.

What does not change: values in, values out (no platform API, no callbacks, no clock reads, no waits inside the library), no
allocation per frame, integer arithmetic, typed times, fixed refresh rates first (variable refresh later, as a mode of the same
pacer), C++ only.

## What is wrong today

Today's pacer takes a frame's start and its work and returns a swap interval, an animation time and one time for the start of
the next frame. Everything else the first integration's sample works out itself:

| The sample works out                                                                          | In this proposal                                             |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| On which side of the present the loop waits (before the frame's start, or before the present) | A setting of the pacer; the plan has both waits              |
| The time carried from frame to frame, started again after a late frame                        | The pacer's own timeline of refreshes                        |
| A time moved onto the display's vertical blanks, and where in a refresh to present            | The pacer, from vertical blank times given to it             |
| A frame held for more refreshes than the present holds, by a vertical blank time              | The plan's present time                                      |
| The same with a timer                                                                         | The plan's present time                                      |
| The same with a present that takes a target time                                              | The plan's present target                                    |
| A one-time pause after start-up, to let waiting frames reach the display                      | Not needed where the display can be waited for; open         |
| Which of these methods a frame uses                                                           | The pacer, from the capabilities that are active             |
| The work it reports: CPU time of this frame plus GPU time of an earlier one, as one number    | Reported as what it is: when each began and ended, per frame |

And one thing nothing handles: **a frame that misses a refresh.** The loop goes on presenting one frame per refresh and the
display shows one per refresh, so one more frame waits to be shown from then on, and every frame after it is a refresh older
when it reaches the screen.

The test bench shows it (`sdk/cpp/pacer/tests`, test code): a simulated loop with today's pacer on a display that takes no
frame at three vertical blanks ends its run three refreshes further behind, in both of the sample's profiles, and the pacer's
answers are the same with and without those blanks. The first integration's frame logs of 2026-10-06, replayed through today's
pacer, give its logged answers back on every frame (three runs of 2,400 frames) and show, in the run that fell behind most,
2,084 of 2,224 frames displayed one to four refreshes after the pacer's intended display time. Those logs are one machine's (one driver, a swap chain in a window, 240 Hz, the driver's display times, not
captures).

## How others handle a queue that fills

A present in FIFO order is taken at once while there is room, and the display takes one frame per refresh. An application that
presents as fast as the display shows therefore keeps whatever number of frames is waiting, and the latency is that number
times the refresh period. This is a known situation. What follows is what each platform's or library's public documentation
says about it (read on 2026-10-07; the pages are quoted for what they say, not for how anything is implemented).

| Approach                                                | Who documents it                                                                                                                      | What it does about waiting frames                                         | What its documentation says it costs                                                            |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| Let the queue fill and block on it                      | Vulkan FIFO, DXGI's default, Android's buffer queue, Metal's drawable pool                                                            | Bounds them at the queue's size; the queue stays full                     | A frame or more of latency                                                                      |
| Wait until an earlier present is done, before the frame | DXGI's waitable swap chain, `VK_KHR_present_wait` and `VK_KHR_present_wait2`                                                          | Limits the presents outstanding to a number the application chooses       | Less CPU and GPU work in parallel; when the wait returns is left to the implementation (Vulkan) |
| Start the frame when the display says                   | Android's Choreographer, Core Animation's display links, Wayland's frame callback, OpenXR's `xrWaitFrame`                             | One frame started per refresh; a queue that exists stays                  | A long frame still queues                                                                       |
| Give the present a time                                 | `VK_EXT_present_timing`, `VK_GOOGLE_display_timing`, `EGL_ANDROID_presentation_time`, Metal's timed presents, Wayland's commit timing | Keeps a frame from being shown early; does not shorten a queue            | No strict guarantee (Vulkan); not on every implementation                                       |
| Report what was shown, and let the application react    | DXGI's frame statistics, the Vulkan and EGL timing extensions, Metal's presented time, Wayland's presentation feedback                | A late frame is seen afterwards; the recovery is the application's        | Late, a limited history, unreliable with several monitors (DXGI)                                |
| Wait on the GPU's work, with times on the presents      | Android's frame pacing library                                                                                                        | Waits are put into the loop so the display catches up                     | One frame is still shown a refresh longer                                                       |
| Delay the frame's work until the GPU is free            | The graphics vendors' low latency features                                                                                            | Empties the queue between the CPU and the GPU, not the one to the display | For loops the GPU limits                                                                        |

In the pages' own words:

- Microsoft, on the default: "the system blocks the thread until it is done presenting a prior frame, making room to queue up
  the new frame … the system will reach a stable equilibrium where the game is always waiting almost a full extra frame", and
  on the waitable swap chain: "For every frame it renders, the app should wait on this handle before starting any rendering
  operations" ([Reduce latency with DXGI 1.3 swap chains](https://learn.microsoft.com/en-us/windows/uwp/gaming/reduce-latency-with-dxgi-1-3-swap-chains)).
- Android's frame pacing library: "The display pipeline contains a queue of frames, typically of size 2, which fills up if the
  game is trying to present frames too quickly … This situation is known as buffer-stuffing or queue-stuffing", and its
  answer: sync fences "to inject waits into the application that allow the display pipeline to catch up, rather than allowing
  back pressure to build up" ([Frame Pacing library](https://developer.android.com/games/sdk/frame-pacing)).
- Vulkan: a FIFO swap chain has "an internal queue … to hold pending presentation requests", of no stated length, and
  "Calls to vkQueuePresentKHR may block, but must return in finite time"
  ([Window System Integration](https://docs.vulkan.org/spec/latest/chapters/VK_KHR_surface/wsi.html)). The present timing
  proposal says of the present wait extension that it "lacks important details such as the latency of the present operation
  itself" ([VK_EXT_present_timing](https://docs.vulkan.org/features/latest/features/proposals/VK_EXT_present_timing.html)):
  the two are meant to be used together.
- Apple: "We're relying on the back pressure of a Drawable being available to set our frame rate for us. On a fixed-rate
  display, we know that this isn't the best idea"
  ([Optimize for variable refresh rate displays](https://developer.apple.com/videos/play/wwdc2021/10147/)).
- Microsoft's flip model page gives the one documented way to see a missed refresh from statistics: keep each present's
  expected refresh count, and "If the actual PresentRefreshCount is later than the expected PresentRefreshCount, a glitch has
  occurred" ([DXGI flip model](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-flip-model)).

What the documentation does not say, and this proposal therefore does not assume: how long a Vulkan FIFO queue is; that a swap
chain's images bound it in a window (the specification lets an acquire hand out an image the presentation engine still uses);
a deadline before the vertical blank by which a compositor must have a frame; exactly when a present wait returns.

**What the pacer does, and why.** The approaches differ in what they need from the platform, so the pacer can not pick one. It
takes the best the application says it has:

1. Where the application can **wait until a present was shown**, the pacer asks for that wait before a frame, for the present a
   fixed number of frames back. It is the documented way, it needs no display times, and it is one rule for every case: a wait
   for a present that was already shown returns at once, so it costs nothing while the loop is in step, and it holds the loop
   for exactly the surplus after a missed refresh, at start-up and after a swap chain was made anew.
2. Where it can not, but the application reports **when frames were shown**, the pacer counts the presents not yet shown and
   holds one frame start for each one too many. The reports come a few frames late, so the correction does too.
3. With **a clock and the refresh period only** it can not see the display. It can still count: the clock says how many
   refreshes have passed, and the pacer knows how many it gave to its frames. So it keeps its frame starts on one grid of
   refresh periods on the clock, and a frame of its own that ran long costs whole steps of that grid, after which the loop is
   where it was against the display ("A grid on the clock" below). What it can not count is a refresh the display lost
   although the frame was ready in time: that it does not learn of, and at a swap interval of one that frame stays waiting.
   What to do about that is an open question below.

A prediction of a miss from when the GPU finished a frame was considered and is not in the design: in the stored runs the
GPU's work ended 0.948 refreshes after the vertical blank in the frames that were shown a refresh late and 0.949 in the frames
that were not. (A GPU end after the refresh a frame was aimed at is a miss for certain, and the swap interval rule counts it:
a frame or two later, when the time is known, and only where it is later by more than the margin of the GPU clock's place on
the CPU clock.)

Two things bear on every step, and are no answer alone:

- **A longer swap interval empties a queue by itself.** At two refreshes per frame the loop presents half as often as the
  display takes frames, so frames that wait are gone after as many frames. Frames piling up is a problem of a swap interval of
  one. (The first integration's runs agree as far as they go: no frame start to display time above two refreshes in the
  stretches at a swap interval of two.)
- **A wait until a swap chain image is free**, before the frame's work, is back-pressure: it holds the loop when the queue
  is full, so the queue sits full. On the one system measured, in a window, as many presents waited as the swap chain has
  images (3 with three images, 2 with two), steadily, with the most frames waiting and the longest time to the display of
  all cases (the first measurements below). Where a driver keeps an image until it left the display it would be two fewer.
  It makes a loop steady and it does not make the queue short, so it reaches no tier: the pacer can be told to use it, and
  promises nothing from it.

The reason for this order is the rule at the top: the calculation is the pacer's, the mechanism is the application's. A wait
on the display is a mechanism that needs no calculation, so it is preferred; each step down replaces it with a calculation
from less information.

## First measurements of the two waits

The first integration built both waits into its Vulkan host and ran them on 2026-10-07 (the same machine, driver and window
as before, 240 Hz, the driver's display times, 2,400 frames a run). **One run each: numbers, not conclusions.**

GPU work of 90 % of a refresh, a swap interval fixed at 1, the frame's start held:

| Run                                     | Earlier frames not yet shown at a frame's start | Frame start to display | Frame start to frame start | Shown for one refresh |
| --------------------------------------- | ----------------------------------------------- | ---------------------- | -------------------------- | --------------------- |
| No wait                                 | 1 (1,014 frames), 2 (1,321), 3 (60)             | 2 to 5 refreshes       | 1.00 refresh               | 1,737 of 2,117        |
| A wait for the present 1 back           | 0 (2,395)                                       | 2 (2,338)              | 1.94 refreshes             | 5 of 2,291            |
| A wait for the present 2 back           | 1 (2,352)                                       | 2 (2,312)              | 0.96 refresh               | 2,257 of 2,291        |
| A wait until the acquired image is free | 3 (2,234)                                       | 4 (2,242)              | 1.00 refresh               | 2,284 of 2,288        |

GPU work of 20 % of a refresh, the pacer on, the present held:

| Run                           | Earlier frames not yet shown at a frame's start | Frame start to display |
| ----------------------------- | ----------------------------------------------- | ---------------------- |
| No wait                       | 2 (2,380)                                       | 2 refreshes (2,377)    |
| A wait for the present 1 back | 0 (2,390)                                       | 1 refresh (2,329)      |

What these runs show, as far as one run of each goes:

- The wait for a present did what its documentation says: with it, the frames waiting stayed at the number asked for, from the
  first frames on and with no pause at start-up.
- Waiting for the present 1 back with work of 90 % of a refresh halved the frame rate: the wait returned after the display had
  taken the frame (a median of 1.0 ms after its first pixel, never before it, in the first integration's reading of 11,460
  waits), which left less than a refresh for the work. Waiting for the present 2 back kept one refresh per frame with one
  frame waiting. That is the choice of k in "Decisions needed".
- The wait for a free image held the loop on this system and steadied it, with the queue full at the image count: the most
  frames waiting and the longest time to the display of the four.
- With the wait, frame starts are uneven while the display times are a refresh apart, as the wait returns at a varying time
  after the display took the frame.

## First measurements of the first two tier pacers

The first two tier pacers (a timer with the refresh period only, `TimerPeriodOnlyPacer`; a timer with a wait for a present,
`TimerWaitForPresentPacer`) were put into the first integration's samples behind an option and run on 2026-10-07 next to
today's path: Vulkan in a window, 240 Hz, variable refresh off, three swap chain images, the driver's display times, 2,400
frames a run. **One run each: numbers, not conclusions.** "Waiting" is the earlier presents not yet shown when a frame starts;
"latency" is from a frame's start to its display, in refreshes, at the median.

A fixed 60 frames a second (four refreshes per frame, 1,071 frames):

| Run                                   | Shown for exactly four refreshes | Where in the refresh the present was made |
| ------------------------------------- | -------------------------------- | ----------------------------------------- |
| Today's path, held by a timer         | 1,052 (3 for three, 16 for five) | Anywhere: it slid through the refresh     |
| A timer, the refresh period only      | 1,071                            | 0.03 to 0.10 after a display time         |
| A timer, a wait for the last present  | 1,071                            | 0.29 to 0.54                              |
| A timer, a wait for the one before it | 1,071                            | 0.62 to 0.69                              |

Today's path ran 0.26 % slow, so its present slid through the refresh; the grid did not slide in any of the three. Where the
grid of the pacer without a wait sat was chance, as designed for that tier, and it sat close to a display time.

A frame that runs long (10 ms more CPU work every 120 frames, 18 of them, a swap interval fixed at 1):

| Run                              | What a long frame cost          | Latency in the frames after it                              |
| -------------------------------- | ------------------------------- | ----------------------------------------------------------- |
| Today's path                     | Two or three refreshes, varying | Another value after each one (1.1 to 2.2 over the run)      |
| A timer, the refresh period only | Exactly two refreshes, 18 of 18 | Back at the value from before, two frames later, every time |

Light work at one refresh per frame:

| Run                                   | Waiting | Latency | Frame start to frame start (1 % to 99 %) |
| ------------------------------------- | ------- | ------- | ---------------------------------------- |
| Today's path, the frame's start held  | 0       | 0.59    | 0.98 to 1.02                             |
| A timer, the refresh period only      | 2       | 2.21    | 0.98 to 1.03                             |
| A timer, a wait for the last present  | 0       | 0.74    | 0.76 to 1.36                             |
| A timer, a wait for the one before it | 0       | 0.52    | 0.99 to 1.02                             |

GPU work of 90 % of a refresh, a swap interval fixed at 1:

| Run                                   | Frame start to frame start                      | Waiting  | Latency (1 % to 99 %) |
| ------------------------------------- | ----------------------------------------------- | -------- | --------------------- |
| Today's path, the frame's start held  | 1.00                                            | 2 mostly | 2.52 (1.49 to 2.88)   |
| A timer, the refresh period only      | 1.00, with stalls of 2 to 4 refreshes 113 times | 1 to 3   | 1.91 (1.68 to 4.97)   |
| A timer, a wait for the last present  | 1.97                                            | 0        | 1.73 (1.45 to 1.86)   |
| A timer, a wait for the one before it | 1.00                                            | 1        | 1.68 (1.28 to 1.86)   |

What these runs show, as far as one run of each goes:

- **The grid does what it is for.** Frames held for four refreshes were shown for exactly four, and a frame that ran long cost
  whole refreshes and nothing after it.
- **The lowest pair's pacer keeps the place it began at, whatever that is.** With light work it kept two presents waiting for
  the whole run: the ones that pile up while a window is new. Today's path has a pause for that, and sat at none. This is the
  open decision about that tier at one refresh per frame, with a number on it.
- **With the GPU limiting the loop that pacer was the worst of the runs**: presents waiting crept up to the number of images,
  the GPU then could not begin a frame, and the loop stalled: 114 of 2,272 frames started more than half a period after the
  time they were given, in 53 stretches that came in groups. It is given the CPU's work only and can not see a refresh the
  display lost; both are named limits of it, and this is what they cost.
- **The wait for a present did what the simulation said**: 1.97 refreshes a frame when waiting for the last present at this
  work (the simulation: about 1.95) and 1.00 when waiting for the one before it.
- **Waiting for the one before the last was the steadiest and had the lowest latency with light work.** Every one of its waits
  returned at once after start-up, so it capped the frames waiting at start-up and then did not disturb the loop. Waiting for
  the last one held the loop every frame and made the frame starts uneven.
- **A wait for a present that is never shown runs into its timeout.** In the first 18 frames of a window some presents get no
  display time (1 to 3 in the eleven runs with a wait for a present, 2 to 8 in the eleven without). Where the pacer waited
  for such a one directly (waiting for the last present), the loop stood for the 250 ms of the timeout, once each in two of
  four runs.
- **With every frame a refresh late and the rule off, the animation ran at half speed.** Waiting for the last present at
  this work, each frame took two refreshes and its animation time advanced by one: the count of refreshes behind the clock
  grew by one a frame (2,457 at the end). An animation time that is never moved after a lost refresh is right for a refresh
  lost now and then, and wrong when every frame loses one and nothing slows the swap interval down.

### What was changed after these runs

Decided on 2026-10-07 from the numbers above, built, and checked on the simulation only. **None of it is measured yet.**

- **One pause after start-up in the lowest pair's pacer.** Half a second after the first frame of a start, the frame after
  is due four refreshes later than it would be, so the display takes the frames that piled up behind the first presents.
  Both numbers are settings (`StartupPauseDelay`, `StartupPauseRefreshes`; no refreshes is no pause) and both are a guess:
  the values are the ones that emptied the queue for today's path on the one system measured. It is never made before the
  system took a present, so a frame is on screen through it. It is made once per start and once per swap chain made anew (a
  present the system did not take, or the application's word), and not at all when the pacer is at two refreshes per frame
  or more at that moment, as the display has taken the waiting frames by then. Its cost where no frame waits: the frame
  before the pause is on screen for five refreshes, once (21 ms at 240 Hz, 83 ms at 60 Hz). On the simulation's display
  with two frames waiting from the start, they wait for the whole run without the pause and none waits after it.
- **The GPU's work as its own stretch of time, in both pacers** (`AddGpuWork`, "A frame's work is two stretches of time"
  below). A frame's work for the swap interval rule is the CPU's and the newest GPU time reported, the longer of the two
  where they lie side by side and the two added where one follows the other. They lie side by side when the newest report
  with an end time shows the next frame's start more than the frame margin before that end, or when the application says it
  lets two frames or more be in flight (`MaxFramesInFlight`), which it has to say for the case the times can not show: at a
  longer swap interval nothing overlaps. On the simulation: with GPU work of 130 % of a refresh and nothing that bounds the
  waiting frames, the lowest pair's pacer without reports stays at one refresh per frame while the frames fall ever further
  behind, and with reports goes to two; with CPU work of 74 % and GPU work of 72 % it stays at one refresh per frame with
  two frames in flight and goes to two with one, which is what the first integration's runs asked for.
- **This does not answer the run above where that pacer stalled.** That run had the swap interval fixed at one, and its GPU
  work of 90 % fits a refresh by any reading. What grew there is the number of frames waiting, from refreshes the display
  lost by itself, and that pacer does not learn of those: it stays what this pair can not do.
- **A loss that repeats is in the animation step.** When the frame before took refreshes more than it was given and the one
  before that did too, the display shows every frame for that much longer, and the step is longer by the fewer of the two
  losses. One lost refresh is still not caught up with, and a swap interval the rule just changed is its answer to the
  losses before it. On the simulation the case that ran at half speed (a wait for the last present, GPU work of 90 %, the
  rule off) keeps up with the clock from the third frame on. A loss every second frame (frames 1.5 refreshes apart) is not a
  loss that repeats by this rule, and the animation still runs slow there until the swap interval rule slows down.
- **The longest a wait for a present may take is counted in the frame's own swap intervals** (`PresentWaitSwapIntervals`,
  four by default) in place of 250 ms: 17 ms at 240 Hz and one refresh per frame.

One thing the simulation showed on the way, **not measured**: with two frames in flight and work close to a refresh on both
the CPU and the GPU, a frame is on screen two refreshes after its start, and a wait for the present before the last (one
present allowed to wait) then holds the loop off its step for about 8 % of the frames. With two presents allowed to wait the
wait returns at once and the loop holds one refresh per frame. So the presents that may wait and the frames in flight are not
independent: see decision 1.

## Second measurements of the two tier pacers

The same cases again on 2026-10-07 with the changes above, on the same system: Vulkan in a window, 240 Hz, variable refresh
off, three swap chain images, the driver's display times, 2,400 frames a run (1,200 at 60 frames a second), the first 120 and
the last 8 left out, the GPU's work reported to the pacer. **One run each: numbers, not conclusions.** "Waiting" and "latency"
as before; "first" is the same case in the first measurements.

A fault of the sample in these runs: it told the pacer that two frames may be in flight in every run, and its host had one
in 24 of the 27. That changes only the work the swap interval rule judges. The two runs it decides (CPU work of 74 % and GPU
work of 72 % with one frame in flight, the rule on) are left out below until they are run again; in them the rule went back
and forth between one and two refreshes per frame (about 1,660 frames at two and 610 at one), which is what a wrong word
for the frames in flight costs.

Light work, the rule on (it stayed at one refresh per frame in every run):

| Run                                   | Waiting            | Latency (1 % to 99 %) | Frame start to frame start (1 % to 99 %) | First   |
| ------------------------------------- | ------------------ | --------------------- | ---------------------------------------- | ------- |
| Today's path, the frame's start held  | 0                  | 0.68 (0.66 to 0.70)   | 0.99 to 1.02                             | 0, 0.59 |
| A timer, the refresh period only      | 1                  | 1.07 (1.04 to 1.09)   | 0.98 to 1.02                             | 2, 2.21 |
| A timer, a wait for the last present  | 0                  | 0.74 (0.49 to 0.89)   | 0.76 to 1.36                             | 0, 0.74 |
| A timer, a wait for the one before it | 0 (2,035), 1 (237) | 0.85 (0.82 to 1.83)   | 0.93 to 1.10                             | 0, 0.52 |

The lowest pair's pacer made its pause once (the frame after it started 4.63 refreshes after the one before). The one present
it counts as waiting is not a frame behind another: by the medians its frames started 0.93 of a refresh after a display time
and were presented 0.95 after one, were not shown at the display time 0.05 of a refresh later, and were shown at the one
after it. So each frame was still on its way when the next one started. Where the grid sits against the display is chance at
this tier, and in this run it sat at the worst place.

A fixed 60 frames a second (four refreshes per frame, 1,071 frames):

| Run                                   | Shown for exactly four refreshes | Where in the refresh the present was made | First                  |
| ------------------------------------- | -------------------------------- | ----------------------------------------- | ---------------------- |
| Today's path, held by a timer         | 1,052 (3 for three, 16 for five) | Anywhere: it slid through the refresh     | The same               |
| A timer, the refresh period only      | 888 (92 for three, 91 for five)  | 0.88 to 0.95 after a display time         | 1,071, at 0.03 to 0.10 |
| A timer, a wait for the last present  | 1,071                            | 0.28 to 0.53                              | 1,071, at 0.29 to 0.54 |
| A timer, a wait for the one before it | 1,071                            | 0.44 to 0.51                              | 1,071, at 0.62 to 0.69 |

The grid of the pacer without a wait landed right before a display time this time, and its presents fell on either side of
it. No pause was made, as designed at two refreshes per frame or more.

A frame that runs long (10 ms more CPU work every 120 frames, 18 of them, a swap interval fixed at 1):

| Run                                   | Waiting            | Latency (1 % to 99 %) | The frame before, the long frame and the frame after were shown for |
| ------------------------------------- | ------------------ | --------------------- | ------------------------------------------------------------------- |
| Today's path, the frame's start held  | 1 (2,152), 0 (120) | 1.54 (0.65 to 1.96)   | 3, 1, 1 (11 times); 2, 1, 1 (6); 4, 1, 1 (1)                        |
| A timer, the refresh period only      | 0                  | 0.93 (0.91 to 0.95)   | 3, 1, 1 once; 17 times the long frame has no display time           |
| A timer, a wait for the last present  | 0                  | 0.74 (0.49 to 0.96)   | 3, 1, 1 (9); 4, 1, 1 (9)                                            |
| A timer, a wait for the one before it | 0 (1,778), 1 (494) | 0.79 (0.72 to 1.89)   | 3, 1, 1 (15); 2, 1, 1 (2); 4, 1, 1 (1)                              |

In 17 of the 18 cases of the pacer without a wait the long frame's present was reported without a display time, and the
display times before and after it are four refreshes apart. The log shows why: in each of the 17 the frame after the long one
started 0.24 to 0.27 of a refresh after the long frame's present and was presented 0.26 to 0.32 of a refresh after it, both
before the same display time. The pacer had let that frame start at once, as less than half a period late for its step.
Whether the long frame was shown is not known without a capture. In the one other case the pacer waited for the next step,
the next present came 0.58 of a refresh after the long frame's, and all three frames have display times. The latency was 1.27
for the frame after a long one and 0.93 from the second on, every time. This is a fault of the rule, changed below.

GPU work of 90 % of a refresh, a swap interval fixed at 1:

| Run                                   | Frame start to frame start | Waiting           | Latency (1 % to 99 %) | First                    |
| ------------------------------------- | -------------------------- | ----------------- | --------------------- | ------------------------ |
| Today's path, the frame's start held  | 1.00                       | 3 mostly          | 3.73 (2.06 to 3.75)   | 2 mostly, 2.52           |
| A timer, the refresh period only      | 1.00 (0.96 to 1.05)        | 2 (2,215), 1 (57) | 2.15 (1.80 to 2.17)   | 1 to 3, 1.91, 113 stalls |
| A timer, a wait for the last present  | 1.97                       | 0                 | 1.73 (1.45 to 1.87)   | The same                 |
| A timer, a wait for the one before it | 0.97 (0.93 to 1.35)        | 1                 | 1.70 (1.36 to 1.88)   | The same                 |

The pacer without a wait did not stall in this run: no frame started more than 1.50 refreshes after the one before it,
outside the pause. Nothing that was changed explains that by itself (the swap interval is fixed and the work fits), and
today's path at the same settings sat at three frames waiting this time and at two in the first run. With a wait for the last
present the animation kept up with the clock: the refreshes behind it were 6 at the end of the run, 5 of them from before
frame 120 (2,457 in the first run).

CPU work of 74 % and GPU work of 69 to 75 % of a refresh, the rule on, two frames in flight:

| Run                                           | Swap interval | Frame start to frame start (1 % to 99 %) | Waiting  | Latency (1 % to 99 %) |
| --------------------------------------------- | ------------- | ---------------------------------------- | -------- | --------------------- |
| A timer, the refresh period only              | 1 throughout  | 1.00 (0.98 to 1.02)                      | 2 mostly | 2.76 (1.74 to 2.78)   |
| A timer, a wait, one present allowed to wait  | 1 throughout  | 1.00 (0.98 to 1.02)                      | 1        | 1.71 (1.69 to 1.74)   |
| A timer, a wait, two presents allowed to wait | 1 throughout  | 1.00 (0.98 to 1.02)                      | 2        | 2.46 (2.44 to 2.48)   |

Both pacers held one refresh per frame at this work with the rule on, where today's rule goes to two. What the simulation
showed for a wait with one present allowed to wait (the loop held off its step for about 8 % of the frames) did not show:
2,270 of the 2,272 waits returned in under an eighth of a refresh. A frame was on screen 1.7 refreshes after its start
here and two in the simulation's display, so the present waited for had been shown.

The wait for a present, over the 14 runs with one: no wait returned before the display time of its present. Where every wait
held the loop it returned a median of 0.92 to 1.24 ms after it (1 %: 0.08 to 0.09 ms, 99 %: 2.10 to 2.28 ms). A wait that ran
out took 4.03 to 4.44 refreshes at one refresh per frame and 16.0 at 60 frames a second, and the loop no longer stood for a
quarter of a second (the host's own wait on today's path stood for 60 refreshes once). It now runs out with two or three
presents allowed to wait as well, once at start-up in five of the seven such runs and in none in the first measurements: each
time the present waited for was one of two or more in a row without a display time, within the first 15 frames.

### What was changed after the second runs

Built and checked on the simulation only. **Not measured.**

- **After a present that was made late, the next present comes a whole period later** (the pacer without a wait). A frame
  that ran long is presented somewhere in a later step of the grid. The next frame takes the next step only when that present
  was made no later in its step than the last present that was on time, and else the step after. Two presents less than a
  period apart can reach the display between the same two refreshes, and one of them then waits for as long as the loop runs
  at one refresh per frame: that is the frame waiting for good that the grid was meant to prevent, and the rule "a start
  less than half a period late keeps its step" let it through after a long frame. On the simulation a frame that ran long
  now leaves as many frames waiting as before it wherever the grid sits against the display (ten places, six lengths). The
  price: after most long frames the next frame starts a refresh later than the next step, so the long frame is on screen
  for two refreshes where one would have done on that display. A late start after a present that was on time keeps its step
  as before. A frame that is presented in time and ready too late for its refresh stays what this pair does not learn of.
- **A later GPU work report for the same frame takes the place of the first**, for an application that learns how long the
  GPU worked before it learns when.
- **A short form of each tier's description**, one line of 44 characters or less.

## Third measurements: the two aims

Two more sets on 2026-10-07 on the same system (Vulkan in a window, 240 Hz, variable refresh off, three swap chain images,
one frame in flight and the pacer told so, the driver's display times, 2,400 frames a run, the first 120 and the last 8 left
out). **One run each: numbers, not conclusions.**

**The refresh period.** The pacer was given 41,664 ticks of 100 ns in every run so far. The display's mode says 240.016 Hz,
a period of 41,663.889 ticks, and the first integration's framework rounds that to whole ticks (2.7 parts in a million). The
display times of five runs give 41,664.60 to 41,664.67 ticks per refresh: 14 to 16 parts in a million more than the pacer was
given, and 17 to 19 more than the mode says. The presentation engine's own clock gives the same, and the frame starts were
exactly on a grid of the period given, so it is neither how the times are placed on the clock nor the loop. The display
refreshes that much slower than its mode says, by the clock the loop waits on. That is the slow rise of the latency in the
earlier runs. No number a system reports at start-up contains it, in whatever unit: a period this exact has to be measured
over many refreshes, from vertical blank times or display times of the display the window is on, against that clock.

**Low latency, the cases that were owed** (six runs, with the rule after a late present):

- CPU work of 74 % and GPU work of 72 % of a refresh, one frame in flight, the rule on: both pacers went to two refreshes per
  frame within the first 50 frames and stayed, 2,268 of 2,272 frames on screen for exactly two refreshes.
- Long frames, the pacer without a wait: all 18 long frames have a display time (1 of 18 before the change), and the frame
  before, the long frame and the frame after were on screen for 3, 2 and 1 refreshes, 18 of 18, with a fixed swap interval
  and with the rule on. The latency after a long frame is what it was before it (1.01). The price is as said: the frame
  after a long one started 4.0 refreshes after the long one began, and the animation time fell behind the clock by three
  refreshes per long frame where it was two.
- In the same two runs the grid sat with the presents at a display time (0.98 and 0.02 of a refresh after one), and apart
  from the long frames 18 or 19 frames have no display time and 37 or 38 display times are two refreshes apart. The place of
  the grid again.
- Light work, three starts of the pacer without a wait: a frame was on screen 1.07, 1.80 and 1.11 refreshes after its start,
  steady within each run. In the run with 1.80 the pause was made and a present still waited for nearly the whole run. Not
  looked into. The pause is a guess, and one start of three shows it.

**Smoothness** (14 runs). Light work, the rule on:

| Run                                                 | Waiting | Latency (1 % to 99 %) | On screen for one refresh | Frames given a time to start at |
| --------------------------------------------------- | ------- | --------------------- | ------------------------- | ------------------------------- |
| A timer, the period only, low latency (same commit) | 0       | 0.65 (0.63 to 0.67)   |                           | 2,272 of 2,272                  |
| A timer, the period only, a reserve of one          | 2       | 2.67 (2.57 to 2.70)   | 2,272 of 2,272            | 0 of 2,272                      |
| A timer, the period only, a reserve of two          | 2       | 2.67 (2.57 to 2.70)   | 2,272 of 2,272            | 0 of 2,272                      |
| A timer, a wait for a present, a reserve of one     | 1       | 1.74 (1.48 to 1.92)   | 2,272 of 2,272            | 927 of 2,272                    |

The pacer without a wait did not pace these two runs: the swap chain did. Start-up left more frames in the swap chain than
the pacer counts, its three images were full with two frames waiting, and the application's own wait for the frame before
let one frame through per refresh. That wait is the host's wait for the GPU to finish the frame before, as it has one frame in
flight: it took 0.93 of a refresh per frame in these runs and 0.002 in the run with low latency, while the GPU's work itself
is 0.20 of a refresh, which fits the earlier finding that the GPU begins a frame only when its image is free. The acquire did
not wait (0.001). A loop that is behind by no more than the reserve gets no
time to start at, as it is meant to make frames back to back until it is ahead again; this loop could not, and stayed
exactly its reserve behind its grid for the whole run (the next frame's time was 0.17 of a refresh before a frame's start
with a reserve of one and 0.80 with a reserve of two). So two frames waited with either setting, and the setting did
nothing. The result of the run is a full queue's: every frame on screen for one refresh, and no slow rise of the latency
(the medians of the run's quarters are 2.669, 2.675, 2.669, 2.672), as a loop held by the swap chain runs at the display's
own rate. With the wait for a present one frame waited, as asked.

Long frames (10 ms more CPU work every 120 frames, 18 of them, a fixed swap interval):

| Run                                             | Waiting  | Latency | Refreshes a frame was repeated for at a long frame                                  |
| ----------------------------------------------- | -------- | ------- | ----------------------------------------------------------------------------------- |
| A timer, the period only, low latency           | 0 or 1   | 1.19    | 3 (the frame before for 3 refreshes, the long frame for 2), 18 of 18                |
| A timer, the period only, a reserve of one      | 2 mostly | 2.47    | 1 (the frame before for 2), 18 of 18, if the frame without a display time was shown |
| A timer, the period only, a reserve of two      | 3 mostly | 3.30    | 0, 18 of 18, if the two frames without a display time were shown                    |
| A timer, a wait for a present, a reserve of one | 1        | 1.74    | 2 in 9 cases, 1 in 9                                                                |

With a reserve, the frames that reach the display while the loop is inside the long frame have no display time: one per
long frame with two frames waiting, two with three. The display times on either side of them are exactly as many refreshes
apart as there are such frames and one more, which fits each of them being on screen for one refresh, and fits a frame
skipped as well: the log can not say which. The times were not lost on the way: the sample read a result for every one of
these presents, three or four frames after the present as for the frames that have a time, and the result carried none. The
driver reported the present and gave no time for it. Read the table with that. After a long frame the loop made one frame back to back with a reserve of one and
two with a reserve of two, as designed. Again more frames waited than the reserve asked for.

GPU work of 90 % of a refresh, a fixed swap interval of one:

| Run                                             | Waiting             | Latency (1 % to 99 %) | On screen for one refresh                                  |
| ----------------------------------------------- | ------------------- | --------------------- | ---------------------------------------------------------- |
| A timer, the period only, a reserve of one      | 3 mostly            | 3.71 (2.22 to 3.73)   | 2,268 of 2,272                                             |
| A timer, the period only, a reserve of two      | 3, 2 and 1 by turns | 3.06 (1.77 to 4.98)   | 2,090 of 2,124 that can be compared; 86 presents not timed |
| A timer, a wait for a present, a reserve of one | 1                   | 1.69 (1.26 to 1.94)   | 2,263 of 2,272                                             |

With a reserve of two the pacer without a wait was not steady: the same full swap chain, without a clean hold.

A fixed 60 frames a second, where there is no reserve: the pacer without a wait had 855 of 1,072 frames on screen for exactly
four refreshes in one run (the present 0.89 of a refresh after a display time) and 1,072 of 1,072 in the other (0.58); the
pacer with a wait 1,072 of 1,072 (0.60). The place of the grid, as with low latency, and this run shows the line it
crossed. For its first 200 frames the present was called 0.92 of a refresh after a display time and every frame was on screen
4.2 refreshes after its start; as the display fell behind the grid the present moved to 0.90 and then 0.87, and from frame
322 on the latency went back and forth between 4.2 and 3.2 refreshes, 217 times. A present called 0.92 of a refresh after a
display time was never shown at the next one, and one called 0.87 after it mostly was. At 15 parts in a million the grid moves
a whole refresh against the display in about four and a half minutes at 240 Hz, so a present held by a timer crosses that
line that often, for some seconds each time, wherever it started.

What these runs show, as far as one run of each goes:

- **Frames that wait do cover a long frame**, by about as many refreshes as wait, if the frames without a display time were
  shown.
- **Without a wait for a present the number that wait is not the pacer's.** It asked for one and two, and two and three
  waited: what start-up leaves comes on top, and the swap chain's size is the limit. With the wait it was what was asked for
  in every run.
- **A loop held by a full swap chain is paced by the display** and does not drift, which a grid on a given period does.
- **The pacer does not know when the system holds the loop.** It is not told of the application's own waits, nor how many
  frames the swap chain holds, so it can not tell a loop that is held from one that is late, and can ask for a reserve the
  swap chain can not take. Both are in "Decisions needed".

## A window that is not shown

Found on the first integration (Windows, 240 Hz at a fixed refresh rate, Vulkan FIFO with `VK_KHR_present_wait2`, light
work, one run of each case; the loop's own times, no display times).

**What the system does.** While another window lies over the application's, a wait for a present does not return before its
time runs out. The present and the acquire return at once and report success, and nothing tells the application: there is no
event for "covered". Losing the focus with the window still in view changed nothing (240 frames in every second, a swap
interval of one), so it is the cover and not the focus.

**What the pacers did**, the window covered for 15 s:

| Pacer                              | While covered                                                                                           | After the window was back                         |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------- |
| A timer, the refresh period only   | Not affected: 240 frames in every second                                                                | Nothing to recover from                           |
| A timer, a wait for a present      | 129 waits ran out; the swap interval went from 1 to 13, a frame every 46 ms at first, 280 ms at the end | A frame every 54 ms for about 2 s, then back to 1 |
| Today's pacer, the host's own wait | 55 waits ran out; the swap interval went from 1 to 8, a frame every 265 to 281 ms                       | A frame every 33 ms for about 2 s, then back to 1 |

**The fault** was the pacer's: it asked for the wait, the wait ran out, the frame started late because of it, and the pacer
counted that frame as late. Enough of those and the rule slowed down, which made the longest time of the next wait longer,
as that is counted in the frame's swap intervals.

**What was changed** (built, checked by unit tests and the simulation, not measured):

- **A frame that the pacer's own wait held until it ran out is not judged**: it is not late and not in the frame window,
  and the grid goes on from where that frame starts. A wait is not work, and a wait the pacer asked for least of all. The
  swap interval then does not change in such a stretch, and the wait's longest time with it.
- **After two waits in a row that ran out the pacer stops waiting** (`PresentWaitsStopped`): one by itself happens, at the
  start of a window, and two in a row is a display that does not take this window's frames. The frames are then paced on the
  timer at the swap interval they had. Each frame's plan still names a present, with a longest time of zero: the application
  does not wait, it only asks whether that present was shown and reports the answer. The present asked after is one that has
  had the time a wait would have given it, and none from before the first wait that ran out. The first one that was shown
  ends it, and the next frame waits as before.
- So a window that is covered costs two waits (eight refreshes at one refresh per frame) and then runs at its frame rate,
  and when it is back the pacer is where it was: the frame window and the swap interval are what they were before.

**A hint from the application: left for later.** Asked: should the pacer take a hint that the focus was lost, and start
again with an empty frame window when it is gained? Decided on 2026-10-07: the change above is what the pacer starts with,
and no hint is taken yet. What speaks against a hint as the fix, and for it as a help later:

- Focus is not what was measured to matter: the window lost it in view and nothing changed, and a window that stays on top
  covers another one without taking its focus.
- Starting again has a cost: the frame window holds what the rule learnt (a game that needs two refreshes per frame), and
  an application that started again at every gain of focus would be late for the frame window's length each time.
- With the change above there is nothing wrong left to clear: no frame of the covered stretch was judged.
- As a help it is worth taking where a platform has it (hidden, shown, minimised, focus): it would save the two waits.
  `Reset` is there today for an application that wants to start again.

**Today's pacer (`FramePacer`) is unchanged**: its host's wait is inside the frame it measures, and it is being replaced.

## The pacer for vertical blank times

The third tier pacer (`VBlankPeriodOnlyPacer`: the frame loop holds a frame and knows where the refreshes are; the refresh
period only for the frames that wait). Built, and checked on the simulation only: **not measured.**

**What knowing the refreshes changes.** The application gives the pacer a vertical blank's time whenever it has one
(`AddVBlank`), and the pacer keeps the display's vertical blanks from the newest reading. Every frame is then for one
vertical blank, the one it is to be shown at, and that blank is the one of the frame before it plus its swap interval.
Nothing slides: each reading puts the frames back on the display, so a refresh period that is a little off does not add up.
A frame is shown at a vertical blank when it is ready the frame margin before it; ready is presented, and with GPU work
reports the GPU done with it. The pacer aims a frame to be ready at one place in the refresh before its blank
(`ReadyPlacePercent`, 50 by default: the middle is as far from either vertical blank as a frame can be, which is the only
default that does not come from one system). The one thing that stays a guess is how long before a vertical blank a display
takes a frame.

**The two aims are when the frame is made.** That is what the first integration's two profiles were, and here it is the aim
and no setting of its own:

| Aim         | When a frame is made                                                                                  | What it gives                                                                                                |
| ----------- | ----------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Smoothness  | It starts at once, and its present is held until the time that has it ready at its place              | Work that runs longer than usual uses up the time the frame would have waited; the frame shown is older      |
| Low latency | Its start is held so that it is ready at its place and no sooner, and it is presented when it is done | The frame shown is as new as it can be; a frame that takes longer than the ones before it can miss its blank |

- **Smoothness** also keeps the reserve at one refresh per frame, as the other pacers do: the presents that may wait less
  one are ready that many refreshes before they are shown. Here the reserve is exact, because the pacer knows which vertical
  blank a frame was ready for.
- **Low latency** holds a frame's start by how long the frames before it took: the longest of the last eight, but no
  longer than the swap interval's time, and the GPU time where it is reported. A frame that is done sooner than that is
  presented no sooner than has it ready when its refresh begins. A frame that missed its blank and was ready late in a
  refresh is not counted on to have made the next one. It makes the one pause after start-up, as the pacer on a timer does.
- **A frame of more than one refresh** is for every nth vertical blank. With low latency it is started in the refresh
  before its vertical blank and presented at once, so it is on screen about as soon after its start as a frame of one
  refresh is, where a pacer that holds the present has made it that many refreshes ahead. With smoothness it is made at once
  and its present held; there is no reserve, as the display takes a frame before the next one is made.

**The animation time is the time a frame is shown, as far as that is known before the frame is made.** A frame that starts
too late for its vertical blank is for the first one it can make, and its animation step is the refreshes from the frame
before it to that blank: nothing is behind, and the same holds for the pause after start-up. What is only known after a
frame (its own work ran long, and it missed its blank) is not caught up with, as at every tier.

**What the simulation shows** (240 Hz, light work unless said, displays that take a frame up to 0.4 of a refresh before
the vertical blank):

| Case                                                            | A timer, the period only                 | Vertical blank times, low latency          | Vertical blank times, smoothness                    |
| --------------------------------------------------------------- | ---------------------------------------- | ------------------------------------------ | --------------------------------------------------- |
| A frame's start to its display                                  | 1.0 refresh, or 2.0, by where it started | 0.65                                       | 1.5 without a reserve, 2.5 with one frame           |
| A display 0.2 % slower than its mode                            | The frames waiting grow without end      | Steady                                     | Steady                                              |
| 60 frames a second, a display 0.05 % slower, timers 0.1 ms late | Frames on screen for three or five       | Every frame for four, 0.65 after its start | Every frame for four, 4.5 after its start           |
| One frame 0.9 of a refresh longer                               | A repeated frame at most places          | A repeated frame, then as before           | Not seen with a frame in reserve                    |
| One frame 2.4 refreshes longer                                  | Repeated frames, then as before          | Repeated frames, then as before            | One frame on screen longer, then the reserve as was |
| GPU work of 90 % of a refresh, reported                         |                                          | Every frame for one refresh                | Every frame for one refresh                         |
| GPU work of 130 % of a refresh, reported, the rule on           |                                          | Two refreshes per frame                    | Two refreshes per frame                             |

**Not in it yet.** The refresh period is the settings': a reading's own period is not used, and the pacer does not measure
the period from the readings (it does not need to, for where its frames are; the animation time still advances by the
period it was given, 17 to 19 parts in a million off on the one system measured). It does not learn of a frame that waits
although it was ready in time, as no pacer does that has the refresh period only for that. The application's own waits and a
swap chain that holds the loop are decision 6.

### Next: vertical blank times with a wait for a present (proposed, not built)

The fourth tier pacer, written down with both aims before any code, for agreement. It is the pacer above plus the wait
that the pacer on a timer has (the frame start plan's wait, `PresentWaitReport`, the presents that may wait, the longest wait counted
in the frame's swap intervals).

**What the wait adds when the vertical blanks are known:**

- **The frames that wait can not grow**, whatever the pacer believed of a frame: the loop stands until the present so many
  back was shown. The pause after start-up is not needed, as in the pacer on a timer with a wait.
- **Which vertical blank a frame was shown at becomes a fact, some frames later.** A wait that held the loop returns
  shortly after the display took the frame (a median of 0.92 to 1.24 ms after its display time in the 14 measured runs at
  240 Hz, which is 0.22 to 0.30 of a refresh, and never before it), so the vertical blank at or before the return is the
  one the frame was shown at. A wait that returned at once says less: the frame was shown at that vertical blank or an
  earlier one. The pacer above has to take a frame as shown where the frame margin says; this one corrects that for the
  frames not yet started when the report comes. That closes the one gap named above: a frame that waited although it was
  ready in time.
- **It is not used to move where a frame is to be ready.** The returns would show at which places frames make their vertical
  blank, and a pacer could move `ReadyPlacePercent` by that. Not proposed for the first version: one system was measured.

**The two aims:**

| Aim         | The wait                                                                              | The frame                                                                                |
| ----------- | ------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Smoothness  | Before the frame starts, for the present as many back as may wait                     | Starts when the wait is over; its present is held to its place, the reserve as above     |
| Low latency | Before the frame starts, for the present as many back as may wait: one is the default | Its start is then held as above (the plan is made again after the wait), present at once |

- With **smoothness** the reserve is the presents that may wait less one, as everywhere, and the wait makes it exact: where
  the pacer's count of vertical blanks is off by one, the wait holds the loop and the report puts the count right.
- With **low latency** the wait for the last present returns a quarter of a refresh into the refresh the frame is made in
  (on the one system measured), before the time the frame's start is held to in light work. Where a frame's work is long
  enough that its start would be before the return, the frame starts when the wait is over and is for the next vertical
  blank: that is the cost of one present allowed to wait, as with a timer (1.97 refreshes a frame at GPU work of 90 %), and
  two allowed to wait is what keeps one refresh per frame there.
- **The animation time** is as above, with the vertical blank the frame before was shown at taken from a wait's report when
  it is there. What a report says of a frame after the frames behind it were made is not caught up with.

**To decide before it is built:** whether the default of the presents that may wait differs by aim here (one with low
latency, two with smoothness), where today one setting with a default of two serves both.

## Every time is in nanoseconds

Decided on 2026-10-07 and built: the whole pacer module counts in nanoseconds, the unit in which no platform loses
anything (a platform that counts in ticks of 100 ns multiplies by 100; one that counts in nanoseconds lost up to 99 ns on
every value it put into ticks, and for a refresh period that is a rate error: 21 parts in a million for the measured
display's 4,166,389 ns when cut to a tick).

- A point on the application's steady clock is a `NanosecondTickCount`, a span a `NanosecondTimeSpan`, a length of time
  that can not be negative a `NanosecondTimeDuration`, and a value for one of the marker's 32-bit fields a
  `NanosecondTimeSpan32`.
- `RefreshPeriod` is exact to 2^-32 of a nanosecond: from a rate (`FromRate`), or from a period in whole nanoseconds as a
  platform gives one (`FromNanosecondTimeSpan`).
- A value that does not fit a 32-bit field of the marker (4.29 s) is capped, never cut and never an error: the CPU busy
  time at the field's largest value, a frame time one below it, as the largest says "on demand" there.
- The pacer's own golden files are in nanoseconds, and row for row what they were in ticks: every decision the same, every
  time within 33 ns of the tick it was. The first integration's frame logs, and the two real logs kept as test data, stay in
  ticks as they were recorded, and are made nanoseconds where they are read.
- The marker's payload still takes tick types until the marker itself counts in nanoseconds; until then an application
  converts where it fills the payload (`ToTimeSpan`, `ToTickCount64`).

## What the application plugs in

### Capabilities

One type, a set of capabilities, in generic terms:

| Capability             | The application can …                                                                                                                         | Examples (not named in the API)                                                      |
| ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `PresentSwapInterval`  | present with a swap interval of 1 to a maximum it gives: the present holds the frame that long                                                | `eglSwapInterval`, DXGI's sync interval                                              |
| `PresentAtTime`        | give a present a time before which the frame is not shown                                                                                     | `VK_EXT_present_timing` (absolute), `EGL_ANDROID_presentation_time`                  |
| `PresentAfterDuration` | give a present a time the frame before it stays on screen at least                                                                            | `VK_EXT_present_timing` (relative), Metal's minimum duration                         |
| `WaitForPresent`       | wait until a present it names was shown, with a timeout                                                                                       | `VK_KHR_present_wait2`, DXGI's waitable swap chain                                   |
| `WaitForGpuWork`       | wait until the GPU finished a frame it names, with a timeout                                                                                  | a fence                                                                              |
| `WaitForImage`         | wait until an image of its swap chain is free to draw into, with a timeout, and say how many images there are                                 | the fence of a Vulkan acquire                                                        |
| `FrameCallback`        | say when the window system called for a new frame                                                                                             | Wayland's frame callback                                                             |
| `VBlankTimes`          | say when a vertical blank was, and the period between them                                                                                    | DXGI's output, Wayland's presentation feedback, Choreographer                        |
| `GpuWorkTimes`         | say when the GPU began and ended its work on a frame, on the application's clock, frames later: with the CPU's own times, how the two overlap | calibrated timestamp queries                                                         |
| `GpuWorkDurations`     | say how long the GPU worked on a frame, frames later: how long, not when                                                                      | timer queries                                                                        |
| `DisplayTimes`         | say when a present was shown, or that it has a result without a time, frames later                                                            | `VK_EXT_present_timing`, DXGI's frame statistics, `EGL_ANDROID_get_frame_timestamps` |

Every application has the baseline, which is no capability: a steady clock it reads, the refresh period of the display its window is on, a wait
until a time on that clock, a present that shows every frame in order for at least a refresh, and two places to wait (before
a frame takes anything, and before its present).

The lowest configuration the design is made for is a little above that baseline: a graphics API without any timing extension,
on a window system that gives its core signals only (a refresh rate and a call for a new frame). Such a call can stop or come
late (for a window that is hidden), so the pacer uses it only while it comes about once per shown frame, and a wait on it has
a timeout.

The application gives the pacer two sets:

- what it **has**: when the pacer is made, and again whenever it changes (a swap chain made anew, the first vertical blank
  time, a swap interval the system refused). With it, as far as it knows them, how many frames it lets be in flight (how far
  its CPU may be ahead of its GPU) and how many images its swap chain has;
- what is **active** now, a subset. This is how an application controls the pacer: a capability that is not active is not used
  and not expected. It replaces the sample's "hold method" option, and it is how a test compares two ways on one system.

### Only what is certain to be of the window's display

The pacer uses nothing about a display unless it is certain to be about the display the application's window is on. This is a
condition of every capability and every value that says something about a display, and it is the application's to meet:

- **The refresh period** is that display's. Not the period a swap chain reports, which on a desktop with displays at different
  rates was measured to be the fastest display's, and not the primary display's where the window is on another.
- **`VBlankTimes`** are that display's vertical blanks. An application that can not be sure of it does not have the capability.
- **A window that moves to another display** changes the period and takes the capability away until the application has times
  of the new one.
- **Display times and the return of a wait for a present** are about the application's own presents, so they are of its
  display by what they are.

The pacer still checks what it is given (readings that do not agree with each other or with the period are not used), but
that check does not make a reading the right display's: readings of another display at the same rate agree with each other
perfectly. So the certainty has to come with the value, and where a platform can not give it, the value is not given.

### Not the least that every platform has

The capabilities and the reports are not cut down to what the poorest platform can give. Where a platform gives more, and a
rule of the pacer or a reader of a log can use it, the application passes it on and the pacer reacts to what it gets. Three
things keep that from becoming a pile of fields:

- **A report takes the most a platform has and says what is absent.** The GPU's work is its begin and its end where a platform
  has both, its end and its length where it has those, its length alone otherwise, and the pacer does with each what that one
  allows. Nothing is rounded down to the least of them.
- **What comes in has a use that is named.** A value is taken when a rule uses it or when it explains a run in a log. A value
  nobody reads is not taken because a platform happens to have it.
- **More never changes what less means.** A pacer given less paces as its tier says; given more it does better, and its
  rating says which.

What Vulkan gives beyond what the proposal takes so far, and what it would be used for:

| What it gives                                                                                                                                               | Use                                                                                                                                                                                        |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| The stages of a present, each with its time: its queue operations ended, it was taken from the queue, its first pixel went out, its first pixel was visible | The time a present was taken from the queue says where a frame waited, which is what is not understood about a frame shown a refresh late. A display report gets the stages a platform has |
| The return of a wait for a present                                                                                                                          | A coarse display time where there are no display times: measured at a median of 1.0 ms after the first pixel, never before it                                                              |
| How far the GPU's clock may be off the CPU's when the two are read against each other                                                                       | The margin below which a GPU moment and a CPU moment count as the same                                                                                                                     |
| The refresh period as the swap chain reports it                                                                                                             | None in the pacer: it is not certain to be the window's display's (measured: the fastest display's of the desktop). It goes into the log                                                   |
| The index of the image a frame drew into, and the number of images                                                                                          | Whether a frame's GPU work began late because its image was not free: the brake seen from inside                                                                                           |

### Whether the present holds the loop

It is a property of the present, as a swap interval or a target time is, and it is given the same way: as capabilities, in
the same sets. It is another side of the present than those, though. A swap interval or a target time says when the
presentation side shows the frame; this says when the call gives the loop back. One present has both: DXGI's present with a
sync interval holds the frame for its refreshes and blocks the caller, and the timed present that was measured holds the frame
and returns at once. So it is no hold tier, and it sits beside whichever of them the set has. There are two capabilities, and
a set has one of them or neither:

| The present …                            | When an application can say so                                                                    | Examples                                                                                                                                                                          |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `PresentWaits`: it waits for the display | Its platform documents that the call blocks until there is room, and it has not switched that off | DXGI's default: "the system blocks the thread until it is done presenting a prior frame"                                                                                          |
| neither: it may wait                     | The platform leaves it open, which is the common case                                             | Vulkan: a present "may block, but must return in finite time"; EGL's swap: its default swap interval of 1 synchronises the swap to a video frame, and nothing says the call waits |
| `PresentReturnsAtOnce`: it does not wait | It asked for a present that returns at once, or it waits elsewhere for the display                | DXGI's do-not-wait flag and its waitable swap chain; a present mode without vertical sync                                                                                         |

The same for the call that gets the next image, where a platform has one that can wait (Metal's does, a Vulkan acquire
may): `AcquireWaits`, `AcquireReturnsAtOnce`, or neither.

Why the pacer needs it:

- **A wait is not work.** Time spent blocked in the present or the acquire is taken out of the frame's CPU time, or the swap
  interval rule slows down a loop that was only waiting.
- **Two waits for one refresh.** Where the present waits for the display, the loop is already held to it, and the pacer's own
  wait before the next frame must not hold it a second time.
- **What it tells about the display.** A present that waits returns at about a refresh, and it caps the frames waiting at what
  the system queues: the same kind of cap as the wait for a free image, full and bounded.

What is said and what happens are kept apart. The pacer measures every present from its call and its return (`AddPresent`),
whatever the set says: a set with neither is settled by what the pacer sees, and a present that was said to wait and returns at once
frame after frame is treated as one that does not, with the tier it is working at saying so. This is where today's pacer
went wrong: it was written for a present that waits for the display, and on the first system measured the present returned in
0.06 ms and the acquire in 0.002 ms, every time.

### Tiers

**A tier is a set of capabilities: the ones a configuration needs to reach it.** Where more than one way reaches the same
tier, the tier has more than one set. A capability set **reaches** a tier when it holds all of one of the tier's sets, and its
**rating** is the best tier it reaches. The rating is a function of the set alone and changes nothing (`Rate(capabilities)`),
so it can be asked for any set, before a pacer exists: "what would I get without the timed present". From the tiers' sets it
also says which capability is missing for the next tier. The pacer gives the rating of the set the application has (the
**capability tier**) and of the active set (the **active tier**), by that same function.

An application that shows the tiers takes their words from the library and writes none of its own (`PacerTierText`): a short
name for each tier and each capability, a line for each tier that says what it uses and what that gives, and how many tiers
of each kind there are.

There are two questions, and each has its tiers. The first integration's document on what a platform offers already keeps
them apart: "Holding a frame is one question, how many presents wait for the display is another".

**How a frame is held for its swap interval.** These are the first integration's four tiers, unchanged in number and meaning,
now written as capability sets:

| Tier | Needs one of                                                   | Who holds the frame                                      |
| ---- | -------------------------------------------------------------- | -------------------------------------------------------- |
| 1    | `PresentAtTime`; `PresentAfterDuration`; `PresentSwapInterval` | The display side: the presentation engine or the driver  |
| 2    | `VBlankTimes`                                                  | The loop, which knows where the refreshes are            |
| 3    | nothing (the baseline)                                         | The loop, on a timer: a guess                            |
| 4    | (the pacer is off)                                             | Nothing. It is what a run uses, never what a set reaches |

**How the frames waiting to be shown are kept few.** These tiers are new:

| Tier | Needs one of           | What the pacer can do                                                                               |
| ---- | ---------------------- | --------------------------------------------------------------------------------------------------- |
| 1    | `WaitForPresent`       | Hold the loop until the display took an earlier frame: no more than k frames ever wait              |
| 2    | `DisplayTimes`         | Count the presents not yet shown, frames later, and take one frame start back for each one too many |
| 3    | nothing (the baseline) | Never plan frames faster than the display takes them. A display that fell behind stays behind       |

Queue tier 1 is measured on one system, one run a case. Queue tier 2 is a rule of this proposal: nothing of it is built,
and it has to be shown in the simulation and then on a run. `WaitForImage` and `FrameCallback` reach no tier: the first fills
the queue where it works, the second is not built anywhere and can stop or come late.

Where the handling at each queue tier comes from:

- **Tier 1** is DXGI's waitable swap chain and Vulkan's present wait, as documented. Microsoft: "For every frame it renders,
  the app should wait on this handle before starting any rendering operations", with a maximum frame latency that is the
  number of frames that may be queued
  ([Reduce latency with DXGI 1.3 swap chains](https://learn.microsoft.com/en-us/windows/uwp/gaming/reduce-latency-with-dxgi-1-3-swap-chains)).
  Khronos: an application can use the wait "to monitor and control the pacing of the application by managing the number of
  outstanding images yet to be presented"
  ([VK_KHR_present_wait](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_present_wait.html)). The pacer's k is
  their frame latency. What is this proposal's own: that the pacer, not the application, says which present to wait for and
  where in the frame.
- **Tier 2** takes how a miss is seen from Microsoft and how it is answered from Android. Seeing it: keep each present's
  expected refresh, and "If the actual PresentRefreshCount is later than the expected PresentRefreshCount, a glitch has
  occurred" ([DXGI flip model](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-flip-model)). Answering
  it: Microsoft's page discards the queued frames; this proposal holds a frame start instead, which is the idea of Android's
  frame pacing library, to "inject waits into the application that allow the display pipeline to catch up, rather than
  allowing back pressure to build up" ([Frame Pacing library](https://developer.android.com/games/sdk/frame-pacing)). That
  library waits on the GPU's work with fences; waiting by the count of presents not yet shown is this proposal's own, and
  not built.
- **Tier 3** has no documented model: what is documented for a loop without any signal is the full queue and its
  back-pressure. Never planning faster than the display is what today's pacer does. The grid on the clock is this project's
  own (the clock carries how many refreshes should have passed). Pacing a little slower than the display, one of the options
  in "Decisions needed", comes from the first integration and rests on no vendor's documentation.

A rating is the pair: "hold 2, queue 1". The two are not put into one number, because neither orders the other: a present with
a swap interval and nothing else holds its frames exactly (hold 1) and can not see its display (queue 3), and a loop that waits
on the vertical blank with a wait for presents is the other way round.

**What the new capabilities change in the four hold tiers: nothing in their number or meaning, and two things around them.**

- None of the new capabilities (`WaitForPresent`, `WaitForImage`, `DisplayTimes`, `GpuWorkTimes`, `FrameCallback`) is a new way
  to hold a frame for its refreshes, so none of them makes a hold tier or moves a configuration between them as they stand.
  Two could become further sets of tier 2, as other ways to know where the refreshes are: `DisplayTimes` (every shown frame's
  time is a refresh) and `WaitForPresent`. Neither is built. For the present wait the first measurement speaks against it as
  it is: the wait returned 0.06 to 2.4 ms after the display took the frame (1 % to 99 % of 11,460 waits), which is over half a
  refresh at 240 Hz, so it would take a filter over many waits before it could place a present.
- The first integration's sentence "At one refresh per frame every tier is the same: a FIFO present holds the frame and nothing
  else is needed" was true of the hold only, and that document now says so. At one refresh per frame the queue tier is what tells
  configurations apart.

And how the two meet: at a longer swap interval held by the loop (hold 2 and 3), a queue empties by itself, so the queue tier
matters at one refresh per frame. At a longer swap interval held by the display side (hold 1), waiting frames are held for
their swap interval too and stay, so there it matters at every swap interval.

**It is not one pacer, it is one per tier.** A tier is what a capability set reaches, and each tier has a pacer of its own that
was designed for it: its rules, its time calculations and what it asks the application to carry out. The application talks
to one front (`FramePacer`, the four calls above), which holds them and gives every frame to the one the active tier names.
Nothing else decides how a frame is paced: the active capabilities give the tier, and the tier names the pacer. So there is no
pacer that tries a mechanism and falls back inside its own rules, and "which pacer is this run on" has one answer, which is
in the log.

With two tier lists a tier is a pair, so there are nine of them: three ways to hold a frame (the display side holds; the
loop waits on the vertical blank; the loop waits on a timer) times three ways to keep the queue (a wait for a present; a
correction from what was seen; the refresh period alone). Each of the nine is a pacer from the outside, with its
own tests, its own golden data, and its own statement of what it promises and what it can not do. Inside they are not nine
copies: each is one hold part and one queue part, and all of them share what does not depend on the tier (the frames in
flight, the work as two stretches of time, the animation time), and the nine for a fixed refresh rate share the timeline of
refreshes and the swap interval rule. The shared parts hold no rule of a tier.

Going from one to another (the active set changed, a signal stopped) is a handover the front makes at a frame's start: the
animation time and the frames in flight go on, and the new pacer starts from what the shared parts know.

A third value is the tier the pacer is **working at** this frame, again a pair: the pacer that is really pacing. It is lower than the active tier while
something a capability promised is missing: no vertical blank time has come yet, display times have stopped, a wait timed
out.

### Per frame

Four calls at four places of a frame, each values in and values out. The application's loop, in any graphics API:

```text
plan     = pacer.PlanFrame(now)                 // before the frame takes anything
           carry out plan's waits
schedule = pacer.BeginFrame(now)                // the frame starts: its CPU start time
           update and draw for schedule.AnimationTime, draw the marker from schedule
present  = pacer.EndFrame(now)                  // the CPU's work is done
           carry out present's wait, present with present's values
           pacer.AddPresent(frame, callTime, returnTime)
```

- **`PlanFrame`** returns what to wait for before the frame: a present to wait for (its frame id and a timeout), where
  `WaitForPresent` is active; then a time to wait until. Either may be absent.
- **`BeginFrame`** returns the schedule, as today: the frame's id, its swap interval, the animation time and step, the intended
  display time, the marker's pacing values. It is a call of its own because a wait can wake late, and the frame is planned from
  when it really started.
- **`EndFrame`** returns how to present: a time to wait until before the present (absent when the present goes at once); the
  swap interval, the target time or the minimum duration to give the present, whichever is active; and the CPU busy time for
  the marker.
- **`AddPresent`** tells the pacer when the present was called and when it returned, which is how it learns whether a present
  waits on this system.

Between frames, as it has them, the application gives what its active capabilities promise, each with the id of the frame it
is about: `AddVBlank` (a vertical blank's time, the period, and when it was read), `AddGpuWork` (begin and end, or a
duration; built in the first two tier pacers), `AddDisplayReport` (a display time, or "a result without a time").

The plan never names a graphics API, and the application never computes a time: it waits until the times it is given and
passes on the values it is given.

## The rules that move into the pacer

**A timeline of refreshes.** With `VBlankTimes` (or display times) the pacer keeps where the refreshes are: a time of one, and
the period, refined from the readings without starting again when it moves a little. Every frame is aimed at one refresh, and
its start, its present and its animation time come from that refresh, not from when the frame before happened to start. A wait
that wakes late then costs that frame and not every frame after it. Without them the pacer counts refreshes on the CPU clock
from the frame starts, as today.

**Where a frame's work sits in the refresh.** One setting replaces the sample's two profiles: the frame is rendered at once and
its present is held (the present's time does not move with the work), or the frame's start is held and it is presented when it
is done (the frame is as fresh as it can be). The pacer gives both waits either way; one of them is absent.

**Every tier's pacer has two aims: latency optimized, and not latency optimized.** This is a rule of the design, at every
tier, and an application chooses between the two. Not latency optimized, a pacer keeps the display supplied: frames that
wait to be shown are a reserve, so where in a refresh a present lands matters less and a frame that runs a little long is
covered, at the price of a frame reaching the screen that many refreshes later. A constant delay does not show in the motion.
Latency optimized, a pacer keeps the frames that wait as few as its tier can, and pays for it with a repeated frame where the
reserve would have covered one. What trades the one against the other belongs to an aim and never to a pacer as such: whether
frames that wait are kept or taken away, how many presents may wait, whether a step of the grid is given up after a frame
that ran long, where a frame's work sits in a refresh.

An earlier version of this document had reducing latency as "an option on top, not a part of every pacer", and the first two
tier pacers were first built that way, with what leans towards latency as their only behaviour. That was a misreading of the
rule. Both now have the two aims as one setting (`PacerSettings::Aim`: `Smoothness`, which is the default, and
`LowLatency`), checked on the simulation only and **not measured**:

| Pacer                            | Smoothness                                                                                                                                                                                                    | Low latency                                                                                                                         |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| A timer, the refresh period only | A reserve at one refresh per frame: the presents that may wait, less one, are made ahead of the display. A frame late within it gives up no step and is made up for; what is beyond it is given up. No pause. | One pause after start-up; a whole period after a late present; a frame half a period late or more takes the step where the loop is. |
| A timer, a wait for a present    | The same reserve, and the wait keeps it to what may wait.                                                                                                                                                     | No frame made ahead; the wait keeps the frames that wait to what may wait.                                                          |

The reserve is the setting for the presents that may wait (two by default, so one frame is made ahead). At two refreshes per
frame or more there is none: the display takes a frame before the next one is made.

What the simulation shows of the pacer without a wait, at every one of ten places the display can take its frame in a
refresh: with a frame made ahead, CPU work of 0.9 of a refresh more in one frame is not seen at all (every frame is on screen
for one refresh), and with the aim of low latency it is a repeated frame at eight of the ten places or more. After a frame
that runs 2.4 refreshes long, one frame is on screen longer and the frames that wait are the reserve again or one more: that
pacer does not see the display, so it gives up the steps it is sure the display repeated a frame for and no more. With a wait
for a present they are exactly what may wait, after a long frame and after refreshes the display lost by itself.

The nearest thing to a measurement of the aim of smoothness so far is the first measurements' runs of the pacer without a
wait, made before its pause existed (one run each): with two presents waiting at one refresh per frame, 2,270 of 2,270 frames
were on screen for exactly one refresh, 2.21 refreshes after their start (1 % to 99 %: 2.18 to 2.23); at 60 frames a second
1,072 of 1,072 were on screen for exactly four. In both runs the time from a frame's start to its display rose slowly and
evenly: between the first and the last quarter of the run its median rose by 0.025 of a refresh in the one (about 1,700
refreshes apart) and by 0.049 in the other (about 3,200 apart). That is about fifteen parts in a million, so the grid on the
clock ran that much faster than the display, and at that rate the frames that wait become one more about every five minutes
at 240 Hz. Its cause has not been looked into. If it is the refresh period, it is the limit of a grid on a period that is
given and not measured, with either aim, and what a reserve that is only counted can not hold against.

Two numbers trade latency against the frame rate a loop can hold: k (the presents that may wait) and the frames in flight
(whether the CPU and the GPU work side by side). Both are given to the pacer, and it paces correctly for what it is given: k
is a setting, the frames in flight are the application's, and the pacer reads from the frames' moments whether the work runs
side by side.

Choosing the two is a separate option, off unless asked for, and built only once the tier pacers are measured: with it on,
the pacer picks k and, where the application says it can change them, the frames in flight, for the least latency that still
holds the frame rate. What it would decide between is in the runs of 2026-10-07 (240 Hz, CPU work of 74 % and GPU work of
72 % of a refresh): two frames in flight at one refresh per frame gave 240 frames a second with a frame shown three refreshes
after its start (12.5 ms), and two refreshes per frame gave 120 frames a second with a frame shown two refreshes after its
start (8.3 ms). Neither is wrong; which one a game wants is the game's to say.

**Where in a refresh a present is made.** A setting, in percent of the refresh, used where the pacer knows the refreshes. Its
default has to come from more than the one system measured so far.

**A frame held for more than one refresh** is held by the best active mechanism: the present's swap interval; else a present
with a time or a minimum duration; else a wait of the loop until the refresh before the one it is aimed at; else a wait on the
timer. The values are the pacer's.

**A grid on the clock, where there is nothing else.** With the refresh period and a clock, a frame start is due at a whole
number of refresh periods from the first one, and stays due there whatever the frames before it did. A frame whose work
overran is known to the pacer from its own times: it ended after the time its swap interval gave it. The frame after it then
starts at the next step of the grid, not at once and not on a new count from the late start. Presents after that are made at
the same place in the display's refresh as before the long frame, so the frames waiting are as many as before it, and the
animation timer does with the refreshes that were lost what "A lost refresh and game time" below says. The grid's place against the display is whatever the loop began with, which
the pacer does not know and does not need: it only has to keep it. Two limits: the period has to be the display's real one, as
a grid on a period that is a little off slides against the display; and it answers the misses the pacer can know of, those of
its own frames. Today's loop does the opposite after a frame that is more than half a frame time late: it starts its count
again from the late start, which is one way a long frame leaves a frame waiting for good.

**The frames waiting to be shown** are bounded as the section above says: a wait for the present **k** frames back where that
exists, a count from display times where it does not, and nothing where there is neither. The same rule
covers a missed refresh, start-up and a swap chain made anew; there is no separate drain.

**A frame's work is two stretches of time, not a sum.** The CPU works on a frame from its start to its submit, and the GPU from
when it takes the frame up to when it is done with it. The two overlap between frames: while the GPU works on one frame, the
CPU can already work on the next. A duration says how long, not when, so two durations can not say how much of the work ran
side by side. The pacer therefore keeps four moments per frame (the CPU's start and end, which it has from its own calls, and
the GPU's begin and end, with `GpuWorkTimes`) and takes three different things from them, where today one number serves for
all three:

- **Whether the frame makes its refresh**: the GPU's end, held against the refresh the frame is aimed at. It includes the time
  the frame waited for the GPU, which is no work of anyone and is in no duration.
- **What frame time the loop can hold**: the CPU and the GPU each work on one frame at a time, so each one's time per frame has
  to fit in the frame time. Where they work side by side, that is the longer of the two. Where the CPU waits for the GPU
  before it starts the next frame, it is the two added. Work of 60 % of a refresh on each fits one refresh per frame side by
  side and needs two when one waits for the other. Which of the two a loop is, and for how much of the time, is read from the
  moments: did the CPU's work on a frame begin before the GPU's work on the frame before it had ended.
- **Which of the two limits the loop**: a GPU that begins a frame later than it was given it was still busy with the frame
  before.

Today's pacer is given the two added, as the first integration's sample reports them, and counts a frame whose work is over
its frame time as late. For a loop that works side by side that reads too much, and the first integration measured it on
2026-10-07 (Vulkan in a window, 240 Hz, the driver's display times, CPU work of 74 % and GPU work of 72 % of a refresh, each
case run twice with the same result; a frame in flight more lets the CPU start a frame while the GPU is on the one before):

| Frames in flight | Swap interval | Frames that started before the GPU was done with the one before | Frame start to frame start | Shown for one refresh         |
| ---------------- | ------------- | --------------------------------------------------------------- | -------------------------- | ----------------------------- |
| 1                | fixed at 1    | 0 of 2,291                                                      | 1.51 refreshes             | 1,111 to 1,129 of about 2,290 |
| 2                | fixed at 1    | 2,289 or 2,290 of 2,291                                         | 1.00 refresh               | 2,284 to 2,286 of about 2,290 |
| 1                | the rule on   | 0 of 2,291                                                      | 2.00 refreshes             | at most 2                     |
| 2                | the rule on   | 0 of 2,291                                                      | 2.00 refreshes             | at most 1                     |

With one frame in flight the loop needs the two added (146 %) and can not hold one refresh per frame. With two it works side
by side, about half a refresh of every frame's CPU work running beside the GPU's work on the frame before, and holds one
refresh per frame at the same work. Today's rule slows both to two refreshes per frame within 50 frames and keeps them
there: for the loop with two frames in flight that is half the frame rate it can hold. (A control with work of 26 % and 24 %
and one frame in flight holds one refresh per frame, as the two added fit.)

**How others measure the two and put them together** (their public documentation, read on 2026-10-07). No source publishes
a formula with thresholds; what they say about the principle agrees with the above:

| Source                         | What it measures                                                                                       | How it puts CPU and GPU together                                                                                                              |
| ------------------------------ | ------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Unity                          | CPU time with its waits taken out; GPU time from the first command of a frame to its completion        | Each against the whole frame time, never added: both "individually have the full frame time", as they work "in parallel"                      |
| Android's frame pacing library | The CPU's time on this frame and the GPU's on the frame before, as durations                           | Two arrangements it switches between: the two "across VSYNC boundaries", or both in one swap interval "if it fits"                            |
| Android's frame metrics        | A total from a frame's begin to its end, and the stages                                                | The total "may not be exactly equal to the sum", "because some stages may happen concurrently"                                                |
| NVIDIA's latency markers       | CPU markers set by the application; the GPU's render start and end and its active time by the driver   | The stages "overlap, which means simply adding them together won't produce a correct latency sum"                                             |
| PresentMon                     | CPU start, CPU busy, CPU wait; from the frame's start to the GPU's start; GPU time, GPU busy, GPU wait | Kept apart: a tool, it decides nothing                                                                                                        |
| Apple's Metal                  | The GPU's start and end of each command buffer on the host's clock                                     | Each read against the frame interval; "for the processors to work in parallel, the CPU should be working at least one frame ahead of the GPU" |
| DXGI                           | Nothing of a frame's work: present and refresh counts                                                  | (Direct3D 12 has timestamps that can be placed on the CPU's clock, and warns that the two clocks drift)                                       |

([Unity](https://docs.unity3d.com/Manual/ProfilerHighlights.html),
[Android's frame pacing library](https://developer.android.com/games/sdk/frame-pacing) and its API reference,
[FrameMetrics](https://developer.android.com/reference/android/view/FrameMetrics),
[NVIDIA](https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-platform/),
[PresentMon](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md),
[Metal](https://developer.apple.com/library/archive/documentation/3DDrawing/Conceptual/MTLBestPracticesGuide/TripleBuffering.html),
[Direct3D 12](https://learn.microsoft.com/en-us/windows/win32/direct3d12/timing).)

Three things this proposal takes from them:

- **The names.** The marker's CPU start time and CPU busy are PresentMon's already. The pacer's values get its other names
  where they mean the same: GPU latency (from a frame's start until the GPU starts on it) and GPU time (from the GPU's first
  to its last work on the frame, gaps included). PresentMon's GPU busy (the GPU actively working) is not what two timestamps
  give.
- **Waits are not work.** Unity and PresentMon both take a thread's waits out of its CPU time. The pacer's CPU time of a
  frame is from its start to the end of its work, and a wait the plan asked for is never inside it.
- **Side by side or in series is a choice, and Android's library makes it.** It turns its pipeline on and off from the times
  it measures. In this proposal that choice is the application's (its frames in flight), and the pacer only reads it. The
  pacer making that choice is the latency option above, not part of a tier pacer.

Two cautions from the same pages: the time between a frame's first and last GPU work can be longer than the work on a GPU
that renders in tiles ("any gaps between phases increase the reported GPU time", Unity), and a long GPU time "may mean that
the GPU was busy with other tasks" (OpenXR's performance counters).

All of this takes the GPU's begin and end of a frame to bound the time the GPU was busy with it, which no graphics API
promises in those words: a timestamp says when the GPU reached a command, and the time between two of them can hold work
for another program or no work at all. On the one system measured it holds. The first integration's Vulkan sample writes a
timestamp as the first and as the last command of the frame's one command buffer and places both on the CPU clock with
calibrated timestamps. In its 15 stored runs of 2026-10-06 (about 35,000 frames):

- no frame's GPU work began before the frame before it had ended;
- a frame's GPU work began within about 0.15 ms of the later of its submit and that end (the medians are 0.01 to 0.14 ms);
- the stretches added up came to between 5 and 92 % of a run's time, each within 4.5 points of the GPU usage the operating
  system reported for the process, and lower in 14 of the 15. What the driver and the presentation engine do for the present
  itself is outside the two timestamps.

One machine and one driver. Three limits follow for the capability, whatever the system:

- An application has to check those three things on its system before its times are read as busy time.
- The length of a stretch is exact (both ends are on the GPU's clock), its place on the CPU clock is not. That sample read
  the two clocks against each other every 240 frames and its GPU times ran up to 0.02 ms early at 240 frames a second and up
  to 0.07 ms at 60, as a sawtooth. So the pacer treats a difference between a CPU moment and a GPU moment that is smaller
  than a margin as none, and the application says how good its placement is.
- Where the first timestamp lands is the driver's choice when the frame waits for its swap chain image: on that driver the
  frame's commands began only when the image was free, and the specification lets another driver begin them earlier.

With `GpuWorkDurations` alone the pacer knows how long the GPU worked and not when. It can not tell the frame's end, nor how
much ran beside the CPU's work, and takes the cautious reading: the two added, unless the application says it lets two or
more frames be in flight, and then the longer. Without either it has the CPU's work alone, and sees a loop the GPU limits only
by its late frames.

**The swap interval rule** keeps its frame window and its two ways to slow down. What it counts as a late frame comes from the
best signal active (a display time later than the refresh the frame was aimed at; else the GPU's end after it; else the frame
starts and the work, as today). The work it compares with the frame time is the frame time the loop can hold, as above; the runs in that section are
what the replay checks it against.

**Present feedback** stops being statistics only where `DisplayTimes` is active: it places the timeline and corrects the queue.
The statistics stay.

## What each tier allows, and whether the display gets back to the animation timer

**The animation timer is the same at every tier.** It advances in whole refreshes of the display, at every tier, so a wait
that wakes early or late never reaches the motion. What it does after a refresh was lost is one rule for all tiers ("A lost
refresh and game time" below).

**What can fall behind is the display.** When the display shows a frame a refresh later than it was animated for, and the
loop goes on at one frame per refresh, every frame after it is also on screen a refresh later than its animation time, by as
many refreshes as there are frames waiting. The motion stays even; the picture is old. "Catching up" below means: can this tier
bring what is on screen back to the animation timer, and why or why not.

### A lost refresh and game time

The animation error of a step is its animation time step minus its display time step. It is about steps, so a constant
distance between the animation time and the time on screen is in none of them: it does not show in motion. Three things follow,
and they are not the same thing:

- **Taking a waiting frame away costs nothing that shows.** While a frame start is held, the display shows the frame that
  waited, so no frame is repeated and every display step stays what it was. This is the queue tiers' work, and it is worth
  doing for the latency alone.
- **Moving the animation timer to where the display is costs one visible step**, to fix a distance that did not show. The
  pacer does not do that for its own sake.
- **A refresh that was lost shows once, and catching game time up shows a second time.** When the display holds a frame a
  refresh longer than it was animated for, that step has an animation error of one refresh, and nothing takes it back. After
  it, either game time stays that refresh behind the clock, with nothing more to see, or the next step is made a refresh
  longer to bring game time back to the clock, which is a second error of one refresh the other way. The same holds for a
  frame of the pacer's own that ran long, by as many refreshes as it lost.

Today's pacer catches up: its animation time follows the clock, and in the stored runs the errors come in such pairs (212
steps a refresh short against 58 a refresh and 89 two refreshes long, in the run that fell behind most). Not catching up
halves the errors that show and lets game time fall behind the clock by one refresh for each one lost. That is nothing to
see while refreshes are lost now and then, and it is a game that runs slow while they are lost all the time (work of 130 %
of a refresh once ran the animation at 73 % of real time, before the pacer counted such frames as late): there the swap
interval rule has to end it by slowing down. Which of the two the pacer does is in "Decisions needed".

### What each hold tier allows

| Hold tier | A frame of swap interval 1                                  | A frame of a longer swap interval                                                  | Does it know where the refreshes are?                             | Status                                                                                              |
| --------- | ----------------------------------------------------------- | ---------------------------------------------------------------------------------- | ----------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| 1         | As tier 2 or 3, by what else the set has                    | Held by the display side for exactly its refreshes, whenever the loop presents it  | Not from this capability                                          | A present with a minimum duration: measured on one system. A swap interval on the present: built    |
| 2         | Starts and is presented at a place in the display's refresh | Presented by the loop in the refresh before the one it is aimed at                 | Yes: an animation time is the time of a real refresh              | Measured on one system at 50 to 240 Hz; built and not measured on three other window systems        |
| 3         | Starts a refresh after the one before, on the clock         | Held by the loop on a timer: a guess, as it does not know where in a refresh it is | No: its refreshes are a grid on the clock, a constant offset away | Measured on one system: next to no frame a refresh off at 50 to 120 Hz, 1 to 35 % of them at 240 Hz |

### What each queue tier allows

| Queue tier | What it allows                                                                    | Does it learn that the display lost a refresh?     | Can the display catch up?                                                       | Why                                                                                                            |
| ---------- | --------------------------------------------------------------------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| 1          | A set number of frames waiting (k), from the first frame on                       | It need not: the wait is for the display itself    | Yes, at once: it costs one frame start, and the frames waiting never go above k | The loop is held until the display took an earlier frame, so it can not present more than the display takes    |
| 2          | The frames waiting, counted a few frames late; the real time of every shown frame | Yes, one to five frames later (as far as measured) | Yes, after those frames: one frame start is taken back for each frame too many  | The pacer knows how many presents are not shown yet, and a loop that presents one frame less lets one be taken |
| 3          | Never more frames than the display takes; frame starts kept on one grid           | No                                                 | Only where the swap interval does it (below). At one refresh per frame: no      | Nothing tells the pacer that a frame waits, and at one frame per refresh nothing takes a waiting frame away    |

Status: queue tier 1 is measured on one system, one run a case, with the wait made by the first integration and not yet
driven by a pacer. Queue tier 2 is a rule of this proposal and not built. Queue tier 3 is today's pacer plus the grid.

**The wait for a free swap chain image, at queue tier 3.** It is core to a swap chain, so a configuration at queue tier 3
can have it, and on the one system measured it worked as a cap and not as a way down (one run a case, GPU work of 90 % of a
refresh, a swap interval fixed at 1):

| Run                                | Frames waiting   | Frame start to display, median (99 %) | Presents without a display time | GPU work that began late |
| ---------------------------------- | ---------------- | ------------------------------------- | ------------------------------- | ------------------------ |
| No wait, three images              | 1 to 3, changing | 2.98 refreshes (4.98)                 | 7.6 %                           | 7.0 %                    |
| A wait for the image, three images | 3, steadily      | 3.91 refreshes (3.94)                 | 0.1 %                           | none                     |
| A wait for the image, two images   | 2, steadily      | 2.92 refreshes                        | not read                        | not read                 |

So it does three things there: the frames waiting can not go above the number of images, they sit at that number, and the
run is steady. It does not bring the display back towards the animation timer: it fixes how far behind it is. That distance
is then known (as many refreshes as there are images), which nothing else at this tier gives, and with the fewest images the
swap chain allows it is no further than without the wait. Whether another driver keeps an image as long is not known, which
is why the pacer does not count on it. Whether it should be a queue tier of its own, between display times and the refresh
period alone, is in "Decisions needed".

### The nine pairs: after a refresh the display lost by itself

| Hold tier, queue tier                   | 1, a wait for a present | 2, display times         | 3, the refresh period only                                                                                                                     |
| --------------------------------------- | ----------------------- | ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| 1, the display side holds               | Catches up at once      | Catches up, frames later | Does not catch up, at any swap interval: the display side holds the waiting frames for their swap interval too                                 |
| 2, the loop waits on the vertical blank | Catches up at once      | Catches up, frames later | Catches up by itself at two refreshes per frame or more, as the display then takes frames faster than the loop presents them. At one: does not |
| 3, the loop waits on a timer            | Catches up at once      | Catches up, frames later | As hold tier 2                                                                                                                                 |

Two things are the same in every pair:

- **After the pacer's own long frame** the loop is back where it was against the display: on the display's refreshes at hold
  tier 2, on the grid on the clock elsewhere. The pacer needs nothing from the display for that.
- **The lost refresh itself is seen on screen once**, as a frame shown a refresh longer than it was animated for. No tier takes
  that back: the frame was already drawn when the refresh was lost. What the tiers differ in is whether it stays as latency
  afterwards.

### Aligning the animation timer with the display

Catching up is about the frames waiting. A second question is how well a frame's animation time can be made the time the
frame is really shown. It has three parts, and they take different tiers:

| What "aligned" means                                                                                          | What it takes                                | Tier                                                            |
| ------------------------------------------------------------------------------------------------------------- | -------------------------------------------- | --------------------------------------------------------------- |
| The animation time is the time of a real refresh, not a point on a grid a constant part of a refresh away     | Knowing where the refreshes are              | Hold tier 2 (vertical blank times), or display times            |
| The whole refreshes between a frame's animation time and its time on screen can not change without being seen | The frames waiting capped, or counted        | Queue tier 1 (capped at k) or queue tier 2 (seen and corrected) |
| The animation time is the frame's true display time                                                           | The display saying when each frame was shown | Queue tier 2 only (display times)                               |

- **Only display times align it fully**, as only they say when a frame reached the screen. They come frames late (one to five
  in what was measured), so what they correct is the frames that follow.
- **Vertical blank times with a wait for a present** (hold tier 2, queue tier 1) come close without them: the animation time
  is on a real refresh and the frames waiting can not grow. What is left is the constant number of refreshes the system takes
  by itself (a compositor's, for one), which the pacer can not know. A constant whole number of refreshes does not show in
  motion; it matters where a game needs the absolute time, for sound.
- **At queue tier 3 it can not be done.** The animation timer is exact against the clock, and how many refreshes behind it the
  screen is can not be known. The wait for a free image is the one thing that fixed that distance, at the image count, on the
  system measured.
- **Hold tier 1 by itself does not give it**: the display side holds the frame for exactly its refreshes, and the application
  is not told where they are.

## How it is checked

- **Replay** (`pacer-replay`, test code): a stored frame log is given to the pacer in the order its application had the
  values, and what the pacer makes of it (where it thinks the refreshes are, how many presents it thinks are waiting, the
  misses it saw) is held against what the log shows the display did. A log shows what a pacer would have seen, never what
  another answer would have caused.
- **Simulation** (`pacer-sim --loop`, test code): a frame loop on a display model with a present that never waits, frames
  queued behind it, an optional bound, and vertical blanks at which no frame is taken. It gets a wait for a present, display
  times that come late and a timer that wakes late, and each tier is run on it: the queue that today stays for the rest of the
  run has to be gone within a number of frames stated per tier.
- **Each of the nine tier pacers** in that simulation, held to what its tier promises. Leaving a capability out of the
  active set is how a test runs a lower tier's pacer on the same model.
- **Golden data, monitor rates 50 to 540 Hz, 100 % coverage, no allocation per frame**, as today. With a clock only the new
  pacer does at least what today's does.
- Then the first integration with its own calculations taken out, the runs of 2026-10-06 again, and a capture analysed with
  the tools, which no run of the pacer has had.

Nothing of this is advice yet: what a tier promises above is what it is designed to do, and it goes into the guide as checked
only when a capture has shown it.

## Reviewed against the first integration's three hosts

The first integration read the calls, the capabilities and the reports against its Vulkan host and its two OpenGL ES hosts
(2026-10-07). The four calls have a place in all three, in the order given above; in Vulkan it is: plan, the waits, the
acquire, the frame's start. What the review found missing, and what becomes of each:

| What the hosts do that the proposal did not cover                                                                                                      | What becomes of it                                                                                                                                                                                                                                                                    |
| ------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A host calls the application's update before its first place to wait, so the update's work is done before the frame's animation time is known          | The host has to plan the frame before the update. A change in a host, named here so that an integration knows                                                                                                                                                                         |
| A host waits by itself between the plan's wake and the frame's start (for the GPU to end an earlier frame, in the acquire), and nothing told the pacer | A report of the application's own waits, each with its kind, begin and end: without it the pacer has nothing to measure an acquire from                                                                                                                                               |
| The waits for the GPU's work and for a free image have no place in a plan                                                                              | They are waits an application makes by itself at the one place it can, and reports; the plan asks for a present and for times only                                                                                                                                                    |
| The present's times are known at the start of the next loop turn, not right after the present                                                          | The present's report is given before the next frame is planned, which is early enough                                                                                                                                                                                                 |
| A frame can be planned and never begun (a swap chain out of date at the acquire), and a present can be refused                                         | Planning twice in a row is allowed, and a present's report says whether the system took it (in the type now)                                                                                                                                                                          |
| The set an application has changes during a run (the first vertical blank time comes after the first frame; a swap interval can be refused later)      | The set an application has can be given at any frame, as the active set                                                                                                                                                                                                               |
| The number of swap chain images and of frames in flight have no place, and a host may not know the frames in flight                                    | Given beside the capabilities as information, each with "not known"                                                                                                                                                                                                                   |
| A present that shows frames out of order or drops them (a present mode without a queue) is nothing the baseline says                                   | The baseline says it: a present that shows every frame in order for at least a refresh. The pacer is not to be used on another kind                                                                                                                                                   |
| A GPU's work can be known as an end and a length, without its begin                                                                                    | The report takes that (in the type now), and the begin is not worked out from it                                                                                                                                                                                                      |
| A vertical blank time comes with how good it is, and can be of another display than the window's                                                       | A time that is not certain to be of the window's display is not given: the application does not have the capability then. How good a time is, is not taken as a value: the pacer checks readings against each other and its period, and what the window system said goes into the log |
| The frame id is not the id the present itself gets: a host numbers its presents, also the refused ones                                                 | The frame id is the key of every report, and the application maps it to its present's id                                                                                                                                                                                              |
| A frame that needs a longer swap interval than the present takes is held partly by the loop                                                            | The tier the pacer is working at, for that frame, is the loop's                                                                                                                                                                                                                       |
| A display seen to refresh at a variable rate makes vertical blank times useless, and the application withdraws them itself today                       | The application says what it knows of variable refresh, and the front stops using the times: a rule that has to move, even before that pass                                                                                                                                           |

## Variable refresh: a later pass, and what is kept open for it now

The rules for a display with a variable refresh rate are not part of this design. There is nothing to design them from: every
stored run has variable refresh off, and the tools measure a display at a fixed refresh rate, so a pacer for it could not be
checked. Four things are settled now, because they cost little now and a second redesign later:

- **It is more tier pacers, not a change to these.** One pacer per tier means a display that shows a frame when it is
  presented gets pacers of its own behind the same front, and the nine for a fixed refresh rate stay as they are.
- **The application says so.** Whether variable refresh is active for its window, and the range, is information it gives with
  its capabilities. Until those pacers exist, the front does not pace such a display with a pacer for a fixed one: the tier it
  is working at says that it is not pacing.
- **Times in the plan are times.** A present's time, the intended display time, the animation step and the target frame time
  are moments and durations. The swap interval is a count beside them for the mechanisms that take one, and nothing a caller
  reads assumes that a display time is a whole number of refreshes after another.
- **The timeline of refreshes belongs to the pacers for a fixed refresh rate**, not to the shared parts. What is shared has no
  refresh grid in it: the frames in flight, the work as two stretches of time, the animation time. The wait for a present
  (queue tier 1) asks nothing about the refresh rate and is expected to carry over as it is.

## Decisions needed

1. **k**, the presents that may be waiting: 1 is the lowest latency and leaves no slack (at work of 90 % of a refresh it
   halved the frame rate in the first measurement), 2 leaves a frame of slack for one refresh more. Proposed: a setting,
   default 2, as that kept the frame rate in every case measured; picking it from the work is the option below. Open
   since: on the simulation a loop with two frames in flight and heavy work needs 3 (above). It did not show on the first
   integration's system in the second measurements, where a frame reached the screen sooner than in the simulation's
   display. Should the pacer take at least one more than the frames in flight the application names, or is that the
   application's to set?
2. **The tiers**: two lists, each tier a set of capabilities, the hold tiers the first integration's four as they are, and
   a rating that is the pair. Proposed as above. If one number is wanted for a rating, the order between the two has to be
   decided: there is no order that follows from the capabilities.
3. **With the refresh period only, at a swap interval of one, a refresh the display lost by itself**: the grid on the clock
   answers the pacer's own long frames. For a frame that was ready in time and still shown a refresh late, which the pacer
   can not learn of: accept that it stays waiting, pause once after start-up as a guess, or pace a little slower than the
   display's measured rate so that the display takes frames slightly faster than the loop makes them and a queue empties (one
   repeated frame every so often; it needs the measured period, not the mode's nominal one)? Proposed by the first
   integration: the last. Not decided. What speaks for a lean towards latency at this tier is that the fault only goes one
   way: nothing here ever takes a waiting frame away, so every lost refresh the pacer does not learn of stays. What it costs
   is one number: paced slower by a share m, the display has a refresh to spare every 1 / m refreshes, which takes one
   waiting frame away if there is one and repeats a frame if there is none. At 0.01 frames a second under 240 that is one
   every 100 s: three waiting frames are gone after five minutes, and a frame is repeated every 100 s from then on.
4. **Start-up and switching the pacer on**: is anything wanted beyond the queue rule (which holds the loop until the first
   presents are shown, where it can wait or see)? Not decided.
5. **The two aims**: decided, and wrongly proposed here before as an option that no tier pacer carries. Every tier's pacer
   has a latency optimized and a not latency optimized form ("The rules that move into the pacer"). Still open is only the
   further step of the pacer choosing k and the frames in flight from the measured work by itself.
6. **Back-pressure as a tier**: a queue tier of its own for a loop that is held by the system when its queue is full (a
   wait for a free image, measured on one system: the frames waiting are capped at the number of images and steady; or a
   present that is known to wait), or a help at the lowest tier that the pacer promises nothing from, as now? If a tier, the
   image wait goes with the fewest images the swap chain allows.
   Since the third measurements this has a case: with the aim of smoothness and no wait for a present, a full swap chain
   paced the loop by itself, steadily in one run and not in another, and the pacer neither chose it nor knew of it.
   Decided on 2026-10-07, not built yet: where the application says the system holds the loop when its queue is full, and
   the pacer sees those waits happen, smoothness means that on purpose (no time to start at, the reserve is what the swap
   chain holds, no frames made ahead on top of it); and the pacer is told of the application's own waits (so a loop that
   is held is not taken for one that is late) and of how many frames the swap chain holds (so no reserve is asked for
   that it can not take). Where nothing holds the loop, the reserve by count stays. Still proposed, and not answered: it
   is how smoothness behaves in the pacers that have the refresh period only, and no queue tier of its own, as it
   promises less than a wait for a present (how many frames wait is the swap chain's number).
7. **The pacer switched off**: does the application go on reporting, so the pacer starts with a history? Not decided.
8. **The aim where two goals pull apart**: it is the application's choice between the two aims. Not latency optimized: no
   missed refreshes first, then the frame rate asked for, and latency is what that costs. Latency optimized: the fewest
   frames waiting first.
9. **After a lost refresh, does game time catch up with the clock?** Catching up (today's behaviour) shows a second error
   of the same size and keeps game time on the clock. Not catching up shows the one error only and leaves game time behind
   the clock by the refreshes lost, which the pacer would report as a number. Proposed: not catching up, with a setting for
   an application that needs game time on the clock (sound, a network), and the swap interval rule as what ends a loss that
   goes on.
10. **When every frame loses a refresh**: decided on 2026-10-07 and built ("What was changed after these runs"): a frame's
    animation step follows a loss that repeats, and stays the swap interval after a loss that does not. Open: a loss
    every second frame is not followed.
11. **The lowest pair at start-up**: decided on 2026-10-07 and built: one pause after start-up, its length and its delay
    settings that are named as a guess, never before a present was taken.
12. **A wait for a present that is never shown**: decided on 2026-10-07 and built: the longest a wait may take is counted in
    the frame's own swap intervals, four by default.
13. **Names**: the capability and call names above are proposals.

## What changes for whom

- The pacer's public types change throughout (the SDK is at 0.1.0 with no release): `FramePacer`'s calls, `FrameSchedule`, the
  settings, present feedback. Every time is in nanoseconds (below).
- The first integration's sample loses its hold methods, profiles and due times, and keeps the waits, the present and the
  measurements.
- The frame log's pacer chunks follow the new calls, and get the capability sets and the ratings.
- Today's guide, its Status table and "Not used yet" are rewritten from this document when the code is.
