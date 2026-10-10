# Test data: a capture and a Perfetto trace (experimental)

**Nothing here was recorded.** The traces are made up in every value by `tools/tests/synthetic_trace.py` to fit the SDK's
golden capture (`sdk/test-data/data/60-busy-full-rate`): a process named `com.example.game`, no device. A recorded trace names
a device and its programs and is never stored in this repository.

| File                       | What                                                                                                         |
| -------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `synthetic.pftrace`        | The made-up trace: clock snapshots, a process, its scheduling, the frame record as sections on its thread, vertical blank events, a frame timeline |
| `synthetic-pieces.pftrace` | The same with the marks a recording has that was written to its file in pieces                               |
| `synthetic-app.pftrace`    | A made-up trace of the application's own: [the frame record](../../../sdk/doc/trace-frame-record.md) as the SDK defines it, and nothing of a system |
| `frames.csv`               | What `tools/trace_augment.py --frames` says the trace says of each frame of the golden capture               |
| `digest.json`              | The length and SHA-256 of the traces the script writes for them, and the numbers the tracks were placed by   |

They keep the two ways of joining a capture with a trace equal: the script (`tools/trace_augment.py`) and the tools
(`measure/libs/MB.FramePacing.TraceJoin`), whose tests must give these bytes. `tools/update_trace_test_data.py` writes the folder
(`uv run --group trace tools/update_trace_test_data.py`), and the scripts' tests fail when it is not what the script makes
now. See [the guide](../../doc/perfetto.md).
