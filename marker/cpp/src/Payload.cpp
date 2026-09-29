// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framemarker/FrameMarker.hpp>
#include <algorithm>

namespace MB::FrameMarker
{
  namespace
  {
    constexpr std::size_t OffsetMagic0 = 0;
    constexpr std::size_t OffsetMagic1 = 1;
    constexpr std::size_t OffsetVersion = 2;
    constexpr std::size_t OffsetKind = 3;
    constexpr std::size_t OffsetFrameIndex = 4;
    constexpr std::size_t OffsetAnimationTicks = 12;
    constexpr std::size_t OffsetRunId = 20;
    constexpr std::size_t OffsetIntendedDisplayTicks = 24;
    constexpr std::size_t OffsetTargetFrameTicks = 32;
    constexpr std::size_t OffsetCpuStartTicks = 36;
    constexpr std::size_t OffsetCpuBusyTicks = 44;
    constexpr std::size_t OffsetPreferredFrameTicks = 48;
    constexpr std::size_t OffsetFlags = 52;
    constexpr std::size_t OffsetStartUtcTicks = PayloadByteCount;
    constexpr std::size_t OffsetSequenceId = OffsetStartUtcTicks + 8;

    static_assert(OffsetTargetFrameTicks + 4 == OffsetCpuStartTicks);
    static_assert(OffsetCpuStartTicks + 8 == OffsetCpuBusyTicks);
    static_assert(OffsetCpuBusyTicks + 4 == OffsetPreferredFrameTicks);
    static_assert(OffsetPreferredFrameTicks + 4 == OffsetFlags);
    static_assert(OffsetFlags + 1 == PayloadByteCount);
    static_assert(PayloadByteCount == 53u);
    static_assert(OffsetFrameIndex + 8 == SyncPayloadByteCount);
    static_assert(OffsetSequenceId + SequenceId::ByteCount == StartPayloadByteCount);
    static_assert(StartPayloadByteCount == 77u);
    static_assert(MaxEncodedPayloadByteCount <= QrCapacityBytes);

    template <std::size_t TByteCount>
    void WriteLE(const std::span<uint8_t> dst, const std::size_t offset, const uint64_t value) noexcept
    {
      for (std::size_t i = 0; i < TByteCount; ++i)
      {
        dst[offset + i] = static_cast<uint8_t>((value >> (8u * i)) & 0xFFu);
      }
    }

    template <std::size_t TByteCount>
    uint64_t ReadLE(const std::span<const uint8_t> src, const std::size_t offset) noexcept
    {
      uint64_t value = 0;
      for (std::size_t i = 0; i < TByteCount; ++i)
      {
        value |= static_cast<uint64_t>(src[offset + i]) << (8u * i);
      }
      return value;
    }

    //! The 53 byte header every kind starts with (a sync marker is its first 12 bytes).
    std::array<uint8_t, PayloadByteCount> EncodeHeader(const Payload& payload) noexcept
    {
      std::array<uint8_t, PayloadByteCount> bytes{};
      bytes[OffsetMagic0] = PayloadMagic0;
      bytes[OffsetMagic1] = PayloadMagic1;
      bytes[OffsetVersion] = PayloadFormatVersion;
      bytes[OffsetKind] = static_cast<uint8_t>(payload.Kind);
      WriteLE<8>(bytes, OffsetFrameIndex, payload.FrameIndex);
      // Two's complement, identical to C# BinaryPrimitives.WriteInt64LittleEndian
      WriteLE<8>(bytes, OffsetAnimationTicks, static_cast<uint64_t>(payload.AnimationTicks));
      WriteLE<4>(bytes, OffsetRunId, payload.RunId);
      WriteLE<8>(bytes, OffsetIntendedDisplayTicks, static_cast<uint64_t>(payload.IntendedDisplayTicks));
      WriteLE<4>(bytes, OffsetTargetFrameTicks, payload.TargetFrameTicks);
      WriteLE<8>(bytes, OffsetCpuStartTicks, static_cast<uint64_t>(payload.CpuStartTicks));
      WriteLE<4>(bytes, OffsetCpuBusyTicks, payload.CpuBusyTicks);
      WriteLE<4>(bytes, OffsetPreferredFrameTicks, payload.PreferredFrameTicks);
      bytes[OffsetFlags] = static_cast<uint8_t>(payload.Flags);
      return bytes;
    }
  }

