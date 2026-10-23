//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What an application logs to its trace for one frame: a slice named for
//* the frame's marker, from the marker's CPU start time for its CPU busy time, with the marker's other times as annotations. TryCreate
//* works it out from the payload the frame's marker is drawn from, so the record and the marker can not disagree; the application gives
//* the values to whatever it traces with. Nothing here writes a trace, reads a clock or allocates (but Name, which makes a string).
//*
//* A marker gets a record when it is a main marker (a frame, start or end marker) whose CPU start time is greater than 0: 0 is "not
//* known" in a marker, and a trace has no time before its clock's start. The times are on the clock the application reads the marker's
//* from, which the record takes to be the system's CLOCK_MONOTONIC (FrameRecordText.ClockId).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Trace
{
  /// <summary>What to log to a trace for one frame.</summary>
  public readonly struct FrameRecord : IEquatable<FrameRecord>
  {
    /// <summary>The most annotations a record has.</summary>
    public const int MaxAnnotationCount = 5;

    private FrameRecord(in Payload payload, int lane)
    {
      Lane = lane;
      RunId = payload.RunId;
      FrameIndex = payload.FrameIndex;
      StartTime = payload.CpuStartTime;
      Duration = payload.CpuBusy;
      AnimationTime = payload.AnimationTime;
      PreferredFrameTime = payload.PreferredFrameTime;
      TargetFrameTime = payload.TargetFrameTime;
      RefreshPeriod = payload.RefreshPeriod;
      IntendedDisplayTime = payload.IntendedDisplayTime;
    }

    /// <summary>The marker's run id: the first number of the slice's name.</summary>
    public readonly uint RunId;

    /// <summary>The marker's frame index: the second number of the slice's name.</summary>
    public readonly ulong FrameIndex;

    /// <summary>Where the slice starts: the marker's CPU start time.</summary>
    public readonly NanosecondTickCount StartTime;

    /// <summary>How long the slice is: the marker's CPU busy time, after the payload's cap. Zero: a slice without length.</summary>
    public readonly NanosecondTimeDuration Duration;

    /// <summary>The marker's animation time: the annotation every record has.</summary>
    public readonly NanosecondTimeSpan AnimationTime;

    /// <summary>The marker's preferred frame time; zero: not known, and no annotation.</summary>
    public readonly NanosecondTimeDuration PreferredFrameTime;

    /// <summary>The marker's target frame time; zero: not known, and no annotation.</summary>
    public readonly NanosecondTimeDuration TargetFrameTime;

    /// <summary>The marker's refresh period; zero: not known, and no annotation.</summary>
    public readonly NanosecondTimeDuration RefreshPeriod;

    /// <summary>The marker's intended display time; zero: not known, and no annotation.</summary>
    public readonly NanosecondTickCount IntendedDisplayTime;

    /// <summary>
    /// Which of the tracks named <see cref="FrameRecordText.TrackName"/> the slice goes on (<see cref="TrackLanes"/>): 0 unless records
    /// overlap.
    /// </summary>
    public readonly int Lane;

    /// <summary>
    /// The record of the marker <paramref name="payload"/> is drawn as; false, with an empty record, for a marker that gets none (a sync
    /// marker, or a CPU start time that is not greater than 0).
    /// </summary>
    public static bool TryCreate(in Payload payload, out FrameRecord record)
    {
      if (payload.Kind == MarkerKind.Sync || payload.CpuStartTime.Nanoseconds <= 0)
      {
        record = default;
        return false;
      }
      record = new FrameRecord(payload, 0);
      return true;
    }

    /// <summary>
    /// The same, with the record's lane from <paramref name="lanes"/> (<see cref="TrackLanes.Assign"/>), which keeps the frames' records
    /// apart when they overlap (frames in flight). Keep one <see cref="TrackLanes"/> for the records of a run, and give it every record, in
    /// the order they are made. A marker that gets no record takes no lane.
    /// </summary>
    public static bool TryCreate(in Payload payload, TrackLanes lanes, out FrameRecord record)
    {
      if (lanes == null)
        throw new ArgumentNullException(nameof(lanes));
      if (!TryCreate(payload, out record))
        return false;
      record = new FrameRecord(payload, lanes.Assign(record.StartTime, record.EndTime));
      return true;
    }

    /// <summary>Where the slice ends: the start plus the length.</summary>
    public NanosecondTickCount EndTime => StartTime + Duration.Value;

    /// <summary>The slice's name, as a new string. In a frame loop use <see cref="TryFormatName"/>.</summary>
    public string Name => FrameRecordName.Format(RunId, FrameIndex);

    /// <summary>
    /// Write the slice's name to <paramref name="destination"/> (<see cref="FrameRecordName.MaxLength"/> characters always fit); false when
    /// it does not fit.
    /// </summary>
    public bool TryFormatName(Span<char> destination, out int charsWritten) =>
      FrameRecordName.TryFormat(RunId, FrameIndex, destination, out charsWritten);

    /// <summary>How many annotations the slice has: the animation time, and each of the other four the marker knows.</summary>
    public int AnnotationCount =>
      1
      + (PreferredFrameTime.Nanoseconds != 0 ? 1 : 0)
      + (TargetFrameTime.Nanoseconds != 0 ? 1 : 0)
      + (RefreshPeriod.Nanoseconds != 0 ? 1 : 0)
      + (IntendedDisplayTime.Nanoseconds != 0 ? 1 : 0);

    /// <summary>
    /// The annotation at <paramref name="index"/> (0 to <see cref="AnnotationCount"/> less one), in the order of the marker's fields: the
    /// animation time, the preferred frame time, the target frame time, the refresh period, the intended display time.
    /// </summary>
    public FrameRecordAnnotation GetAnnotation(int index)
    {
      if (index == 0)
        return new FrameRecordAnnotation(FrameRecordText.AnimationNs, AnimationTime.Nanoseconds);
      int position = 0;
      if (PreferredFrameTime.Nanoseconds != 0 && ++position == index)
        return new FrameRecordAnnotation(FrameRecordText.MarkerPreferredNs, PreferredFrameTime.Nanoseconds);
      if (TargetFrameTime.Nanoseconds != 0 && ++position == index)
        return new FrameRecordAnnotation(FrameRecordText.MarkerTargetNs, TargetFrameTime.Nanoseconds);
      if (RefreshPeriod.Nanoseconds != 0 && ++position == index)
        return new FrameRecordAnnotation(FrameRecordText.MarkerRefreshPeriodNs, RefreshPeriod.Nanoseconds);
      if (IntendedDisplayTime.Nanoseconds != 0 && ++position == index)
        return new FrameRecordAnnotation(FrameRecordText.IntendedDisplayNs, IntendedDisplayTime.Nanoseconds);
      throw new ArgumentOutOfRangeException(nameof(index), index, "Not an annotation of this record");
    }

    public bool Equals(FrameRecord other) =>
      RunId == other.RunId
      && FrameIndex == other.FrameIndex
      && StartTime == other.StartTime
      && Duration == other.Duration
      && AnimationTime == other.AnimationTime
      && PreferredFrameTime == other.PreferredFrameTime
      && TargetFrameTime == other.TargetFrameTime
      && RefreshPeriod == other.RefreshPeriod
      && IntendedDisplayTime == other.IntendedDisplayTime
      && Lane == other.Lane;

    public override bool Equals(object obj) => obj is FrameRecord other && Equals(other);

    public override int GetHashCode()
    {
      // Ten fields: more than HashCode.Combine takes
      var hash = new HashCode();
      hash.Add(RunId);
      hash.Add(FrameIndex);
      hash.Add(StartTime);
      hash.Add(Duration);
      hash.Add(AnimationTime);
      hash.Add(PreferredFrameTime);
      hash.Add(TargetFrameTime);
      hash.Add(RefreshPeriod);
      hash.Add(IntendedDisplayTime);
      hash.Add(Lane);
      return hash.ToHashCode();
    }

    public static bool operator ==(FrameRecord left, FrameRecord right) => left.Equals(right);

    public static bool operator !=(FrameRecord left, FrameRecord right) => !left.Equals(right);

    public override string ToString() => Name;
  }
}
