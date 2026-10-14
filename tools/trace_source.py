# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""EXPERIMENTAL (measure/doc/perfetto.md). Reads what trace_augment.py needs of a Perfetto trace, with Perfetto's own Python
package: the trace's clocks, the frame record the application logged, its processes, the display's vertical blanks, the
system's frame timeline and a thread's scheduling states.

The package reads a trace through Perfetto's trace processor, a native program it downloads on first use (a pinned version,
checked by its SHA-256, kept under the user's home folder) and talks to on this machine only. trace_processor names one that is
on the disk already.

Every time is whole nanoseconds on the trace's clock."""

from dataclasses import dataclass
from pathlib import Path
from types import TracebackType

from perfetto.trace_processor import TraceProcessor, TraceProcessorConfig
from trace_alignment import (  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)
    RECORD_NAME_PREFIX,
    ClockSnapshot,
    FrameRecord,
    parse_record_name,
)
from trace_events import TOOLS_ROOT_PREFIX, TraceReadError  # pyright: ignore[reportImplicitRelativeImport]
from trace_frames import SurfaceFrame, ThreadState  # pyright: ignore[reportImplicitRelativeImport]

# The kernel's vertical blank events, as the trace processor shows them: slices named so on tracks named vblank-<crtc>
DRM_VBLANK_TRACKS = "vblank-*"
DRM_VBLANK_SLICE = "signal"


@dataclass(frozen=True)
class Process:
    upid: int
    pid: int | None
    name: str


@dataclass(frozen=True)
class VBlankSource:
    """Where a trace has the display's vertical blanks: the kernel's events of one display controller, or a counter that
    changes with every one. count is how many it has."""

    kind: str
    name: str
    count: int

    @property
    def is_counter(self) -> bool:
        return self.kind == "counter"


def quoted(text: str) -> str:
    """A text as an SQL string."""
    return "'" + text.replace("'", "''") + "'"


