// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/core/ByteSpanUtil.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/capture/CaptureDataRecord.hpp>
#include <mb/framepacing/data/capture/CaptureDataStatus.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include "detail/CaptureDataFormat.hpp"

namespace MB::FramePacing::Data
{
  namespace
  {
    constexpr std::size_t OffsetStatus = 28;
    constexpr std::size_t OffsetMainLength = 29;
    constexpr std::size_t OffsetSecondLength = 30;
    constexpr std::size_t OffsetMain = 32;
    constexpr std::size_t OffsetSecond = OffsetMain + CaptureDataFormat::MainMarkerCapacity;

    static_assert(OffsetSecond + CaptureDataFormat::SecondMarkerCapacity == CaptureDataFormat::RecordSize);
    // Either slot holds any marker the marker module encodes
    static_assert(CaptureDataFormat::MainMarkerCapacity >= MB::FramePacing::Marker::Payload::MaxEncodedByteCount);
    static_assert(CaptureDataFormat::SecondMarkerCapacity >= MB::FramePacing::Marker::Payload::MaxEncodedByteCount);

    bool TryDecode(const std::vector<uint8_t>& bytes, MB::FramePacing::Marker::Payload& rPayload,
                   MB::FramePacing::Marker::StartMetadata* pMetadata) noexcept
    {
      return !bytes.empty() && MB::FramePacing::Marker::TryDecodePayload(bytes, rPayload, pMetadata);
    }
  }

  bool CaptureDataRecord::TryDecodeMain(MB::FramePacing::Marker::Payload& rPayload, MB::FramePacing::Marker::StartMetadata* pMetadata) const noexcept
  {
    return TryDecode(MainBytes, rPayload, pMetadata);
  }

  bool CaptureDataRecord::TryDecodeSecond(MB::FramePacing::Marker::Payload& rPayload) const noexcept
  {
    return TryDecode(SecondBytes, rPayload, nullptr);
  }

  CaptureDataRecord CaptureDataRecord::Parse(const std::span<const uint8_t> bytes)
  {
    if (bytes.size() < CaptureDataFormat::RecordSize)
    {
      throw DataFormatError("A capture data record is 256 bytes");
    }
    const uint8_t status = bytes[OffsetStatus];
    const std::size_t mainLength = bytes[OffsetMainLength];
    const std::size_t secondLength = bytes[OffsetSecondLength];
    if (status > static_cast<uint8_t>(CaptureDataStatus::Torn) || mainLength > CaptureDataFormat::MainMarkerCapacity ||
        secondLength > CaptureDataFormat::SecondMarkerCapacity)
    {
      throw DataFormatError("Invalid capture data record");
    }
    CaptureDataRecord record;
    record.CaptureIndex = ByteSpanUtil::ReadLE<int64_t>(bytes, 0);
    record.HostTime = TickCount64(ByteSpanUtil::ReadLE<int64_t>(bytes, 8));
    const auto deviceTicks = ByteSpanUtil::ReadLE<int64_t>(bytes, 16);
    if (deviceTicks != CaptureDataFormat::UnknownTicks)
    {
      record.DeviceTime = TickCount64(deviceTicks);
    }
    record.SourceDrops = ByteSpanUtil::ReadLE<uint32_t>(bytes, 24);
    record.CaptureStatus = static_cast<CaptureDataStatus>(status);
    const auto main = bytes.subspan(OffsetMain, mainLength);
    const auto second = bytes.subspan(OffsetSecond, secondLength);
    record.MainBytes.assign(main.begin(), main.end());
    record.SecondBytes.assign(second.begin(), second.end());
    return record;
  }
}
