#ifndef MB_FRAMEPACINGDATA_LIBRARYVERSION_HPP
#define MB_FRAMEPACINGDATA_LIBRARYVERSION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <string_view>

namespace MB::FramePacingData
{
  //! A library version, MAJOR.MINOR.PATCH, and as text ("0.1.0"). GetLibraryVersion() gives the linked library's.
  struct LibraryVersion
  {
    int Major{0};
    int Minor{0};
    int Patch{0};
    std::string_view Text;
  };
}

#endif
