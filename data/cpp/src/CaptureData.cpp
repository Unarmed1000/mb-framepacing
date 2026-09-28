// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framemarker/FrameMarker.hpp>
#include <mb/framepacingdata/FramePacingData.hpp>
#include <array>
#include <bit>
#include <stdexcept>
#include <string>

namespace MB::FramePacingData
{
  namespace
  {
    constexpr std::size_t OffsetMarkers = 72;
    constexpr std::size_t MarkerSize = 24;
    constexpr uint32_t FramesStoredFlag = 1u;
    constexpr uint32_t CameraFlag = 2u;
    constexpr std::size_t OffsetStatus = 28;
    constexpr std::size_t OffsetMainLength = 29;
    constexpr std::size_t OffsetSecondLength = 30;
    constexpr std::size_t OffsetMain = 32;
    constexpr std::size_t OffsetSecond = OffsetMain + MainMarkerCapacity;

    static_assert(OffsetMarkers + (MaxMarkerLocations * MarkerSize) <= CaptureDataHeaderSize);
    static_assert(OffsetSecond + SecondMarkerCapacity == CaptureDataRecordSize);

    uint64_t ReadU64(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      uint64_t value = 0;
      for (std::size_t i = 0; i < 8; ++i)
      {
        value |= static_cast<uint64_t>(bytes[offset + i]) << (8u * i);
      }
      return value;
    }

    uint32_t ReadU32(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return static_cast<uint32_t>(bytes[offset]) | (static_cast<uint32_t>(bytes[offset + 1]) << 8u) |
             (static_cast<uint32_t>(bytes[offset + 2]) << 16u) | (static_cast<uint32_t>(bytes[offset + 3]) << 24u);
    }

    uint16_t ReadU16(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return static_cast<uint16_t>(static_cast<uint32_t>(bytes[offset]) | (static_cast<uint32_t>(bytes[offset + 1]) << 8u));
    }

    int32_t ReadI32(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return static_cast<int32_t>(ReadU32(bytes, offset));
    }

    int64_t ReadI64(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return static_cast<int64_t>(ReadU64(bytes, offset));
    }

    double ReadF64(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return std::bit_cast<double>(ReadU64(bytes, offset));
    }

    DataRect ReadRect(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
    {
      return {ReadI32(bytes, offset), ReadI32(bytes, offset + 4), ReadI32(bytes, offset + 8), ReadI32(bytes, offset + 12)};
    }

    bool TryDecode(const std::vector<uint8_t>& bytes, MB::FrameMarker::Payload& rPayload, MB::FrameMarker::StartMetadata* pMetadata) noexcept
    {
      return !bytes.empty() && MB::FrameMarker::TryDecodePayload(bytes, rPayload, pMetadata);
    }
  }

  CaptureDataHeader CaptureDataHeader::Parse(const std::span<const uint8_t> bytes)
  {
    if (bytes.size() < CaptureDataHeaderSize || ReadU32(bytes, 0) != CaptureDataMagic)
    {
      throw DataFormatError("Not an mb-framepacing capture data file (.mbcd)");
    }
    const uint16_t version = ReadU16(bytes, 4);
    if (version > CaptureDataFormatVersion)
    {
      throw DataFormatError("The capture data file has format version " + std::to_string(version) + ", newer than this reader reads (" +
                            std::to_string(CaptureDataFormatVersion) + "): update the tools or the library");
    }
    if (version != CaptureDataFormatVersion)
    {
      throw DataFormatError("Unsupported capture data file format version " + std::to_string(version));
    }
    if (ReadU16(bytes, 6) != CaptureDataHeaderSize || ReadU32(bytes, 8) != CaptureDataRecordSize)
    {
      throw DataFormatError("Unexpected capture data header or record size");
    }
    const uint32_t markerCount = ReadU32(bytes, 64);
    if (markerCount > MaxMarkerLocations)
    {
      throw DataFormatError("Invalid marker count in the capture data header");
    }

    const uint32_t flags = ReadU32(bytes, 12);
    CaptureDataHeader header;
    header.Width = ReadI32(bytes, 16);
    header.Height = ReadI32(bytes, 20);
    header.FrameRateNumerator = ReadU32(bytes, 24);
    header.FrameRateDenominator = ReadU32(bytes, 28);
    header.SourceWidth = ReadI32(bytes, 32);
    header.SourceHeight = ReadI32(bytes, 36);
    header.Region = ReadRect(bytes, 40);
    for (std::size_t i = 0; i < markerCount; ++i)
    {
      const std::size_t offset = OffsetMarkers + (i * MarkerSize);
      header.Markers.push_back({ReadRect(bytes, offset), ReadF64(bytes, offset + 16)});
    }
    header.FramesStored = (flags & FramesStoredFlag) != 0u;
    header.Camera = (flags & CameraFlag) != 0u;
    return header;
  }

