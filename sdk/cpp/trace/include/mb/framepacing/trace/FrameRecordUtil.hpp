#ifndef MB_FRAMEPACING_TRACE_FRAMERECORDUTIL_HPP
#define MB_FRAMEPACING_TRACE_FRAMERECORDUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/trace/FrameRecord.hpp>

//! The frame record of a marker's payload: what the application
//! logs to its trace for the frame the marker is drawn in. It reads no clock, calls no tracing API and allocates nothing.
namespace MB::FramePacing::Trace::FrameRecordUtil
{
  //! The record of the frame whose main marker is drawn from payload. False, with rRecord empty (FrameRecord()), for a marker that
  //! gets no record: a sync marker, a kind that is none of MarkerKind's, and a CPU start time that is not greater than 0 (0 is
  //! "not known" in a marker, and a trace has no time before its clock's start).
  //!
  //! The name is made from the payload's run id and frame index, the slice starts at its CPU start time and is as long as its CPU
  //! busy time, and the annotations are its animation time, always, and its preferred frame time, target frame time and intended
  //! display time, each unless it is 0 ("not known"). Every value is the payload's, to the nanosecond, so after the payload's caps.
  [[nodiscard]] bool TryCreate(const Marker::Payload& payload, FrameRecord& rRecord) noexcept;
}

#endif
