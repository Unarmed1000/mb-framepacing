#ifndef MB_FRAMEPACINGDATA_DATARECT_HPP
#define MB_FRAMEPACINGDATA_DATARECT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacingData
{
  //! An integer pixel rectangle: [X, X + Width) x [Y, Y + Height).
  struct DataRect
  {
    int32_t X{0};
    int32_t Y{0};
    int32_t Width{0};
    int32_t Height{0};

    constexpr bool operator==(const DataRect&) const noexcept = default;
  };
}

#endif
