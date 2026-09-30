#ifndef MB_FRAMEPACING_CORE_LIBRARYVERSION_HPP
#define MB_FRAMEPACING_CORE_LIBRARYVERSION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <string_view>

namespace MB::FramePacing
{
  //! The library version, MAJOR.MINOR.PATCH, and as text ("0.1.0", or "0.2.0-beta.1" for a pre-release). GetLibraryVersion() gives the
  //! linked library's.
  struct LibraryVersion
  {
    int Major{0};
    int Minor{0};
    int Patch{0};
    std::string_view Text;
    //! The pre-release ("alpha.1", "beta.2", "rc.1"), empty for a release
    std::string_view Prerelease;
  };
}

#endif
