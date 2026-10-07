// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The present feedback of sdk/doc/pacer.md: what the platform measured for an earlier frame.
#include <mb/framepacing/pacer/frame/PresentFeedback.hpp>

namespace MB::FramePacing::Pacer
{
  PresentFeedback PresentFeedback::Shown(const uint64_t frameId, const NanosecondTickCount displayTime) noexcept
  {
    return {frameId, PresentResult::Shown, displayTime, false, {}};
  }

  PresentFeedback PresentFeedback::Shown(const uint64_t frameId, const NanosecondTickCount displayTime,
                                         const NanosecondTickCount presentTime) noexcept
  {
    return {frameId, PresentResult::Shown, displayTime, true, presentTime};
  }

  PresentFeedback PresentFeedback::NotShown(const uint64_t frameId) noexcept
  {
    return {frameId, PresentResult::NotShown, {}, false, {}};
  }
}
