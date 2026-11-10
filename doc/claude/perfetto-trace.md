# Perfetto trace: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **A capture and a Perfetto trace** (`measure/doc/perfetto.md`; the user, 2026-10-09: load a trace with a capture for extra
  information, add the capture's measurements to the trace, select the trace in the GUI and the console app "so we can use it for
  what we present", and save the augmented trace "as part of the report").
  - **Graduated from experimental on 2026-10-14** (the user's word, with the SDK's trace module: the frame record is what the join
    stands on), after a recorded trace had been through it: no `--experimental`, no GUI switch, in the README's workflow and in
    `usage.md`, the trace module on by default in C++ and Conan. Never bring the "no recorded trace has been through it" notices
    back. A trace names a device and its programs: **never store a recorded trace in the repository** or quote from one without
    the user's word. **Nothing of a trace that names a program or a device goes onto a card** (the hover and the
    description have values and the system's own verdict words; the layer name is in the CSV only).
  - **Two implementations, held to the same bytes:** the script (`tools/trace_augment.py`, Perfetto's Python package) and the
    tools (`measure/libs/MB.FramePacing.TraceJoin`, a port part for part). `measure/test-data/trace` is what the script makes from the
    golden capture and a made-up trace (`tools/update_trace_test_data.py`; a test of the scripts fails when the folder is stale),
    and the C# tests compare with it: the written traces by length and SHA-256, the per-frame CSV byte for byte. Change the two
    together, then regenerate the folder.
  - **The join is by the frame record, and a trace without it is refused** (the user, 2026-10-10: the record is "what needs to
    be logged to perfetto for this merge ... to work", "fully standarized preferably with the necessary strings defined by our sdk",
    in a trace module of the SDK; a trace without the record: refuse). `TraceJoiner` places a run by `FrameRecordCheck.Of`: the
    median of slice start minus the marker's CPU start time is what a marker's time needs added (`ClockOffsetNs`; 0 for an
    application whose marker times are read from `CLOCK_MONOTONIC`). The application is the process the record's track belongs to
    (`TraceLoader.ProcessOf`), unless named. There are **no application clock options any more** (`--trace-app-clock`,
    `--trace-app-clock-offset-ns` and their fields are gone): never bring back a join that assumes which clock an application's is.
    What stays by time: which of the system's frame timeline frames is which, and the capture's clock against the application's.
  - **The tools:** `--trace` on `import` (stored in capture.json's `trace`, `TraceJoinOptions`) and `analyze` (for that analysis;
    `--no-trace`), the trace's own options (`--trace-process`, …), `trace-info`, `config --set-trace-processor`; the GUI's
    Perfetto trace card (Capture), row (Analyze) and trace processor (Settings), always shown. `capture`
    takes no trace: while a live capture runs its trace is still being recorded.
    - **`--trace-app <file>`** (`TraceJoinOptions.AppTracePath`; the console only, the GUI keeps what a capture names): a trace
      the application wrote itself, for a frame record that is in a file of its own. With `--trace` the run is placed in the
      joined trace by it (`TraceLoader.Load` reads both; the monotonic clock's snapshots tie the two files). Alone (`Path` empty)
      nothing is joined and no trace notice is given: the analysis has a `FrameRecordCheck` per run
      (`AnalysisReport.TraceRecordChecks`, `TraceCheckLines`) and writes the check file. An application's own trace given as
      `--trace` is joined like any trace.
    - `CaptureAnalyzer` joins each run (`TraceLoader` reads the trace once and keeps it for the next analysis of the same file;
      `TraceJoiner`) and writes `run-<id>-trace.csv`, the tools' own file and **not part of the analysis output format**; an
      analysis that joins none removes it. What does not fit is a warning, but for a trace the analysis itself was given.
    - `--charts` and Save charts also write `run-<id>-augmented.pftrace` (`AugmentedTraceFiles`) and read it back: a trace is
      copied whole, so not by every analysis (the user's choice: with the charts). It is written before the cards: a report card
      written next to it names it with a link (`ReportOptions.AugmentedTraceFile`, set by `ReportFiles` when the file is there;
      the user, 2026-10-14: "the saved report being able to link to the saved augmented file").
    - **Opening it in ui.perfetto.dev** (`trace-open`, the GUI's Open in Perfetto; the user's choice to keep them beside the
      link): `PerfettoTraceServer` serves the file once on 127.0.0.1:9001 (the only local origin the viewer's content security
      policy allows; Perfetto's own `tools/open_trace_in_ui` does the same) with `Access-Control-Allow-Origin:
https://ui.perfetto.dev`, answers a private network preflight, stops after one send or two minutes; `PerfettoUi.Link` adds
      `visStart`/`visEnd` (`TraceReader.AddedRange`) and startup commands that expand our group and pin the refreshes, frames on
      screen, CPU and the application's `mbfp frames`. Checked headless (Edge 155) with the local network access check off: a
      browser asks the user once; headless can not, so never test it with the check on. A page opened from disk can not hand
      the viewer a trace (Perfetto's docs): no report links the viewer directly.
    - Presented (the user's choice: hover and description first, no panel): `ChartRun.Trace` (a row per frame, live and from the
      CSV: the card from the files stays the live one), `CardHover`'s `trace, …` lines, two description lines on the report card.
      A run without a trace gives byte for byte the card and hover it gave before.
  - **Do they line up** (the user, 2026-10-09: "are we able to validate that the QR codes and perfetto information line up?"):
    every join works out three numbers that do not depend on each other (`TraceLineUpCheck` → `TraceLineUp`, printed by `analyze`
    and written as `run-<id>-trace-check.txt`): the thread awake for the markers' CPU busy time (and matched a frame off), the
    measured display time minus the system's present time being one value, and the markers as decoded against the frame record
    (`FrameRecordCheck`: slice start and length against the CPU start and busy times, each annotation against the marker's value).
    **Numbers only, no pass or fail** until recorded traces have shown what is usual, and none of them shows a delay the whole
    run shares.
  - **The frame record is the SDK's** (`sdk/doc/trace-frame-record.md`, agreed with the first integration and then, on the
    user's word, fixed in every string): a slice per frame whose main marker is drawn with a CPU start time greater than 0, named
    `mbfp frame <run id> <frame index>`, from the marker's CPU start time for its CPU busy time as the payload carries them, on
    `CLOCK_MONOTONIC`, in the category `mbfp`, on a track named `mbfp frames` under the application's process (or the thread's
    own track for a writer that can only begin and end a section there); annotations in the order of the marker's fields,
    `animationNs` always and `markerPreferredNs`, `markerTargetNs`, `markerRefreshPeriodNs`, `intendedDisplayNs` unless 0.
    - **The SDK says what to log and the application logs it** (the user: the app logs it "so it gets correctly sandwiched
      with the perfetto trace of the app"; shown the sketch of the Perfetto calls: "this is fine"). No trace writer and no
      tracing API in the SDK; an optional header that makes one library's calls is a roadmap option, not built.
    - C++ `FrameRecordText` (the strings), `FrameRecordName` (inline, no allocation), `FrameRecord`, `FrameRecordAnnotation`,
      `FrameRecordUtil::TryCreate(payload, record)`; C# the same names with `FrameRecord.TryCreate` and, for readers,
      `FrameRecordName.TryParse`. Both are held to `sdk/test-data/trace/frame-records.csv`.
    - **Overlapping frames: `TrackLanes`** (the user, 2026-10-23, alpha.5): a track's slices must follow each other or nest, and a
      trace pairs an end with the slice opened last, so two records that overlap on one track end each other's, silently (found
      testing the guide against Perfetto v52: wrong lengths, no error counted). A pipelined engine's frames overlap; a sequential
      loop's never do. `TrackLanes` (C++ and C#, values in, no allocation) gives each slice the first lane free at its start (16 at
      most, `Overflows()` beyond), `TryCreate(payload, lanes, record)` sets `FrameRecord::Lane`, the track's id among the tracks
      named `mbfp frames` (lane 0 for a sequential loop: one track; the viewer shows same-named tracks as one). Held to
      `sdk/test-data/trace/track-lanes.csv` (made from a Python model of the rule). One `TrackLanes` per track name: part 2's GPU
      slices take lanes too. The tools read the record from every track of that name.
    - **The how-to guides** (the user, 2026-10-23, for alpha.5): `sdk/doc/marker-logging.md` (a frame loop that fills every
      marker field, where each value comes from per platform, how to check it) and `sdk/doc/trace-logging.md` (the record and
      part 2 with Perfetto's Tracing SDK v49+, a recording config, the check). C++ only, full Perfetto code (the user's choice);
      the code was compiled and run against Perfetto v52.0 and read back with `trace-info`: keep it compiling when either changes. On by default:
      `MB_FRAMEPACING_BUILD_TRACE` (needs the marker module; OFF leaves it out), Conan's `with_trace` (True), the consumer
      check (`subdirectory-trace`, `package-trace` against the default install, which must hold it). Not in Python (the script has the two strings it reads, held to the golden data by a test) and not in the Unity package.
    - **Part 2, optional and not checked against the capture** (the user's request of 2026-10-14, agreed design): what happened
      around each frame, in the same document and module. Per frame `mbfp wait|present|gpu <run> <index>` slices and an
      `mbfp display <run> <index>` moment on tracks `mbfp waits|presents|gpu|display`; on `mbfp run` the moments
      `mbfp not presented <run> <index>`, `mbfp environment`, `mbfp swap chain recreated`, `mbfp swap interval change` (with a
      `runId` annotation). Values in no API's words (present modes `queue`, `queue, late at once`, `newest at refresh`,
      `immediate`, `unknown`), but `platformFeatures`, the plain list of the extensions and protocols found. Part 2 may be logged for
      every frame, a marker drawn or not (the user, 2026-10-15), by the ids the marker would carry; part 1 only where it is drawn. Strings in
      `FrameRecordText` (C++ and C#, one table), names by `FrameDetail` / `FrameDetailName` (C# `TryParse`), held to
      `sdk/test-data/trace/frame-details.csv`. `sdk/test-data/trace/example-app.pftrace` is the made-up example with all of it
      (`synthetic_trace.build_example`, written and checked by `update_trace_test_data.py`). The tools only count it
      (`TraceInfo.FrameRecordDetails`, `trace-info`); `tools/trace_diagnose.py` uses it (phase B, below).
    - The tools read it with the SDK's strings (`MB.FramePacing.TraceJoin` references `MB.FramePacing.Trace`; the tools' library
      was renamed to give the SDK the name): `FrameRecordSlice` is a record read from a trace, `TraceReader.FrameRecords` reads
      it from any track with the process of the track.
    - The made-up traces have both forms: `synthetic.pftrace` logs it as sections on the application's thread (every ninth
      1500 ns late, one frame in fifty none), `synthetic-app.pftrace` (`synthetic_trace.build_app`) as the SDK defines it, with
      annotations. The made-up application's clock is 100 s from the record's clock on purpose: nothing tells the join so.
  - **What went wrong: phase B** (the user, 2026-10-14: "a script that can analyze the augmented report and try to figure out
    what went wrong", "using the CPU + capture side to correlate things"; his choices of 2026-10-15: a script first, a port
    into the tools only once recorded traces show it is useful; it reads the analysis folder; program names in the local text
    file only; no diagnosis track in the augmented trace yet). `tools/trace_diagnose.py` (reads: the run's frames,
    `run-<id>-trace.csv`, the augmented trace through `trace_source.py`'s `frame_details`, `run_moments`, `waited_for_cpu`)
    and `tools/trace_diagnosis.py` (the facts, standard library: `FACT_ORDER`, a frame grouped by its first, every fact held
    against the frames on time, repeats, bursts, the run's moments) write `run-<id>-diagnosis.txt` and `.csv`. **Facts only,
    never a guessed cause** ("nothing in the trace explains it"); part 2 is the application's word; a time against a frame's
    due refresh carries the clocks' tie by the frames. Tests: `test_trace_diagnosis` (made-up joins and part 2 on known frames
    of the golden capture), `test_trace_diagnose` (end to end with the SDK's example trace). The tools do not run it.
  - **Perfetto's trace processor is the user's own**, found as ffmpeg is (`TraceProcessorLocator`: `--trace-processor`,
    `MB_TRACE_PROCESSOR`, `traceProcessorPath`, PATH, the folder Perfetto's Python package downloads into) and never bundled or
    downloaded by the tools. `TraceProcessor` starts it on the loopback address with the trace on its command line and asks SQL
    over its HTTP interface. No protobuf library: `ProtoWriter` / `ProtoReader` write and read the few messages field by field.
    The tests that need it (category `trace-processor`) are skipped when there is none, and CI sets `MB_REQUIRE_TRACE_PROCESSOR`
    so that there they fail instead (`tools/get_trace_processor.py` fetches the package's pinned copy for the .NET jobs).
  - **The trace's times are plain whole nanoseconds (`long`, `…Ns`)** in the TraceJoin library, not the capture's typed times: a
    trace's clock is added and compared as a number, as the script does, never as a wrapping counter.
  - The script's parts: `trace_alignment.py` (the clocks, integer arithmetic), `trace_events.py` (a capture's frames as tracks) and
    `trace_frames.py` (the per-frame join) need nothing but the standard library and are tested alone; `trace_tracks.py` (writes)
    and `trace_source.py` (reads) are the only files that import Perfetto's package (`# pyright: basic`, as `shader_render.py`).
    The package is the dependency group `trace` of `pyproject.toml` (`uv run --group trace ...`; pinned in `uv.lock`); it
    downloads the trace processor on first use into the user's home folder.
  - **The tracks go before the trace's own packets, on the trace's own clock with no clock named** (`trace_tracks.py`,
    `TraceTrackWriter`): a reader takes a long recording piece by piece and drops what comes after with an earlier time
    (`sorter_push_event_out_of_order`; a third of the events of a test trace with two such marks, when they were appended). Keep
    the tests of it (`test_a_trace_written_in_pieces`, `TheAugmentedTrace_IsTheScripts_AndReadsBackWhole`). The frame timeline's
    messages are not in the Python package's protos: the tests write them field by field (`tools/tests/synthetic_trace.py`).
  - **A frame's slices the tools add have the frame record's name** (`mbfp frame <run id> <frame index>`; the user, 2026-10-14):
    the viewer highlights every slice of a hovered or clicked name, so the application's slice and ours go together. No arrows
    from the application's slice (tried: one way works only for a record on a thread, the other edits the recorded packets).
    So **the readers of the record leave out every slice under a track named `mb-framepacing: …`** (`trace_source.frame_records`,
    `TraceReader.FrameRecords`, by the track's `parent_id`); keep the tests of it. The first track, **Refreshes (captured)**, is a
    slice per capture of a capture card, a refresh long, `refresh a` / `refresh b` by the capture's index so the viewer colours
    them in turn, `refresh, not decoded`, gaps where nothing was captured; none where the capture period is not the refresh
    period (a camera). Instants keep `#<frame index>` in their names. **A frame on screen carries its verdict as `correlation_id_str`**
    (field 53; `trace_events.verdict`, `TraceEvents.VerdictOf`; the user's choice, 2026-10-14): late, delta time jitter, off,
    static, unknown, on time. The viewer picks the colours by hash (no colour field in the protos, Chrome's `cname` ignored): never
    choose strings for the colour they hash to.
  - The clocks are tied by the frames only (`measure/doc/perfetto.md` "The clocks"): a delay the whole run shares can not be told
    from the clocks' difference. Never describe a track's position, or what the trace says of a frame, as measured beyond that.
  - `usage.md` has a short section on it (5.) and the README one ("A Perfetto trace of the run"); `perfetto.md` has the whole of
    it. DocImages has two pictures of it (`gui-trace-capture.png`, `gui-trace-analysis.png`) and checks the whole way through the
    GUI when a trace processor is found.
