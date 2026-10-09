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
- and for the whole run the kernel's vertical blank events, VBLANK_BEFORE_INTENDED_NS before each refresh the pacer aimed at.

The frame timeline's messages are not among the ones Perfetto's Python package has (they are Android's, an extension of the
packet): they are written here field by field, by the numbers of Perfetto's frameworks_native_trace_packet.proto."""

from pathlib import Path

from perfetto.protos.perfetto.trace.perfetto_trace_pb2 import TracePacket
from perfetto.trace_builder.proto_builder import TraceProtoBuilder

from .golden_capture import frames

APP_CLOCK_OFFSET_NS = 100_000_000_000
BOOT_MINUS_MONOTONIC_NS = 5_000_000_000
VBLANK_BEFORE_INTENDED_NS = 3_000_000
PREEMPTED_NS = 200_000
SURFACE_BEFORE_CPU_NS = 100_000
SURFACE_AFTER_CPU_NS = 500_000

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

    # The scheduler's and the display controller's events, in time order on one CPU
    events: list[tuple[int, int, tuple[int, int, int] | None]] = []
    for frame in worked:
        begin = trace_ns(frame.cpu_start_ns or 0)
        finish = begin + (frame.cpu_busy_ns or 0)
        events.append((begin, 1, (0, TASK_RUNNING, APP_PID)))
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
        if switch is None:
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


def write(path: Path, with_service_events: bool = False) -> None:
    _ = path.write_bytes(build(with_service_events))
