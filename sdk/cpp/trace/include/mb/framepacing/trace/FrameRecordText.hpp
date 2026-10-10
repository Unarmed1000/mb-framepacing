#ifndef MB_FRAMEPACING_TRACE_FRAMERECORDTEXT_HPP
#define MB_FRAMEPACING_TRACE_FRAMERECORDTEXT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <string_view>

//! EXPERIMENTAL (the trace module, sdk/doc/trace-frame-record.md). The strings of the frame record, the slice an application logs
//! to its trace for every frame: the category, the name of the track, the start of a slice's name and the names of the
//! annotations. The tools find the record by these strings, so an application logs them as they are. Every text is a string
//! literal: it is there for the life of the program, nothing is allocated, and its data() ends with a zero, so it can be given to
//! a function that takes a C string.
namespace MB::FramePacing::Trace::FrameRecordText
{
  //! The category of every slice.
  inline constexpr std::string_view Category = "mbfp";
  //! The name of the track the slices are on, under the application's process.
  inline constexpr std::string_view TrackName = "mbfp frames";
  //! What a slice's name begins with, the space included: the frame's run id and frame index follow (FrameRecordName).
  inline constexpr std::string_view NamePrefix = "mbfp frame ";

  //! The annotation with the marker's animation time.
  inline constexpr std::string_view AnimationNs = "animationNs";
  //! The annotation with the marker's preferred frame time.
  inline constexpr std::string_view MarkerPreferredNs = "markerPreferredNs";
  //! The annotation with the marker's target frame time.
  inline constexpr std::string_view MarkerTargetNs = "markerTargetNs";
  //! The annotation with the marker's intended display time.
  inline constexpr std::string_view IntendedDisplayNs = "intendedDisplayNs";

  //! The clock a slice's times are on: Perfetto's built-in clock for the system's CLOCK_MONOTONIC, which the times in the marker
  //! are read from.
  inline constexpr uint32_t ClockId = 3;
}

#endif
