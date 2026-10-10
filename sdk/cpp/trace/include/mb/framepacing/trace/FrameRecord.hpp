#ifndef MB_FRAMEPACING_TRACE_FRAMERECORD_HPP
#define MB_FRAMEPACING_TRACE_FRAMERECORD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/trace/FrameRecordAnnotation.hpp>
#include <mb/framepacing/trace/FrameRecordName.hpp>
#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <span>

namespace MB::FramePacing::Trace
{
  //! EXPERIMENTAL (the trace module, sdk/doc/trace-frame-record.md). What an application logs to its trace for one frame: a slice
  //! with this name, in the category FrameRecordText::Category, on the track named FrameRecordText::TrackName, from StartTime for
  //! Duration on the clock FrameRecordText::ClockId, with every entry of Annotations(). FrameRecordUtil::TryCreate makes it from
  //! the payload the frame's marker is drawn from, so the record and the marker can not disagree. The application writes the
  //! slice with its own tracing: the SDK writes no trace.
  struct FrameRecord
  {
    //! The most annotations a record has.
    static constexpr std::size_t MaxAnnotationCount = 4;

    //! The slice's name.
    FrameRecordName Name;
    //! Where the slice starts, on the system's CLOCK_MONOTONIC: the marker's CPU start time.
    NanosecondTickCount StartTime;
    //! How long the slice is: the marker's CPU busy time. Zero: a slice without length.
    NanosecondTimeDuration Duration;
    //! The annotations, of which the first AnnotationCount are the record's: read them through Annotations().
    std::array<FrameRecordAnnotation, MaxAnnotationCount> AnnotationBuffer{};
    //! How many annotations the record has, at most MaxAnnotationCount.
    uint32_t AnnotationCount{0};

    //! The record's annotations, in the order of the marker's fields.
    [[nodiscard]] constexpr std::span<const FrameRecordAnnotation> Annotations() const noexcept
    {
      assert(AnnotationCount <= MaxAnnotationCount);
      return {AnnotationBuffer.data(), AnnotationCount};
    }

    //! Where the slice ends, on the same clock: StartTime and Duration. The count wraps around, as every point on a clock does.
    [[nodiscard]] constexpr NanosecondTickCount EndTime() const noexcept
    {
      return StartTime + Duration.Value();
    }
  };
}

#endif
