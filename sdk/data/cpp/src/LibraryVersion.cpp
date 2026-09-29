// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The only file that includes the generated Version.hpp: a version bump rebuilds it alone, not every file that includes the library.
#include <mb/framepacingdata/FramePacingData.hpp>
#include <mb/framepacingdata/Version.hpp>

namespace MB::FramePacingData
{
  LibraryVersion GetLibraryVersion() noexcept
  {
    return {VersionMajor, VersionMinor, VersionPatch, VersionString, VersionPrerelease};
  }
}