class TraceSource:
    """A trace that is open for reading. Close it (or use it in a with statement): it holds a running trace processor."""

    def __init__(self, path: Path, trace_processor: Path | None = None) -> None:
        if not path.is_file():
            raise TraceReadError(f"{path} is not a file.")
        config = TraceProcessorConfig(bin_path=str(trace_processor) if trace_processor is not None else None)
        try:
            self._processor = TraceProcessor(trace=str(path), config=config)
        except Exception as error:
            # The package raises its own exception, and whatever starting the program or the download raised
            raise TraceReadError(f"{path.name} could not be read with Perfetto's trace processor: {error}") from error

    def __enter__(self) -> "TraceSource":
        return self

    def __exit__(self, kind: type[BaseException] | None, error: BaseException | None, traceback: TracebackType | None) -> None:
        self.close()

    def close(self) -> None:
        self._processor.close()

    def _rows(self, sql: str) -> list:
        try:
            return list(self._processor.query(sql))
        except Exception as error:
            raise TraceReadError(f"The trace processor refused a query ({error}): {sql}") from error

    def trace_range(self) -> tuple[int, int]:
        """The first and the last time of the trace."""
        row = self._rows("SELECT start_ts, end_ts FROM trace_bounds")[0]
        return int(row.start_ts), int(row.end_ts)

    def clock_snapshots(self, clock_id: int) -> list[ClockSnapshot]:
        """The trace's snapshots of one system clock, ascending by that clock."""
        rows = self._rows(f"SELECT ts, clock_value FROM clock_snapshot WHERE clock_id = {clock_id} ORDER BY clock_value, ts")
        return [ClockSnapshot(int(row.ts), int(row.clock_value)) for row in rows]

    def clocks(self) -> list[tuple[int, str | None, int]]:
        """The clocks the trace has snapshots of: id, name where it has one, and how many snapshots."""
        rows = self._rows("SELECT clock_id, clock_name, COUNT(*) AS snapshots FROM clock_snapshot GROUP BY clock_id, clock_name ORDER BY clock_id")
        return [(int(row.clock_id), row.clock_name, int(row.snapshots)) for row in rows]

    def errors(self) -> dict[str, int]:
        """What the trace processor counted as errors while it read the trace, by name."""
        rows = self._rows("SELECT name, SUM(value) AS total FROM stats WHERE severity = 'error' AND value > 0 GROUP BY name ORDER BY name")
        return {str(row.name): int(row.total) for row in rows}

    def frame_records(self) -> list[FrameRecord]:
        """The frame record: the slices named for a frame on any track, ascending by their start, with the process their
        track belongs to. Their times are on the trace's own clock here. A slice that had not ended when the trace did, or
        whose name is not a frame's, is left out, and so are the tools' own slices of a frame (an augmented trace's tracks,
        under a track named for a run)."""
        rows = self._rows(
            "SELECT s.ts AS ts, s.name AS name, p.name AS process FROM slice s "
            + "LEFT JOIN thread_track tt ON tt.id = s.track_id LEFT JOIN thread th ON th.utid = tt.utid "
            + "LEFT JOIN process_track pt ON pt.id = s.track_id LEFT JOIN process p ON p.upid = COALESCE(pt.upid, th.upid) "
            + "LEFT JOIN track own ON own.id = s.track_id LEFT JOIN track parent ON parent.id = own.parent_id "
            + f"WHERE s.name GLOB {quoted(RECORD_NAME_PREFIX + '*')} AND s.dur >= 0 "
            + f"AND IFNULL(parent.name, '') NOT GLOB {quoted(TOOLS_ROOT_PREFIX + '*')} ORDER BY s.ts"
        )
        records: list[FrameRecord] = []
        for row in rows:
            ids = parse_record_name(str(row.name))
            if ids is not None:
                records.append(FrameRecord(ids[0], ids[1], int(row.ts), str(row.process) if row.process is not None else None))
        return records

    def processes(self, name_glob: str = "*") -> list[Process]:
        """The processes with a name that fits the pattern (* and ? as in a file name), by name."""
        rows = self._rows(f"SELECT upid, pid, name FROM process WHERE name GLOB {quoted(name_glob)} ORDER BY name, upid")
        return [Process(int(row.upid), int(row.pid) if row.pid is not None else None, str(row.name)) for row in rows]

    def vblank_sources(self) -> list[VBlankSource]:
        """The kernel's vertical blank events per display controller, then the counters with VSYNC in their name."""
        sources: list[VBlankSource] = []
        drm = self._rows(
            "SELECT t.name AS name, COUNT(*) AS count FROM slice s JOIN track t ON s.track_id = t.id "
            + f"WHERE t.name GLOB {quoted(DRM_VBLANK_TRACKS)} AND s.name = {quoted(DRM_VBLANK_SLICE)} GROUP BY t.name ORDER BY t.name"
        )
        sources += [VBlankSource("kernel", str(row.name), int(row.count)) for row in drm]
        counters = self._rows(
            "SELECT t.name AS name, COUNT(*) AS count FROM counter c JOIN counter_track t ON c.track_id = t.id "
            + "WHERE t.name GLOB '*VSYNC*' OR t.name GLOB '*vsync*' GROUP BY t.name ORDER BY t.name"
        )
        sources += [VBlankSource("counter", str(row.name), int(row.count)) for row in counters]
        return sources

    def vblanks(self, source: VBlankSource) -> list[int]:
        """The times of one source's vertical blanks, ascending."""
        if source.is_counter:
            sql = "SELECT DISTINCT c.ts AS ts FROM counter c JOIN counter_track t ON c.track_id = t.id " + f"WHERE t.name = {quoted(source.name)} ORDER BY c.ts"
        else:
            sql = (
                "SELECT DISTINCT s.ts AS ts FROM slice s JOIN track t ON s.track_id = t.id "
                + f"WHERE t.name = {quoted(source.name)} AND s.name = {quoted(DRM_VBLANK_SLICE)} ORDER BY s.ts"
            )
        return [int(row.ts) for row in self._rows(sql)]

    def surface_frames(self, process_glob: str, layer_glob: str = "*") -> list[SurfaceFrame]:
        """The frame timeline's actual frames of the application (the processes and layers that fit the patterns), ascending by
        their start. A frame that had not ended when the trace did is left out."""
        rows = self._rows(
            "SELECT a.ts AS ts, a.dur AS dur, a.surface_frame_token AS surface_token, a.display_frame_token AS display_token, "
            + "a.layer_name AS layer_name, a.present_type AS present_type, a.jank_type AS jank_type, a.jank_tag AS jank_tag, "
            + "a.on_time_finish AS on_time_finish, a.gpu_composition AS gpu_composition "
            + "FROM actual_frame_timeline_slice a JOIN process p USING (upid) "
            + f"WHERE p.name GLOB {quoted(process_glob)} AND IFNULL(a.layer_name, '') GLOB {quoted(layer_glob)} "
            + "AND IFNULL(a.surface_frame_token, 0) != 0 AND a.dur >= 0 ORDER BY a.ts, a.surface_frame_token"
        )
        return [
            SurfaceFrame(
                start_ns=int(row.ts),
                end_ns=int(row.ts) + int(row.dur),
                surface_frame_token=int(row.surface_token),
                display_frame_token=int(row.display_token) if row.display_token is not None else None,
                layer_name=row.layer_name,
                present_type=row.present_type,
                jank_type=row.jank_type,
                jank_tag=row.jank_tag,
                on_time_finish=bool(row.on_time_finish) if row.on_time_finish is not None else None,
                gpu_composition=bool(row.gpu_composition) if row.gpu_composition is not None else None,
            )
            for row in rows
        ]

    def layers(self, process_glob: str) -> list[tuple[str, str, int]]:
        """The frame timeline's layers of the processes that fit the pattern: process, layer, and how many frames."""
        rows = self._rows(
            "SELECT p.name AS process, IFNULL(a.layer_name, '') AS layer, COUNT(*) AS frames "
            + "FROM actual_frame_timeline_slice a JOIN process p USING (upid) "
            + f"WHERE p.name GLOB {quoted(process_glob)} AND IFNULL(a.surface_frame_token, 0) != 0 GROUP BY p.name, a.layer_name ORDER BY frames DESC"
        )
        return [(str(row.process), str(row.layer), int(row.frames)) for row in rows]

    def display_presents(self) -> dict[int, int]:
        """When the system says each of its display frames was presented, by the frame's token: the end of the frame timeline's
        actual display frame, which runs from the compositor's start on it to the update on screen."""
        rows = self._rows(
            "SELECT display_frame_token AS token, MAX(ts + dur) AS presented FROM actual_frame_timeline_slice "
            + "WHERE IFNULL(surface_frame_token, 0) = 0 AND display_frame_token IS NOT NULL AND dur >= 0 GROUP BY display_frame_token"
        )
        return {int(row.token): int(row.presented) for row in rows}

    def threads(self, process_glob: str) -> list[tuple[int, str, str, bool]]:
        """The threads of the processes that fit the pattern: utid, process, thread name, and whether it is the main thread."""
        rows = self._rows(
            "SELECT t.utid AS utid, p.name AS process, IFNULL(t.name, '') AS thread, IFNULL(t.is_main_thread, 0) AS is_main "
            + f"FROM thread t JOIN process p USING (upid) WHERE p.name GLOB {quoted(process_glob)} ORDER BY p.name, is_main DESC, t.utid"
        )
        return [(int(row.utid), str(row.process), str(row.thread), bool(row.is_main)) for row in rows]

    def thread_states(self, utid: int) -> list[ThreadState]:
        """One thread's scheduling states, ascending. A state that had not ended when the trace did is left out."""
        rows = self._rows(f"SELECT ts, dur, state FROM thread_state WHERE utid = {utid} AND dur > 0 ORDER BY ts")
        return [ThreadState(int(row.ts), int(row.ts) + int(row.dur), str(row.state)) for row in rows]

    def added(self, root: str) -> tuple[dict[str, int], dict[str, int]]:
        """What a trace has under the root track of that name, per track name: its slices (a moment is a slice without length
        there), and its counter values."""
        parent = f"(SELECT id FROM track WHERE name = {quoted(root)})"
        slices = self._rows(
            f"SELECT t.name AS name, COUNT(*) AS count FROM slice s JOIN track t ON s.track_id = t.id WHERE t.parent_id IN {parent} GROUP BY t.name"
        )
        counters = self._rows(
            f"SELECT t.name AS name, COUNT(*) AS count FROM counter c JOIN counter_track t ON c.track_id = t.id WHERE t.parent_id IN {parent} GROUP BY t.name"
        )
        return {str(row.name): int(row.count) for row in slices}, {str(row.name): int(row.count) for row in counters}
