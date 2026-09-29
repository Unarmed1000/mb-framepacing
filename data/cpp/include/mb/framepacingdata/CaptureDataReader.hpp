#ifndef MB_FRAMEPACINGDATA_CAPTUREDATAREADER_HPP
#define MB_FRAMEPACINGDATA_CAPTUREDATAREADER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/CaptureDataHeader.hpp>
#include <mb/framepacingdata/CaptureDataRecord.hpp>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <vector>

namespace MB::FramePacingData
{
  //! Reads captures.mbcd: the header, then the records by index or all of them. A partial last record (a capture stopped mid-write) is
  //! ignored. Throws DataFormatError for a file it cannot read, std::runtime_error when the file cannot be opened.
  class CaptureDataReader
  {
    std::ifstream m_file;
    CaptureDataHeader m_header;
    int64_t m_recordCount{0};

  public:
    explicit CaptureDataReader(const std::filesystem::path& path);

    const CaptureDataHeader& Header() const noexcept
    {
      return m_header;
    }

    int64_t RecordCount() const noexcept
    {
      return m_recordCount;
    }

    CaptureDataRecord ReadRecord(int64_t index);

    //! Every record, in file order.
    std::vector<CaptureDataRecord> ReadAll();
  };
}

#endif
