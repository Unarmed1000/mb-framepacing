#ifndef MB_FRAMEPACING_TRACE_FRAMEDETAIL_HPP
#define MB_FRAMEPACING_TRACE_FRAMEDETAIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Trace
{
  //! What of a frame a slice or a moment of the frame record's second part is (sdk/doc/trace-frame-record.md, part 2: optional,
  //! not checked against the capture). Its name is the detail's prefix and the frame's ids (FrameDetailName).
  enum class FrameDetail : uint8_t
  {
    //! A wait before the frame's CPU work (FrameRecordText::WaitNamePrefix, on WaitTrackName)
    Wait = 1,
    //! The frame's present call (PresentNamePrefix, on PresentTrackName)
    Present = 2,
    //! The GPU's work on the frame (GpuNamePrefix, on GpuTrackName)
    Gpu = 3,
    //! A moment: when the platform says the frame was shown (DisplayNamePrefix, on DisplayTrackName)
    Display = 4,
    //! A moment: a frame that was made and never presented (NotPresentedNamePrefix, on RunTrackName)
    NotPresented = 5,
  };
}

#endif