  bool CaptureDataRecord::TryDecodeMain(MB::FrameMarker::Payload& rPayload, MB::FrameMarker::StartMetadata* pMetadata) const noexcept
  {
    return TryDecode(MainBytes, rPayload, pMetadata);
  }

  bool CaptureDataRecord::TryDecodeSecond(MB::FrameMarker::Payload& rPayload) const noexcept
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
    record.CaptureIndex = ReadI64(bytes, 0);
    record.HostTicks = ReadI64(bytes, 8);
    record.DeviceTicks = ReadI64(bytes, 16);
    record.Flags = ReadU32(bytes, 24);
    record.Status = static_cast<CaptureDataStatus>(status);
    const auto main = bytes.subspan(OffsetMain, mainLength);
    const auto second = bytes.subspan(OffsetSecond, secondLength);
    record.MainBytes.assign(main.begin(), main.end());
    record.SecondBytes.assign(second.begin(), second.end());
    return record;
  }

  CaptureDataReader::CaptureDataReader(const std::filesystem::path& path)
    : m_file(path, std::ios::binary)
  {
    if (!m_file)
    {
      throw std::runtime_error("Cannot open '" + path.string() + "'");
    }
    std::array<uint8_t, CaptureDataHeaderSize> bytes{};
    m_file.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (m_file.gcount() != static_cast<std::streamsize>(bytes.size()))
    {
      throw DataFormatError("'" + path.string() + "' is too short to be a capture data file");
    }
    m_header = CaptureDataHeader::Parse(bytes);
    // A capture that was killed mid-write may end with a partial record; it is ignored
    const auto size = static_cast<int64_t>(std::filesystem::file_size(path));
    m_recordCount = (size - static_cast<int64_t>(CaptureDataHeaderSize)) / static_cast<int64_t>(CaptureDataRecordSize);
  }

  CaptureDataRecord CaptureDataReader::ReadRecord(const int64_t index)
  {
    if (index < 0 || index >= m_recordCount)
    {
      throw std::out_of_range("Record " + std::to_string(index) + " of " + std::to_string(m_recordCount));
    }
    std::array<uint8_t, CaptureDataRecordSize> bytes{};
    m_file.clear();
    m_file.seekg(static_cast<std::streamoff>(CaptureDataHeaderSize) +
                 (static_cast<std::streamoff>(index) * static_cast<std::streamoff>(CaptureDataRecordSize)));
    m_file.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (m_file.gcount() != static_cast<std::streamsize>(bytes.size()))
    {
      throw DataFormatError("Unexpected end of capture data file");
    }
    return CaptureDataRecord::Parse(bytes);
  }

  std::vector<CaptureDataRecord> CaptureDataReader::ReadAll()
  {
    std::vector<CaptureDataRecord> records;
    records.reserve(static_cast<std::size_t>(m_recordCount));
    for (int64_t i = 0; i < m_recordCount; ++i)
    {
      records.push_back(ReadRecord(i));
    }
    return records;
  }
}
