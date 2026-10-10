#ifndef MB_FRAMEPACING_TRACE_FRAMERECORDANNOTATION_HPP
#define MB_FRAMEPACING_TRACE_FRAMERECORDANNOTATION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <string_view>

namespace MB::FramePacing::Trace
{
  //! EXPERIMENTAL (the trace module, sdk/doc/trace-frame-record.md). One annotation of a frame record's slice: one of the marker's
  //! times, under its name.
  struct FrameRecordAnnotation
  {
    //! The annotation's name: one of FrameRecordText's, a string literal whose data() ends with a zero.
    std::string_view Name;
    //! The marker's value, a whole number of nanoseconds.
    int64_t Value{0};

    constexpr bool operator==(const FrameRecordAnnotation&) const noexcept = default;
  };
}

#endif
