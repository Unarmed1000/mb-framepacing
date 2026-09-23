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
    constexpr std::size_t OffsetStartUtcTicks = PayloadByteCount;
    constexpr std::size_t OffsetStartNameLength = OffsetStartUtcTicks + 8;
    constexpr std::size_t OffsetStartName = OffsetStartNameLength + 1;

    static_assert(OffsetRunId + 4 == PayloadByteCount);
    static_assert(OffsetStartName == StartPayloadFixedByteCount);
    static_assert(MaxStartNameBytes <= 255u);

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
  }

  std::array<uint8_t, PayloadByteCount> EncodePayload(const Payload& payload) noexcept
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
    return bytes;
  }

  std::size_t EncodePayload(const Payload& payload, const StartMetadata& metadata, const std::span<uint8_t> dst) noexcept
  {
    const bool isStart = payload.Kind == MarkerKind::SequenceStart;
    if (isStart && metadata.Name.size() > MaxStartNameBytes)
    {
      return 0;
    }
    const std::size_t byteCount = isStart ? StartPayloadFixedByteCount + metadata.Name.size() : PayloadByteCount;
    if (dst.size() < byteCount)
    {
      return 0;
    }

    const std::array<uint8_t, PayloadByteCount> header = EncodePayload(payload);
    std::copy(header.begin(), header.end(), dst.begin());
    if (isStart)
    {
      WriteLE<8>(dst, OffsetStartUtcTicks, static_cast<uint64_t>(metadata.UtcTicks));
      dst[OffsetStartNameLength] = static_cast<uint8_t>(metadata.Name.size());
      for (std::size_t i = 0; i < metadata.Name.size(); ++i)
      {
        dst[OffsetStartName + i] = static_cast<uint8_t>(metadata.Name[i]);
      }
    }
    return byteCount;
  }

  bool TryDecodePayload(const std::span<const uint8_t> bytes, Payload& rPayload, StartMetadata* const pMetadata) noexcept
  {
    if (bytes.size() < PayloadByteCount || bytes[OffsetMagic0] != PayloadMagic0 || bytes[OffsetMagic1] != PayloadMagic1 ||
        bytes[OffsetVersion] != PayloadFormatVersion || bytes[OffsetKind] > MaxMarkerKindValue)
    {
      return false;
    }

    const auto kind = static_cast<MarkerKind>(bytes[OffsetKind]);
    StartMetadata metadata;
    if (kind == MarkerKind::SequenceStart)
    {
      if (bytes.size() < StartPayloadFixedByteCount)
      {
        return false;
      }
      const std::size_t nameLength = bytes[OffsetStartNameLength];
      if (nameLength > MaxStartNameBytes || bytes.size() != StartPayloadFixedByteCount + nameLength)
      {
        return false;
      }
      metadata.UtcTicks = static_cast<int64_t>(ReadLE<8>(bytes, OffsetStartUtcTicks));
      metadata.Name = std::string_view(reinterpret_cast<const char*>(bytes.data() + OffsetStartName), nameLength);
    }
    else if (bytes.size() != PayloadByteCount)
    {
      return false;
    }

    rPayload.Kind = kind;
    rPayload.FrameIndex = ReadLE<8>(bytes, OffsetFrameIndex);
    rPayload.AnimationTicks = static_cast<int64_t>(ReadLE<8>(bytes, OffsetAnimationTicks));
    rPayload.RunId = static_cast<uint32_t>(ReadLE<4>(bytes, OffsetRunId));
    if (pMetadata != nullptr)
    {
      *pMetadata = metadata;
    }
    return true;
  }
}
