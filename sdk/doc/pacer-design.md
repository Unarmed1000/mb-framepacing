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
- **The lowest tier's pacer keeps the place it began at, whatever that is.** With light work it kept two presents waiting for
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

- **One pause after start-up in the lowest tier's pacer.** Half a second after the first frame of a start, the frame after
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
  waiting frames, the lowest tier's pacer without reports stays at one refresh per frame while the frames fall ever further
  behind, and with reports goes to two; with CPU work of 74 % and GPU work of 72 % it stays at one refresh per frame with
  two frames in flight and goes to two with one, which is what the first integration's runs asked for.
- **This does not answer the run above where that pacer stalled.** That run had the swap interval fixed at one, and its GPU
  work of 90 % fits a refresh by any reading. What grew there is the number of frames waiting, from refreshes the display
  lost by itself, and that pacer does not learn of those: it stays what this tier can not do.
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

The lowest tier's pacer made its pause once (the frame after it started 4.63 refreshes after the one before). The one present
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
  as before. A frame that is presented in time and ready too late for its refresh stays what this tier does not learn of.
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
  timer at the swap interval they had. Once in sixteen frames the plan still names a present, with a longest time of zero:
  the application does not wait, it only asks whether that present was shown and reports the answer. The present asked after
  is one that has had the time a wait would have given it, and none from before the first wait that ran out. Two answers in
  a row that say shown end it, and the next frame waits as before. A frame that the asking held is not judged either.
- So a window that is covered costs two waits (eight refreshes at one refresh per frame) and then runs at its frame rate
  less what the asking costs, and when it is back the pacer is where it was: the frame window and the swap interval are
  what they were before.

**The rerun**, with the first version of this change, which asked every frame and took one answer (the same system, one run
of light work and one of work that has the swap interval at two; the window covered for 15 s):

| Work  | Before the cover           | Covered, the old pacer       | Covered, the change                           | After the window was back                   |
| ----- | -------------------------- | ---------------------------- | --------------------------------------------- | ------------------------------------------- |
| Light | 240 frames a second        | 44 down to 3 frames a second | 54 to 81; the swap interval 1 for 6 s, then 2 | 120 frames a second for about 2 s, then 240 |
| Heavy | 120 (a swap interval of 2) | Not run                      | 62 or 63, the swap interval 2 throughout      | 120 from the first full second              |

What it showed of the system, covered:

- **Asking is not free.** A wait with a longest time of zero returned after 10.7 ms (light, 784 frames) and 8.3 ms (heavy,
  834 frames), not at once, and a wait that ran out took about 10 ms more than the time it was given. That is what held the
  frame rate down: with the asking every frame a frame took 15.6 ms.
- **A covered window's frames are shown now and then.** About once a second an answer said shown (the stop ended 21 times
  in the 15 s of the light run), a few waits then returned at once, one returned shown after about 14 ms, and two ran out
  again. The frames after the waits that returned shown late are late by the pacer's count, rightly, and 34 of them in
  the light run were what took the swap interval from 1 to 2.

**What was changed after the rerun** (the two bullets above have it): the pacer asks once in sixteen frames and not every
frame; it takes two answers in a row that say shown before it waits again, as one can be a covered window's frame shown in
passing; and a frame that an answer held is not judged, as a frame that a wait held is not.

**Measured with that** (the same system, one run each; the window put under another application's window for 15 s, the
focus not touched):

| Work  | Before the cover           | Covered                                         | After the window was back                        |
| ----- | -------------------------- | ----------------------------------------------- | ------------------------------------------------ |
| Light | 240 frames a second        | 228 to 241 in every second, the swap interval 1 | The waits back after 0.15 s, 240 frames a second |
| Heavy | 120 (a swap interval of 2) | 113 to 115, the swap interval 2                 | The waits back after 0.25 s, 120 frames a second |

The waits were stopped in every covered frame, in one stretch. An answer took 3.8 ms at one refresh per frame and 8.2 ms at
two (10.7 and 8.3 ms in the run before): not a fixed cost, it looks like the time to the next refresh the loop is paced to.
A window of the application's own laid over it did nothing: the system held the frames back only under another
application's window.

Another program was using this machine's GPU and CPU while these runs were made, so they were made again.

**Measured again on a quiet machine** (the first integration at 431960c, Windows, one display on, 240 Hz at a fixed refresh rate, Vulkan FIFO with a wait for a
present, the vertical blank of the display the window is on, a window, light work unless said, the presents that may wait
left to the pacer, 1,400 frames a run with the first 240 left out, driver display times; the program that had used the
machine closed; one run each, the window covered for 15 s):

| Pacer and work                                          | Before | Covered, frames a second | Swap interval | Waits that ran out | Stretches the waits were stopped in | The waits back after |
| ------------------------------------------------------- | ------ | ------------------------ | ------------- | ------------------ | ----------------------------------- | -------------------- |
| A timer, a wait for a present, light                    | 240    | 229 to 241               | 1 throughout  | 200                | 2 (3,572 of 3,574 frames)           | 0.159 s              |
| A timer, a wait for a present, heavy                    | 120    | 91 to 118                | 2 throughout  | 88                 | 10 (1,619 of 1,648 frames)          | 0.251 s              |
| Vertical blank times, a wait for a present, low latency | 240    | 210 to 232               | 1 throughout  | 165                | 6 (3,321 of 3,335 frames)           | 0.161 s              |
| Vertical blank times, a wait for a present, smoothness  | 240    | 216 to 239               | 1 throughout  | 191                | 7 (3,465 of 3,486 frames)           | 0.159 s              |

So it holds for both pacers that wait: the swap interval did not change in any covered stretch, and the frame rate was
what it had been as soon as the waits were back. The waits were stopped in several stretches and not in one as in the run
before: a covered window's frames are shown now and then, two answers in a row then say shown, and the next two waits run
out again. That is what the covered frame rate is short of the rate before.

One thing the cover left behind in the pacer for vertical blank times: frames counted as shown later than worked out (12
and 21 at the end of the two runs, against 5 to 7 in runs that were not covered), and with them the place a frame is to be
ready at had moved to 1.04 ms and to the start of the refresh, where it stays. Every one of those frames came within four
frames of a wait that ran out or of the waits starting again (nine times in the two runs). **Changed for it** (unit tests;
not measured again): a frame shown later within eight frames after a wait that ran out, or while the waits are stopped, is
counted and does not move the place, and a place that was moved within the eight frames before a wait ran out is moved
back. A window that is not shown says nothing of where a display takes a frame. Three readings were off in each run.

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

## A loop the system holds

Decision 6, built for the pacer on a timer with the refresh period only (`TimerPeriodOnlyPacer`). Checked on the simulation
only: **not measured.**

**What the application gives:**

- **Its own waits**, the ones the pacer did not ask for, each after the wait and before the frame starts
  (`AddSystemWait`, a `SystemWaitReport`: the kind, when it began and when it ended). There are two kinds. A wait for an
  image (`SystemWaitKind::Acquire`) is the display's side holding the loop while its queue is full, and so is a present
  that waited, which the present's own report has. A wait for a frame slot (`SystemWaitKind::FrameSlot`) is the GPU not being
  done with an earlier frame.
- **The swap chain's images** (`PacerSettings::SwapChainImages`, zero for not known). One is on screen and one is drawn
  into, so that many less two can wait, and no tier pacer keeps a larger reserve (`ReserveFrames`).
- **That the system holds the loop while its queue is full** (`PacerSettings::SystemHoldsLoop`). A setting for now: the
  tier pacers take no capability set until the front is built, where it is the capabilities `PresentWaits` and
  `AcquireWaits`.

**What the pacer does with them**, with the aim of smoothness at one refresh per frame and that setting on:

- The reserve is what the swap chain holds (its images less two, where they are known), whatever the presents that may
  wait say: nothing is made ahead on top of it.
- The loop is held to a quarter of a refresh period before a frame is due. So the loop is there first, and the system's
  wait, not the pacer's timer, says when the frame starts. The timer is what is left when the system does not hold the loop
  after all: the frames then start a quarter of a period early, a period apart, and nothing runs away.
- A frame whose start the display's side held for an eighth of a refresh period or more **past the time the loop is held
  to** is one the system let through when it had room, which is when the display took a frame. It is not late and gives up
  no step, and the grid is moved to its start. So the loop is paced by the display, in step with it whichever way the given
  refresh period is off. A wait that was over before that time paced nothing, however long it was: the timer did.
- A wait for a frame slot excuses nothing: a frame that starts late by it is late by the GPU's work, as before. It is
  counted (`FrameSlotHeldFrames`, next to `SystemHeldFrames`).
- Without the setting, with the aim of low latency, or at two refreshes per frame or more, the reports are counted and
  nothing changes.

**What the simulation shows** (240 Hz, light work, a swap chain whose wait for an image holds the loop while none is free,
a display whose refresh period is 0.2 % off what the loop was told):

| The display is  | Images | The pacer goes by its timer                                                                      | The system paces the loop                                  |
| --------------- | ------ | ------------------------------------------------------------------------------------------------ | ---------------------------------------------------------- |
| As told         | 3      | Every frame for one refresh, one frame waiting                                                   | The same                                                   |
| 0.2 % slower    | 3      | Every frame for one refresh; a frame counted late now and then                                   | Every frame for one refresh, none late                     |
| 0.2 % faster    | 3      | The frames that wait run out: frames on screen twice, 0.2 to 1.6 refreshes from start to display | Every frame for one refresh, one frame waiting             |
| As told, or off | 4      | As with three images: the reserve asked for is one frame                                         | Two frames waiting, steady, three refreshes to the display |

**What it does not answer.** On the one system measured the wait for an image never held the loop in a window (0.002 ms),
and what paced the loop there was the host's wait for the GPU's work on the frame before. That is a wait for a frame slot,
which this change does not take as the display's. Whether that system has a case where the display's side holds the loop
(full screen, where the swap chain kept to its images; a present that waits) is to be measured.

**First runs with the waits reported** (the first integration, Windows, 240 Hz, a window, three images, light work, the
setting off; two runs of 1,400 frames): with smoothness a wait for a frame slot held 1,393 frames and the display's side 3;
with low latency each held 2. So in a window it is the frame slot that paces a loop that is not held to a time, as the
earlier runs said, and the setting has nothing to work with there.

Another program was using this machine's GPU and CPU while these runs were made. Made again on a quiet machine (one run
each, 1,400 frames): with smoothness a wait for a frame slot held 1,384 frames and the display's side 1, a frame on screen
11.77 ms after its start (2.83 refreshes), every frame for one refresh; with low latency they held 2 and none, 1.41 ms
(0.34 of a refresh), every frame for one refresh. So it stands.