  std::size_t EncodePayload(const Payload& payload, const StartMetadata& metadata, const std::span<uint8_t> dst) noexcept
  {
    const bool isStart = payload.Kind == MarkerKind::SequenceStart;
    const std::size_t byteCount = isStart ? StartPayloadByteCount : payload.Kind == MarkerKind::Sync ? SyncPayloadByteCount : PayloadByteCount;
    if (dst.size() < byteCount)
    {
      return 0;
    }

    // A sync marker is the start of the header: magic, format version, kind and frame index
    const std::array<uint8_t, PayloadByteCount> header = EncodeHeader(payload);
    std::copy_n(header.begin(), std::min(byteCount, PayloadByteCount), dst.begin());
    if (isStart)
    {
      WriteLE<8>(dst, OffsetStartUtcTicks, static_cast<uint64_t>(metadata.UtcTicks));
      std::copy_n(metadata.Id.Bytes.begin(), SequenceId::ByteCount, dst.subspan(OffsetSequenceId).begin());
    }
    return byteCount;
  }

  bool TryDecodePayload(const std::span<const uint8_t> bytes, Payload& rPayload, StartMetadata* const pMetadata) noexcept
  {
    if (bytes.size() < SyncPayloadByteCount || bytes[OffsetMagic0] != PayloadMagic0 || bytes[OffsetMagic1] != PayloadMagic1 ||
        bytes[OffsetVersion] != PayloadFormatVersion || bytes[OffsetKind] > MaxMarkerKindValue)
    {
      return false;
    }

    const auto kind = static_cast<MarkerKind>(bytes[OffsetKind]);
    if (kind == MarkerKind::Sync)
    {
      if (bytes.size() != SyncPayloadByteCount)
      {
        return false;
      }
      rPayload = Payload{ReadLE<8>(bytes, OffsetFrameIndex), 0, 0u, kind};
      if (pMetadata != nullptr)
      {
        *pMetadata = StartMetadata{};
      }
      return true;
    }
    if (bytes.size() < PayloadByteCount)
    {
      return false;
    }
    StartMetadata metadata;
    if (kind == MarkerKind::SequenceStart)
    {
      if (bytes.size() != StartPayloadByteCount)
      {
        return false;
      }
      metadata.UtcTicks = static_cast<int64_t>(ReadLE<8>(bytes, OffsetStartUtcTicks));
      std::copy_n(bytes.subspan(OffsetSequenceId).begin(), SequenceId::ByteCount, metadata.Id.Bytes.begin());
    }
    else if (bytes.size() != PayloadByteCount)
    {
      return false;
    }

    rPayload.Kind = kind;
    rPayload.FrameIndex = ReadLE<8>(bytes, OffsetFrameIndex);
    rPayload.AnimationTicks = static_cast<int64_t>(ReadLE<8>(bytes, OffsetAnimationTicks));
    rPayload.RunId = static_cast<uint32_t>(ReadLE<4>(bytes, OffsetRunId));
    rPayload.IntendedDisplayTicks = static_cast<int64_t>(ReadLE<8>(bytes, OffsetIntendedDisplayTicks));
    rPayload.TargetFrameTicks = static_cast<uint32_t>(ReadLE<4>(bytes, OffsetTargetFrameTicks));
    rPayload.CpuStartTicks = static_cast<int64_t>(ReadLE<8>(bytes, OffsetCpuStartTicks));
    rPayload.CpuBusyTicks = static_cast<uint32_t>(ReadLE<4>(bytes, OffsetCpuBusyTicks));
    rPayload.PreferredFrameTicks = static_cast<uint32_t>(ReadLE<4>(bytes, OffsetPreferredFrameTicks));
    // Every value is accepted: bits without a name are reserved and kept
    rPayload.Flags = static_cast<MarkerFlags>(bytes[OffsetFlags]);
    if (pMetadata != nullptr)
    {
      *pMetadata = metadata;
    }
    return true;
  }
}
