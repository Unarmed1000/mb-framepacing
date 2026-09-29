#ifndef MB_FRAMEPACINGDATA_DATAFORMATERROR_HPP
#define MB_FRAMEPACINGDATA_DATAFORMATERROR_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <stdexcept>
#include <string>

namespace MB::FramePacingData
{
  //! A file is not in a format this library reads: another kind of file, damaged content, or a newer format version (the message then
  //! says to update the tools or the library).
  class DataFormatError : public std::runtime_error
  {
  public:
    explicit DataFormatError(const std::string& message)
      : std::runtime_error(message)
    {
    }
  };
}

#endif
