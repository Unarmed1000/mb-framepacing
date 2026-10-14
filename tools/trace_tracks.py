# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""Writes a capture's tracks (trace_events) as the packets of a Perfetto trace, with
Perfetto's own Python package: a trace of their own, or added to the bytes of a trace that was recorded.

A Perfetto protobuf trace is its packets one after the other, and packets put next to a trace's bytes are part of it (the way
Perfetto's own documentation adds symbols to a trace). What is added is told apart from everything a trace has by three
things, all made from the run and the track's name so that the same capture gives the same bytes:

- its own packet sequences, one per track (a sequence is a writer, and each track is written in time order);
- its own track ids, under one root track named for the run;
- its own flow numbers, for the arrow from a frame's CPU slice to its slice on screen.

**The tracks go before the recorded trace's packets, with every time on the trace's own clock and no clock named.** A reader
takes a trace that was written to its file in pieces (a long recording) piece by piece: packets after the last piece whose
times lie in earlier ones come too late and are left out, which the tests show (a third of the events, for a trace with two
such marks). Before the first piece nothing is too late. And a time without a clock is the trace's own time whatever its
clock snapshots say later in the file, so nothing depends on where they are: the caller converts with the trace's snapshots
(trace_alignment.to_trace_ns)."""

import gzip
import hashlib
import shutil
from collections.abc import Callable, Iterable, Mapping
from dataclasses import dataclass, field
from pathlib import Path
from typing import BinaryIO

from perfetto.protos.perfetto.trace.perfetto_trace_pb2 import TracePacket, TrackDescriptor, TrackEvent
from perfetto.trace_builder.proto_builder import StreamingTraceProtoBuilder
from trace_events import (  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)
    COUNTER_TRACKS,
    TOOLS_ROOT_PREFIX,
    TRACK_DESCRIPTIONS,
    ArgValue,
    Counter,
    Event,
    Instant,
    Slice,
    TraceFormatError,
    Track,
)

# The packet sequences of what is added: this number and the ones after it, one per track. A recording's own writers are
# numbered from 1 upwards
SEQUENCE_BASE = 0x4D42_5000

GZIP_MAGIC = b"\x1f\x8b"
ZSTD_MAGIC = b"\x28\xb5\x2f\xfd"


@dataclass
class Written:
    """What was written, per track: for the check that reads the result back."""

    slices: dict[Track, int] = field(default_factory=dict)
    instants: dict[Track, int] = field(default_factory=dict)
    counters: dict[Track, int] = field(default_factory=dict)
    packets: int = 0


def stable_id(run_key: str, name: str) -> int:
    """An id made from the run and a name: 63 bits of their SHA-256, never zero. The same run and name give the same id."""
    digest = hashlib.sha256(f"mb-framepacing/{run_key}/{name}".encode()).digest()
    return (int.from_bytes(digest[:8], "little") & ((1 << 63) - 1)) or 1


def root_name(run_name: str) -> str:
    """The name of the track everything of a run is under."""
    return TOOLS_ROOT_PREFIX + run_name


def copy_source(source: Path, output: BinaryIO) -> None:
    """The bytes of a recorded trace, as packets can be put next to them: a whole-file gzip is unpacked, a trace as it is."""
    with source.open("rb") as file:
        magic = file.read(4)
    if magic.startswith(ZSTD_MAGIC):
        raise TraceFormatError(f"{source.name} is a zstd file: unpack it first (zstd -d), and give the trace inside.")
    if magic.startswith(GZIP_MAGIC):
        with gzip.open(source, "rb") as unpacked:
            shutil.copyfileobj(unpacked, output)
        return
    if not magic.startswith(b"\x0a"):
        raise TraceFormatError(f"{source.name} is not a Perfetto protobuf trace (it does not begin with a packet): JSON and other formats are not taken.")
    with source.open("rb") as file:
        shutil.copyfileobj(file, output)


def _annotate(event: TrackEvent, args: Iterable[tuple[str, ArgValue]]) -> None:
    for name, value in args:
        annotation = event.debug_annotations.add()
        annotation.name = name
        # A bool is an int to Python: asked for first
        if isinstance(value, bool):
            annotation.bool_value = value
        elif isinstance(value, int):
            annotation.int_value = value
        elif isinstance(value, float):
            annotation.double_value = value
        else:
            annotation.string_value = value


class TrackWriter:
    """Writes the tracks of one run to a file that is open for writing bytes. to_trace_ns puts a time of the events (on the
    system clock the application's clock is) on the trace's own clock."""

    def __init__(self, output: BinaryIO, run_key: str, run_name: str, to_trace_ns: Callable[[int], int]) -> None:
        self._builder = StreamingTraceProtoBuilder(output)
        self._run_key = run_key
        self._root_name = root_name(run_name)
        self._root_id = stable_id(run_key, "root")
        self._to_trace_ns = to_trace_ns
        self.written = Written()

    def _packet(self) -> TracePacket:
        self.written.packets += 1
        return self._builder.create_packet()

    def write_attributes(self, attributes: Mapping[str, int | str]) -> None:
        """What the tracks rest on (the clocks' offsets, what they were placed by), as slices' arguments can not say it: the
        root track's description, which the viewer shows for the track."""
        packet = self._packet()
        descriptor = packet.track_descriptor
        descriptor.uuid = self._root_id
        descriptor.name = self._root_name
        descriptor.child_ordering = TrackDescriptor.EXPLICIT
        lines = ["Measured by mb-framepacing from a capture of the display, and placed on this trace's clock."]
        lines += [f"{key}: {value}" for key, value in attributes.items()]
        descriptor.description = "\n".join(lines)
        self._builder.write_packet(packet)

    def write_track(self, track: Track, events: Iterable[Event]) -> None:
        """One track: its descriptor, then its events in the order given, which is time order."""
        rank = list(Track).index(track)
        track_id = stable_id(self._run_key, track.value)
        sequence = SEQUENCE_BASE + rank

        packet = self._packet()
        descriptor = packet.track_descriptor
        descriptor.uuid = track_id
        descriptor.parent_uuid = self._root_id
        descriptor.name = track.value
        descriptor.description = TRACK_DESCRIPTIONS[track]
        descriptor.sibling_order_rank = rank
        descriptor.sibling_merge_behavior = TrackDescriptor.SIBLING_MERGE_BEHAVIOR_NONE
        if track in COUNTER_TRACKS:
            descriptor.counter.unit_name = "ms"
        self._builder.write_packet(packet)

        for event in events:
            if isinstance(event, Slice):
                start = self._to_trace_ns(event.start_ns)
                begin = self._event(sequence, track_id, start, TrackEvent.TYPE_SLICE_BEGIN)
                begin.track_event.name = event.name
                _annotate(begin.track_event, event.args)
                if event.flow != 0:
                    begin.track_event.flow_ids.append(stable_id(self._run_key, f"flow/{event.flow}"))
                self._builder.write_packet(begin)
                # A slice does not end before it begins, also where the clock it was on is one that can go back
                end = max(self._to_trace_ns(event.end_ns), start)
                self._builder.write_packet(self._event(sequence, track_id, end, TrackEvent.TYPE_SLICE_END))
                self.written.slices[track] = self.written.slices.get(track, 0) + 1
            elif isinstance(event, Instant):
                instant = self._event(sequence, track_id, self._to_trace_ns(event.time_ns), TrackEvent.TYPE_INSTANT)
                instant.track_event.name = event.name
                _annotate(instant.track_event, event.args)
                self._builder.write_packet(instant)
                self.written.instants[track] = self.written.instants.get(track, 0) + 1
            else:
                counter: Counter = event
                value = self._event(sequence, track_id, self._to_trace_ns(counter.time_ns), TrackEvent.TYPE_COUNTER)
                value.track_event.double_counter_value = counter.value
                self._builder.write_packet(value)
                self.written.counters[track] = self.written.counters.get(track, 0) + 1

    def _event(self, sequence: int, track_id: int, time_ns: int, kind: TrackEvent.Type) -> TracePacket:
        if time_ns < 0:
            raise TraceFormatError(
                f"A time before the clock's zero can not be written ({time_ns} ns): the run's frames are placed before the trace's " + "clock began."
            )
        # No clock is named: the time is the trace's own
        packet = self._packet()
        packet.timestamp = time_ns
        packet.trusted_packet_sequence_id = sequence
        packet.track_event.type = kind
        packet.track_event.track_uuid = track_id
        return packet
