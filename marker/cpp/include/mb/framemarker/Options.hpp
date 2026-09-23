#ifndef MB_FRAMEMARKER_OPTIONS_HPP
#define MB_FRAMEMARKER_OPTIONS_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framemarker/Constants.hpp>
#include <cstdint>

namespace MB::FrameMarker
{
  //! How large the marker is drawn.
  struct Options
  {
    //! Size of one QR module in source pixels. See doc/marker-format.md "Sizing" or RecommendModuleSizePx.
    int32_t ModuleSizePx{6};
    //! White border around the symbol in modules. The QR specification asks for 4.
    int32_t QuietZoneModules{RecommendedQuietZoneModules};
  };
}

#endif
