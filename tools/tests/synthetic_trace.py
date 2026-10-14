# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""A Perfetto trace made to fit the SDK's golden capture, for the tests: what a device's trace of that run could hold, built
with Perfetto's own writer and made up in every value. No recorded trace is ever in the repository.

The run's markers carry times on a pacer clock that starts near zero. The made-up device has them on its CLOCK_MONOTONIC
APP_CLOCK_OFFSET_NS later, and its CLOCK_BOOTTIME (the trace's own clock) BOOT_MINUS_MONOTONIC_NS after that. For every frame
that has a CPU start time the trace has:

- the application's main thread running from the CPU start time for the CPU busy time, and every tenth frame put aside for
  PREEMPTED_NS in the middle of it;
- the frame in the system's frame timeline: the application's frame from a little before its CPU start to a little after
  its CPU work ended, and the display frame it went into, which the system says was presented at the frame's intended display
  time (a late frame is 'Late Present' with the application to blame);
- the application's own trace section around the frame's CPU work, named 'mbfp frame <run id> <frame index>': what an
  application writes so that a frame of the trace is known by its marker's frame index. One frame in fifty has none, and every
  ninth begins SECTION_LATE_NS after the marker's CPU start time;
- and for the whole run the kernel's vertical blank events, VBLANK_BEFORE_INTENDED_NS before each refresh the pacer aimed at.

build_app() is a second made-up trace, of the application's own: what an application that writes a Perfetto trace itself
puts into it, and nothing of a system. It has the frame record as the SDK defines it (sdk/doc/trace-frame-record.md): a
slice per frame named 'mbfp frame <run id> <frame index>', in the category 'mbfp' on a track named 'mbfp frames' under the
application's process, from the marker's CPU start time for the marker's CPU busy time, stamped on CLOCK_MONOTONIC, with the
marker's other times as annotations. Every value is the marker's own, but for what a test needs to see counted: one frame in fifty has no slice,
only every other slice has the two frame times, a few have an animation time that is a nanosecond off, and one slice is
another run's.

The frame timeline's messages are not among the ones Perfetto's Python package has (they are Android's, an extension of the
packet): they are written here field by field, by the numbers of Perfetto's frameworks_native_trace_packet.proto."""

from pathlib import Path

from perfetto.protos.perfetto.trace.perfetto_trace_pb2 import TracePacket, TrackEvent
from perfetto.trace_builder.proto_builder import TraceProtoBuilder

from .golden_capture import frames

APP_CLOCK_OFFSET_NS = 100_000_000_000
BOOT_MINUS_MONOTONIC_NS = 5_000_000_000
VBLANK_BEFORE_INTENDED_NS = 3_000_000
PREEMPTED_NS = 200_000
SURFACE_BEFORE_CPU_NS = 100_000
SURFACE_AFTER_CPU_NS = 500_000
SECTION_LATE_NS = 1_500
# The golden capture's run
RUN_ID = 1

APP_PID = 4242
APP_NAME = "com.example.game"
APP_LAYER = "com.example.game/Main#0"
OTHER_PID = 999
COMPOSITOR_PID = 300
CLOCK_MONOTONIC = 3
CLOCK_BOOTTIME = 6
# The kernel's task states in a sched_switch: runnable (put aside while it could run) and sleeping
TASK_RUNNING = 0
TASK_INTERRUPTIBLE = 1
SEQUENCE = 7
# The application's own trace: its sequence, its tracks, and what is off in it on purpose
APP_SEQUENCE = 21
APP_PROCESS_TRACK = 0x6D62_6670_0000_0001
APP_RECORD_TRACK = 0x6D62_6670_0000_0002
RECORD_CATEGORY = "mbfp"
RECORD_TRACK_NAME = "mbfp frames"
RECORD_ANIMATION_OFF_NS = 1
OTHER_RUN_ID = 2

# The frame timeline, as Perfetto's frameworks_native_trace_packet.proto numbers it: the packet's field, the kinds of event in
# it, and the values of what the system says of a frame
PACKET_FRAME_TIMELINE_EVENT = 76
ACTUAL_DISPLAY_FRAME_START = 2
ACTUAL_SURFACE_FRAME_START = 4
FRAME_END = 5
PRESENT_ON_TIME = 1
PRESENT_LATE = 2
JANK_NONE = 1
JANK_APP_DEADLINE_MISSED = 64
PREDICTION_VALID = 1


def trace_ns(marker_ns: int) -> int:
    """A time of the markers on the made-up device's trace clock."""
    return marker_ns + APP_CLOCK_OFFSET_NS + BOOT_MINUS_MONOTONIC_NS


def refresh_ns(index: int) -> int:
    """The pacer clock's time of a refresh of the clip: a sixtieth of a second apart, to the nanosecond."""
    return ((index * 1_000_000_000) + 30) // 60


def presented_ns(intended_display_ns: int, cpu_start_ns: int, cpu_busy_ns: int) -> int:
    """When the made-up system says a frame was presented, on the trace's clock: at its intended display time, and never
    sooner than a millisecond after its CPU work ended."""
    return max(trace_ns(intended_display_ns), trace_ns(cpu_start_ns) + cpu_busy_ns + 1_000_001)


def is_preempted(frame_index: int) -> bool:
    return (frame_index % 10) == 0


def has_section(frame_index: int) -> bool:
    """The made-up application names its frames in the trace, all but one in fifty."""
    return (frame_index % 50) != 7


def section_late_ns(frame_index: int) -> int:
    """How long after a frame's CPU start time its section begins: nothing, and 1500 ns for every ninth frame."""
    return SECTION_LATE_NS if (frame_index % 9) == 0 else 0


def section_name(frame_index: int) -> str:
    """A frame's trace section: 'mbfp frame <run id> <frame index>'."""
    return f"mbfp frame {RUN_ID} {frame_index}"


def has_record(frame_index: int) -> bool:
    """The made-up application's own trace has a slice for its frames, all but one in fifty."""
    return (frame_index % 50) != 21


def has_frame_times(frame_index: int) -> bool:
    """Every other slice of it also has the marker's target and preferred frame time."""
    return (frame_index % 2) == 0


def record_animation_off_ns(frame_index: int) -> int:
    """How far a slice's animation time is from the marker's: nothing, and a nanosecond for one frame in 97."""
    return RECORD_ANIMATION_OFF_NS if (frame_index % 97) == 5 else 0


def _switch(bundle, time_ns: int, previous: int, previous_state: int, following: int) -> None:
    event = bundle.event.add()
    event.timestamp = time_ns
    event.pid = previous
    switch = event.sched_switch
    switch.prev_comm = "task"
    switch.prev_pid = previous
    switch.prev_prio = 120
    switch.prev_state = previous_state
    switch.next_comm = "task"
    switch.next_pid = following
    switch.next_prio = 120


def _varint(value: int) -> bytes:
    """A number as protobuf writes it: seven bits a byte, the low ones first; a negative one as its 64 bits."""
    value &= (1 << 64) - 1
    out = bytearray()
    while value >= 0x80:
        out.append((value & 0x7F) | 0x80)
        value >>= 7
    out.append(value)
    return bytes(out)


def _number(field: int, value: int) -> bytes:
    return _varint(field << 3) + _varint(value)


def _bytes(field: int, value: bytes) -> bytes:
    return _varint((field << 3) | 2) + _varint(len(value)) + value


def _timeline_packet(time_ns: int, kind: int, event: bytes) -> bytes:
    """One packet with a frame timeline event, as the bytes of a trace that holds it."""
    packet = TracePacket()
    packet.timestamp = time_ns
    packet.trusted_packet_sequence_id = SEQUENCE
    return _bytes(1, packet.SerializeToString() + _bytes(PACKET_FRAME_TIMELINE_EVENT, _bytes(kind, event)))


def _service_packet(time_ns: int, flushed: bool) -> bytes:
    packet = TracePacket()
    packet.timestamp = time_ns
    packet.trusted_packet_sequence_id = 1
    if flushed:
        packet.service_event.all_data_sources_flushed = True
    else:
        packet.service_event.read_tracing_buffers_completed = True
    return _bytes(1, packet.SerializeToString())


def build(with_service_events: bool = False) -> bytes:
    """The trace's bytes. with_service_events: with the marks a recording that is written to its file in pieces has between
    them (all data sources flushed, the buffers read), twice in the run."""
    builder = TraceProtoBuilder()
    golden = frames()
    worked = [f for f in golden if f.cpu_start_ns is not None and f.cpu_busy_ns is not None]
    start = trace_ns(min(f.cpu_start_ns or 0 for f in worked)) - 1_000_000_000
    end = trace_ns(max((f.intended_display_ns or 0) for f in golden)) + 1_000_000_000

    # The clocks, at the trace's start: the monotonic clock is behind the trace's own
    packet = builder.add_packet()
    packet.timestamp = start
    packet.trusted_packet_sequence_id = SEQUENCE
    for clock_id, value in ((CLOCK_BOOTTIME, start), (CLOCK_MONOTONIC, start - BOOT_MINUS_MONOTONIC_NS)):
        clock = packet.clock_snapshot.clocks.add()
        clock.clock_id = clock_id
        clock.timestamp = value

    # The processes and the application's thread
    packet = builder.add_packet()
    packet.timestamp = start
    packet.trusted_packet_sequence_id = SEQUENCE
    for pid, name in ((APP_PID, APP_NAME), (COMPOSITOR_PID, "/system/bin/compositor"), (OTHER_PID, "other")):
        process = packet.process_tree.processes.add()
        process.pid = pid
        process.ppid = 1
        process.cmdline.append(name)
        thread = packet.process_tree.threads.add()
        thread.tid = pid
        thread.tgid = pid
        thread.name = "main"

    # The scheduler's and the display controller's events, in time order on one CPU; and the application's own trace section
    # around each frame's CPU work, named for the frame (a text: "B|<pid>|<name>" begins one, "E|<pid>" ends it)
    events: list[tuple[int, int, tuple[int, int, int] | str | None]] = []
    for frame in worked:
        begin = trace_ns(frame.cpu_start_ns or 0)
        finish = begin + (frame.cpu_busy_ns or 0)
        events.append((begin, 1, (0, TASK_RUNNING, APP_PID)))
        if has_section(frame.frame_index):
            events.append((begin + section_late_ns(frame.frame_index), 2, f"B|{APP_PID}|{section_name(frame.frame_index)}\n"))
            events.append((finish, 0, f"E|{APP_PID}\n"))
        if is_preempted(frame.frame_index) and (frame.cpu_busy_ns or 0) > (4 * PREEMPTED_NS):
            middle = begin + ((frame.cpu_busy_ns or 0) // 2)
            events.append((middle, 1, (APP_PID, TASK_RUNNING, OTHER_PID)))
            events.append((middle + PREEMPTED_NS, 1, (OTHER_PID, TASK_INTERRUPTIBLE, APP_PID)))
        events.append((finish, 1, (APP_PID, TASK_INTERRUPTIBLE, 0)))
    first_refresh = min((f.intended_display_ns or 0) for f in golden if f.intended_display_ns is not None)
    index = round(first_refresh * 60 / 1_000_000_000) - 20
    while trace_ns(refresh_ns(index)) < end:
        events.append((trace_ns(refresh_ns(index)) - VBLANK_BEFORE_INTENDED_NS, 0, None))
        index += 1
    events.sort(key=lambda event: (event[0], event[1]))

    packet = builder.add_packet()
    packet.trusted_packet_sequence_id = SEQUENCE
    bundle = packet.ftrace_events
    bundle.cpu = 0
    sequence_number = 0
    for time_ns, _, switch in events:
        if isinstance(switch, str):
            event = bundle.event.add()
            event.timestamp = time_ns
            event.pid = APP_PID
            event.print.buf = switch
        elif switch is None:
            sequence_number += 1
            event = bundle.event.add()
            event.timestamp = time_ns
            event.pid = 0
            event.drm_vblank_event.crtc = 0
            event.drm_vblank_event.high_prec = 1
            event.drm_vblank_event.seq = sequence_number
            event.drm_vblank_event.time = time_ns
        else:
            _switch(bundle, time_ns, *switch)

    # The system's frame timeline, after everything the builder has
    parts = [builder.serialize()]
    marks = sorted({len(worked) // 3, (2 * len(worked)) // 3}) if with_service_events else []
    for position, frame in enumerate(worked):
        begin = trace_ns(frame.cpu_start_ns or 0)
        finish = begin + (frame.cpu_busy_ns or 0)
        late = "Late" in frame.flags
        surface_token = 1_000 + frame.frame_index
        display_token = 5_000 + frame.frame_index
        cookie = frame.frame_index * 2
        present = PRESENT_LATE if late else PRESENT_ON_TIME
        jank = JANK_APP_DEADLINE_MISSED if late else JANK_NONE

        surface = (
            _number(1, cookie)
            + _number(2, surface_token)
            + _number(3, display_token)
            + _number(4, APP_PID)
            + _bytes(5, APP_LAYER.encode())
            + _number(6, present)
            + _number(7, 0 if late else 1)
            + _number(8, 0)
            + _number(9, jank)
            + _number(10, PREDICTION_VALID)
        )
        parts.append(_timeline_packet(begin - SURFACE_BEFORE_CPU_NS, ACTUAL_SURFACE_FRAME_START, surface))
        parts.append(_timeline_packet(finish + SURFACE_AFTER_CPU_NS, FRAME_END, _number(1, cookie)))
        if frame.intended_display_ns is not None:
            display = (
                _number(1, cookie + 1)
                + _number(2, display_token)
                + _number(3, COMPOSITOR_PID)
                + _number(4, present)
                + _number(5, 0 if late else 1)
                + _number(6, 0)
                + _number(7, jank)
                + _number(8, PREDICTION_VALID)
            )
            parts.append(_timeline_packet(finish + 1_000_000, ACTUAL_DISPLAY_FRAME_START, display))
            presented = presented_ns(frame.intended_display_ns, frame.cpu_start_ns or 0, frame.cpu_busy_ns or 0)
            parts.append(_timeline_packet(presented, FRAME_END, _number(1, cookie + 1)))
        if position in marks:
            for _ in range(2):
                parts.append(_service_packet(finish + 2_000_000, flushed=True))
                parts.append(_service_packet(finish + 2_000_001, flushed=False))
    return b"".join(parts)


def _record(builder: TraceProtoBuilder, run_id: int, frame_index: int, start_ns: int, busy_ns: int, values: dict[str, int]) -> None:
    """One frame's slice in the application's own trace: its begin with the name and the annotations, and its end, both
    stamped on CLOCK_MONOTONIC."""
    packet = builder.add_packet()
    packet.timestamp = start_ns
    packet.timestamp_clock_id = CLOCK_MONOTONIC
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    packet.track_event.type = TrackEvent.TYPE_SLICE_BEGIN
    packet.track_event.track_uuid = APP_RECORD_TRACK
    packet.track_event.categories.append(RECORD_CATEGORY)
    packet.track_event.name = f"mbfp frame {run_id} {frame_index}"
    for name, value in values.items():
        annotation = packet.track_event.debug_annotations.add()
        annotation.name = name
        annotation.int_value = value
    packet = builder.add_packet()
    packet.timestamp = start_ns + busy_ns
    packet.timestamp_clock_id = CLOCK_MONOTONIC
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    packet.track_event.type = TrackEvent.TYPE_SLICE_END
    packet.track_event.track_uuid = APP_RECORD_TRACK


def build_app() -> bytes:
    """The bytes of the application's own trace: a snapshot of its clocks, its tracks, and its record of its frames."""
    builder = TraceProtoBuilder()
    worked = [f for f in frames() if f.cpu_start_ns is not None and f.cpu_busy_ns is not None]
    start = trace_ns(min(f.cpu_start_ns or 0 for f in worked)) - 1_000_000_000

    packet = builder.add_packet()
    packet.timestamp = start
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    for clock_id, value in ((CLOCK_BOOTTIME, start), (CLOCK_MONOTONIC, start - BOOT_MINUS_MONOTONIC_NS)):
        clock = packet.clock_snapshot.clocks.add()
        clock.clock_id = clock_id
        clock.timestamp = value

    packet = builder.add_packet()
    packet.timestamp = start
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    packet.track_descriptor.uuid = APP_PROCESS_TRACK
    packet.track_descriptor.process.pid = APP_PID
    packet.track_descriptor.process.process_name = APP_NAME
    packet = builder.add_packet()
    packet.timestamp = start
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    packet.track_descriptor.uuid = APP_RECORD_TRACK
    packet.track_descriptor.parent_uuid = APP_PROCESS_TRACK
    packet.track_descriptor.name = RECORD_TRACK_NAME

    # A marker's times are on the application's clock: the made-up device's CLOCK_MONOTONIC is APP_CLOCK_OFFSET_NS later
    last_end = 0
    for frame in worked:
        begin = (frame.cpu_start_ns or 0) + APP_CLOCK_OFFSET_NS
        last_end = max(last_end, begin + (frame.cpu_busy_ns or 0))
        if not has_record(frame.frame_index):
            continue
        values = {
            "intendedDisplayNs": frame.intended_display_ns or 0,
            "animationNs": frame.animation_ns + record_animation_off_ns(frame.frame_index),
        }
        if has_frame_times(frame.frame_index):
            values["markerTargetNs"] = frame.marker_target_ns or 0
            values["markerPreferredNs"] = frame.marker_preferred_ns or 0
        _record(builder, RUN_ID, frame.frame_index, begin, frame.cpu_busy_ns or 0, values)
    # Another run's frame, with a frame index this run has too: it is not this run's
    _record(builder, OTHER_RUN_ID, worked[0].frame_index, last_end + 1_000_000, 2_000_000, {})
    return builder.serialize()


def write(path: Path, with_service_events: bool = False) -> None:
    _ = path.write_bytes(build(with_service_events))


def write_app(path: Path) -> None:
    _ = path.write_bytes(build_app())


# The example's tracks under the application's process (sdk/doc/trace-frame-record.md, part 2), and what the made-up application
# says of itself
EXAMPLE_TRACKS = {
    "mbfp waits": 0x6D62_6670_0000_0010,
    "mbfp presents": 0x6D62_6670_0000_0011,
    "mbfp gpu": 0x6D62_6670_0000_0012,
    "mbfp display": 0x6D62_6670_0000_0013,
    "mbfp run": 0x6D62_6670_0000_0014,
}
EXAMPLE_WAIT_NS = 1_500_000
EXAMPLE_PRESENT_NS = 200_000
EXAMPLE_GPU_AFTER_PRESENT_NS = 2_000_000
EXAMPLE_ENVIRONMENT: dict[str, int | str] = {
    "graphicsApi": "vulkan",
    "windowSystem": "android",
    "presentMode": "queue",
    "imageCount": 3,
    "maxFramesInFlight": 2,
    "variableRefresh": "off",
    "vsyncSource": "present wait",
    "pacerTier": "3.1+",
    "pacerAim": "smoothness",
    "capabilities": "WaitForPresent, VBlankTimes, DisplayTimes",
    "activeCapabilities": "WaitForPresent, VBlankTimes, DisplayTimes",
    "platformFeatures": "VK_KHR_present_wait2, VK_GOOGLE_display_timing",
}


def _event(builder: TraceProtoBuilder, time_ns: int, track: int, kind: TrackEvent.Type, name: str = "", values: dict[str, int | str] | None = None) -> None:
    """One TrackEvent of the application's own sequence, stamped on CLOCK_MONOTONIC, with its annotations."""
    packet = builder.add_packet()
    packet.timestamp = time_ns
    packet.timestamp_clock_id = CLOCK_MONOTONIC
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    packet.track_event.type = kind
    packet.track_event.track_uuid = track
    if kind != TrackEvent.TYPE_SLICE_END:
        packet.track_event.categories.append(RECORD_CATEGORY)
        packet.track_event.name = name
    for key, value in (values or {}).items():
        annotation = packet.track_event.debug_annotations.add()
        annotation.name = key
        if isinstance(value, str):
            annotation.string_value = value
        else:
            annotation.int_value = value


def _slice(builder: TraceProtoBuilder, track: int, name: str, begin_ns: int, end_ns: int, values: dict[str, int | str] | None = None) -> None:
    _event(builder, begin_ns, track, TrackEvent.TYPE_SLICE_BEGIN, name, values)
    _event(builder, max(end_ns, begin_ns), track, TrackEvent.TYPE_SLICE_END)


def build_example() -> bytes:
    """The SDK's example of the frame record with all of its second part (sdk/test-data/trace/example-app.pftrace): the made-up
    application's own trace for the golden capture, with the record of every frame that has CPU times and, around each frame, its
    wait for an earlier present, its present call, the GPU's work on it and the display time the platform reported, then the run's
    environment, a swap chain made at the start and a moment for every frame index never presented. Made up in every value; its
    display times are the capture's, put on the application's clock."""
    from .golden_capture import CAPTURE_MINUS_PACER_NS, PERIOD_NS

    builder = TraceProtoBuilder()
    worked = [f for f in frames() if f.cpu_start_ns is not None and f.cpu_busy_ns is not None]
    start = trace_ns(min(f.cpu_start_ns or 0 for f in worked)) - 1_000_000_000

    packet = builder.add_packet()
    packet.timestamp = start
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    packet.sequence_flags = TracePacket.SEQ_INCREMENTAL_STATE_CLEARED
    for clock_id, value in ((CLOCK_BOOTTIME, start), (CLOCK_MONOTONIC, start - BOOT_MINUS_MONOTONIC_NS)):
        clock = packet.clock_snapshot.clocks.add()
        clock.clock_id = clock_id
        clock.timestamp = value
    packet = builder.add_packet()
    packet.timestamp = start
    packet.trusted_packet_sequence_id = APP_SEQUENCE
    packet.track_descriptor.uuid = APP_PROCESS_TRACK
    packet.track_descriptor.process.pid = APP_PID
    packet.track_descriptor.process.process_name = APP_NAME
    for name, uuid in (("mbfp frames", APP_RECORD_TRACK), *EXAMPLE_TRACKS.items()):
        packet = builder.add_packet()
        packet.timestamp = start
        packet.trusted_packet_sequence_id = APP_SEQUENCE
        packet.track_descriptor.uuid = uuid
        packet.track_descriptor.parent_uuid = APP_PROCESS_TRACK
        packet.track_descriptor.name = name

    def app_ns(marker_ns: int) -> int:
        return marker_ns + APP_CLOCK_OFFSET_NS

    run = EXAMPLE_TRACKS["mbfp run"]
    first = app_ns(worked[0].cpu_start_ns or 0) - 10_000_000
    _event(builder, first, run, TrackEvent.TYPE_INSTANT, "mbfp swap chain recreated", {"runId": RUN_ID, "reason": "created"})
    _event(builder, first + 1, run, TrackEvent.TYPE_INSTANT, "mbfp environment", {"runId": RUN_ID, **EXAMPLE_ENVIRONMENT, "refreshPeriodNs": PERIOD_NS})

    previous_present_end = 0
    previous_gpu_end = 0
    for index, frame in enumerate(worked):
        begin = app_ns(frame.cpu_start_ns or 0)
        busy = frame.cpu_busy_ns or 0
        presented = begin + busy
        ids = f"{RUN_ID} {frame.frame_index}"
        # Frame indices the run never presented, at the start of the frame after them
        for skipped in range(frame.frame_index - frame.skipped_before, frame.frame_index):
            _event(builder, begin, run, TrackEvent.TYPE_INSTANT, f"mbfp not presented {RUN_ID} {skipped}")
        # The wait for an earlier present, up to the frame's start: never before the previous present returned
        wait_begin = max(begin - EXAMPLE_WAIT_NS, previous_present_end)
        _slice(builder, EXAMPLE_TRACKS["mbfp waits"], f"mbfp wait {ids}", wait_begin, begin, {"kind": "present"})
        # The record
        values: dict[str, int | str] = {"animationNs": frame.animation_ns}
        if frame.marker_preferred_ns:
            values["markerPreferredNs"] = frame.marker_preferred_ns
        if frame.marker_target_ns:
            values["markerTargetNs"] = frame.marker_target_ns
        if frame.intended_display_ns:
            values["intendedDisplayNs"] = frame.intended_display_ns
        _slice(builder, APP_RECORD_TRACK, f"mbfp frame {ids}", begin, presented, values)
        # The present call and the GPU's work, which ends a little after it, before the next frame's begins
        present_end = presented + EXAMPLE_PRESENT_NS
        swap_interval = max(1, round((frame.marker_target_ns or PERIOD_NS) / PERIOD_NS))
        _slice(
            builder,
            EXAMPLE_TRACKS["mbfp presents"],
            f"mbfp present {ids}",
            presented,
            present_end,
            {"swapInterval": swap_interval, "result": "accepted", "framesInFlight": 1 + index % 2},
        )
        previous_present_end = present_end
        gpu_begin = max(begin + busy // 2, previous_gpu_end)
        following = app_ns(worked[index + 1].cpu_start_ns or 0) + (worked[index + 1].cpu_busy_ns or 0) // 2 if index + 1 < len(worked) else None
        gpu_end = presented + EXAMPLE_GPU_AFTER_PRESENT_NS
        if following is not None:
            gpu_end = min(gpu_end, following)
        _slice(builder, EXAMPLE_TRACKS["mbfp gpu"], f"mbfp gpu {ids}", gpu_begin, gpu_end)
        previous_gpu_end = gpu_end
        # When the platform says it was shown: the capture's display time, on the application's clock
        shown = app_ns(frame.first_seen_ns - CAPTURE_MINUS_PACER_NS)
        _event(builder, shown, EXAMPLE_TRACKS["mbfp display"], TrackEvent.TYPE_INSTANT, f"mbfp display {ids}", {"source": "VK_GOOGLE_display_timing"})
    return builder.serialize()


def write_example(path: Path) -> None:
    _ = path.write_bytes(build_example())
