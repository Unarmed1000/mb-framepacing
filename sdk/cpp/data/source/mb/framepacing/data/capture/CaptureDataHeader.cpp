// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/core/ByteSpanUtil.hpp>
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/capture/CaptureDataHeader.hpp>
#include <string>
#include "detail/CaptureDataFormat.hpp"

namespace MB::FramePacing::Data
{
  namespace
  {
    constexpr std::size_t OffsetMarkers = 72;
    constexpr std::size_t MarkerSize = 24;
    constexpr uint32_t FramesStoredFlag = 1u;
    constexpr uint32_t CameraFlag = 2u;

    static_assert(OffsetMarkers + (CaptureDataFormat::MaxMarkerLocations * MarkerSize) <= CaptureDataFormat::HeaderSize);

    Rectangle ReadRect(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return {ByteSpanUtil::ReadLE<int32_t>(bytes, offset), ByteSpanUtil::ReadLE<int32_t>(bytes, offset + 4),
              ByteSpanUtil::ReadLE<int32_t>(bytes, offset + 8), ByteSpanUtil::ReadLE<int32_t>(bytes, offset + 12)};
    }
  }

  CaptureDataHeader CaptureDataHeader::Parse(const std::span<const uint8_t> bytes)
  {
    if (bytes.size() < CaptureDataFormat::HeaderSize || ByteSpanUtil::ReadLE<uint32_t>(bytes, 0) != CaptureDataFormat::Magic)
    {
      throw DataFormatError("Not an mb-framepacing capture data file (.mbcd)");
    }
    const auto version = ByteSpanUtil::ReadLE<uint16_t>(bytes, 4);
    if (version > CaptureDataFormat::Version)
    {
      throw DataFormatError("The capture data file has format version " + std::to_string(version) + ", newer than this reader reads (" +
                            std::to_string(CaptureDataFormat::Version) + "): update the tools or the library");
    }
    if (version != CaptureDataFormat::Version)
    {
      throw DataFormatError("Unsupported capture data file format version " + std::to_string(version));
    }
    if (ByteSpanUtil::ReadLE<uint16_t>(bytes, 6) != CaptureDataFormat::HeaderSize ||
        ByteSpanUtil::ReadLE<uint32_t>(bytes, 8) != CaptureDataFormat::RecordSize)
    {
      throw DataFormatError("Unexpected capture data header or record size");
    }
    const auto markerCount = ByteSpanUtil::ReadLE<uint32_t>(bytes, 64);
    if (markerCount > CaptureDataFormat::MaxMarkerLocations)
    {
      throw DataFormatError("Invalid marker count in the capture data header");
    }

    const auto flags = ByteSpanUtil::ReadLE<uint32_t>(bytes, 12);
    CaptureDataHeader header;
    header.Width = ByteSpanUtil::ReadLE<int32_t>(bytes, 16);
    header.Height = ByteSpanUtil::ReadLE<int32_t>(bytes, 20);
    header.FrameRateNumerator = ByteSpanUtil::ReadLE<uint32_t>(bytes, 24);
    header.FrameRateDenominator = ByteSpanUtil::ReadLE<uint32_t>(bytes, 28);
    header.SourceWidth = ByteSpanUtil::ReadLE<int32_t>(bytes, 32);
    header.SourceHeight = ByteSpanUtil::ReadLE<int32_t>(bytes, 36);
    header.Region = ReadRect(bytes, 40);
    for (std::size_t i = 0; i < markerCount; ++i)
    {
      const std::size_t offset = OffsetMarkers + (i * MarkerSize);
      header.Markers.push_back({ReadRect(bytes, offset), ByteSpanUtil::ReadLE<double>(bytes, offset + 16)});
    }
    header.FramesStored = (flags & FramesStoredFlag) != 0u;
    header.Camera = (flags & CameraFlag) != 0u;
    return header;
  }
}
