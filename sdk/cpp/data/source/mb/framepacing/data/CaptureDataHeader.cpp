// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/data/CaptureDataHeader.hpp>
#include <mb/framepacing/data/Constants.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <string>
#include "detail/LittleEndian.hpp"

namespace MB::FramePacing::Data
{
  namespace
  {
    constexpr std::size_t OffsetMarkers = 72;
    constexpr std::size_t MarkerSize = 24;
    constexpr uint32_t FramesStoredFlag = 1u;
    constexpr uint32_t CameraFlag = 2u;

    static_assert(OffsetMarkers + (MaxMarkerLocations * MarkerSize) <= CaptureDataHeaderSize);

    Rectangle ReadRect(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return {Detail::ReadI32(bytes, offset), Detail::ReadI32(bytes, offset + 4), Detail::ReadI32(bytes, offset + 8),
              Detail::ReadI32(bytes, offset + 12)};
    }
  }

  CaptureDataHeader CaptureDataHeader::Parse(const std::span<const uint8_t> bytes)
  {
    if (bytes.size() < CaptureDataHeaderSize || Detail::ReadU32(bytes, 0) != CaptureDataMagic)
    {
      throw DataFormatError("Not an mb-framepacing capture data file (.mbcd)");
    }
    const uint16_t version = Detail::ReadU16(bytes, 4);
    if (version > CaptureDataFormatVersion)
    {
      throw DataFormatError("The capture data file has format version " + std::to_string(version) + ", newer than this reader reads (" +
                            std::to_string(CaptureDataFormatVersion) + "): update the tools or the library");
    }
    if (version != CaptureDataFormatVersion)
    {
      throw DataFormatError("Unsupported capture data file format version " + std::to_string(version));
    }
    if (Detail::ReadU16(bytes, 6) != CaptureDataHeaderSize || Detail::ReadU32(bytes, 8) != CaptureDataRecordSize)
    {
      throw DataFormatError("Unexpected capture data header or record size");
    }
    const uint32_t markerCount = Detail::ReadU32(bytes, 64);
    if (markerCount > MaxMarkerLocations)
    {
      throw DataFormatError("Invalid marker count in the capture data header");
    }

    const uint32_t flags = Detail::ReadU32(bytes, 12);
    CaptureDataHeader header;
    header.Width = Detail::ReadI32(bytes, 16);
    header.Height = Detail::ReadI32(bytes, 20);
    header.FrameRateNumerator = Detail::ReadU32(bytes, 24);
    header.FrameRateDenominator = Detail::ReadU32(bytes, 28);
    header.SourceWidth = Detail::ReadI32(bytes, 32);
    header.SourceHeight = Detail::ReadI32(bytes, 36);
    header.Region = ReadRect(bytes, 40);
    for (std::size_t i = 0; i < markerCount; ++i)
    {
      const std::size_t offset = OffsetMarkers + (i * MarkerSize);
      header.Markers.push_back({ReadRect(bytes, offset), Detail::ReadF64(bytes, offset + 16)});
    }
    header.FramesStored = (flags & FramesStoredFlag) != 0u;
    header.Camera = (flags & CameraFlag) != 0u;
    return header;
  }
}
