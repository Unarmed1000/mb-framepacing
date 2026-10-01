// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/capture/CaptureDataHeader.hpp>
#include <mb/framepacing/data/capture/CaptureDataReader.hpp>
#include <mb/framepacing/data/capture/CaptureDataRecord.hpp>
#include <array>
#include <stdexcept>
#include <string>
#include "detail/CaptureDataFormat.hpp"

namespace MB::FramePacing::Data
{
  CaptureDataReader::CaptureDataReader(const std::filesystem::path& path)
    : m_file(path, std::ios::binary)
  {
    if (!m_file)
    {
      throw std::runtime_error("Cannot open '" + path.string() + "'");
    }
    std::array<uint8_t, CaptureDataFormat::HeaderSize> bytes{};
    m_file.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (m_file.gcount() != static_cast<std::streamsize>(bytes.size()))
    {
      throw DataFormatError("'" + path.string() + "' is too short to be a capture data file");
    }
    m_header = CaptureDataHeader::Parse(bytes);
    // A capture that was killed mid-write may end with a partial record; it is ignored
    const auto size = static_cast<int64_t>(std::filesystem::file_size(path));
    m_recordCount = (size - static_cast<int64_t>(CaptureDataFormat::HeaderSize)) / static_cast<int64_t>(CaptureDataFormat::RecordSize);
  }

  CaptureDataRecord CaptureDataReader::ReadRecord(const int64_t index)
  {
    if (index < 0 || index >= m_recordCount)
    {
      throw std::out_of_range("Record " + std::to_string(index) + " of " + std::to_string(m_recordCount));
    }
    std::array<uint8_t, CaptureDataFormat::RecordSize> bytes{};
    m_file.clear();
    m_file.seekg(static_cast<std::streamoff>(CaptureDataFormat::HeaderSize) +
                 (static_cast<std::streamoff>(index) * static_cast<std::streamoff>(CaptureDataFormat::RecordSize)));
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
