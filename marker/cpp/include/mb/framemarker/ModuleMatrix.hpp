#ifndef MB_FRAMEMARKER_MODULEMATRIX_HPP
#define MB_FRAMEMARKER_MODULEMATRIX_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framemarker/Constants.hpp>
#include <array>
#include <cstddef>
#include <cstdint>

namespace MB::FrameMarker
{
  //! The QR module matrix, 1 = dark module. Only the top-left Size x Size modules are used; rows are QrModuleCount apart.
  struct ModuleMatrix
  {
    int32_t Size{0};
    std::array<uint8_t, static_cast<std::size_t>(QrModuleCount) * QrModuleCount> Modules{};

    constexpr bool IsDark(const int32_t x, const int32_t y) const noexcept
    {
      return Modules[(static_cast<std::size_t>(y) * QrModuleCount) + static_cast<std::size_t>(x)] != 0;
    }
  };
}

#endif
