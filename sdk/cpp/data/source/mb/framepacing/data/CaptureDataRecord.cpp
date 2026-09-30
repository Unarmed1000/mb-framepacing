// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/CaptureDataRecord.hpp>
#include <mb/framepacing/data/CaptureDataStatus.hpp>
#include <mb/framepacing/data/Constants.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/Payload.hpp>
#include <mb/framepacing/marker/StartMetadata.hpp>
#include "detail/LittleEndian.hpp"

namespace MB::FramePacing::Data
{
  namespace
  {
    constexpr std::size_t OffsetStatus = 28;
    constexpr std::size_t OffsetMainLength = 29;
    constexpr std::size_t OffsetSecondLength = 30;
    constexpr std::size_t OffsetMain = 32;
    constexpr std::size_t OffsetSecond = OffsetMain + MainMarkerCapacity;

    static_assert(OffsetSecond + SecondMarkerCapacity == CaptureDataRecordSize);

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
    if (bytes.size() < CaptureDataRecordSize)
    {
      throw DataFormatError("A capture data record is 192 bytes");
    }
    const uint8_t status = bytes[OffsetStatus];
    const std::size_t mainLength = bytes[OffsetMainLength];
    const std::size_t secondLength = bytes[OffsetSecondLength];
    if (status > static_cast<uint8_t>(CaptureDataStatus::Torn) || mainLength > MainMarkerCapacity || secondLength > SecondMarkerCapacity)
    {
      throw DataFormatError("Invalid capture data record");
    }
    CaptureDataRecord record;
    record.CaptureIndex = Detail::ReadI64(bytes, 0);
    record.HostTicks = Detail::ReadI64(bytes, 8);
    record.DeviceTicks = Detail::ReadI64(bytes, 16);
    record.SourceDrops = Detail::ReadU32(bytes, 24);
    record.Status = static_cast<CaptureDataStatus>(status);
    const auto main = bytes.subspan(OffsetMain, mainLength);
    const auto second = bytes.subspan(OffsetSecond, secondLength);
    record.MainBytes.assign(main.begin(), main.end());
    record.SecondBytes.assign(second.begin(), second.end());
    return record;
  }
}
