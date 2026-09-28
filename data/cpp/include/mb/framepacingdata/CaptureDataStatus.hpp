#ifndef MB_FRAMEPACINGDATA_CAPTUREDATASTATUS_HPP
#define MB_FRAMEPACINGDATA_CAPTUREDATASTATUS_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacingData
{
  //! What reading a capture's markers gave.
  enum class CaptureDataStatus : uint8_t
  {
    Undecodable = 0,
    Decoded = 1,
    //! The markers at different heights of the frame disagree (the capture shows parts of two frames).
    Torn = 2,
  };
}

#endif
