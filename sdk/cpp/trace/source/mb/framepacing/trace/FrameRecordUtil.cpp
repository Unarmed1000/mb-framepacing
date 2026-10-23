// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The frame record of a marker's payload (sdk/doc/trace-frame-record.md, which is the reference): which markers
// get a record, and the name, the times and the annotations of the slice an application logs for one.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/trace/FrameRecordName.hpp>
#include <mb/framepacing/trace/FrameRecordText.hpp>
#include <mb/framepacing/trace/FrameRecordUtil.hpp>
#include <mb/framepacing/trace/TrackLanes.hpp>
#include <cstdint>

namespace MB::FramePacing::Trace::FrameRecordUtil
{
  bool TryCreate(const Marker::Payload& payload, FrameRecord& rRecord) noexcept
  {
    rRecord = FrameRecord();
    // A main marker (a frame, start or end marker) gets a record. A sync marker repeats the ids of the main marker it is drawn
    // with, and a kind that is none of MarkerKind's is no marker.
    const Marker::MarkerKind kind = payload.Kind();
    const bool isMainMarker =
      kind == Marker::MarkerKind::Frame || kind == Marker::MarkerKind::SequenceStart || kind == Marker::MarkerKind::SequenceEnd;
    // A CPU start time of 0 is "not known", and a trace has no time before its clock's start
    if (!isMainMarker || payload.CpuStartTime().Nanoseconds() <= 0)
    {
      return false;
    }

    rRecord.Name = FrameRecordName(payload.RunId(), payload.FrameIndex());
    rRecord.StartTime = payload.CpuStartTime();
    // The payload capped it at what a marker carries, so the slice is as long as the marker says
    rRecord.Duration = payload.CpuBusy();

    // The marker's other times, in the order of its fields. The animation time is always known; of the others a 0 is "not known"
    uint32_t count = 0;
    rRecord.AnnotationBuffer[count] = {FrameRecordText::AnimationNs, payload.AnimationTime().Nanoseconds()};
    ++count;
    if (payload.PreferredFrameTime() != NanosecondTimeDuration())
    {
      rRecord.AnnotationBuffer[count] = {FrameRecordText::MarkerPreferredNs, payload.PreferredFrameTime().Nanoseconds()};
      ++count;
    }
    if (payload.TargetFrameTime() != NanosecondTimeDuration())
    {
      rRecord.AnnotationBuffer[count] = {FrameRecordText::MarkerTargetNs, payload.TargetFrameTime().Nanoseconds()};
      ++count;
    }
    if (payload.RefreshPeriod() != NanosecondTimeDuration())
    {
      rRecord.AnnotationBuffer[count] = {FrameRecordText::MarkerRefreshPeriodNs, payload.RefreshPeriod().Nanoseconds()};
      ++count;
    }
    if (payload.IntendedDisplayTime() != NanosecondTickCount())
    {
      rRecord.AnnotationBuffer[count] = {FrameRecordText::IntendedDisplayNs, payload.IntendedDisplayTime().Nanoseconds()};
      ++count;
    }
    rRecord.AnnotationCount = count;
    return true;
  }

  bool TryCreate(const Marker::Payload& payload, TrackLanes& rLanes, FrameRecord& rRecord) noexcept
  {
    if (!TryCreate(payload, rRecord))
    {
      return false;
    }
    rRecord.Lane = rLanes.Assign(rRecord.StartTime, rRecord.EndTime());
    return true;
  }
}
