# A capture and a Perfetto trace of the same run (experimental)

> **Experimental.** Built against traces its own tests make, by our reading of Perfetto's formats. **No recorded trace has been
> through it yet**: how it matches the system's frames to the capture's, and where it finds a display's vertical blanks, is
> unproven until one has. Its options and its output may change.

A capture card records what the display was sent, and the tools measure when each frame was really on screen. A
[Perfetto](https://perfetto.dev) trace recorded on the device at the same time has what the system did meanwhile: its threads,
its compositor, its own account of every frame. `tools/trace_augment.py` joins the two, both ways:

- **The capture into the trace:** a copy of the trace with the capture's measurements added as tracks, to open in
  [the trace viewer](https://ui.perfetto.dev) next to everything the system recorded.
- **The trace into the capture's frames:** a CSV with a line per presented frame and what the trace says of that frame.

Neither input is changed and nothing is uploaded: the viewer reads a trace in the browser, on your machine.

**A trace names the device and its programs.** An augmented trace holds everything the recorded one did. Keep both as private
as the recording: this repository holds none, and its tests make their own.

## What it needs

- A capture folder that was analysed (`import` or `analyze`: it has `analysis/summary.json`).
- A Perfetto trace of the same run, in Perfetto's protobuf format (`.pftrace`, `.perfetto-trace`; a whole-file gzip is unpacked).
- **Markers with the CPU start time**, and best with the pacer's intended display time
  ([the marker's fields](../../sdk/doc/marker-fields.md)): they are what ties the capture to the trace, [below](#the-clocks).
- [uv](https://docs.astral.sh/uv/) and this repository. The tool uses Perfetto's own Python package, which `uv run --group trace`
  installs for it (the group `trace` of `pyproject.toml`).
- **Perfetto's trace processor**, a native program that reads the trace. The package downloads it on first use (a pinned version,
  checked against its SHA-256, kept under your home folder) and talks to it on this machine only; that download needs `curl`.
  `--trace-processor <path>` uses one you already have.

## The commands

```
uv run --group trace tools/trace_augment.py --inspect run.pftrace
uv run --group trace tools/trace_augment.py <capture folder> run.pftrace -o run-augmented.pftrace
uv run --group trace tools/trace_augment.py <capture folder> run.pftrace --frames run-frames.csv --process "com.example.game*"
uv run --group trace tools/trace_augment.py <capture folder> -o run-capture.pftrace
```

1. `--inspect` lists what a trace has to go by: its clocks, its vertical blank sources, its processes, their layers in the
   frame timeline and (with `--process`) their threads. Start here to pick the options.
2. `-o` writes the trace with the capture's tracks added, then reads it back and says whether every track has what was written.
3. `--frames` writes the per-frame CSV and prints what it found. `-o` and `--frames` can be given together.
4. Without a trace, `-o` writes the capture alone as a trace: the same tracks, on the capture's own clock.

| Option                       | What                                                                                                                              |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `--run <id>`                 | The run, for a capture with more than one                                                                                         |
| `--from`, `--to <seconds>`   | A stretch of the run, counted from its first frame                                                                                |
| `--app-clock <clock>`        | The system clock the application's steady clock is: `monotonic` (the default), `boottime`, `monotonic-raw`, `realtime`            |
| `--app-clock-offset-ns <ns>` | What to add to a marker's time to get that clock's, for an application whose clock counts from an epoch of its own                |
| `--capture-offset-ns <ns>`   | The capture's clock minus the application's, when you know it: nothing is worked out from the frames then                         |
| `--vblanks <name>`           | Which of the trace's vertical blank sources to use, or `none`. Unset: the kernel's vertical blank events, when the trace has them |
| `--process <pattern>`        | The application's process name in the trace (`*` and `?` as in a file name): needed for the system's account and the CPU's time   |
| `--layer <pattern>`          | The application's layer in the frame timeline, when it has several                                                                |
| `--thread <name>`            | The thread whose CPU time is counted: the process's main thread unless named                                                      |
| `--trace-processor <path>`   | A `trace_processor_shell` that is on the disk already                                                                             |
| `--no-check`                 | Do not read the written trace back                                                                                                |

## The clocks

Three clocks meet, and nothing ties the capture's to the trace's but the frames.

| Step                                             | From                                                                                                                                                                                                                                                              | How sure                                                                                                                                |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| The application's clock to the trace's           | The markers carry the CPU start time and the intended display time on the application's steady clock. That clock is taken to be the system's `CLOCK_MONOTONIC` (what `std::chrono::steady_clock` is on Linux and Android), which the trace's own snapshots tie in | Exact when the clock is the one named. Checked: the tool stops when fewer than half of the CPU start times lie inside the trace         |
| The capture's clock to the application's         | The analysis's own alignment, read back from the frames it wrote. Without intended display times: no frame is on screen before its CPU work ended                                                                                                                 | The differences between frames are exact. Where the whole run sits rests on its on-time frames                                          |
| A finer position, on the trace's vertical blanks | The measured display times are whole refreshes apart, so all of them are moved by the one part of a refresh that puts them on the trace's vertical blanks                                                                                                         | Printed with how many frames then have a vertical blank within half a refresh and how far the furthest is. Which refresh is not from it |

**What this can not tell.** A delay that the whole run shares can not be told from the difference between the two clocks: when
every frame of a run is a refresh late, the tracks are a refresh early against the trace. Differences between frames (a frame
that stayed a refresh longer, a late frame) are exact. The marker's fields have the same limit and say so.

A capture whose markers carry no times at all can only be placed with `--capture-offset-ns`.

## The tracks

Under one track named `mb-framepacing: <run>`, in this order. A slice's arguments have its values.

| Track                          | What                                                                                                      |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| On screen (measured)           | A slice per frame from its display time for its time on screen, named `#<frame index>`                    |
| Intended display (the pacer's) | A slice per frame from its intended display time to the next frame's                                      |
| CPU (from the markers)         | A slice from a frame's CPU start time for its CPU busy time, with an arrow to the frame's slice on screen |
| Animation error (ms)           | A value per frame: animation time step minus display time step                                            |
| Display time step (ms)         | A value per frame: how long the frame before it stayed                                                    |
| Lateness (ms)                  | A value per frame: how long after its intended display time it was shown                                  |
| Frame events                   | Late frames, frame indices that were skipped, frames out of order, torn frames                            |
| Capture events                 | The capture's own gaps: captures not decoded, not recorded, dropped by the source or missed               |

The terms are [the vocabulary's](../../sdk/doc/vocabulary.md), and a name says what the capture's data says and no more: a frame
index that was skipped is not called dropped.

The same capture and the same trace give the same bytes. What is added has packet sequences, track ids and arrow numbers of its
own, made from the run and the track's name.

**The tracks are written before the trace's own packets, with their times on the trace's own clock.** A reader takes a long
recording, one that was written to its file in pieces, piece by piece, and leaves out what comes after the last piece with a
time from an earlier one: added to the end of a test trace with two such marks, a third of the tracks' events were gone. Before
the first piece nothing is too late.

## The frames CSV

A header line, then a line per presented frame in display order. Every time is whole nanoseconds on the trace's clock, and a
cell is empty where the trace does not say. Each group of columns is there when the trace has what it needs.

| Columns                                                                                                                                                                              | What                                                                                                                                                                                | Needs in the trace                        |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| `frameIndex`, `displayTraceNs`, `cpuStartTraceNs`, `cpuEndTraceNs`                                                                                                                   | The frame: its display time as measured, and its marker's CPU start time and the end of its CPU busy time                                                                           | Nothing                                   |
| `vblankTraceNs`, `displayMinusVBlankNs`                                                                                                                                              | The vertical blank nearest the display time (within half a refresh), and how far the display time is from it                                                                        | A vertical blank source                   |
| `surfaceFrameToken`, `displayFrameToken`, `layerName`, `presentType`, `jankType`, `jankTag`, `onTimeFinish`, `gpuComposition`, `systemPresentTraceNs`, `displayMinusSystemPresentNs` | The system's own account: the application's frame in the frame timeline, what the system says of it, when it says the frame was presented, and the measured display time minus that | Android's frame timeline, and `--process` |
| `cpuRunningNs`, `cpuRunnableNs`, `cpuSleepingNs`                                                                                                                                     | Of the frame's CPU busy time, on one thread: running, waiting for a CPU, and neither                                                                                                | Scheduling events, and `--process`        |

**Which frame of the trace is a frame of the capture is worked out from times alone**: the application's frame in the frame
timeline whose time the end of the capture frame's CPU work lies in. A trace and a capture share no id. This is the part a
recorded trace has to prove.

`displayMinusSystemPresentNs` is the reason for the file: **where the system's own account of a frame and the measured one
differ**. It carries the limit of [the clocks](#the-clocks): its changes from frame to frame are exact, a constant part of it is
not.

## Limits

- No recorded trace has been through it.
- The vertical blank sources it finds are the kernel's vertical blank events and counters with `VSYNC` in their name. Which
  counter is the display's own is not something it knows: `--inspect` lists them and `--vblanks` picks one.
- That every track is read back whole is checked with the trace processor, on traces the tests make (one of them with the marks
  of a recording written in pieces). The viewer uses the same reader, but nothing here has looked at a trace in the viewer.
- Frames are matched by time, not by an id ([above](#the-frames-csv)).
- The capture's tracks are added for one run of one capture at a time.
