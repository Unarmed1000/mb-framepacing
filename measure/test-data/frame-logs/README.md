# Test data: frame logs for the frame stages charts (experimental)

**Nothing here was recorded.** A frame log is the CSV the frame pacer's first integration writes for a run: a row per frame with
the moments of the frame on the application's CPU clock, in ticks of 100 ns. No log of a capture session is stored in this
repository; these are made up or simulated.

| File                    | What                                                                                                                                                                                                                                         |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `sim-loop.csv`          | The pacer's own simulation: `pacer-sim --loop late --frames 200 --gpu-percent 72`                                                                                                                                                            |
| `made-up.csv`           | Arithmetic, with what a simulation does not write: the update, the acquire, a frame slot wait, the marker, vertical blanks as read, timed presents, frames reported without a display time, a frame that took too long, a slower swap interval |
| `made-up-row-early.csv` | The same run as a log that has the values of a frame's start one row early (an OpenGL ES log of the sample before its fix)                                                                                                                   |
| `expected/`             | What `tools/frame_stages_chart.py` gave for them before it was replaced by the tools' `frame-stages` command: the trace files and both views of the charts                                                                                   |

`measure/libs/MB.FramePacing.Charts` (`Stages/`) is a port of that script, and its tests hold it to `expected/`: the trace files
byte for byte, the charts shape for shape (every rectangle, line, text and outline with its class, its place to a tenth of a
pixel and its tooltip).
