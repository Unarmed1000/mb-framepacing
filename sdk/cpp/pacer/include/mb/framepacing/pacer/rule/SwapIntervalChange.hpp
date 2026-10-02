#ifndef MB_FRAMEPACING_PACER_RULE_SWAPINTERVALCHANGE_HPP
#define MB_FRAMEPACING_PACER_RULE_SWAPINTERVALCHANGE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). What the swap interval rule decided on a frame.
  enum class SwapIntervalChange : uint8_t
  {
    None = 0,
    //! A longer swap interval: a lower frame rate
    Slower = 1,
    //! A shorter swap interval: a higher frame rate
    Faster = 2,
  };
}

#endif
