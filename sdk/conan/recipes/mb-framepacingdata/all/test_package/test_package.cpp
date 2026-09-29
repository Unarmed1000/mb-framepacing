// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The Conan package's test: the data library and the marker library it brings along both link, and both report their version.
#include <mb/framemarker/FrameMarker.hpp>
#include <mb/framepacingdata/FramePacingData.hpp>
#include <cstdio>
#include <string_view>

int main()
{
  const std::string_view data = MB::FramePacingData::GetLibraryVersion().Text;
  const std::string_view marker = MB::FrameMarker::GetLibraryVersion().Text;
  std::printf("mb_framepacingdata %.*s, mb_framemarker %.*s\n", static_cast<int>(data.size()), data.data(), static_cast<int>(marker.size()),
              marker.data());
  return data.empty() || marker.empty() ? 1 : 0;
}
