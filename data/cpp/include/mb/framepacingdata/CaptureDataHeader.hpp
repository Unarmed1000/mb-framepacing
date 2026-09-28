#ifndef MB_FRAMEPACINGDATA_CAPTUREDATAHEADER_HPP
#define MB_FRAMEPACINGDATA_CAPTUREDATAHEADER_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/DataRect.hpp>
#include <mb/framepacingdata/MarkerLocation.hpp>
#include <cstdint>
#include <span>
#include <vector>

namespace MB::FramePacingData
{
  //! The header of captures.mbcd: the frames the markers were read from, where the markers are (the main marker first), whether the frames
  //! were stored too (frames.mbfc) and whether it is an EXPERIMENTAL camera capture.
  struct CaptureDataHeader
  {
    int32_t Width{0};
    int32_t Height{0};
    //! The source's nominal frame rate as a fraction; 0/0 = unknown.
    uint32_t FrameRateNumerator{0};
    uint32_t FrameRateDenominator{0};
    //! The source's frame size before scaling and cropping.
    int32_t SourceWidth{0};
    int32_t SourceHeight{0};
    //! The stored region of the source, in source pixels; empty = the whole frame.
    DataRect Region{};
    std::vector<MarkerLocation> Markers;
    bool FramesStored{false};
    bool Camera{false};

    //! Parse a header. Throws DataFormatError for another file, a newer format version or wrong sizes.
    static CaptureDataHeader Parse(std::span<const uint8_t> bytes);

    bool operator==(const CaptureDataHeader&) const = default;
  };
}

#endif
