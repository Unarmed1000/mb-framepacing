#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""The bytes an executable loads from its file: the sum of its sections' sizes, for measure_sdk_size.py.

An executable's file size grows in steps: every part of it is padded to a page (4 KiB on Linux, 16 KiB on macOS arm64) or a block
(512 bytes on Windows), so a few KiB of code can add nothing to the file or a whole page. The sections' own sizes have no padding:
code, constants and initialised data, to the byte. Left out, on every platform: data that is only zeros when the program starts
(.bss: no bytes in the file), symbols and debug information (not loaded, or not in a section).

Reads the three formats the SDK is measured in, all 64-bit and little-endian: PE (Windows), ELF (Linux) and Mach-O (macOS).

  python tools/executable_sections.py <executable> ...     # print each one's bytes
"""

import struct
import sys
from pathlib import Path

ELF_MAGIC = b"\x7fELF"
MACHO_MAGIC_64 = 0xFEEDFACF
# ELF: a section that is loaded (SHF_ALLOC) and has bytes in the file (not SHT_NOBITS)
ELF_SHF_ALLOC = 0x2
ELF_SHT_NOBITS = 8
# Mach-O: the load command of a 64-bit segment, and the section types that are only zeros when the program starts
MACHO_LC_SEGMENT_64 = 0x19
MACHO_ZERO_FILL_TYPES = (0x1, 0xC, 0x12)  # S_ZEROFILL, S_GB_ZEROFILL, S_THREAD_LOCAL_ZEROFILL


class UnknownFormatError(ValueError):
    """The file is not a 64-bit little-endian PE, ELF or Mach-O executable."""


def _pe_bytes(data: bytes) -> int:
    (header,) = struct.unpack_from("<I", data, 0x3C)
    if data[header : header + 4] != b"PE\0\0":
        raise UnknownFormatError("an MZ file without a PE header")
    section_count, optional_size = struct.unpack_from("<H", data, header + 6)[0], struct.unpack_from("<H", data, header + 20)[0]
    table = header + 24 + optional_size
    total = 0
    for index in range(section_count):
        virtual_size, _, raw_size = struct.unpack_from("<III", data, table + 40 * index + 8)
        # The virtual size is the section's own; where it is larger than what the file holds, the rest is zeros
        total += min(virtual_size, raw_size)
    return total


def _elf_bytes(data: bytes) -> int:
    if data[4] != 2 or data[5] != 1:
        raise UnknownFormatError("an ELF file that is not 64-bit little-endian")
    (table,) = struct.unpack_from("<Q", data, 0x28)
    entry_size, count = struct.unpack_from("<HH", data, 0x3A)
    total = 0
    for index in range(count):
        kind, flags = struct.unpack_from("<IQ", data, table + entry_size * index + 4)
        (size,) = struct.unpack_from("<Q", data, table + entry_size * index + 32)
        if flags & ELF_SHF_ALLOC and kind != ELF_SHT_NOBITS:
            total += size
    return total


def _macho_bytes(data: bytes) -> int:
    command_count = struct.unpack_from("<I", data, 16)[0]
    offset = 32
    total = 0
    for _ in range(command_count):
        command, command_size = struct.unpack_from("<II", data, offset)
        if command == MACHO_LC_SEGMENT_64:
            (section_count,) = struct.unpack_from("<I", data, offset + 64)
            for index in range(section_count):
                section = offset + 72 + 80 * index
                (size,) = struct.unpack_from("<Q", data, section + 40)
                (flags,) = struct.unpack_from("<I", data, section + 64)
                if flags & 0xFF not in MACHO_ZERO_FILL_TYPES:
                    total += size
        offset += command_size
    return total


def loaded_bytes(executable: Path) -> int:
    """The bytes of the executable's sections that the file holds and the program loads."""
    data = executable.read_bytes()
    if data[:2] == b"MZ":
        return _pe_bytes(data)
    if data[:4] == ELF_MAGIC:
        return _elf_bytes(data)
    if len(data) >= 4 and struct.unpack_from("<I", data, 0)[0] == MACHO_MAGIC_64:
        return _macho_bytes(data)
    raise UnknownFormatError(f"{executable}: not a 64-bit little-endian PE, ELF or Mach-O file")


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    for name in sys.argv[1:]:
        print(f"{loaded_bytes(Path(name))}\t{name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
