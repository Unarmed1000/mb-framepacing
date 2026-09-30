#ifndef MB_FRAMEPACING_CORE_GETLIBRARYVERSION_HPP
#define MB_FRAMEPACING_CORE_GETLIBRARYVERSION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/LibraryVersion.hpp>

namespace MB::FramePacing
{
  //! The linked library's version (every module has the same). Version.hpp has it at compile time; this header does not include it, so a
  //! version bump does not rebuild every file that includes the library.
  LibraryVersion GetLibraryVersion() noexcept;
}

#endif
