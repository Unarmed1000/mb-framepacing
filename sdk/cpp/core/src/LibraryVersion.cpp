// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The only file that includes the generated Version.hpp: a version bump rebuilds it alone, not every file that includes the library.
#include <mb/framepacing/Core.hpp>
#include <mb/framepacing/core/Version.hpp>

namespace MB::FramePacing
{
  LibraryVersion GetLibraryVersion() noexcept
  {
    return {VersionMajor, VersionMinor, VersionPatch, VersionString, VersionPrerelease};
  }
}