**A second system, where the present waits for a share of a refresh** (the first integration on Linux with a Wayland
compositor, a 60 Hz mode, four images, smoothness, 600 frames a run with the first 120 left out; a virtual machine, as
was learnt afterwards, so its display is the virtual machine's and it has no display times):

| The setting | A frame every             | The present waited      | The wait for an image                       | Held by the display's side | Late frames |
| ----------- | ------------------------- | ----------------------- | ------------------------------------------- | -------------------------- | ----------- |
| Off         | 16.66 ms (13.82 to 19.50) | 6.07 ms (5.11 to 10.62) | 0.03 ms; none an eighth of a period or more | 599 of 600 frames          | 0           |
| On          | 7.13 ms (5.36 to 29.32)   | 6.00 ms (5.03 to 12.71) | 0.007 ms; 121 of 480 an eighth or more      | 599 of 600 frames          | 0           |

With the setting on the loop ran at more than twice the rate of its mode. The present waits there in every frame, for
about a third of a refresh period, and that is more than the eighth that counted as the display's side holding the loop:
so every frame was taken as let through by the display, no start time was given, and the present's 6 ms was all that paced
most frames. What was on screen is not known. So the timer was not what was left where the system does not hold the loop
to the display after all: not on a system that holds it for a share of a refresh.

**What was changed for it** (unit tests and the simulation; not measured again): only the part of a wait that comes after
the time the loop is held to counts for letting a frame through, as the list above now says. A present that waits a third
of a refresh in every frame is over before that time: the frames then start where the timer holds them, a period apart,
and the grid stays. The count of frames the display's side held (`SystemHeldFrames`) is still of the waits as reported.

## The pacer for vertical blank times

The pacer of tier 3.2 (`VBlankPeriodOnlyPacer`: the frame loop holds a frame and knows where the refreshes are; the refresh
period only for the frames that wait). Built, checked on the simulation, and run on one system with driver display times
("First runs" below): no capture of it has been analysed with the tools.

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

**First runs** (the first integration, Windows, 240 Hz, light work, the vertical blank of the display the window is on read
in every frame, driver display times; two runs of 1,160 frames, not the measurement list): with low latency a frame was on
screen 2.82 ms after its start at the median, 0.68 of a refresh where the simulation has 0.65, with one frame of 1,158 not on
screen for exactly one refresh and one pause after start-up. With smoothness and two presents that may wait it was 11.77 ms,
2.83 refreshes where the simulation has 2.5, every frame on screen for one refresh. No reading was off the ones before it.

Another program was using this machine's GPU and CPU while these runs were made. **Made again on a quiet machine**
(the first integration at 431960c, Windows, one display on, 240 Hz at a fixed refresh rate, Vulkan FIFO with a wait for a
present, the vertical blank of the display the window is on, a window, light work unless said, the presents that may wait
left to the pacer, 1,400 frames a run with the first 240 left out, driver display times; the program that had used the
machine closed):

| Run                     | Frames not on screen for one refresh | A frame's start to its display   | Start-up pause | Readings that were off |
| ----------------------- | ------------------------------------ | -------------------------------- | -------------- | ---------------------- |
| Low latency             | 0 of 1,158                           | 2.81 ms, 0.68 of a refresh       | One            | None                   |
| Smoothness              | 0 of 1,156                           | 11.75 ms, 2.82 refreshes         | None           | None                   |
| Low latency, 4 min 22 s | 4 of 62,577                          | 2.845 to 2.849 ms in each minute | One            | None                   |

The long run is the one that asks whether anything slides: the time from a frame's start to its display was 2.849, 2.845,
2.845, 2.846 and 2.848 ms in its five minutes (the last one 22 s), where the pacer on a timer rose by about fifteen parts
in a million of the time passed. The swap interval was 1 throughout and the animation was no refresh behind the clock at
the end. One run, closed by hand.

**Readings that are no vertical blank times** (the first integration on a second system: Linux, a Wayland compositor, a
60 Hz mode, one run of 600 frames a pacer; the readings there are the display times of the application's own earlier frames,
which the compositor reports as in sync and from the hardware's clock. Learnt afterwards: that system is a virtual
machine and its display the virtual machine's, so this is a system whose display times are poor, and says nothing of that
window system in general):

- A new display time came for every other frame only, and the time between two of them was 33.3 ms at the median but 27.4
  to 38.2 ms: up to 5 ms off two refreshes. As vertical blanks they fit no grid: 231 of the 239 steps were more than an
  eighth of a refresh off a whole number of refreshes.
- The pacer took every one of them, as it did then, and its loop followed them: a frame every 15.5 ms at the median, 8.7 ms
  at the shortest and 31.9 ms at the longest. The pacer on a timer had 13.8 to 19.5 ms on the same system, and today's pacer
  15.5 to 17.7 ms. On the first system, with the vertical blank of the display the window is on, no reading in 1,160 was
  off.

What was changed for it, in both pacers of vertical blank times (unit tests and the simulation; not measured again):

- **A reading that is off is not taken by itself.** More than an eighth of a refresh period from where the readings before
  it put the vertical blanks, it is counted (`VBlankJumps`) and the frames go on by the refresh period from the last
  reading that was taken. Eight in a row that are on one grid of their own are the display's, which has changed, and the
  last of them moves the pacer's grid to it. A count that rises with nearly every reading says that the source is no
  vertical blank time; the pacer is then a pacer on a timer, which is what such a system has.
- **A reading that is taken moves the vertical blanks a quarter of the way to it**, the first one whole. One reading is not
  exact, and the readings after it move them the rest of the way, so a display that is a little off its period is followed
  as before.
- In the simulation, with every reading up to 0.3 of a refresh off either way, the frames start a refresh apart and the
  swap interval stays; before the change the loop followed the readings.

The application does not have to judge its source: it gives what the window system gives, and the pacer says what it made
of it.

**Not in it yet.** The refresh period is the settings': a reading's own period is not used, and the pacer does not measure
the period from the readings (it does not need to, for where its frames are; the animation time still advances by the
period it was given, 17 to 19 parts in a million off on the one system measured). It does not learn of a frame that waits
although it was ready in time, as no pacer does that has the refresh period only for that. The application's own waits and a
swap chain that holds the loop are decision 6.

### Vertical blank times with a wait for a present

The pacer of tier 3.1 (`VBlankWaitForPresentPacer`). Built, checked on the simulation, and run on one system with driver
display times ("First runs" below): no capture of it has been analysed with the tools. It is the
pacer above plus the wait that the pacer on a timer has (the frame start plan's wait, `PresentWaitReport`, the presents that
may wait, the longest wait counted in the frame's swap intervals, and what it does while a window is not shown).

**What the wait adds when the vertical blanks are known:**

- **The frames that wait can not grow**, whatever the pacer believed of a frame: the loop stands until the present so many
  back was shown. The pause after start-up is not needed, as in the pacer on a timer with a wait.
- **Which vertical blank a frame was shown at becomes a fact, some frames later.** A wait that held the loop returns
  shortly after the display took the frame (a median of 0.92 to 1.24 ms after its display time in the 14 measured runs at
  240 Hz, which is 0.22 to 0.30 of a refresh, and never before it), so the vertical blank at or before the return is the
  one the frame was shown at. The display takes one frame per refresh, so the frame that was last made is shown as many
  blanks later as it was made frames later, at the soonest. Where that is later than the pacer had worked out, that frame
  is late and the next one is for a blank that much later (`ShownLaterByWaits`). A wait that returned at once for the frame
  last made says that it was shown by then, which the aim of low latency does not count on by itself. This closes the gap
  named above: a frame that waited although it was ready in time.
- **Where a frame has to be ready is learnt from it.** The first version of this text left that out, and the simulation
  showed that it can not be left out: a display that takes a frame sooner before a vertical blank than the pacer has it
  ready shows every frame a blank late, the wait says so every time, and a pacer that only counts those frames as late
  slows down without end (a swap interval of 13 in the simulation). So: one frame shown later than worked out is a refresh
  the display lost. A second one within eight frames is a display that takes a frame sooner than that (or a GPU whose time
  nobody reported), and the place a frame is to be ready at is moved an eighth of a refresh period earlier
  (`ReadyPlaceNow`), down to the start of the refresh. What is still late with a frame ready when its refresh begins is
  late by its work, and the rule answers it.
- **The place goes back** (built on 2026-10-08, after the first runs below; the simulation only). What moved it may have
  passed: a swap chain's first frames did, in every run on the one system measured. So after a frame window's length
  without a frame shown later the place is tried one step later (`ReadyPlaceTries`). If no frame is shown later in the
  sixteen frames after that, the try holds, and the next step is tried after another frame window's length. If one is,
  the try is taken back at once (`ReadyPlaceTriesTakenBack`) and the next one comes after twice as long, doubled up to
  ten times. A window that stops being shown during a try puts the place where it was before the try, uncounted. What
  it costs where the place was right: in the simulation's display, which does take a frame earlier than the settings'
  place, a try is answered by two frames on screen a refresh longer; with a frame window of two seconds that was eight
  such frames in 8,000 (the tries at 2, 6, 14 and 30 s), and ever fewer after.
  A frame shown later around a wait that ran out, or while the waits are stopped, moves nothing ("A window that is not
  shown", above).

**The two aims:**

| Aim         | The wait                                                          | The frame                                                                                |
| ----------- | ----------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Smoothness  | Before the frame starts, for the present as many back as may wait | Starts when the wait is over; its present is held to its place, the reserve as above     |
| Low latency | The same                                                          | Its start is then held as above (the plan is made again after the wait), present at once |

**The pacer picks the presents that may wait** (decided on 2026-10-08): an application says what it aims for and nothing
of this. The pick is two at every tier and with either aim, so one may wait: one gave half the frame rate at heavy work on
the first integration, and frames for one and for two refreshes in turn in the simulation. With vertical blank times and
the aim of low latency two costs nothing, as a frame's start is held and nothing waits. The number can still be set
(`SetWaitingPresents`), for measuring and for tests; whether the pick should differ by tier or by aim is left to what the
measurements show.

**What the simulation shows** (240 Hz, light work, two presents that may wait unless said):

| Case                                                                  | Vertical blank times alone                            | With the wait, low latency                                                               | With the wait, smoothness       |
| --------------------------------------------------------------------- | ----------------------------------------------------- | ---------------------------------------------------------------------------------------- | ------------------------------- |
| A display that takes a frame up to 0.4 of a refresh before the blank  | Every frame for one refresh, none waiting             | The same                                                                                 | The same, one frame in reserve  |
| The display loses three refreshes by itself                           | Three frames wait from then on, three refreshes later | As before, a few frames after each                                                       | The reserve as it was           |
| A display that takes a frame 0.5 to 0.8 of a refresh before the blank | Every frame misses its blank and waits a refresh      | A few frames late, the place moves, then one refresh from start to display, none waiting | One frame in reserve throughout |
| GPU work of 60 % of a refresh that nobody reports                     | A frame waits                                         | The same finding out, none waiting                                                       | One frame in reserve            |
| One present that may wait, a display that takes a frame 0.4 before    |                                                       | Frames on screen for one and for two refreshes in turn                                   | The same                        |
| One present that may wait, GPU work of 90 % reported                  |                                                       | Two refreshes per frame, as with a timer                                                 |                                 |

The last two rows are the cost of one present that may wait, as with a timer: the wait for the last present returns a share
of a refresh into the refresh the frame is made in, and what is left of it has to hold the frame's work.

**First runs** (the first integration at 431960c, Windows, one display on, 240 Hz at a fixed refresh rate, Vulkan FIFO with a wait for a
present, the vertical blank of the display the window is on, a window, light work unless said, the presents that may wait
left to the pacer, 1,400 frames a run with the first 240 left out, driver display times; the program that had used the
machine closed):

| Run                                    | Frames not on screen for one refresh | A frame's start to its display, median            | Shown later by the waits | The ready place at the end (from 2.08 ms) | Waits that ran out |
| -------------------------------------- | ------------------------------------ | ------------------------------------------------- | ------------------------ | ----------------------------------------- | ------------------ |
| Low latency, five runs                 | 0 of 1,158 in every run              | 3.88, 3.88, 3.35, 3.88, 3.87 ms (0.93, once 0.80) | 6, 6, 5, 6, 7            | 1.04 ms in four, 1.56 ms in one           | 1, 1, 1, 1, 0      |
| Smoothness, two runs                   | 0 of 1,157 in both                   | 7.21 and 7.20 ms (1.73 refreshes)                 | 5, 5                     | 1.56 ms in both                           | 1, 0               |
| Low latency, one present that may wait | 0 of 1,158                           | 3.20 ms (0.77 of a refresh)                       | 4                        | 1.56 ms                                   | not read           |

The swap interval was 1 in every frame of every run, no reading was off, and with low latency no step had an animation
error over 1 ms. Two things in the numbers:

- **The ready place moved in every run, and that is the latency over the pacer without the wait.** That pacer had a frame
  on screen 0.68 of a refresh after its start with the place at the middle. Here the place ended a quarter of a refresh
  period earlier in four runs and an eighth in one, and the frames were on screen 0.93 and 0.80 of a refresh after their
  start: the same amounts later.
- **What moved it was the start of the run, in all eight runs.** Every frame that the waits said was shown later came in
  the first 13 frames (the first at frame 2 or 3, the last at frame 10 to 13), where the display times have a frame on
  screen for two refreshes every third frame or so. Not one came in the 1,387 frames after. So on this system the
  learning took a swap chain's first frames for a display that takes its frames early, and paid up to a quarter of a
  refresh for the rest of the run. Changed since: the place goes back (above). Not measured again.
- **One present that may wait** did not give frames for one and for two refreshes in turn, as it does in the simulation:
  every frame was on screen for one. One run, light work.

**What it does not do.** The vertical blank a wait's return falls after is taken as the one the frame was shown at. That
holds while the return comes within a refresh period of the display taking the frame, which it did at 240 Hz; at a much
higher refresh rate a return that late would read as a blank more. The refresh period is the settings', as above.

## Every time is in nanoseconds

Decided on 2026-10-07 and built: the whole pacer module counts in nanoseconds, the unit in which no platform loses
anything (a platform that counts in ticks of 100 ns multiplies by 100; one that counts in nanoseconds lost up to 99 ns on
every value it put into ticks, and for a refresh period that is a rate error: 21 parts in a million for the measured
display's 4,166,389 ns when cut to a tick).

- A point on the application's steady clock is a `NanosecondTickCount`, a span a `NanosecondTimeSpan`, and a length of
  time that can not be negative a `NanosecondTimeDuration`: a frame time and the CPU busy time are durations, as the
  marker's payload takes them, and the payload caps what its four bytes do not hold.
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

| Capability             | The application can …                                                                                                                                  | Examples (not named in the API)                                                                                                      |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `PresentSwapInterval`  | present with a swap interval of 1 to a maximum it gives: the present holds the frame that long                                                         | `eglSwapInterval`, DXGI's sync interval                                                                                              |
| `PresentAtTime`        | give a present a time before which the frame is not shown                                                                                              | `VK_EXT_present_timing` (absolute), `EGL_ANDROID_presentation_time`                                                                  |
| `PresentAfterDuration` | give a present a time the frame before it stays on screen at least                                                                                     | `VK_EXT_present_timing` (relative), Metal's minimum duration                                                                         |
| `WaitForPresent`       | wait until a present it names was shown, with a timeout                                                                                                | `VK_KHR_present_wait2`, DXGI's waitable swap chain                                                                                   |
| `WaitForGpuWork`       | wait until the GPU finished a frame it names, with a timeout                                                                                           | a fence                                                                                                                              |
| `WaitForImage`         | wait until an image of its swap chain is free to draw into, with a timeout, and say how many images there are                                          | the fence of a Vulkan acquire                                                                                                        |
| `FrameCallback`        | say when the window system called for a new frame                                                                                                      | Wayland's frame callback                                                                                                             |
| `VBlankTimes`          | say when a vertical blank was, and the period between them                                                                                             | DXGI's output, Wayland's presentation feedback, Choreographer                                                                        |
| `GpuWorkTimes`         | say when the GPU began and ended its work on a frame, on the application's clock, frames later: with the CPU's own times, how the two overlap          | calibrated timestamp queries                                                                                                         |
| `GpuWorkDurations`     | say how long the GPU worked on a frame, frames later: how long, not when                                                                               | timer queries                                                                                                                        |
| `DisplayTimes`         | say when a present was shown, or that it has a result without a time, frames later                                                                     | `VK_EXT_present_timing`, DXGI's frame statistics, `EGL_ANDROID_get_frame_timestamps`                                                 |
| `PresentSkipsOverdue`  | say that of two presents whose times have both passed its display's side shows the later and never the earlier: a fact of its platform or present mode | Vulkan's `FIFO_LATEST_READY` present mode with a target time, the composition swapchain of Windows 11, Android's buffers with a time |

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
and returns at once. So it makes no tier either, and it sits beside whichever of them the set has. There are two capabilities, and
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

**A tier is a set of capabilities, and it names how a frame is paced.** A capability set **reaches** a tier when it holds
what the tier needs, and its **rating** is the best tier it reaches. The rating is a function of the set alone and changes
nothing (`Rate(capabilities)`), so it can be asked for any set, before a pacer exists: "what would I get without the wait for
a present". It also says which capabilities would raise the tier. The pacer gives the rating of the set the application has
(the **capability tier**) and of the active set (the **active tier**), by that same function.

An application that shows the tiers takes their words from the library and writes none of its own (`PacerTierText`): a short
name for each tier and each capability, the number a tier is shown as ("3.1"), a line for each tier and each major tier
that says what it uses and what that gives, a shorter line of 44 characters or less, how many tiers there are, and the
mark for a set that reports display times.

**The list is three major tiers of four sub tiers each** (decided on 2026-10-09; what it replaced is below). A major
tier is who places a frame on its refresh, and a tier is written as its major tier and its sub tier: "3.1". It is
still one order, 1.1 the best and 3.4 the baseline that every set reaches, and no two sets that are paced differently
share a place.

| Major tier | Who places the frame                                           | Needs                                   | Status                                                                            |
| ---------- | -------------------------------------------------------------- | --------------------------------------- | --------------------------------------------------------------------------------- |
| 1          | The display's side, and it skips a frame that is overdue       | `PresentAtTime` + `PresentSkipsOverdue` | Rated only: no pacer is built, and a set that reaches it is paced as major tier 2 |
| 2          | The display's side, every frame in the order it was presented  | `PresentAtTime`                         | Built (`TierPacer`); the simulation only, no system measured                      |
| 3          | The frame loop: it has to make the present at the right moment | nothing                                 | Built; first runs and measurements on one system                                  |

Inside a major tier a sub tier is a rank, from two capabilities:

- **A wait for a present** (`WaitForPresent`): the loop is held until the display took an earlier frame, so the frames that
  wait can not grow. Without one the loop is held on a timer.
- **Vertical blank times** (`VBlankTimes`): the pacer knows where the display's refreshes are. Without them it counts
  refresh periods on the clock.

| Tier        | Needs, with its major tier's     | What holds the loop      | Refreshes from      | Status                                                               |
| ----------- | -------------------------------- | ------------------------ | ------------------- | -------------------------------------------------------------------- |
| 1.1 and 2.1 | `WaitForPresent` + `VBlankTimes` | The display took a frame | Vertical blanks     | As its major tier                                                    |
| 1.2 and 2.2 | `WaitForPresent`                 | The display took a frame | A grid on the clock | As its major tier                                                    |
| 1.3 and 2.3 | `VBlankTimes`                    | A timer                  | Vertical blanks     | As its major tier                                                    |
| 1.4 and 2.4 | nothing more                     | A timer                  | A grid on the clock | As its major tier                                                    |
| 3.1         | `VBlankTimes` + `WaitForPresent` | The display took a frame | Vertical blanks     | Built (`VBlankWaitForPresentPacer`); first runs on one system        |
| 3.2         | `VBlankTimes`                    | A timer                  | Vertical blanks     | Built (`VBlankPeriodOnlyPacer`); first runs on one system            |
| 3.3         | `WaitForPresent`                 | The display took a frame | A grid on the clock | Built (`TimerWaitForPresentPacer`); measured on one system           |
| 3.4         | nothing (the baseline)           | A timer                  | A grid on the clock | Built (`TimerPeriodOnlyPacer`); measured on one system, one run each |

- **The rule of the order:** who places the frame first, which is the major tier. Inside it, where the display's side
  places the frame, what holds the loop comes before where the refreshes are. Where the loop places it, the vertical
  blank times come before the wait, as they are what makes the loop's placing good (decided from the runs on a quiet
  machine, below). So a sub tier is a rank inside its major tier and no code for a capability: the second sub tier is a
  wait for a present in major tiers 1 and 2 and vertical blank times in major tier 3. **Every order in this list is a
  proposal until the tiers have been measured against each other.** The eight tiers of major tiers 2 and 3 can be
  reached on one system that has the three capabilities, by leaving them out of the active set.
- **Why major tiers** (2026-10-09): the list had become a group by one feature with the other features ranked inside
  it, and twelve numbers in a row said less than that. A new way to place a frame is a new major tier and renumbers
  nothing (a display that skips now, and a display with a variable refresh rate later: "Variable refresh"), a major
  tier's sub order can change when it is measured without touching the others, and the number says the two things apart.
- **A time on the present by itself puts a set in major tier 2.** With it the display's side decides which refresh a
  frame is shown at, from a time the pacer worked out, so where in a refresh the present call lands stops mattering.
  That is the fault measured again and again in major tier 3: a timer that lands at the edge of a refresh, and the
  place a frame has to be ready at. It needs no vertical blank times: a time before which a frame is not shown can be a
  step of the grid on the clock (a constant offset to the real refreshes does not show).
- **Only a time before which a frame is not shown does** (decision 16, decided on 2026-10-09; found while building the
  two, "The timed present, as built", below). Such a time places the frame: whenever the present is made, the frame is
  shown at its refresh. A time the frame before it stays (`PresentAfterDuration`) places nothing by itself, as it counts
  from wherever that frame was shown: it keeps a frame from being shown a refresh early, and at one refresh per frame
  that is what a display that shows one frame per refresh does anyway. So it makes no tier. It is in a rating beside
  the tier, as the display's side holding a frame (`DisplaySideHolds`, with a swap interval on the present), and its
  duration is given on every present wherever it is active, at whatever tier.
- **A display's side that skips a frame that is overdue is major tier 1** (decided on 2026-10-09: rated now, built
  later; "A display that skips a frame that is overdue", below). With two frames due, it shows the later and never the
  earlier: after a frame that came late the frame on screen is the one made for that refresh, and frames can not pile
  up, which is the one thing a time on the present does not do for a loop without a wait. `PresentSkipsOverdue` counts
  only with `PresentAtTime`: without a time nothing is due. It is a fact of a platform or of a present mode, and
  nothing a pacer switches. **No pacer is built for it.** A set that reaches major tier 1 is paced as the same sub tier
  of major tier 2 (`PacerTierUtil::PacedAs`, and the tier the pacer is working at says so), and that pacer takes every
  present as shown: where a display did skip a frame, its counts are off by that frame until the pacer is built.
- **What vertical blank times add to a time on the present** (tiers 2.1 and 2.3 over 2.2 and 2.4, and the same in major
  tier 1): the time given is a real refresh, so
  nothing slides. A grid on the clock ran 15 to 19 parts in a million off the display in the runs of the one machine
  measured, which is a refresh about every four to five minutes at 240 Hz: one frame is then on screen a refresh more or
  less. And the
  aim of low latency knows when to start a frame (0.68 against 0.95 of a refresh from a frame's start to its display, in
  the runs of tiers 3.2 and 3.3).
- **What a timed present does not do:** on a display that shows every frame in the order it was presented, it does not
  shorten a queue. After a refresh the display lost by itself, tiers 2.3 and 2.4 do not learn of the frame that waits,
  where tier 3.1 does. So tiers 2.3 and 2.4 above tier 3.1 is the rule of the order and nothing a run has shown. Some displays
  do not show every frame: "A display that skips a frame that is overdue", below.
- **What is measured of a timed present** (the first integration's own loop, Windows, the relative kind only, as that
  system has no absolute one; captures of 2026-10-05 as its documents have them, driver display times): at two refreshes
  per frame on 240 Hz, 4 of
  2,336 frames were not on screen for two refreshes, against 116 of 2,335 with the loop held on a timer; at 120, 60 and
  50 Hz 336 or 337 of 337 were. **At one refresh per frame it has not been run.** With the window next to a display at
  twice its refresh rate every frame was held twice as long as asked: the refreshes a duration is counted in have to be
  those of the window's display.
- **Every tier has both aims** ("The rules that move into the pacer"). How few frames wait is the aim's, at every tier,
  and it makes no tier.

**Beside the tier, in a rating:**

- **`+`: display times are reported** (`DisplayTimes`, `PacerRating::ReportsDisplayTimes`, shown after the tier's number:
  "3.1+"). The animation error can then be worked out where the application runs: the animation time step is the pacer's,
  and the display time step is between two reported times. It is statistics, as display times are in today's pacer, and
  changes no frame and no tier. Driver display times are no measurement by the tools, and the mark says that they are
  reported, not that they are right. Pacing by them is "Pacing by display times", below.
  **Built on 2026-10-08** (`DisplayErrorCounter`, `TierPacer::AddDisplayReport` and `DisplayErrors`; the simulation and
  unit tests only): the application gives each frame's display time back by its frame id, a few frames later, or says
  that it was never shown. The rules are the measuring tools': a frame is judged when it and the frame before it were
  both reported as shown; its animation error is its animation time step less the time between the two display times;
  more than 1 ms either way is an error frame; half a refresh or more is a frame at another refresh than it was made
  for, and late when it is the later one; a step next to a frame without a display time is not judged. Counted since
  the pacer was made and for about the last second (eight eighths of a second), over the last 64 frames at most, with
  nothing allocated. A pause the pacer did not ask for shows as one late frame. What it is not: the tools read the
  display, and this reads what the platform says.
  **On one system** (the first integration, 2026-10-08, the conditions of "The duration on one system"): 16 runs of
  1,200 frames with the driver's first pixel times reported. The pacer's five counts were the first integration's own
  script's, number for number, in all 16, a run with 134 frames off their swap interval among them; no report was
  refused; and the runs with reports were the runs without them (the same times from start to display, the same frames
  waiting). No frame had an error over 1 ms and under half a refresh, and none of those within half a refresh was off
  by more than 0.01 ms: that driver's times are on the refresh grid to a few microseconds, which does not say whether
  they are measured or worked out.
- **A swap interval on the present** (`PresentSwapInterval` of two or more, `DisplaySideHolds`): the display's side holds a
  frame of more than one refresh for exactly its refreshes, whenever the loop presents it. Below tier 2.4 that is the one way
  the display's side holds a frame, and it does nothing at one refresh per frame, so it changes no tier. A frame the
  display's side holds is held while it waits too, so waiting frames do not go away by themselves at a longer swap
  interval, as they do where the loop holds the frame. Not used by a pacer yet.
- **A wait for the GPU's work** (`WaitForGpuWork`: a fence): the tiers without a wait for a present (3, 4, 6 and 8) hold
  the loop until the GPU finished an earlier frame, where the application can wait for that. The pacer says which frame:
  the one before with the aim of low latency, the one before that with smoothness. It keeps the loop from running ahead of
  the GPU and says nothing of the display, so it is no tier. The one run there is of it (the first integration's own wait
  of that kind, with smoothness on tier 3.4) had every frame on screen for one refresh, 2.83 refreshes after its start: it
  paces at a full queue.
  **Built on 2026-10-08** (`GpuWaitRule`, `FrameStartPlan::WaitForGpuWorkFrameId`, `GpuWaitReport`,
  `TierPacer::AddGpuWait`; the simulation and unit tests only). Where `WaitForGpuWork` is active and `WaitForPresent`
  is not, the frame start plan asks for the wait before the wait for the start time: for the frame before the one
  that is about to be made with the aim of low latency (one frame in flight), and for the frame before that with
  smoothness where the application lets two frames be in flight (`MaxFramesInFlight`; one otherwise). It is the
  application's one wait for a frame slot: the pacer names the frame, the application makes no such wait of its own next
  to it, reports what became of the wait and asks for the plan again. A wait may take a few of the frame's swap
  intervals, and one that runs out is counted (`GpuWaitTimeouts`). A frame's work is judged by the frames in flight the
  wait makes. In the simulation, with GPU work of 130 % of a refresh that nobody reports: a loop without any wait had
  more than 20 frames the GPU had not got to, and with the wait none (low latency) or one (smoothness). The wait says
  that the GPU is done with a frame, not how long it worked: the rule stays at two refreshes per frame only where the
  GPU's work is reported too. And what the aim of low latency costs a loop whose CPU and GPU each work 72 % of a
  refresh: with smoothness the two are side by side and a frame a refresh holds; with low latency they come one after
  the other, and it is two refreshes per frame. Nothing finer than the frame before and the one before that is decided
  until it is measured under a real GPU load, with and without the wait, with both aims.

**What reaches no tier and is no fact of a rating.** `WaitForImage` fills the queue where it works, and `FrameCallback`
is not built anywhere and can stop or come late. `GpuWorkTimes` and what a present does to the loop (`PresentWaits` and the
others, above) are information every tier takes. A wait for a present is not taken as a way to know where the refreshes
are: it returned 0.06 to 2.4 ms after the display took the frame (1 % to 99 % of 11,460 waits), which is over half a
refresh at 240 Hz. With the vertical blanks known it is used for less: a wait's return says which of them a frame was
shown at.

**Vertical blank times before a wait for a present** (tiers 3.2 and 3.3; decided on 2026-10-08 from the runs on a quiet
machine, one system, driver display times). Each has what the other lacks: tier 3.2 knows where the refreshes are and does
not learn of a frame that waits, tier 3.3 keeps the frames that wait to a number and has its refreshes as a grid on the
clock. What the runs say:

- Both had every frame on screen for its swap interval (tier 3.2 at one refresh per frame; tier 3.3 at one, and at two in the
  941 frames before the cover of one run).
- With the aim of low latency a frame was on screen 0.68 of a refresh after its start at tier 3.2 and 0.95 at tier 7. With
  smoothness it was 2.82 refreshes at tier 3.2 and 1.75 at tier 3.3: tier 3.2 keeps a frame more in reserve there, which is how
  its aim is built and not what the tier can do.
- What tier 3.2 lacks did not show: in 62,577 frames the time from a frame's start to its display did not rise, so no frame
  came to wait. What tier 3.3 lacks is where a timer's grid lands in a refresh, which is chance for each run, and at a longer
  swap interval it has shown as frames a refresh off (the first integration's own loop on a timer: 1 to 35 % of the frames
  at 240 Hz).
- Not measured: tier 3.2 at a longer swap interval, tier 3.3 over minutes, either one captured with the tools.

Where the handling comes from:

- **A wait for a present** is DXGI's waitable swap chain and Vulkan's present wait, as documented. Microsoft: "For every
  frame it renders, the app should wait on this handle before starting any rendering operations", with a maximum frame
  latency that is the number of frames that may be queued
  ([Reduce latency with DXGI 1.3 swap chains](https://learn.microsoft.com/en-us/windows/uwp/gaming/reduce-latency-with-dxgi-1-3-swap-chains)).
  Khronos: an application can use the wait "to monitor and control the pacing of the application by managing the number of
  outstanding images yet to be presented"
  ([VK_KHR_present_wait](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_present_wait.html)). The presents
  that may wait are their frame latency. What is this proposal's own: that the pacer, not the application, says which
  present to wait for, where in the frame, and how many may wait.
- **A timed present with a wait for the GPU's work** is how Android's frame pacing library is described: it "uses
  presentation timestamps to make sure frames are presented at the proper time and sync fences to avoid buffer stuffing",
  and injects "waits into the application that allow the display pipeline to catch up, rather than allowing back pressure
  to build up" ([Frame Pacing library](https://developer.android.com/games/sdk/frame-pacing)). Its page does not say where
  in a frame the wait sits or how a late frame is answered; those are this proposal's own.
- **The refresh period only for the frames that wait** has no documented model: what is documented for a loop without any
  signal is the full queue and its back-pressure. Never planning faster than the display is what today's pacer does. The
  grid on the clock is this project's own (the clock carries how many refreshes should have passed). Pacing a little slower
  than the display, one of the options in "Decisions needed", comes from the first integration and rests on no vendor's
  documentation.

**What this replaced.** Four lists came before this one, and each was wrong in a way worth keeping in mind:

- Two lists, how a frame is held and how the frames that wait are kept few, with a rating that was the pair and nine
  pacers. The second list was the aim of low latency written as tiers, from before the rule that every tier has both aims,
  and a pair of numbers with no order between them is no answer to "what am I paced by".
- One list of four (2026-10-08, built): vertical blank times and a wait for a present, in the four combinations that are
  tiers 3.1 to 3.4 now. Its top was what the one test system has, and a timed present was "a mechanism beside the tier" that
  no pacer used. A system with a timed present and no wait for a present would have rated second with its best mechanism
  unused.
- One list of eight (2026-10-08, built): the tiers that are major tiers 2 and 3 now, numbered 1 to 8, with either timed
  present in its top four. It had no place for a display that skips a frame that is overdue; a time the frame before
  stays was rated as placing a frame, which it does not; and eight numbers in a row hid that the list is a group by who
  places the frame with the rest ranked inside it.
- A list of six that asked for vertical blank times as well in its top tiers, and then one that had them "used when they
  are there" inside two tiers. A tier's number is to say how good the pacing is, so two sets that are paced differently
  do not share one.

### How a pacer is put together

**A tier is a combination of three choices, and each choice is one part.** The four pacers of tiers 3.1 to 3.4 were built as
four whole classes, the later ones from a copy of an earlier one: the two with vertical blank times share 134 of 158
lines of code, the two with a wait 74 of 110. Each thing that is missing (a timed present, a wait for the GPU's work,
display times) would have been written up to four times, and a front would have had to hand a run over between four
classes. So they are taken apart, under their own tests and with no change in what they do, and the eight tiers that
have a pacer are put together from the parts:

| Part                     | What it holds                                                                                                               | Its kinds                                                                     |
| ------------------------ | --------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Where the refreshes are  | The times of the display's refreshes, and which one a moment falls in                                                       | A grid on the clock; vertical blanks from readings                            |
| What holds the loop      | Which wait the frame start plan asks for, how long at most, and what a wait that ran out means (a window that is not shown) | A timer only; a wait for the GPU's work; a wait for a present                 |
| Who places the frame     | When the present is made and what it is given, and with low latency when the frame starts                                   | The loop (a place in the refresh to be ready at); the display's side (a time) |
| What every tier has      | The frames and their ids, the work as two stretches of time, the swap interval rule, the animation time, the system's waits | One                                                                           |
| What was shown (the `+`) | Display reports, and the animation error from them                                                                          | One, used where display times are reported                                    |

- **The active capabilities pick the parts, and nothing else does.** No part tries a mechanism and falls back on another
  inside its own rules: a wait that stops because a window is not shown is that part's rule, and the tier the pacer is
  working at says so.
- **One pacer holds the parts** and takes the capability sets: what the application has, and what of it is active
  (`TierPacer`). A change of the active set takes effect when the frame that is open has ended.
  The wait for a present is switched on or off where it is. Where the change is in who places the frames (the grid on the
  clock, the vertical blanks), the frames and their ids, the animation time, the swap interval with the rule's frame
  window, the GPU's work on the frames in flight and the presents that can be waited for are handed over; the first frame
  after it starts when the frame before it said the next one would, and is not judged against a place it never had. A
  vertical blank reading from before is not kept: the times are read anew. Nothing is allocated for it.
- **A handover keeps what is on its way.** The part that takes over is to end up where a run of its own would be, with no
  frame on screen a refresh more or less for it and no frame more waiting. Each of these was a fault first, found on
  the first integration's system or on the simulation's display, and is now what a handover does:
  - the frames that were made ahead (smoothness, one refresh per frame) are not made again: the first frame is placed as
    if this part had made them;
  - the presents go on a swap interval apart: the part that hands over says when its last present is made and when the
    next would be, the grid on the clock puts its step for the first frame where that makes its present, and the
    vertical blanks take the first frame for no blank sooner than a swap interval after the one the last frame was
    ready for;
  - a loop that a wait for a present held starts its next frame a swap interval after the last one, not at a time the
    wait had kept it from: this holds too where only the wait is given up on the grid on the clock, which then starts
    again at the next frame;
  - the pause after start-up is the swap chain's: made once, whichever part places the frames when it is due.
    Found with it: with low latency on vertical blanks a frame that was done early was presented at the vertical blank
    itself, where a display may still take it for that blank. It is presented the frame margin into the refresh now. On
    the first integration's system that changed the start of the runs with vertical blank times, the wait and low
    latency (three runs each way, the second handover block's conditions): the place a frame is to be ready at was
    learnt one step earlier in a run's first dozen frames than before (a quarter of a refresh against an eighth, the
    eighth being taken back by frame 11 before), and went back a step about every 490 frames, as it is built to: to
    where it began by frame 492, 490 and 990. Until then a frame was on screen up to 1 ms later after its start (a
    median of 2.84, 2.84 and 3.34 ms against 2.82). Every frame was on screen one refresh either way.
- **As built** there are two ways a frame is placed, each a class with the wait and the timed present as options: on a
  grid on the clock (`ClockGridLoopPacer`, tiers 2.2, 2.4, 3.3 and 3.4) and on the display's vertical blanks (`VBlankLoopPacer`,
  tiers 2.1, 2.3, 3.1 and 3.2). The wait (`PresentWaitRule`), the vertical blanks from readings (`VBlankTimeline`) and the values
  a timed present is given (`DisplayPlacementUtil`) are parts of their own, with their own tests. What every tier has (the
  frames, the work, the rule, the animation time) is still in both of the two, and is handed from one to the other, not
  shared. Checked by the tiers' own tests, by 280 runs of the simulation that came out byte for byte as before the
  pacers were taken apart, and by the first integration against its last pin. The handover is checked on the
  simulation's display: each of the twelve changes between the four ways of pacing, with both aims, at one, two and
  four refreshes per frame, leaves every frame on screen for its swap interval, the animation time no further behind
  the clock, and no frame more waiting than a run of the new way has (`tests/ActiveSetChangeLoopTests.cpp`; the four
  simulated loops are one loop now, which changes its active set in a run).
  **On one system, after these fixes** (the first integration, 2026-10-08; the conditions of "The duration on one
  system", with every other session on the machine asked to pause first and 1.9 to 6.9 % of the CPU in use by other
  programs; driver display times): 140 changes, 84 at one refresh per frame (three runs an aim) and 28 each at two and
  at four (one run an aim). In all of them the frame id, the swap interval and the animation step went on, and all 1,538
  intervals on screen around the changes were the swap interval. With smoothness no more presents waited after a change
  than in a run of the new way by itself, and for the two ways without a wait one fewer (1 against 2: such a run makes
  its frame ahead at its start, and a part that takes over does not). The refreshes the animation time is behind the
  clock rose at two changes only: where the wait for a present is switched on over a timer (one long frame start while
  the frames that wait come down, 5 of 6 times when the change was the first of a run, 3 of 3 with smoothness and 2 of 3
  with low latency, and not once of 6 later in it), and from a timer to vertical blanks with the wait with low latency
  (by one at the second frame, 3 of 3). The frame rows say what that one is: on the timer a frame was on screen 6.8 ms
  after its start, a refresh later than a pacer without a wait or display times has it, so the first frame on vertical
  blanks was made for a refresh a frame was still on its way to; the wait then said so, the second frame started 8.2 ms
  after the first, and from it on a frame was on screen 2.8 ms after its start. On screen every frame followed the one
  before it by one refresh. It is the wait finding a frame that waited, which the timer could not know of, and letting
  it through once: nothing to fix in the handover. With low latency the frame starts move at a change between a timer
  and vertical blanks, by up to three and a half refreshes at four refreshes per frame, as the two start a frame at
  different places before its refresh; no frame was on screen longer or shorter for it.
- **Each tier is still a pacer from the outside**: its own tests, and its own statement of what it promises and what it
  can not do. The four class names of tiers 3.1 to 3.4 stay, each the one pacer with its capabilities fixed.
- **Each part has both aims** where the aim bears on it: how many presents may wait and which frame's GPU work is waited
  for, a frame made ahead or a start that is held, a present at once with its time or a start held so that the frame is
  ready just in time.

#### The timed present, as built

Built on 2026-10-08 against the simulation's display, which was taught to honour both times on a present. **No system
has been measured with it**, and the one system at hand has the second kind only ("Tiers"). Every number here is the
simulation's, at 240 Hz.

**What the pacer gives a present** (`PresentPlan`, one of the two): where `PresentAtTime` is active, `NotBeforeTime`:
the frame's intended display time less half a refresh period. Otherwise, where `PresentAfterDuration` is active,
`MinimumDuration`: the frame's swap interval in refreshes less half a period. Half a period is as far from the refresh
before as from the frame's own, so neither a grid on the clock that is off the display's refreshes nor a period that is
a little off puts a frame on another refresh. With both active the time is given: it says which refresh. An application
that wants the duration leaves `PresentAtTime` out of the active set. One platform's own answer to a time that aims at a
vertical blank is a flag that lets a frame be shown at the start of the refresh its time falls in the first half of,
recommended "to compensate for small precision errors that may cause an image to be displayed one refresh cycle later
than intended" (the second page linked below). The half period is this proposal's way to the same end where there is
no such flag; neither has been run on a system.

**What the loop does**, by the kind and the aim:

| The present takes             | Smoothness                                                                                                                                                                                                   | Low latency                                                                                                                                                         |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A time not to show before     | No present is held: a frame is presented when it is done, and the display's side holds it. On vertical blanks, what held the present holds the next frame's start, so the loop makes its frames as far ahead | The start is held as without one; a frame is presented when it is done, and the time keeps one that is done early from being shown early. No present is held either |
| A time the frame before stays | Everything the loop does without a timed present, the held presents included, and the duration next to it                                                                                                    | The same                                                                                                                                                            |

The second row is the correction of the first design, in which both kinds dropped the loop's holds. A duration counts
from wherever the frame before was shown. Presented when done, the frames of a run at four refreshes per frame were each shown one refresh after their start, where the pacer's intended display time said four: the loop's hold is what puts the first
frame of a run on its refresh, and the duration keeps the ones after it there.

**What it changes, and what it does not** (`tests/TimedPresentLoopTests.cpp`, `tests/TierPacerTests.cpp`):

- **Where nothing goes wrong, nothing**: with light work every frame of all four ways of pacing is shown at the same
  vertical blank with either timed present as without one, with as many frames waiting, with both aims.
- **A present the loop holds to the edge of a refresh** (120 frames a second on a timer that wakes up to 0.1 ms late, a
  display that takes a frame 85 % of a refresh before its vertical blank): without a timed present 1,395 of 2,900 frames
  were not on screen for two refreshes. With either timed present none. With the time a frame was on screen two refreshes
  after its start and none waited; with the duration three, and one waited: the first frame that fell on the far side
  moved every frame after it a refresh later, where the loop's present then has the whole refresh. That is the shape of
  what the first integration measured with the duration (4 of 2,336 against 116 of 2,335).
- **A grid on the clock that slides against the display** (60 frames a second, a display 0.05 % slower than its mode):
  with the time on the present there is no moment near a vertical blank, and a frame is on screen a refresh less once
  per refresh of sliding: 12 in 6,000 frames, never two within 300.
- **It does not shorten a queue** (the simulation's display shows every frame, in order): on a display 0.2 % slower than
  its mode a loop on a timer had six frames waiting
  after 3,000 with either timed present, as without one. With a wait for a present, one at most.
- **After a refresh the display lost by itself**, at two refreshes per frame without a wait for a present: with the
  time, the next frame is shown at the vertical blank it was made for (one refresh after the late one), as without a
  timed present. With the duration every later frame stays a refresh later than it was made for, and nothing tells the
  pacer.
- **A duration never gives back what it took.** A timer and a wait for a present at two refreshes per frame on that same
  display: 2.18 refreshes from a frame's start to its display without a timed present, 3.66 with the duration (what the
  first frames of the run were late by stayed), 1.56 with the time. No frame was off its two refreshes in any of the
  three.
- **With a time on the present the display does what the pacer worked out, and no sooner.** Two things follow. The
  reserve of the aim of smoothness is there: without a timed present a frame that is ready early is shown early, whatever
  the pacer made it for. And where the pacer is more careful than the display, frames wait that an untimed present would
  have had shown: with two frames in flight and work of 72 % of a refresh on the CPU and on the GPU, on a display that
  takes a frame up to its vertical blank, the frames that were ready inside the pacer's frame margin were shown without
  a timed present (87 of 2,900 frames off their refresh, no frame slowed down) and were late with the time (the rule
  slowed down nearly half of the frames, and a frame was on screen a refresh later after its start). On a display that
  takes a frame 13 % of a refresh before its vertical blank, just over that margin (an eighth), the two slowed down
  alike.
- **Switched on and off while the frames go on**: the timed present is a part of the active set like any other, and a
  change of it is no handover: the frame ids, the animation steps and the intended display times go on, and it is the
  next present that gets the time or does not any more.

**Not built:** seeing that a present's time was not kept (tiers 2.1 and 2.2 could, by their wait); a swap interval on the
present.

#### A display that skips a frame that is overdue

Researched on 2026-10-09 from the platforms' own pages; **not built, not in the simulation, not run anywhere**. The
question: two frames wait, the time of the first has passed and the second is due now. A display that shows every
frame in order shows the first and is a refresh late from then on. One that skips shows the second, at the refresh it
was made for: no frame is on screen with an animation time that is not its refresh's, nothing waits a refresh longer,
and the one frame's work is lost. That is not the catching up this proposal keeps away from ("A lost refresh and game
time"): no frame is given another time, a stale one is left out. Only a time before which a frame is not shown can do
it. A time the frame before stays counts from the frame that was shown, and can not leave one out.

| Where                                                               | What its documentation says                                                                                                                                                                                                                                                                                                                                                                                   | Skips    |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| Vulkan, `VK_PRESENT_MODE_FIFO_KHR`                                  | "one request is removed from the beginning of the queue and processed during each vertical blanking period in which the queue is non-empty"                                                                                                                                                                                                                                                                   | No       |
| Vulkan, `VK_PRESENT_MODE_FIFO_LATEST_READY_KHR`                     | With a target present time from `VK_GOOGLE_display_timing` or the `presentAtAbsoluteTime` feature: "If the target present time is less-than or equal-to the current time, the presentation engine dequeues the image and checks the next one. The image of the last dequeued request is presented. The other dequeued requests are dropped."                                                                  | Yes      |
| Windows 11, the composition swapchain's presents with a target time | "If there are multiple _ready_ presents, all but the latest (that is, the present with the greatest present identifier) will be _skipped_"; a present is ready when its drawing is done and its target time is met. Its status is then `PresentStatus_Skipped`                                                                                                                                                | Yes      |
| Wayland, `commit-timing-v1`                                         | The content is "presented as closely as possible to, but not before, the specified time", and content updates are applied in the order they are received. `presentation-time` has an event for an update that "was never displayed to the user", and `fifo-v1` exists to keep an update on screen for a refresh. Whether two updates whose times have both passed are both shown is not said in what was read | Not said |
| Android, `ASurfaceTransaction_setDesiredPresentTime`                | Presented at or after the time; a later transaction with an earlier time does not go before an earlier one. `EGL_ANDROID_presentation_time` says only that the time is passed along. Nothing on leaving a buffer out                                                                                                                                                                                          | Not said |
| Metal, `present(at:)`                                               | Presented at the time when its drawing is done before it, and as soon as possible when it is done after. Nothing on another drawable that is due as well                                                                                                                                                                                                                                                      | Not said |

Sources:
[VkPresentModeKHR](https://docs.vulkan.org/refpages/latest/refpages/source/VkPresentModeKHR.html),
[Composition swapchain programming guide](https://learn.microsoft.com/en-us/windows/win32/comp_swapchain/comp-swapchain),
[PresentStatus](https://learn.microsoft.com/en-us/windows/win32/api/presentation/ne-presentation-presentstatus),
[commit-timing-v1](https://wayland.app/protocols/commit-timing-v1),
[presentation-time](https://wayland.app/protocols/presentation-time), [fifo-v1](https://wayland.app/protocols/fifo-v1),
[Native Activity (NDK reference)](https://developer.android.com/ndk/reference/group/native-activity),
[EGL_ANDROID_presentation_time](https://registry.khronos.org/EGL/extensions/ANDROID/EGL_ANDROID_presentation_time.txt),
[MTLDrawable present(at:)](<https://developer.apple.com/documentation/metal/mtldrawable/present(at:)>).

What it would take here, as an open point and nothing decided: it is something the application chooses (a present
mode, an API), so it is a capability of its own next to `PresentAtTime`; the simulation's display would have to be able
to skip; and a pacer has to take a frame that is never shown (a wait for its present runs out, its display report says
so, and the frame after it is judged against the frame before it). The first integration's Windows system has no
present that takes a time before which a frame is not shown, so it can not run it as it is.

#### The duration on one system

The first integration carried the duration out on 2026-10-08, at the commit after the one that built it: Windows, Vulkan,
one driver, a window on a 240 Hz display with a second display at 120 Hz on, variable refresh off on both, a machine
with no input for an hour and 1.5 to 8.5 % of its CPU in use by other programs (other work on the machine was not
stopped for the runs, but for this repository's builds). Its present takes a relative target time and nothing else, so the time before which a
frame is not shown was not run. Each of the four kinds without a timed present, with both aims, at one, two and four
refreshes per frame, a run without the duration and one with it right after, 1,200 frames each, three rounds: 144
runs, counted from frame 240. **Driver display times, not a measurement by the tools, and three runs a setting.**

| Refreshes per frame | Frames not on screen for their swap interval, without | With the duration | From a frame's start to its display                                                                                                         |
| ------------------- | ----------------------------------------------------- | ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| 1                   | 1 of 22,963                                           | 1 of 22,970       | The same with and without, wherever a setting was steady from round to round                                                                |
| 2                   | 6 of 22,984                                           | 0 of 22,972       | Vertical blank times alone: one refresh later in five of six runs, three later in one. On a timer: 0.6 to 15 ms later. With both: no change |
| 4                   | 59 of 22,983                                          | 147 of 22,983     | On a timer with smoothness: up to 3 ms later; with low latency it differed from round to round either way. Vertical blank times: no change  |

- **At one refresh per frame it changed nothing**, as a display that shows one frame per refresh does that by itself.
- **At two it did what the simulation said**: the frames that were off were gone, and frames were shown later for it,
  by whole refreshes where the loop knows the vertical blanks and has no wait. Within a run the time to the display did
  not climb after frame 240: what was taken was taken in the first second.
- **At four it did not.** Of the 147 frames off with the duration, 144 were in two runs of a timer with a wait for a
  present and low latency (119 and 25); the one run of that setting with frames off without the duration had 55. In
  those runs the present was called 0.10 to 0.18 of a refresh before the frame's display time, so a frame made or missed
  that refresh. And in the worse run 60 of 959 frames were shown 12.499 ms after the frame before them although
  their present was given 14.582 ms: on this system a relative target time did not keep a frame from being shown
  sooner than that after the display time the driver reports for the frame before. The specification counts such a time "from the previous presentation's
  `VK_PRESENT_STAGE_IMAGE_FIRST_PIXEL_VISIBLE_BIT_EXT` stage"
  ([VkPresentTimingInfoFlagBitsEXT](https://docs.vulkan.org/refpages/latest/refpages/source/VkPresentTimingInfoFlagBitsEXT.html)),
  and says that the implementation "attempts to align" a frame with its time and that the application "would strictly
  prefer the image to not be visible before" it
  ([VkPresentTimingInfoEXT](https://docs.vulkan.org/refpages/latest/refpages/source/VkPresentTimingInfoEXT.html)): a
  preference, and no promise. This driver reported no time for that stage in any of the run's 960 frames (it reports
  the stage before it, first pixel out, which is the display time used here), so what it counted from is not known.
- The frames that waited and the refreshes the animation time fell behind the clock did not differ with the duration.

The same session ran the changes of the active set again (84 changes, three runs an aim): the frame id, the swap
interval, the animation step and the start no earlier than the frame before said held in all of them, and all 918
intervals on screen around the changes were one refresh. From vertical blank times to the grid on the clock with
smoothness the animation time no longer fell behind (it had, by two refreshes, in three of three before the handover
stopped making the frames ahead again). Switching the wait for a present on still costs one long frame start with
smoothness, while the frames that wait come down to what may wait. The pause after start-up was made once over
thirteen changes in each of two runs.

**A start the system held, at the start of a run** (the same session; vertical blank times, no wait for a present,
smoothness, one refresh per frame; two runs, alike). Up to the 16th frame the pacer's held presents paced the loop.
Then the application's own wait for a frame slot held the loop, 6.6 ms before one frame and 20.6 ms before the next
(its acquire took no time), and from there on that wait paced the loop, 3.8 to 3.9 ms a frame, with no present held.
For the frame that started 21 ms after the one before it the pacer stepped the animation time five refreshes: it was
for the first vertical blank it could make. The display showed it one refresh after the frame before it, as the frames
before it were still on their way: a frame was on screen 35 ms after its start before those two waits and 12 ms after
it from then on. So the animation jumped four refreshes with no frame held on screen, and the refreshes the pacer
counts as behind the clock did not move. What the pacer did is what it is built to do without a wait for a present or
display times: a start that comes late is time that passed. The pacer on the grid on the clock has a rule for this
("A loop the system holds": where the application says that the system holds the loop, a wait of the display's side
that held the loop lets the frame through and no step is lost); the pacers on vertical blanks are not told of the
application's waits at all. Decision 17.

**Decided and built on 2026-10-08** (`VBlankLoopPacer`, `TierPacer::AddSystemWait`, `DisplayHeldRefreshes`; the
simulation and unit tests only). On vertical blanks, with a wait for a present or without one:

- **What counts as the display's side holding the loop** before a frame: a wait for an image the application reports
  (`AddSystemWait`), a present that waited, and a wait for a frame slot or for the GPU's work (the application's own, or
  the one the plan asks for) for as long as the GPU did not work. How long the GPU worked is not in a wait: it is taken
  to be no more than the GPU's time on a frame as it was last reported and the frame margin. Without a reported GPU time
  such a wait is the GPU's, whole: a loop the GPU limits is late and is not to be read as held by the display.
- **What the pacer does with it**: where the frame's start is too late for the vertical blank its swap interval after
  the last one, and the display's side held the loop an eighth of a refresh or more, the vertical blanks it was held
  over (no more than the hold covers) are no refreshes that were lost. The frame is for the blank its swap interval
  after the last one, counted without them: its animation step is its swap interval, its intended display time is still
  the first real vertical blank it can make, it is no late frame to the rule, and the refreshes are behind the clock
  (`RefreshesBehindClock`, and counted by themselves in `DisplayHeldRefreshes`). What the start is late by beyond the
  hold is late as any start is.
- **In the simulation** (a swap chain of three images whose display takes no frame at six vertical blanks in a row, so
  the loop is held by its wait for an image; vertical blank times, no wait for a present, both aims): not told of the
  wait, one frame's animation time stepped six refreshes while the frame followed the one before it on screen by one;
  told of it, every frame's animation time was a refresh after the one before it. The display showed the same frames at
  the same times either way. With a wait for a present no frame's animation time stepped over them told or not: the
  wait says where the frames were shown.
- **Not run on a system.** On the one that showed the case the wait was the frame slot's, so there it rests on the
  GPU's work being reported.

A third value is the tier the pacer is **working at** this frame: the tier of the parts that are really pacing. It is lower
than the active tier while something a capability promised is missing: no vertical blank time has come yet, the readings
turned out to be no vertical blank times, the waits for a present stopped because none is shown, a present's time was not
kept (built for a missing reading and for waits that stopped).

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
  waited, so no frame is repeated and every display step stays what it was. This is the work of a wait for a present, and it is worth
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

### What each tier allows

Tiers 2.1 to 2.4 are built against the simulation's display and measured on no system, but for the duration on one ("The
duration on one system"); what is said of them is what the design is to do.

| Tier                                                           | Who puts a frame on its refresh                               | Does it know where the refreshes are?                             | The frames that wait                                                               | After a refresh the display lost by itself                                                                                                           |
| -------------------------------------------------------------- | ------------------------------------------------------------- | ----------------------------------------------------------------- | ---------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2.1, timed present, a wait for a present, vertical blank times | The display's side, from a time that is a real refresh        | Yes: an animation time is the time of a real refresh              | Never more than may wait, from the first frame on                                  | It learns of it from the wait, a few frames later: that frame is late and the next is for a later refresh. Nothing more waits                        |
| 2.2, timed present, a wait for a present                       | The display's side, from a step of the grid on the clock      | No: its refreshes are a grid on the clock, a constant offset away | Never more than may wait, from the first frame on                                  | It need not learn of it: the wait is for the display itself, and it costs one frame start                                                            |
| 2.3, timed present, vertical blank times                       | The display's side, from a time that is a real refresh        | Yes                                                               | Not known. A frame that was ready in time and still shown late waits from then on  | It does not learn of it. At one refresh per frame the frame stays waiting; at two or more the display's side holds the waiting frame as well         |
| 2.4, timed present                                             | The display's side, from a step of the grid on the clock      | No                                                                | Not known                                                                          | As tier 2.3                                                                                                                                          |
| 3.1, vertical blank times, a wait for a present                | The loop: the frame is ready at a place in the refresh before | Yes: an animation time is the time of a real refresh              | Never more than may wait, from the first frame on                                  | It learns of it from the wait, a few frames later: that frame is late and the next is for a later vertical blank. Nothing more waits                 |
| 3.2, vertical blank times                                      | The loop: the frame is ready at a place in the refresh before | Yes                                                               | Not known. A frame that was ready in time and still shown late waits from then on  | It does not learn of it. At one refresh per frame the frame stays waiting; at two or more the display takes it before the next is made               |
| 3.3, a wait for a present                                      | The loop, on a timer: where in a refresh it lands is chance   | No: its refreshes are a grid on the clock, a constant offset away | Never more than may wait, from the first frame on                                  | It need not learn of it: the wait is for the display itself, and it costs one frame start                                                            |
| 3.4, the baseline                                              | The loop, on a timer: where in a refresh it lands is chance   | No                                                                | Not known. Never more frames than the display takes; frame starts kept on one grid | As tier 3.2. Where the grid is faster than the display the frames that wait grow (by two runs' numbers, one more about every five minutes at 240 Hz) |

What the two aims are at tiers 3.1 to 3.4, as built (tiers 2.1 to 2.4: "The timed present, as built", above):

| Tier | Smoothness (the default)                                                                                                                    | Low latency                                                                                                                               |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| 3.1  | The wait, then the frame starts at once and its present is held to its place in the refresh; one frame in reserve at one refresh per frame  | The wait, then the frame's start is held so that it is ready at its place and no sooner; presented when it is done                        |
| 3.2  | The frame starts at once and its present is held to its place; one frame in reserve at one refresh per frame                                | The frame's start is held, presented when done; a frame of more than one refresh is started in the refresh before its blank. One pause    |
| 3.3  | One frame made ahead of the display, and the wait keeps the frames that wait to what may wait                                               | No frame made ahead; the wait keeps the frames that wait to what may wait                                                                 |
| 3.4  | One frame made ahead; a frame late within the reserve gives up no step. Where the application says the system holds the loop, that paces it | One pause after start-up (a guess); a whole period after a late present; a frame half a period late or more takes the step the loop is at |

A frame of more than one refresh, by who holds it. The status is of the first integration's own loop, from before the tier
pacers:

| Who holds it                                                                     | How                                                               | Status                                                                                                                                  |
| -------------------------------------------------------------------------------- | ----------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| The display's side (tiers 2.1 to 2.4; below them a swap interval on the present) | For exactly its refreshes, whenever the loop presents it          | A present with a minimum duration: measured on one system, at two refreshes per frame and slower. A swap interval on the present: built |
| The loop, with vertical blank times (tiers 3.1 and 3.2)                          | Ready in the refresh before the vertical blank it is for          | Measured on one system at 50 to 240 Hz; built and not measured on three other window systems                                            |
| The loop, on a timer (tiers 3.3 and 3.4)                                         | On a timer: a guess, as it does not know where in a refresh it is | Measured on one system: next to no frame a refresh off at 50 to 120 Hz, 1 to 35 % of them at 240 Hz                                     |

Two things are the same at every tier:

- **After the pacer's own long frame** the loop is back where it was against the display: on the display's vertical blanks
  where it has their times, on the grid on the clock where it has not. The pacer needs nothing from the display for that.
- **The lost refresh itself is seen on screen once**, as a frame shown a refresh longer than it was animated for. No tier takes
  that back: the frame was already drawn when the refresh was lost. What the tiers differ in is whether it stays as latency
  afterwards.

Status: tiers 3.3 and 3.4 are measured on the first integration's first system, one run a case, and tiers 3.1 and 3.2 have their
first runs there on a quiet machine (their sections above). Tiers 2.1 to 2.4 are built against the simulation, and the
duration alone has run on that system. The second system is a virtual
machine whose display times are poor: what it showed is in "A loop the system holds" and "Readings that are no vertical
blank times". All of it is driver display times, and no run of a tier pacer has been captured and analysed with the tools.

**The wait for a free swap chain image, without a wait for a present.** It is core to a swap chain, so a configuration at
tier 3.2 or 3.4 can have it, and on the one system measured it worked as a cap and not as a way down (one run a case, GPU work
of 90 % of a refresh, a swap interval fixed at 1):

| Run                                | Frames waiting   | Frame start to display, median (99 %) | Presents without a display time | GPU work that began late |
| ---------------------------------- | ---------------- | ------------------------------------- | ------------------------------- | ------------------------ |
| No wait, three images              | 1 to 3, changing | 2.98 refreshes (4.98)                 | 7.6 %                           | 7.0 %                    |
| A wait for the image, three images | 3, steadily      | 3.91 refreshes (3.94)                 | 0.1 %                           | none                     |
| A wait for the image, two images   | 2, steadily      | 2.92 refreshes                        | not read                        | not read                 |

So it does three things there: the frames waiting can not go above the number of images, they sit at that number, and the
run is steady. It does not bring the display back towards the animation timer: it fixes how far behind it is. That distance
is then known (as many refreshes as there are images), which nothing else at these tiers gives, and with the fewest images
the swap chain allows it is no further than without the wait. Whether another driver keeps an image as long is not known,
which is why the pacer does not count on it. It makes no tier: what the pacer does where a system holds the loop is in "A
loop the system holds".

### Aligning the animation timer with the display

Catching up is about the frames waiting. A second question is how well a frame's animation time can be made the time the
frame is really shown. It has three parts, and they take different things:

| What "aligned" means                                                                                          | What it takes                                | Where                                                                   |
| ------------------------------------------------------------------------------------------------------------- | -------------------------------------------- | ----------------------------------------------------------------------- |
| The animation time is the time of a real refresh, not a point on a grid a constant part of a refresh away     | Knowing where the refreshes are              | Tiers 2.1, 2.3, 3.1 and 3.2 (vertical blank times), or display times    |
| The whole refreshes between a frame's animation time and its time on screen can not change without being seen | The frames waiting capped, or counted        | Tiers 2.1, 2.2, 3.1 and 3.3 (capped at what may wait), or display times |
| The animation time is the frame's true display time                                                           | The display saying when each frame was shown | Display times only, which pace nothing yet                              |

- **Only display times align it fully**, as only they say when a frame reached the screen. They come frames late (one to five
  in what was measured), so what they correct is the frames that follow.
- **Tiers 2.1 and 3.1** come close without them: the animation time is on a real refresh and the frames waiting can not grow. What is
  left is the constant number of refreshes the system takes by itself (a compositor's, for one), which the pacer can not
  know. A constant whole number of refreshes does not show in motion; it matters where a game needs the absolute time, for
  sound.
- **Without a wait for a present the second part can not be had** (tiers 2.3, 2.4, 3.2 and 3.4). How many refreshes behind the animation timer the screen is can not
  be known. The wait for a free image is the one thing that fixed that distance, at the image count, on the system measured.
- **A timed present gives none of the three by itself**: the display's side shows the frame at the refresh it was given
  for, and the application is not told where the refreshes are or whether the frame was shown there.

### Pacing by display times

Display times are reported and counted (the `+` beside a tier), and they pace nothing. Pacing by them is a proposed
rule, with nothing of it designed as a part or built. Whether it becomes a kind of the part that holds the loop is
decided when it is designed, with both aims.

- **What display times give**: the real time of every shown frame, one to five frames after it (as far as measured), and so
  the count of presents that are not shown yet.
- **The rule proposed**: keep each present's expected refresh, and when a frame was shown later than expected, take one
  frame start back for each frame too many. The display then catches up, those frames later.
- **Where it comes from**: seeing a miss is Microsoft's: "If the actual PresentRefreshCount is later than the expected
  PresentRefreshCount, a glitch has occurred"
  ([DXGI flip model](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-flip-model)). Answering it:
  Microsoft's page discards the queued frames; this proposal holds a frame start instead, which is the idea of Android's
  frame pacing library, to "inject waits into the application that allow the display pipeline to catch up, rather than
  allowing back pressure to build up" ([Frame Pacing library](https://developer.android.com/games/sdk/frame-pacing)). That
  library waits on the GPU's work with fences; waiting by the count of presents not yet shown is this proposal's own.
- **What was tried**: today's pacer measured its frames by their display times before they became statistics only. It
  reacted two to four frames later, and at work of 90 % of a refresh it counted about 40 % more late frames than the
  display had events (one machine, one capture session).

## How it is checked

- **Replay** (`pacer-replay`, test code): a stored frame log is given to the pacer in the order its application had the
  values, and what the pacer makes of it (where it thinks the refreshes are, how many presents it thinks are waiting, the
  misses it saw) is held against what the log shows the display did. A log shows what a pacer would have seen, never what
  another answer would have caused.
- **Simulation** (`pacer-sim --loop`, test code): a frame loop on a display model with a present that never waits, frames
  queued behind it, an optional bound, and vertical blanks at which no frame is taken. It gets a wait for a present, display
  times that come late and a timer that wakes late, and each tier is run on it: the queue that today stays for the rest of the
  run has to be gone within a number of frames stated per tier.
- **Every tier** in that simulation, held to what its tier promises. Leaving a capability out of the
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
  presented gets tiers of its own behind the same front (a major tier, or more than one), and the ones for a fixed
  refresh rate stay as they are.
- **The application says so.** Whether variable refresh is active for its window, and the range, is information it gives with
  its capabilities. Until those pacers exist, the front does not pace such a display with a pacer for a fixed one: the tier it
  is working at says that it is not pacing.
- **Times in the plan are times.** A present's time, the intended display time, the animation step and the target frame time
  are moments and durations. The swap interval is a count beside them for the mechanisms that take one, and nothing a caller
  reads assumes that a display time is a whole number of refreshes after another.
- **The timeline of refreshes belongs to the pacers for a fixed refresh rate**, not to the shared parts. What is shared has no
  refresh grid in it: the frames in flight, the work as two stretches of time, the animation time. The wait for a present
  asks nothing about the refresh rate and is expected to carry over as it is.

## Decisions needed

1. **k**, the presents that may be waiting: 1 is the lowest latency and leaves no slack (at work of 90 % of a refresh it
   halved the frame rate in the first measurement), 2 leaves a frame of slack for one refresh more. Proposed: a setting,
   default 2, as that kept the frame rate in every case measured; picking it from the work is the option below. Open
   since: on the simulation a loop with two frames in flight and heavy work needs 3 (above). It did not show on the first
   integration's system in the second measurements, where a frame reached the screen sooner than in the simulation's
   display. Should the pacer take at least one more than the frames in flight the application names, or is that the
   application's to set?
2. **The tiers**: decided on 2026-10-08: one list, each tier a combination of a timed present, a wait for a present and
   vertical blank times; display times a mark beside the tier (`+`), a wait for the GPU's work and a swap interval on
   the present mechanisms beside it; the pacers put together from parts ("Tiers", "How a pacer is put together").
   Decided on 2026-10-09: the list is three major tiers (who places the frame) of four sub tiers each, written "3.1",
   and its first major tier is a display's side that skips a frame that is overdue, which is rated and has no pacer.
   Built: the list, its rating, and a pacer for every tier of major tiers 2 and 3. Open: every order in the list,
   until the tiers are measured against each other.
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
6. **A loop the system holds when its queue is full** (a wait for a free image, measured on one system: the frames
   waiting are capped at the number of images and steady; or a present that is known to wait). It had a case since the third
   measurements: with the aim of smoothness and no wait for a present, a full swap chain paced the loop by itself, steadily
   in one run and not in another, and the pacer neither chose it nor knew of it.
   Decided on 2026-10-07 and built for the pacer on a timer with the refresh period only ("A loop the system holds",
   above): the application reports its own waits and the swap chain's images, and where it says that the system holds the
   loop while its queue is full, the aim of smoothness lets the system pace the loop. It is how smoothness behaves in that
   pacer and makes no tier, as it promises less than a wait for a present (how many frames wait is the swap chain's
   number): settled with the one list of tiers (decision 2).
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
11. **The lowest tier at start-up**: decided on 2026-10-07 and built: one pause after start-up, its length and its delay
    settings that are named as a guess, never before a present was taken.
12. **A wait for a present that is never shown**: decided on 2026-10-07 and built: the longest a wait may take is counted in
    the frame's own swap intervals, four by default. Decided on 2026-10-09 and built: and never less than a least time,
    50 ms unless it is set (`PacerSettings::MinWaitTimeout`, `WaitTimeoutAt`), for the wait for a present and the wait
    for the GPU's work alike. Four refreshes are 16.7 ms at 240 Hz, and on the one system measured the wait for the
    GPU's work ran out once at the start of every run with that. Android's frame pacing library gives its wait 50 ms
    too, and takes one that ran out as done.
13. **Names**: the capability and call names above are proposals.
14. **Where a frame has to be ready, at the start of a run** (the pacers of tiers 3.1 and 3.2, where the loop places the
    frame). On the one system measured the learning was moved by a swap chain's first frames in every run, and never by
    anything after them. Decided on 2026-10-08 and built: the place goes back ("Vertical blank times with a wait for a
    present"). The other way, to learn nothing for a time after a start, was not taken: a display that does take its
    frames early then shows every frame late for that time, and the swap interval rule may slow down before the place
    has moved. Not measured: whether the first try on that system holds, which would give back the quarter of a refresh.
15. **A wait for the GPU's work as a hold of the loop**: decided on 2026-10-08 and built: a mechanism the tiers without
    a wait for a present use where the application has it, and no tier ("Tiers"). Which frame: the one before with
    low latency, the one before that with smoothness, as it was proposed (confirmed on 2026-10-08). To be measured with
    and without it under a real GPU load, with both aims, before anything is said of what it is worth or anything
    finer is decided. One thing to watch then: on the one system the wait for a frame slot held the loop while the
    GPU's work itself was short (decision 17), so there it brakes from the display's side.

16. **Which timed present puts a set where the display's side places the frame**: decided on 2026-10-09 and built: only
    a time before which a frame is not shown (`PresentAtTime`), by the list's own rule of who places the frame. A time
    the frame before stays (`PresentAfterDuration`) is rated beside the tier as a swap interval on the present is
    ("the display's side holds"), and its duration is given at every tier. What spoke against it: the duration is the
    only timed present that has been measured on a system. There it took the frames off their refresh away at two
    refreshes per frame and not at four ("The duration on one system").

17. **A start the system held, on vertical blanks**: decided on 2026-10-08 and built ("A start the system held, at
    the start of a run"). The pacers on vertical blanks take the application's own waits, and a start that the
    display's side held is not stepped over: the frame is for the vertical blank a swap interval after the last one,
    and the refreshes are counted as behind the clock. What decided it: whether a wait for a frame slot that holds the
    loop means frames that are still on their way or a GPU that does not keep up can be told from the GPU's reported
    work, and on the one system it was the first (the GPU's work on a frame took 0.10 ms, and it did not begin the two
    frames until 10 and 21 ms after they were submitted; the frame slot came free when it had ended the frame
    before). So the rule needs no guess: a wait that held the loop while the GPU did no work on the frame it waited for
    is the display's side holding the loop, whatever the wait is called. Still one system and the start of a run, and
    not run there since.

## What changes for whom

- The pacer's public types change throughout (the SDK is at 0.1.0 with no release): `FramePacer`'s calls, `FrameSchedule`, the
  settings, present feedback. Every time is in nanoseconds (below).
- The first integration's sample loses its hold methods, profiles and due times, and keeps the waits, the present and the
  measurements.
- The frame log's pacer chunks follow the new calls, and get the capability sets and the ratings.
- Today's guide, its Status table and "Not used yet" are rewritten from this document when the code is.
