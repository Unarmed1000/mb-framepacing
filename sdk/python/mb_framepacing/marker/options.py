# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""Options: how large the marker is drawn, as in the C# library (MB.FramePacing.Marker.Options) and the C++ library
(MB::FramePacing::Marker::Options)."""

from dataclasses import dataclass
from typing import Self

from ..point import Point
from .constants import (
    DEFAULT_MODULE_SIZE_PX,
    MAX_MODULE_SIZE_PX,
    MAX_QUIET_ZONE_MODULES,
    MIN_MODULE_SIZE_PX,
    RECOMMENDED_INSET_PX,
    RECOMMENDED_QUIET_ZONE_MODULES,
    qr_module_count_for,
)
from .structures import MarkerKind


@dataclass(frozen=True, slots=True, init=False)
class Options:
    """How large the marker is drawn: the size of one QR module in source pixels and the white border around the symbol in modules.
    Always valid: the module size is within [MIN_MODULE_SIZE_PX, MAX_MODULE_SIZE_PX] and the quiet zone within [0,
    MAX_QUIET_ZONE_MODULES]; the constructor clamps a value outside into its range."""

    module_size_px: int
    quiet_zone_modules: int

    def __init__(self, module_size_px: int = DEFAULT_MODULE_SIZE_PX, quiet_zone_modules: int = RECOMMENDED_QUIET_ZONE_MODULES) -> None:
        """See doc/marker-format.md "Sizing", or Options.recommended for a capture's scaling. The QR specification asks for a quiet zone
        of 4 modules."""
        object.__setattr__(self, "module_size_px", max(MIN_MODULE_SIZE_PX, min(module_size_px, MAX_MODULE_SIZE_PX)))
        object.__setattr__(self, "quiet_zone_modules", max(0, min(quiet_zone_modules, MAX_QUIET_ZONE_MODULES)))

    @classmethod
    def recommended(cls, source_height: int, stored_height: int, mjpeg: bool = False) -> Self:
        """The recommended options for a capture that stores the `source_height` pixel high output `stored_height` pixels high: 3 stored
        pixels per module (4 when the capture card delivers MJPEG) after all scaling (source -> capture -> stored), and the recommended
        quiet zone. A height of 0 or less means no scaling."""
        return cls(_module_size_for_stored_px(4 if mjpeg else 3, source_height, stored_height))

    @classmethod
    def minimum(cls, source_height: int, stored_height: int) -> Self:
        """The smallest module size that still decodes: 2 stored pixels per module after all scaling, and the recommended quiet zone."""
        return cls(_module_size_for_stored_px(2, source_height, stored_height))

    @property
    def quiet_zone_px(self) -> int:
        """The quiet zone in source pixels: the offset from the marker's origin to its symbol."""
        return self.quiet_zone_modules * self.module_size_px

    def marker_size_px(self, kind: MarkerKind = MarkerKind.FRAME) -> int:
        """Width and height in source pixels of a marker (symbol + quiet zone). Frame, start and end markers have one size, the sync
        marker is smaller."""
        return (qr_module_count_for(kind) + (2 * self.quiet_zone_modules)) * self.module_size_px

    def recommended_origin(self, kind: MarkerKind, source_width: int, source_height: int, align_px: int = 1) -> Point:
        """Recommended origin of a marker in a `source_width` x `source_height` output: the main marker (frame, start and end) top-left,
        the sync marker bottom-left, RECOMMENDED_INSET_PX from the edges. `align_px` should be the capture's integer downscale ratio (1 if
        none) so module edges land on stored pixel edges."""
        del source_width  # the markers sit at the left edge; the width is part of the API as in the C# and C++ libraries
        inset = _align_up(RECOMMENDED_INSET_PX, align_px)
        if kind == MarkerKind.SYNC:
            return Point(inset, _align_down(source_height - inset - self.marker_size_px(kind), align_px))
        return Point(inset, inset)


def _divide(numerator: int, denominator: int) -> int:
    """Integer division truncating toward zero, as in C# and C++ (Python's // floors)."""
    quotient = abs(numerator) // abs(denominator)
    return quotient if (numerator >= 0) == (denominator > 0) else -quotient


def _ceil_divide(numerator: int, denominator: int) -> int:
    return _divide(numerator + denominator - 1, denominator)


def _align_down(value: int, alignment: int) -> int:
    return value if alignment <= 1 else _divide(value, alignment) * alignment


def _align_up(value: int, alignment: int) -> int:
    return value if alignment <= 1 else _ceil_divide(value, alignment) * alignment


def _module_size_for_stored_px(stored_px_per_module: int, source_height: int, stored_height: int) -> int:
    """The module size that gives `stored_px_per_module` stored pixels per module, ceil(stored_px_per_module x source_height /
    stored_height), within the valid module sizes."""
    if source_height <= 0 or stored_height <= 0:
        return stored_px_per_module
    return max(stored_px_per_module, min(_ceil_divide(stored_px_per_module * source_height, stored_height), MAX_MODULE_SIZE_PX))
